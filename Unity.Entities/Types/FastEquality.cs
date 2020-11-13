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

[assembly: InternalsVisibleTo("Unity.Entities.Tests")]

namespace Unity.Entities
{
    [BurstCompile]
    [GenerateBurstMonoInterop("FastEquality")]
    public partial struct FastEquality
    {

// While UNITY_DOTSRUNTIME not using Tiny BCL can compile most of this code, UnsafeUtility doesn't currently provide a
// FieldOffset method so we disable for UNITY_DOTSRUNTIME rather than NET_DOTS
#if !UNITY_DOTSRUNTIME
        [Obsolete("FastEquality.Layout is deprecated and will be removed as component comparisons no longer require it. (RemovedAfter 2021-01-12).", true)]
        public struct Layout
        {
            public int offset;
            public int count;
            public bool Aligned4;

            public override string ToString()
            {
                return $"offset: {offset} count: {count} Aligned4: {Aligned4}";
            }
        }

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

        public struct TypeInfo
        {
            public unsafe delegate bool CompareEqualDelegate(void* lhs, void* rhs);
            public unsafe delegate int GetHashCodeDelegate(void* obj);
            public unsafe delegate bool ManagedCompareEqualDelegate(object lhs, object rhs);
            public unsafe delegate int ManagedGetHashCodeDelegate(object obj);

            public uint TypeSize;
            public Delegate EqualFn;
            public Delegate GetHashFn;

            public static TypeInfo Null => new TypeInfo();
        }

        private const int FNV_32_PRIME = 0x01000193;

#if !UNITY_DOTSRUNTIME
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

        public static unsafe int GetHashCode<T>(T lhs, TypeInfo typeInfo) where T : struct
        {
            return GetHashCode(UnsafeUtility.AddressOf(ref lhs), typeInfo);
        }

        public static unsafe int GetHashCode<T>(ref T lhs, TypeInfo typeInfo) where T : struct
        {
            return GetHashCode(UnsafeUtility.AddressOf(ref lhs), typeInfo);
        }

        public static unsafe int GetHashCode(void* dataPtr, TypeInfo typeInfo)
        {
            if (typeInfo.GetHashFn != null)
            {
                TypeInfo.GetHashCodeDelegate fn = (TypeInfo.GetHashCodeDelegate)typeInfo.GetHashFn;
                return fn(dataPtr);
            }

            return Hash32((byte*) dataPtr, typeInfo.TypeSize);
        }

        static FastEquality()
        {
            Initialize();
        }

        [BurstMonoInteropMethod]
        private static unsafe int _Hash32(byte* input, uint len)
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
        public static unsafe bool ManagedEquals(object lhs, object rhs, TypeInfo typeInfo)
        {
            var fn = (TypeInfo.ManagedCompareEqualDelegate)typeInfo.EqualFn;

            if (fn != null)
                return fn(lhs, rhs);

            return new ManagedObjectEqual().CompareEqual(lhs, rhs);
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

        public static unsafe bool Equals<T>(T lhs, T rhs, TypeInfo typeInfo) where T : struct
        {
            return Equals(UnsafeUtility.AddressOf(ref lhs), UnsafeUtility.AddressOf(ref rhs), typeInfo);
        }

        public static unsafe bool Equals<T>(ref T lhs, ref T rhs, TypeInfo typeInfo) where T : struct
        {
            return Equals(UnsafeUtility.AddressOf(ref lhs), UnsafeUtility.AddressOf(ref rhs), typeInfo);
        }

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
}
