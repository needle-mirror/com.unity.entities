using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Core;
using Unity.Entities.Serialization;
using Unity.Profiling;

[assembly: InternalsVisibleTo("Unity.Entities.Tests")]

namespace Unity.Entities
{
    /// <summary>
    /// Utility to look up equality and hashcode functions for component types.
    /// </summary>
    /// <remarks>The TypeManager uses this type internally and you shouldn't need to create or directly use
    /// this in the majority of use cases. When possible, a Burst-compilable implementation is preferred,
    /// but if an IEquatable{T} method isn't marked as Burst compiled, the managed
    /// version is used instead.</remarks>
    [BurstCompile]
    public partial struct FastEquality
    {

// While UNITY_DOTSRUNTIME not using Tiny BCL can compile most of this code, UnsafeUtility doesn't currently provide a
// FieldOffset method so we disable for UNITY_DOTSRUNTIME rather than NET_DOTS
#if !UNITY_DOTSRUNTIME
        static readonly ProfilerMarker ManagedEqualsMarker = new ProfilerMarker("FastEquality.ManagedEquals with IPropertyVisitor fallback (Missing IEquatable interface)");

        internal static TypeInfo CreateTypeInfo<T>() where T : struct
        {
            if (TypeUsesDelegates(typeof(T)))
                return CreateManagedTypeInfo(typeof(T));
            else
                return CreateTypeInfoBlittable(typeof(T));
        }

        internal static TypeInfo CreateTypeInfo(Type type)
        {
            if (TypeUsesDelegates(type))
                return CreateManagedTypeInfo(type);
            else
                return CreateTypeInfoBlittable(type);
        }

        private struct Dummy : IEquatable<Dummy>
        {
            public bool Equals(Dummy other)
            {
                return true;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }

        private struct CompareImpl<T> where T : struct, IEquatable<T>
        {
            public static unsafe bool CompareFunc(void* lhs, void* rhs)
            {
                return UnsafeUtility.AsRef<T>(lhs).Equals(UnsafeUtility.AsRef<T>(rhs));
            }
        }

        private struct GetHashCodeImpl<T> where T : struct, IEquatable<T>
        {
            public static unsafe int GetHashCodeFunc(void* lhs)
            {
                return UnsafeUtility.AsRef<T>(lhs).GetHashCode();
            }
        }

        private struct ManagedCompareImpl<T> where T : IEquatable<T>
        {
            public static unsafe bool CompareFunc(object lhs, object rhs)
            {
                return ((T)lhs).Equals((T)rhs);
            }
        }

        private struct ManagedGetHashCodeImpl<T>
        {
            public static unsafe int GetHashCodeFunc(object val)
            {
                return ((T)val).GetHashCode();
            }
        }

        private unsafe static TypeInfo CreateManagedTypeInfo(Type t)
        {
            // ISharedComponentData Type must implement IEquatable<T>
            if (typeof(ISharedComponentData).IsAssignableFrom(t))
            {
                if (!typeof(IEquatable<>).MakeGenericType(t).IsAssignableFrom(t))
                {
                    throw new ArgumentException($"type {t} is a ISharedComponentData and has managed references, you must implement IEquatable<T>");
                }

                // Type must override GetHashCode()
                var ghcMethod = t.GetMethod(nameof(GetHashCode));
                if (ghcMethod.DeclaringType != t)
                {
                    throw new ArgumentException($"type {t} is a/has managed references or implements IEquatable<T>, you must also override GetHashCode()");
                }
            }

            MethodInfo equalsFn = null;
            MethodInfo getHashFn = null;

            if (t.IsClass)
            {
                if (typeof(IEquatable<>).MakeGenericType(t).IsAssignableFrom(t))
                    equalsFn = typeof(ManagedCompareImpl<>).MakeGenericType(t).GetMethod(nameof(ManagedCompareImpl<Dummy>.CompareFunc));

                var ghcMethod = t.GetMethod(nameof(GetHashCode));
                if (ghcMethod.DeclaringType == t)
                    getHashFn = typeof(ManagedGetHashCodeImpl<>).MakeGenericType(t).GetMethod(nameof(ManagedGetHashCodeImpl<Dummy>.GetHashCodeFunc));

                return new TypeInfo
                {
                    EqualFn = equalsFn != null ? Delegate.CreateDelegate(typeof(TypeInfo.ManagedCompareEqualDelegate), equalsFn) : null,
                    GetHashFn = getHashFn != null ? Delegate.CreateDelegate(typeof(TypeInfo.ManagedGetHashCodeDelegate), getHashFn) : null,
                };
            }
            else
            {
                equalsFn = typeof(CompareImpl<>).MakeGenericType(t).GetMethod(nameof(CompareImpl<Dummy>.CompareFunc));
                getHashFn = typeof(GetHashCodeImpl<>).MakeGenericType(t).GetMethod(nameof(GetHashCodeImpl<Dummy>.GetHashCodeFunc));

                return new TypeInfo
                {
                    EqualFn = Delegate.CreateDelegate(typeof(TypeInfo.CompareEqualDelegate), equalsFn),
                    GetHashFn = Delegate.CreateDelegate(typeof(TypeInfo.GetHashCodeDelegate), getHashFn),
                    TypeSize = (uint) UnsafeUtility.SizeOf(t)
                };
            }
        }

        private static TypeInfo CreateTypeInfoBlittable(Type type)
        {
            return new TypeInfo { TypeSize = (uint) UnsafeUtility.SizeOf(type)};
        }

#endif

        /// <summary>
        /// Type holding one component's Equals and GetHashCode implementations as well as the size
        /// of the underlying unmanaged component type
        /// </summary>
        public struct TypeInfo
        {
            /// <summary>
            /// Equals method delegate for comparing two component instances whose type is not known at compile time.
            /// </summary>
            public unsafe delegate bool CompareEqualDelegate(void* lhs, void* rhs);

            /// <summary>
            /// GetHashCode method delegate for hashing a component whose type is not known at compile time.
            /// </summary>
            public unsafe delegate int GetHashCodeDelegate(void* obj);

            /// <summary>
            /// Equals method delegate for comparing two managed components.
            /// </summary>
            public unsafe delegate bool ManagedCompareEqualDelegate(object lhs, object rhs);

            /// <summary>
            /// GetHashCode method delegate for hashing a managed component.
            /// </summary>
            public unsafe delegate int ManagedGetHashCodeDelegate(object obj);

            /// <summary>
            /// Size of an unmanaged component type.
            /// </summary>
            public uint TypeSize;

            /// <summary>
            /// Holds the Equals delegate to use when comparing two instances of a component.
            /// </summary>
            public Delegate EqualFn;

            /// <summary>
            /// Holds the GetHashCode delegate to use for a component.
            /// </summary>
            public Delegate GetHashFn;

            /// <summary>
            /// Represents an invalid TypeInfo instance.
            /// </summary>
            public static TypeInfo Null => new TypeInfo();
        }


        private const int FNV_32_PRIME = 0x01000193;

#if !UNITY_DOTSRUNTIME

        /// <summary>
        /// Returns the hash code for a managed component. Internally, this is exclusively used for managed shared
        /// components.
        /// </summary>
        /// <param name="lhs">Boxed managed component.</param>
        /// <param name="typeInfo">TypeInfo to provide the GetHashCode method to invoke.</param>
        /// <returns>Hash code of the managed component</returns>
        public static unsafe int ManagedGetHashCode(object lhs, TypeInfo typeInfo)
        {
            var fn = (TypeInfo.ManagedGetHashCodeDelegate)typeInfo.GetHashFn;
            if (fn != null)
                return fn(lhs);

            var hash = 0;
            using (var buffer = new UnsafeAppendBuffer(16, 16, Allocator.Temp))
            {
                var writer = new ManagedObjectBinaryWriter(&buffer);
                writer.WriteObject(lhs);

                hash = Hash32(buffer.Ptr, (uint) buffer.Length);

                foreach (var obj in writer.GetUnityObjects())
                {
                    hash *= FNV_32_PRIME;
                    hash ^= obj.GetHashCode();
                }
            }

            return hash;
        }
#endif

        internal static unsafe int GetHashCode<T>(T lhs) where T : struct
        {
            return Hash32((byte*)UnsafeUtility.AddressOf(ref lhs), (uint) UnsafeUtility.SizeOf<T>());
        }

        /// <summary>
        /// Returns the hash code for an unmanaged component.
        /// </summary>
        /// <typeparam name="T">Type of unmanaged component.</typeparam>
        /// <param name="lhs">Component to hash.</param>
        /// <param name="typeInfo">TypeInfo to provide the GetHashCode method to invoke.</param>
        /// <returns>Hash code of the component</returns>
        public static unsafe int GetHashCode<T>(T lhs, TypeInfo typeInfo) where T : struct
        {
            return GetHashCode(UnsafeUtility.AddressOf(ref lhs), typeInfo);
        }

        /// <summary>
        /// Returns the hash code for an unmanaged component.
        /// </summary>
        /// <typeparam name="T">Type of unmanaged component.</typeparam>
        /// <param name="lhs">Component to hash.</param>
        /// <param name="typeInfo">TypeInfo to provide the GetHashCode method to invoke</param>
        /// <returns>Hash code of the component</returns>
        public static unsafe int GetHashCode<T>(ref T lhs, TypeInfo typeInfo) where T : struct
        {
            return GetHashCode(UnsafeUtility.AddressOf(ref lhs), typeInfo);
        }

        /// <summary>
        /// Returns the hash code for an unmanaged component.
        /// </summary>
        /// <param name="dataPtr">Pointer to component.</param>
        /// <param name="typeInfo">TypeInfo to provide the GetHashCode method to invoke.</param>
        /// <returns>Hash code of the component</returns>
        public static unsafe int GetHashCode(void* dataPtr, TypeInfo typeInfo)
        {
            if (typeInfo.GetHashFn != null)
            {
                TypeInfo.GetHashCodeDelegate fn = (TypeInfo.GetHashCodeDelegate)typeInfo.GetHashFn;
                return fn(dataPtr);
            }

            return Hash32((byte*) dataPtr, typeInfo.TypeSize);
        }

        [BurstCompile]
        internal static unsafe int Hash32(byte* input, uint len)
        {
            int hash = 0;
            for (int i = 0; i < len; ++i)
            {
                hash *= FNV_32_PRIME;
                hash ^= input[i];
            }

            return hash;
        }

#if !UNITY_DOTSRUNTIME

        /// <summary>
        /// Compares two managed component types.
        /// </summary>
        /// <param name="lhs">Component on the left-side of the comparison.</param>
        /// <param name="rhs">Component on the right-side of the comparison.</param>
        /// <param name="typeInfo">TypeInfo to provide the Equals method to invoke.</param>
        /// <returns>Returns true if the components are equal. False otherwise.</returns>
        public static unsafe bool ManagedEquals(object lhs, object rhs, TypeInfo typeInfo)
        {
            var fn = (TypeInfo.ManagedCompareEqualDelegate)typeInfo.EqualFn;

            if (fn != null)
                return fn(lhs, rhs);

            using (ManagedEqualsMarker.Auto())
            {
                return new ManagedObjectEqual().CompareEqual(lhs, rhs);
            }
        }
#endif

        internal static unsafe bool Equals<T>(T lhs, T rhs) where T : struct
        {
            return UnsafeUtility.MemCmp(UnsafeUtility.AddressOf(ref lhs), UnsafeUtility.AddressOf(ref rhs), UnsafeUtility.SizeOf<T>()) == 0;
        }

        internal static unsafe bool Equals(void* lhs, void* rhs, int typeSize)
        {
            return UnsafeUtility.MemCmp(lhs, rhs, typeSize) == 0;
        }

        /// <summary>
        /// Compares two component types.
        /// </summary>
        /// <typeparam name="T">Type of component.</typeparam>
        /// <param name="lhs">Component on the left-side of the comparison.</param>
        /// <param name="rhs">Component on the right-side of the comparison.</param>
        /// <param name="typeInfo">TypeInfo to provide the Equals method to invoke.</param>
        /// <returns>Returns true if the components are equal. False otherwise.</returns>
        public static unsafe bool Equals<T>(T lhs, T rhs, TypeInfo typeInfo) where T : struct
        {
            return Equals(UnsafeUtility.AddressOf(ref lhs), UnsafeUtility.AddressOf(ref rhs), typeInfo);
        }

        /// <summary>
        /// Compares two component types.
        /// </summary>
        /// <typeparam name="T">Type of component.</typeparam>
        /// <param name="lhs">Component on the left-side of the comparison.</param>
        /// <param name="rhs">Component on the right-side of the comparison.</param>
        /// <param name="typeInfo">TypeInfo to provide the Equals method to invoke.</param>
        /// <returns>Returns true if the components are equal. False otherwise.</returns>
        public static unsafe bool Equals<T>(ref T lhs, ref T rhs, TypeInfo typeInfo) where T : struct
        {
            return Equals(UnsafeUtility.AddressOf(ref lhs), UnsafeUtility.AddressOf(ref rhs), typeInfo);
        }

        /// <summary>
        /// Compares two component types.
        /// </summary>
        /// <param name="lhsPtr">Pointer to the component on the left-side of the comparison.</param>
        /// <param name="rhsPtr">Pointer to the component on the right-side of the comparison.</param>
        /// <param name="typeInfo">TypeInfo to provide the Equals method to invoke.</param>
        /// <returns>Returns true if the components are equal. False otherwise.</returns>
        public static unsafe bool Equals(void* lhsPtr, void* rhsPtr, TypeInfo typeInfo)
        {
#if !UNITY_DOTSRUNTIME
            if (typeInfo.EqualFn != null)
            {
                var fn = (TypeInfo.CompareEqualDelegate)typeInfo.EqualFn;
                return fn(lhsPtr, rhsPtr);
            }
#endif

            return Equals(lhsPtr, rhsPtr, (int) typeInfo.TypeSize);
        }

#if !UNITY_DOTSRUNTIME
        private static bool TypeUsesDelegates(Type t)
        {
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            // We have custom delegates to allow for class IComponentData comparisons
            // but any other non-value type should be ignored
            if (t.IsClass && typeof(IComponentData).IsAssignableFrom(t))
                return true;
#endif
            if (!t.IsValueType)
                return false;

            // Things with managed references must use delegate comparison.
            if (!UnsafeUtility.IsUnmanaged(t))
                return true;

            return typeof(IEquatable<>).MakeGenericType(t).IsAssignableFrom(t);
        }

        /// <summary>
        /// Internal method used for populating closed forms of generic equality methods to ensure IL2CPP
        /// can generate transpiled implementations and ensure they are not-stripped from builds.
        /// </summary>
        /// <param name="type">Type to close</param>
        /// <param name="output">Set of fully qualified closed generic type names to preserve</param>
        public static void AddExtraAOTTypes(Type type, HashSet<String> output)
        {
            if (!TypeUsesDelegates(type))
                return;

            if (type.IsClass)
            {
                if (typeof(IEquatable<>).MakeGenericType(type).IsAssignableFrom(type))
                    output.Add(typeof(ManagedCompareImpl<>).MakeGenericType(type).ToString());

                var ghcMethod = type.GetMethod(nameof(GetHashCode));
                if (ghcMethod.DeclaringType == type)
                    output.Add(typeof(ManagedGetHashCodeImpl<>).MakeGenericType(type).ToString());
            }
            else
            {
                output.Add(typeof(CompareImpl<>).MakeGenericType(type).ToString());
                output.Add(typeof(GetHashCodeImpl<>).MakeGenericType(type).ToString());
            }
        }

#endif
    }

    /// <summary>
    /// Provides fast equality and hash code computation, compatible with Burst, for unmanaged types implementing the IEquatable{T} interface
    /// </summary>
    /// <remarks>
    /// The public user based APIs are exposed through <see cref="TypeManager.EqualsWithBurst"/> and <see cref="TypeManager.GetHashCodeWithBurst"/>.
    /// </remarks>
    public struct BurstEquality
    {
        private struct Dummy : IEquatable<Dummy>
        {
            public bool Equals(Dummy other)
            {
                return true;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }

        [BurstCompile]
        private struct CompareImpl<T> where T : struct, IEquatable<T>
        {
            [BurstCompile(CompileSynchronously = true)]
            public static unsafe bool CompareFunc(void* lhs, void* rhs)
            {
                return UnsafeUtility.AsRef<T>(lhs).Equals(UnsafeUtility.AsRef<T>(rhs));
            }
        }

        [BurstCompile]
        private struct GetHashCodeImpl<T> where T : struct, IEquatable<T>
        {
            [BurstCompile(CompileSynchronously = true)]
            public static unsafe int GetHashCodeFunc(void* lhs)
            {
                return UnsafeUtility.AsRef<T>(lhs).GetHashCode();
            }
        }

        /// <summary>
        /// Delegate to use when Burst compiling a component's Equals implementation.
        /// </summary>
        public unsafe delegate bool CompareEqualDelegate(void* lhs, void* rhs);

        /// <summary>
        /// Delegate to use when Burst compiling a component's GetHashCode implementation.
        /// </summary>
        public unsafe delegate int GetHashCodeDelegate(void* lhs);


        /// <summary>
        /// Wrapper type holding a component's Equals and GetHashCode implementations.
        /// </summary>
        public readonly struct TypeInfo
        {
            /// <summary>
            /// Equals method delegate.
            /// </summary>
            public readonly CompareEqualDelegate CompareEqualMono;

            /// <summary>
            /// GetHashCode method delegate.
            /// </summary>
            public readonly GetHashCodeDelegate GetHashCodeMono;

            /// <summary>
            /// Internal. Used by codegen.
            /// </summary>
            public readonly struct BurstOnly
            {
                /// <summary>
                /// Internal. Used by codegen.
                /// </summary>
                public bool NotDefault { get; }

                [BurstDiscard]
                private static void CheckDelegate(ref bool useDelegate)
                {
                    //@TODO: This should use BurstCompiler.IsEnabled once that is available as an efficient API.
                    useDelegate = true;
                }

                /// <summary>
                /// Used to determine if we should use the Burst compiled delegate or use the managed delegate.
                /// </summary>
                /// <returns>Returns true if we should use the managed delegate.</returns>
                public static bool UseDelegate()
                {
                    bool result = false;
                    CheckDelegate(ref result);
                    return result;
                }

                /// <summary>
                /// Burst function pointer for Equals()
                /// </summary>
                public readonly FunctionPointer<CompareEqualDelegate> CompareEqualBurst;
                /// <summary>
                /// Burst function pointer for GetHashCode()
                /// </summary>
                public readonly FunctionPointer<GetHashCodeDelegate> GetHashCodeBurst;

                /// <summary>
                /// Internal. Used by codegen.
                /// </summary>
                /// <param name="equalBurstFunction">Equals Burst function pointer</param>
                /// <param name="getHashBurstFunction">GetHashCode Burst function pointer</param>
                public BurstOnly(FunctionPointer<CompareEqualDelegate> equalBurstFunction, FunctionPointer<GetHashCodeDelegate> getHashBurstFunction)
                {
                    NotDefault = true;
                    CompareEqualBurst = equalBurstFunction;
                    GetHashCodeBurst = getHashBurstFunction;
                }
            }

            private readonly BurstOnly _burst;

            /// <summary>
            /// Internal. Used by codegen.
            /// </summary>
            public BurstOnly Burst => _burst;

            /// <summary>
            /// Internal. Used by codegen.
            /// </summary>
            /// <param name="equalDel">Equals delegate</param>
            /// <param name="getHashDel">GetHashCode delegate</param>
            /// <param name="equalBurstFunction">Equals Burst function pointer</param>
            /// <param name="getHashBurstFunction">GetHashCode Burst function pointer</param>
            public TypeInfo(CompareEqualDelegate equalDel, GetHashCodeDelegate getHashDel, FunctionPointer<CompareEqualDelegate> equalBurstFunction, FunctionPointer<GetHashCodeDelegate> getHashBurstFunction)
            {
                CompareEqualMono = equalDel;
                GetHashCodeMono = getHashDel;
                _burst = new BurstOnly(equalBurstFunction, getHashBurstFunction);
            }
        }

#if !UNITY_DOTSRUNTIME
        /// <summary>
        /// Creates TypeInfo for a given component type.
        /// </summary>
        /// <param name="type">Component type.</param>
        /// <param name="typeInfo">Output TypeInfo.</param>
        /// <returns>Returns true if equality info can be generated. False if the component's equality methods cannot be
        /// burst compiled (or the type itself isn't Burst compatible).</returns>
        public static bool Create(Type type, out TypeInfo typeInfo)
        {
            // Burst equality works only for unmanaged types implementing the IEquatable<T> and ISharedComponentData interfaces
            if (UnsafeUtility.IsUnmanaged(type) == false ||
                typeof(IEquatable<>).MakeGenericType(type).IsAssignableFrom(type) == false ||
                typeof(ISharedComponentData).IsAssignableFrom(type) == false)
            {
                typeInfo = default;
                return false;
            }

            var equalsFn = typeof(CompareImpl<>).MakeGenericType(type).GetMethod(nameof(CompareImpl<Dummy>.CompareFunc));
            var equalDel = (CompareEqualDelegate) Delegate.CreateDelegate(typeof(CompareEqualDelegate), equalsFn);
            var equalBurstFunction = BurstCompiler.CompileFunctionPointer(equalDel);

            var getHashFn = typeof(GetHashCodeImpl<>).MakeGenericType(type).GetMethod(nameof(GetHashCodeImpl<Dummy>.GetHashCodeFunc));
            var getHashDel = (GetHashCodeDelegate) Delegate.CreateDelegate(typeof(GetHashCodeDelegate), getHashFn);
            var getHashBurstFunction = BurstCompiler.CompileFunctionPointer(getHashDel);

            typeInfo = new TypeInfo(equalDel, getHashDel, equalBurstFunction, getHashBurstFunction);
            return true;
        }
#endif
    }
}
