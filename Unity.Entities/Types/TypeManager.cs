using System;
using System.Collections.Generic;
using System.Diagnostics;
#if !NET_DOTS
using System.Linq;
#endif
using Unity.Burst;
using System.Reflection;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Profiling;
using Unity.Core;
using System.Threading;

namespace Unity.Entities
{
    /// <summary>
    /// [WriteGroup] Can exclude components which are unknown at the time of creating the query that have been declared
    /// to write to the same component.
    ///
    /// This allows for extending systems of components safely without editing the previously existing systems.
    ///
    /// The goal is to have a way for systems that expect to transform data from one set of components (inputs) to
    /// another (output[s]) be able to declare that explicit transform, and they exclusively know about one set of
    /// inputs. If there are other inputs that want to write to the same output, the query shouldn't match because it's
    /// a nonsensical/unhandled setup. It's both a way to guard against nonsensical components (having two systems write
    /// to the same output value), and a way to "turn off" existing systems/queries by putting a component with the same
    /// write lock on an entity, letting another system handle it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public class WriteGroupAttribute : Attribute
    {
        public WriteGroupAttribute(Type targetType)
        {
            TargetType = targetType;
        }

        public Type TargetType;
    }

    /// <summary>
    /// [DisableAutoTypeRegistration] prevents a Component Type from being registered in the TypeManager
    /// during TypeManager.Initialize(). Types that are not registered will not be recognized by EntityManager.
    /// </summary>
    public class DisableAutoTypeRegistration : Attribute
    {
    }

    public static unsafe partial class TypeManager
    {
        [AttributeUsage(AttributeTargets.Struct)]
        public class ForcedMemoryOrderingAttribute : Attribute
        {
            public ForcedMemoryOrderingAttribute(ulong ordering)
            {
                MemoryOrdering = ordering;
            }

            public ulong MemoryOrdering;
        }

        [AttributeUsage(AttributeTargets.Struct)]
        public class TypeVersionAttribute : Attribute
        {
            public TypeVersionAttribute(int version)
            {
                TypeVersion = version;
            }

            public int TypeVersion;
        }

        public enum TypeCategory
        {
            /// <summary>
            /// Implements IComponentData (can be either a struct or a class)
            /// </summary>
            ComponentData,
            /// <summary>
            /// Implements IBufferElementData (struct only)
            /// </summary>
            BufferData,
            /// <summary>
            /// Implement ISharedComponentData (struct only)
            /// </summary>
            ISharedComponentData,
            /// <summary>
            /// Is an Entity
            /// </summary>
            EntityData,
            /// <summary>
            /// Inherits from UnityEngine.Object (class only)
            /// </summary>
            UnityEngineObject,
            [Obsolete("TypeCategory.Class is deprecated. Please use TypeCategory.UnityEngineObject (RemovedAfter 2020-11-14) (UnityUpgradable) -> UnityEngineObject", true)]
            Class = UnityEngineObject
        }

        public const int HasNoEntityReferencesFlag = 1 << 24; // this flag is inverted to ensure the type id of Entity can still be 1
        public const int SystemStateTypeFlag = 1 << 25;
        public const int BufferComponentTypeFlag = 1 << 26;
        public const int SharedComponentTypeFlag = 1 << 27;
        public const int ManagedComponentTypeFlag = 1 << 28;
        public const int ChunkComponentTypeFlag = 1 << 29;
        public const int ZeroSizeInChunkTypeFlag = 1 << 30; // TODO: If we can ensure TypeIndex is unsigned we can use the top bit for this

        public const int ClearFlagsMask = 0x00FFFFFF;
        public const int SystemStateSharedComponentTypeFlag = SystemStateTypeFlag | SharedComponentTypeFlag;
        public const int ManagedSharedComponentTypeFlag = ManagedComponentTypeFlag | SharedComponentTypeFlag;

        public const int MaximumChunkCapacity = int.MaxValue;
        public const int MaximumSupportedAlignment = 16;
        public const int MaximumTypesCount = 1024 * 10;
        /// <summary>
        /// BufferCapacity is by default calculated as DefaultBufferCapacityNumerator / sizeof(BufferElementDataType)
        /// thus for a 1 byte component, the maximum number of elements possible to be stored in chunk memory before
        /// the buffer is allocated separately from chunk data, is DefaultBufferCapacityNumerator elements.
        /// For a 2 byte sized component, (DefaultBufferCapacityNumerator / 2) elements can be stored, etc...
        /// </summary>
        public const int DefaultBufferCapacityNumerator = 128;

        const int                           kInitialTypeCount = 2; // one for 'null' and one for 'Entity'
        static int                          s_TypeCount;
        static bool                         s_Initialized;
        static NativeArray<TypeInfo>        s_TypeInfos;
        static NativeHashMap<ulong, int>    s_StableTypeHashToTypeIndex;
        static NativeList<EntityOffsetInfo> s_EntityOffsetList;
        static NativeList<EntityOffsetInfo> s_BlobAssetRefOffsetList;
        static NativeList<int>              s_WriteGroupList;
        static List<FastEquality.TypeInfo>  s_FastEqualityTypeInfoList;
        static List<Type>                   s_Types;
        static List<string>                 s_TypeNames;
        public static IEnumerable<TypeInfo> AllTypes { get { return s_TypeInfos.GetSubArray(0, s_TypeCount); } }

#if !UNITY_DOTSRUNTIME
        static bool                         s_AppDomainUnloadRegistered;
        static Dictionary<Type, int>        s_ManagedTypeToIndex;
        static Dictionary<Type, Exception>  s_FailedTypeBuildException;
        public static int                   ObjectOffset;
        internal static Type                UnityEngineObjectType;
        internal static Type                GameObjectEntityType;


        // TODO: this creates a dependency on UnityEngine, but makes splitting code in separate assemblies easier. We need to remove it during the biggere refactor.
        struct ObjectOffsetType
        {
            void* v0;
            // Object layout in CoreCLR is different than in Mono or IL2CPP, as it has only one
            // pointer field as the object header. It is probably a bad idea to depend on VM internal
            // like this at all.
        #if !ENABLE_CORECLR
            void* v1;
        #endif
        }


        public static void RegisterUnityEngineObjectType(Type type)
        {
            if (type == null || !type.IsClass || type.IsInterface || type.FullName != "UnityEngine.Object")
                throw new ArgumentException($"{type} must be typeof(UnityEngine.Object).");
            UnityEngineObjectType = type;
        }

#endif

        public static TypeInfo[] GetAllTypes()
        {
            var res = new TypeInfo[s_TypeCount];

            for (var i = 0; i < s_TypeCount; i++)
            {
                res[i] = s_TypeInfos[i];
            }

            return res;
        }

        public struct EntityOffsetInfo
        {
            public int Offset;
        }

        public readonly struct TypeInfo
        {
            public TypeInfo(int typeIndex, TypeCategory category, int entityOffsetCount, int entityOffsetStartIndex,
                            ulong memoryOrdering, ulong stableTypeHash, int bufferCapacity, int sizeInChunk, int elementSize,
                            int alignmentInBytes, int maximumChunkCapacity, int writeGroupCount, int writeGroupStartIndex,
                            bool hasBlobRefs, int blobAssetRefOffsetCount, int blobAssetRefOffsetStartIndex, int fastEqualityIndex, int typeSize)
            {
                TypeIndex = typeIndex;
                Category = category;
                EntityOffsetCount = entityOffsetCount;
                EntityOffsetStartIndex = entityOffsetStartIndex;
                MemoryOrdering = memoryOrdering;
                StableTypeHash = stableTypeHash;
                BufferCapacity = bufferCapacity;
                SizeInChunk = sizeInChunk;
                ElementSize = elementSize;
                AlignmentInBytes = alignmentInBytes;
                MaximumChunkCapacity = maximumChunkCapacity;
                WriteGroupCount = writeGroupCount;
                WriteGroupStartIndex = writeGroupStartIndex;
                _HasBlobAssetRefs = hasBlobRefs ? 1 : 0;
                BlobAssetRefOffsetCount = blobAssetRefOffsetCount;
                BlobAssetRefOffsetStartIndex = blobAssetRefOffsetStartIndex;
                FastEqualityIndex = fastEqualityIndex; // Only used for Hybrid types (should be removed once we code gen all equality cases)
                TypeSize = typeSize;
            }

            public readonly int TypeIndex;
            // Note that this includes internal capacity and header overhead for buffers.
            public readonly int SizeInChunk;
            // Sometimes we need to know not only the size, but the alignment.  For buffers this is the alignment
            // of an individual element.
            public readonly int AlignmentInBytes;
            // Alignment of this type in a chunk.  Normally the same
            // as AlignmentInBytes, but that might be less than this
            // for buffer elements, whereas the buffer itself must
            // be aligned to the maximum.
            public int AlignmentInChunkInBytes
            {
                get
                {
                    if (Category == TypeCategory.BufferData)
                        return MaximumSupportedAlignment;
                    return AlignmentInBytes;
                }
            }
            // Normally the same as SizeInChunk (for components), but for buffers means size of an individual element.
            public readonly int          ElementSize;
            public readonly int          BufferCapacity;
            public readonly TypeCategory Category;
            public readonly ulong        MemoryOrdering;
            public readonly ulong        StableTypeHash;
            public readonly int          EntityOffsetCount;
            internal readonly int        EntityOffsetStartIndex;
            readonly int                 _HasBlobAssetRefs;
            public   readonly int        BlobAssetRefOffsetCount;
            internal readonly int        BlobAssetRefOffsetStartIndex;
            public   readonly int        WriteGroupCount;
            internal readonly int        WriteGroupStartIndex;
            public   readonly int        MaximumChunkCapacity;
            internal readonly int        FastEqualityIndex;
            public   readonly int        TypeSize;

            public bool IsZeroSized => SizeInChunk == 0;
            public bool HasWriteGroups => WriteGroupCount > 0;
            /// <summary>
            /// For struct IComponentData this gurantees that there are blob asset refs
            /// For class based IComponentData it is possible that there are blob asset references. (Polymorphic referenced can not be proven statically)
            /// </summary>
            public bool HasBlobAssetRefs => _HasBlobAssetRefs != 0;

            // NOTE: We explicitly exclude Type as a member of TypeInfo so the type can remain a ValueType
            public Type Type => TypeManager.GetType(TypeIndex);

            public string DebugTypeName
            {
                get
                {
#if !UNITY_DOTSRUNTIME
                    if (Type != null)
                        return Type.FullName;
                    else
                        return "<unavailable>";
#else
                    int index = TypeIndex & ClearFlagsMask;
                    if (index < s_TypeNames.Count)
                        return s_TypeNames[index];
                    else
                        return $"StableTypeHash: {StableTypeHash}";
#endif
                }
            }
        }

        internal static EntityOffsetInfo* GetEntityOffsetsPointer()
        {
            return (EntityOffsetInfo*)SharedEntityOffsetInfo.Ref.Data;
        }

        internal static EntityOffsetInfo* GetEntityOffsets(in TypeInfo typeInfo)
        {
            return GetEntityOffsetsPointer() + typeInfo.EntityOffsetStartIndex;
        }

        /// <summary>
        /// Note this function will always return a pointer even if the given type has
        /// no Entity offsets. Always check/iterate over the returned pointer using the
        /// returned count
        /// </summary>
        /// <param name="typeIndex"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static EntityOffsetInfo* GetEntityOffsets(int typeIndex, out int count)
        {
            var typeInfo = GetTypeInfoPointer() + (typeIndex & ClearFlagsMask);
            count = typeInfo->EntityOffsetCount;
            return GetEntityOffsets(*typeInfo);
        }

        internal static EntityOffsetInfo* GetBlobAssetRefOffsetsPointer()
        {
            return (EntityOffsetInfo*)SharedBlobAssetRefOffset.Ref.Data;
        }

        // Note this function will always return a pointer even if the given type has
        // no BlobAssetReference offsets. Always check/iterate the returned pointer
        // against the TypeInfo.BlobAssetReferenceCount
        internal static EntityOffsetInfo* GetBlobAssetRefOffsets(in TypeInfo typeInfo)
        {
            return GetBlobAssetRefOffsetsPointer() + typeInfo.BlobAssetRefOffsetStartIndex;
        }

        internal static int* GetWriteGroupsPointer()
        {
            return (int*)SharedWriteGroup.Ref.Data;
        }

        internal static int* GetWriteGroups(in TypeInfo typeInfo)
        {
            if (typeInfo.WriteGroupCount == 0)
                return null;

            return GetWriteGroupsPointer() + typeInfo.WriteGroupStartIndex;
        }

        public static ref readonly TypeInfo GetTypeInfo(int typeIndex)
        {
            return ref GetTypeInfoPointer()[typeIndex & ClearFlagsMask];
        }

        public static ref readonly TypeInfo GetTypeInfo<T>()
        {
            return ref GetTypeInfoPointer()[GetTypeIndex<T>() & ClearFlagsMask];
        }

        internal static TypeInfo * GetTypeInfoPointer()
        {
            return (TypeInfo*)SharedTypeInfo.Ref.Data;
        }

        public static Type GetType(int typeIndex)
        {
            var typeIndexNoFlags = typeIndex & ClearFlagsMask;
            Assert.IsTrue(typeIndexNoFlags >= 0 && typeIndexNoFlags < s_Types.Count);
            return s_Types[typeIndexNoFlags];
        }

        public static int GetTypeCount()
        {
            return s_TypeCount;
        }

        public static FastEquality.TypeInfo GetFastEqualityTypeInfo(TypeInfo typeInfo)
        {
            return s_FastEqualityTypeInfoList[typeInfo.FastEqualityIndex];
        }

        public static bool IsBuffer(int typeIndex) => (typeIndex & BufferComponentTypeFlag) != 0;
        public static bool IsSystemStateComponent(int typeIndex) => (typeIndex & SystemStateTypeFlag) != 0;
        public static bool IsSystemStateSharedComponent(int typeIndex) => (typeIndex & SystemStateSharedComponentTypeFlag) == SystemStateSharedComponentTypeFlag;
        public static bool IsSharedComponentType(int typeIndex) => (typeIndex & SharedComponentTypeFlag) != 0;
        public static bool IsManagedComponent(int typeIndex) => (typeIndex & (ManagedComponentTypeFlag | ChunkComponentTypeFlag | SharedComponentTypeFlag)) == ManagedComponentTypeFlag;
        public static bool IsManagedSharedComponent(int typeIndex) => (typeIndex & ManagedSharedComponentTypeFlag) == ManagedSharedComponentTypeFlag;
        public static bool IsManagedType(int typeIndex) => (typeIndex & ManagedComponentTypeFlag) != 0;
        public static bool IsZeroSized(int typeIndex) => (typeIndex & ZeroSizeInChunkTypeFlag) != 0;
        public static bool IsChunkComponent(int typeIndex) => (typeIndex & ChunkComponentTypeFlag) != 0;
        public static bool HasEntityReferences(int typeIndex) => (typeIndex & HasNoEntityReferencesFlag) == 0;

        public static int MakeChunkComponentTypeIndex(int typeIndex) => (typeIndex | ChunkComponentTypeFlag | ZeroSizeInChunkTypeFlag);

        private static void AddTypeInfoToTables(Type type, TypeInfo typeInfo, string typeName)
        {
            if (!s_StableTypeHashToTypeIndex.TryAdd(typeInfo.StableTypeHash, typeInfo.TypeIndex))
            {
                int previousTypeIndex = s_StableTypeHashToTypeIndex[typeInfo.StableTypeHash] & ClearFlagsMask;
                throw new ArgumentException($"{type} and {s_Types[previousTypeIndex]} have a conflict in the stable type hash. Use the [TypeVersion(...)] attribute to force a different stable type hash for one of them.");
            }

            // Debug.Log($"{type} -> {typeInfo.StableTypeHash}");

            s_TypeInfos[typeInfo.TypeIndex & ClearFlagsMask] = typeInfo;
            s_Types.Add(type);
            s_TypeNames.Add(typeName);
            Assert.AreEqual(s_TypeCount, typeInfo.TypeIndex & ClearFlagsMask);
            s_TypeCount++;

#if !UNITY_DOTSRUNTIME
            if (type != null)
            {
                SharedTypeIndex.Get(type) = typeInfo.TypeIndex;
                s_ManagedTypeToIndex.Add(type, typeInfo.TypeIndex);
            }
#endif
        }

#if UNITY_EDITOR
        // Todo: Remove this once UnityEngine supports deterministically ordered [InitializeOnLoad] method invocations (likely sometime in 2021.x)
        // This function uses reflection to find a static BurstLoader property and calls it. The sole reason of this
        // is to force the BurstLoader's cctor to be invoked (accessing the property will trigger this if the cctor
        // hasn't been caleed already without risking us calling it directly potentially invoking the cctor more
        // than once). We do this to allow TypeManager to make use of BurstCompiled functions (which requires burst to
        // have been initialized before our first call to Burst.CompileFunctionPointer)
        static void InitializeBurst()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (assembly.GetName().Name == "Unity.Burst.Editor")
                {
                    var burstLoaderType = assembly.GetType("Unity.Burst.Editor.BurstLoader");
                    var isDebuggingProperty = burstLoaderType.GetProperty("IsDebugging");
                    var getMethod = isDebuggingProperty.GetGetMethod();
                    getMethod.Invoke(null, null);
                    return;
                }
            }
        }
#endif

        /// <summary>
        /// Initializes the TypeManager with all ECS type information. May be called multiple times; only the first call
        /// will do any work. Always must be called from the main thread.
        /// </summary>
        public static void Initialize()
        {
            if (s_Initialized)
                return;

#if UNITY_EDITOR
            if (!UnityEditorInternal.InternalEditorUtility.CurrentThreadIsMainThread())
                throw new InvalidOperationException("Must be called from the main thread");

            InitializeBurst();
#endif
            s_Initialized = true;

#if !UNITY_DOTSRUNTIME
            if (!s_AppDomainUnloadRegistered)
            {
                // important: this will always be called from a special unload thread (main thread will be blocking on this)
                AppDomain.CurrentDomain.DomainUnload += (_, __) =>
                {
                    if (s_Initialized)
                        Shutdown();
                };
                s_AppDomainUnloadRegistered = true;
            }

            ObjectOffset = UnsafeUtility.SizeOf<ObjectOffsetType>();
            s_ManagedTypeToIndex = new Dictionary<Type, int>(1000);
            s_FailedTypeBuildException = new Dictionary<Type, Exception>();
#endif

            s_TypeCount = 0;
            s_TypeInfos = new NativeArray<TypeInfo>(MaximumTypesCount, Allocator.Persistent);
            s_StableTypeHashToTypeIndex = new NativeHashMap<ulong, int>(MaximumTypesCount, Allocator.Persistent);
            s_EntityOffsetList = new NativeList<EntityOffsetInfo>(Allocator.Persistent);
            s_BlobAssetRefOffsetList = new NativeList<EntityOffsetInfo>(Allocator.Persistent);
            s_WriteGroupList = new NativeList<int>(Allocator.Persistent);
            s_FastEqualityTypeInfoList = new List<FastEquality.TypeInfo>();
            s_Types = new List<Type>();
            s_TypeNames = new List<string>();

            InitializeSystemsState();

            InitializeFieldInfoState();

            // There are some types that must be registered first such as a null component and Entity
            RegisterSpecialComponents();
            Assert.IsTrue(kInitialTypeCount == s_TypeCount);

#if !UNITY_DOTSRUNTIME
            InitializeAllComponentTypes();
#else
            // Registers all types and their static info from the static type registry
            RegisterStaticAssemblyTypes();
#endif

            // Must occur after we've constructed s_TypeInfos
            InitializeSharedStatics();
        }

        static void InitializeSharedStatics()
        {
            SharedTypeInfo.Ref.Data = new IntPtr(s_TypeInfos.GetUnsafePtr());
            SharedEntityOffsetInfo.Ref.Data = new IntPtr(s_EntityOffsetList.GetUnsafePtr());
            SharedBlobAssetRefOffset.Ref.Data = new IntPtr(s_BlobAssetRefOffsetList.GetUnsafePtr());

            SharedWriteGroup.Ref.Data = new IntPtr(s_WriteGroupList.GetUnsafePtr());
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            SharedWriteGroup.AtomicSafetyHandle = AtomicSafetyHandle.Create();
#endif
        }

        static void ShutdownSharedStatics()
        {
            SharedTypeInfo.Ref.Data = default;
            SharedEntityOffsetInfo.Ref.Data = default;
            SharedBlobAssetRefOffset.Ref.Data = default;

            SharedWriteGroup.Ref.Data = default;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(SharedWriteGroup.AtomicSafetyHandle);
#endif
        }

        static void RegisterSpecialComponents()
        {
            // Push Null TypeInfo -- index 0 is reserved for null/invalid in all arrays index by (TypeIndex & ClearFlagsMask)
            s_FastEqualityTypeInfoList.Add(FastEquality.TypeInfo.Null);
            AddTypeInfoToTables(null,
                new TypeInfo(0, TypeCategory.ComponentData, 0, -1,
                    0, 0, -1, 0, 0, 0,
                    int.MaxValue, 0, -1, false, 0,
                    -1, 0, 0),
                "Null");

            // Push Entity TypeInfo
            var entityTypeIndex = 1;
            ulong entityStableTypeHash;
            int entityFastEqIndex = -1;
#if !UNITY_DOTSRUNTIME
            entityStableTypeHash = TypeHash.CalculateStableTypeHash(typeof(Entity));
            entityFastEqIndex = s_FastEqualityTypeInfoList.Count;
            s_FastEqualityTypeInfoList.Add(FastEquality.CreateTypeInfo(typeof(Entity)));
#else
            entityStableTypeHash = GetEntityStableTypeHash();
#endif
            // Entity is special and is treated as having an entity offset at 0 (itself)
            s_EntityOffsetList.Add(new EntityOffsetInfo() { Offset = 0 });
            AddTypeInfoToTables(typeof(Entity),
                new TypeInfo(1, TypeCategory.EntityData, entityTypeIndex, 0,
                    0, entityStableTypeHash, -1, UnsafeUtility.SizeOf<Entity>(),
                    UnsafeUtility.SizeOf<Entity>(), CalculateAlignmentInChunk(sizeof(Entity)),
                    int.MaxValue, 0, -1, false, 0,
                    -1, entityFastEqIndex, UnsafeUtility.SizeOf<Entity>()),
                "Unity.Entities.Entity");

            SharedTypeIndex<Entity>.Ref.Data = entityTypeIndex;
        }

        /// <summary>
        /// Removes all ECS type information and any allocated memory. May only be called once globally, and must be
        /// called from the main thread.
        /// </summary>
        public static void Shutdown()
        {
            // TODO, with module loaded type info, we cannot shutdown
#if UNITY_EDITOR
            if (!UnityEditorInternal.InternalEditorUtility.CurrentThreadIsMainThread())
                throw new InvalidOperationException("Must be called from the main thread");
#endif

            if (!s_Initialized)
                return;

            s_Initialized = false;

            s_TypeCount = 0;
            s_FastEqualityTypeInfoList.Clear();
            s_Types.Clear();
            s_TypeNames.Clear();

            ShutdownSystemsState();
            ShutdownFieldInfoState();

#if !UNITY_DOTSRUNTIME
            s_FailedTypeBuildException = null;
            s_ManagedTypeToIndex.Clear();
#endif

            DisposeNative();

            ShutdownSharedStatics();
        }

        static void DisposeNative()
        {
            s_TypeInfos.Dispose();
            s_StableTypeHashToTypeIndex.Dispose();
            s_EntityOffsetList.Dispose();
            s_BlobAssetRefOffsetList.Dispose();
            s_WriteGroupList.Dispose();
        }

        private static int FindTypeIndex(Type type)
        {
#if !UNITY_DOTSRUNTIME
            if (type == null)
                return 0;

            int res;
            if (s_ManagedTypeToIndex.TryGetValue(type, out res))
                return res;
            else
                return -1;
#else
            // skip 0 since it is always null
            for (var i = 1; i < s_Types.Count; i++)
                if (type == s_Types[i])
                    return s_TypeInfos[i].TypeIndex;

            throw new ArgumentException("Tried to GetTypeIndex for type that has not been set up by the static type registry.");
#endif
        }

        [BurstDiscard]
        static void ManagedException<T>()
        {
            ManagedException(typeof(T));
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void BurstException<T>()
        {
            throw new ArgumentException($"Unknown Type:`{typeof(T)}` All ComponentType must be known at compile time & be successfully registered. For generic components, each concrete type must be registered with [RegisterGenericComponentType].");
        }

        static void ManagedException(Type type)
        {
#if !UNITY_DOTSRUNTIME
            s_FailedTypeBuildException.TryGetValue(type, out var exception);
            // When the type is known but failed to build, we repeat the reason why it failed to build instead.
            if (exception != null)
                throw new ArgumentException(exception.Message);
            // Otherwise it wasn't registered at all
            else
#endif
            throw new ArgumentException($"Unknown Type:`{type}` All ComponentType must be known at compile time. For generic components, each concrete type must be registered with [RegisterGenericComponentType].");
        }

        public static int GetTypeIndex<T>()
        {
            var index = SharedTypeIndex<T>.Ref.Data;

            if (index <= 0)
            {
                ManagedException<T>();
                BurstException<T>();
            }

            return index;
        }

        public static int GetTypeIndex(Type type)
        {
            var index = FindTypeIndex(type);

            if (index == -1)
                ManagedException(type);

            return index;
        }

        public static bool Equals<T>(ref T left, ref T right) where T : struct
        {
#if !UNITY_DOTSRUNTIME
            var typeIndex = GetTypeIndex<T>();
            if(IsSharedComponentType(typeIndex))
                return FastEquality.Equals(ref left, ref right, s_FastEqualityTypeInfoList[GetTypeInfo(typeIndex).FastEqualityIndex]);

            return FastEquality.Equals(left, right);
#else
            return UnsafeUtility.MemCmp(UnsafeUtility.AddressOf(ref left), UnsafeUtility.AddressOf(ref right), UnsafeUtility.SizeOf<T>()) == 0;
#endif
        }

        public static bool Equals(void* left, void* right, int typeIndex)
        {
#if !UNITY_DOTSRUNTIME
            var typeInfo = GetTypeInfo(typeIndex);
            if(IsSharedComponentType(typeIndex))
                return FastEquality.Equals(left, right, s_FastEqualityTypeInfoList[typeInfo.FastEqualityIndex]);

            return FastEquality.Equals(left, right, typeInfo.TypeSize);
#else
            var typeInfo = GetTypeInfo(typeIndex);
            return UnsafeUtility.MemCmp(left, right, typeInfo.TypeSize) == 0;
#endif
        }

        public static bool Equals(object left, object right, int typeIndex)
        {
#if !UNITY_DOTSRUNTIME
            if (left == null || right == null)
            {
                return left == right;
            }

            if (IsManagedComponent(typeIndex))
            {
                var typeInfo = s_FastEqualityTypeInfoList[GetTypeInfo(typeIndex).FastEqualityIndex];
                return FastEquality.ManagedEquals(left, right, typeInfo);
            }
            else
            {
                var leftptr = (byte*)UnsafeUtility.PinGCObjectAndGetAddress(left, out var lhandle) + ObjectOffset;
                var rightptr = (byte*)UnsafeUtility.PinGCObjectAndGetAddress(right, out var rhandle) + ObjectOffset;

                var result = Equals(leftptr, rightptr, typeIndex);

                UnsafeUtility.ReleaseGCObject(lhandle);
                UnsafeUtility.ReleaseGCObject(rhandle);
                return result;
            }
#else
            return GetBoxedEquals(left, right, typeIndex & ClearFlagsMask);
#endif
        }

        public static bool Equals(object left, void* right, int typeIndex)
        {
#if !UNITY_DOTSRUNTIME
            var leftptr = (byte*)UnsafeUtility.PinGCObjectAndGetAddress(left, out var lhandle) + ObjectOffset;

            var result = Equals(leftptr, right, typeIndex);

            UnsafeUtility.ReleaseGCObject(lhandle);
            return result;
#else
            return GetBoxedEquals(left, right, typeIndex & ClearFlagsMask);
#endif
        }

        public static int GetHashCode<T>(ref T val) where T : struct
        {
#if !UNITY_DOTSRUNTIME
            var typeIndex = GetTypeIndex<T>();
            if(IsSharedComponentType(typeIndex))
                return FastEquality.GetHashCode(val, s_FastEqualityTypeInfoList[GetTypeInfo(typeIndex).FastEqualityIndex]);

            return FastEquality.GetHashCode(val);
#else
            return (int)XXHash.Hash32((byte*)UnsafeUtility.AddressOf(ref val), UnsafeUtility.SizeOf<T>());
#endif
        }

        public static int GetHashCode(void* val, int typeIndex)
        {
#if !UNITY_DOTSRUNTIME
            var typeInfo = s_FastEqualityTypeInfoList[GetTypeInfo(typeIndex).FastEqualityIndex];
            return FastEquality.GetHashCode(val, typeInfo);
#else
            var typeInfo = GetTypeInfo(typeIndex);
            return (int)XXHash.Hash32((byte*)val, typeInfo.TypeSize);
#endif
        }

        public static int GetHashCode(object val, int typeIndex)
        {
#if !UNITY_DOTSRUNTIME
            if (IsManagedComponent(typeIndex))
            {
                var typeInfo = s_FastEqualityTypeInfoList[GetTypeInfo(typeIndex).FastEqualityIndex];
                return FastEquality.ManagedGetHashCode(val, typeInfo);
            }
            else
            {
                var ptr = (byte*)UnsafeUtility.PinGCObjectAndGetAddress(val, out var handle) + ObjectOffset;
                var result = GetHashCode(ptr, typeIndex);

                UnsafeUtility.ReleaseGCObject(handle);
                return result;
            }
#else
            return GetBoxedHashCode(val, typeIndex & ClearFlagsMask);
#endif
        }

        public static int GetTypeIndexFromStableTypeHash(ulong stableTypeHash)
        {
            if (s_StableTypeHashToTypeIndex.TryGetValue(stableTypeHash, out var typeIndex))
                return typeIndex;
            return -1;
        }

        public static object ConstructComponentFromBuffer(int typeIndex, void* data)
        {
#if !UNITY_DOTSRUNTIME
            var tinfo = GetTypeInfo(typeIndex);
            Type type = GetType(typeIndex);
            object obj = Activator.CreateInstance(type);
            unsafe
            {
                var ptr = UnsafeUtility.PinGCObjectAndGetAddress(obj, out var handle);
                UnsafeUtility.MemCpy(ptr, data, tinfo.SizeInChunk);
                UnsafeUtility.ReleaseGCObject(handle);
            }

            return obj;
#else
            return ConstructComponentFromBuffer(data, typeIndex & ClearFlagsMask);
#endif
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private static readonly Type[] s_SingularInterfaces =
        {
            typeof(IComponentData),
            typeof(IBufferElementData),
            typeof(ISharedComponentData),
        };

        internal static void CheckComponentType(Type type)
        {
            int typeCount = 0;
            foreach (Type t in s_SingularInterfaces)
            {
                if (t.IsAssignableFrom(type))
                    ++typeCount;
            }

            if (typeCount > 1)
                throw new ArgumentException($"Component {type} can only implement one of IComponentData, ISharedComponentData and IBufferElementData");
        }

#endif

        public static NativeArray<int> GetWriteGroupTypes(int typeIndex)
        {
            var type = GetTypeInfo(typeIndex);
            var writeGroups = GetWriteGroups(type);
            var writeGroupCount = type.WriteGroupCount;
            var arr = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(writeGroups, writeGroupCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref arr, SharedWriteGroup.AtomicSafetyHandle);
#endif
            return arr;
        }

        // TODO: Fix our wild alignment requirements for chunk memory (easier said than done)
        /// <summary>
        /// Our alignment calculations for types are taken from the perspective of the alignment of the type _specifically_ when
        /// stored in chunk memory. This means a type's natural alignment may not match the AlignmentInChunk value. Our current scheme is such that
        /// an alignment of 'MaximumSupportedAlignment' is assumed unless the size of the type is smaller than 'MaximumSupportedAlignment' and is a power of 2.
        /// In such cases we use the type size directly, thus if you have a type that naturally aligns to 4 bytes and has a size of 8, the AlignmentInChunk will be 8
        /// as long as 8 is less than 'MaximumSupportedAlignment'.
        /// </summary>
        /// <param name="sizeOfTypeInBytes"></param>
        /// <returns></returns>
        internal static int CalculateAlignmentInChunk(int sizeOfTypeInBytes)
        {
            int alignmentInBytes = MaximumSupportedAlignment;
            if (sizeOfTypeInBytes < alignmentInBytes && CollectionHelper.IsPowerOfTwo(sizeOfTypeInBytes))
                alignmentInBytes = sizeOfTypeInBytes;

            return alignmentInBytes;
        }

#if !UNITY_DOTSRUNTIME

        private static bool IsSupportedComponentType(Type type)
        {
            return typeof(IComponentData).IsAssignableFrom(type)
                || typeof(ISharedComponentData).IsAssignableFrom(type)
                || typeof(IBufferElementData).IsAssignableFrom(type);
        }

        static void AddUnityEngineObjectTypeToListIfSupported(HashSet<Type> componentTypeSet, Type type)
        {
            if (type == GameObjectEntityType)
                return;
            if (type.ContainsGenericParameters)
                return;
            if (type.IsAbstract)
                return;
            componentTypeSet.Add(type);
        }

        static bool IsInstantiableComponentType(Type type)
        {
            if (type.IsAbstract)
                return false;

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            if (!type.IsValueType && !typeof(IComponentData).IsAssignableFrom(type))
                return false;
#else
            if (!type.IsValueType && typeof(IComponentData).IsAssignableFrom(type))
                throw new ArgumentException($"Type '{type.FullName}' inherits from IComponentData but has been defined as a managed type. " +
                    $"Managed component support has been explicitly disabled via the 'UNITY_DISABLE_MANAGED_COMPONENTS' define. " +
                    $"Change the offending type to be a value type or re-enable managed component support.");

            if (!type.IsValueType)
                return false;
#endif

            // Don't register open generics here.  It's an open question
            // on whether we should support them for components at all,
            // as with them we can't ever see a full set of component types
            // in use.
            if (type.ContainsGenericParameters)
                return false;

            if (type.GetCustomAttribute(typeof(DisableAutoTypeRegistration)) != null)
                return false;

            return true;
        }
        static void AddComponentTypeToListIfSupported(HashSet<Type> typeSet, Type type)
        {
            // XXX There's a bug in the Unity Mono scripting backend where if the
            // Mono type hasn't been initialized, the IsUnmanaged result is wrong.
            // We force it to be fully initialized by creating an instance until
            // that bug is fixed.
            try
            {
                var inst = Activator.CreateInstance(type);
            }
            catch (Exception)
            {
                // ignored
            }

            typeSet.Add(type);
        }

        static void InitializeAllComponentTypes()
        {
#if UNITY_EDITOR
            var stopWatch = new Stopwatch();
            stopWatch.Start();
#endif
            try
            {
                Profiler.BeginSample("InitializeAllComponentTypes");
                var componentTypeSet = new HashSet<Type>();

                // Inject types needed for Hybrid
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in assemblies)
                {
                    if (assembly.GetName().Name == "Unity.Entities.Hybrid")
                        GameObjectEntityType = assembly.GetType("Unity.Entities.GameObjectEntity");
                }

                if (GameObjectEntityType == null)
                    throw new Exception("Required Unity.Entities.GameObjectEntity types not found.");

                UnityEngineObjectType = typeof(UnityEngine.Object);

#if UNITY_EDITOR && false
                foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom<UnityEngine.Object>())
                    AddUnityEngineObjectTypeToListIfSupported(componentTypeSet, type);
                foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom<IComponentData>())
                    AddComponentTypeToListIfSupported(componentTypeSet, type);
                foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom<IBufferElementData>())
                    AddComponentTypeToListIfSupported(componentTypeSet, type);
                foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom<ISharedComponentData>())
                    AddComponentTypeToListIfSupported(componentTypeSet, type);
#else
                foreach (var assembly in assemblies)
                {
                    IsAssemblyReferencingEntitiesOrUnityEngine(assembly, out var isAssemblyReferencingEntities,
                        out var isAssemblyReferencingUnityEngine);
                    var isAssemblyRelevant = isAssemblyReferencingEntities || isAssemblyReferencingUnityEngine;

                    if (!isAssemblyRelevant)
                        continue;

                    var assemblyTypes = assembly.GetTypes();

                    // Register UnityEngine types (Hybrid)
                    if (isAssemblyReferencingUnityEngine)
                    {
                        foreach (var type in assemblyTypes)
                        {
                            if (UnityEngineObjectType.IsAssignableFrom(type))
                                AddUnityEngineObjectTypeToListIfSupported(componentTypeSet, type);
                        }
                    }

                    // Register ComponentData types
                    if (isAssemblyReferencingEntities)
                    {
                        foreach (var type in assemblyTypes)
                        {
                            if (!IsInstantiableComponentType(type))
                                continue;

                            // XXX There's a bug in the Unity Mono scripting backend where if the
                            // Mono type hasn't been initialized, the IsUnmanaged result is wrong.
                            // We force it to be fully initialized by creating an instance until
                            // that bug is fixed.
                            try
                            {
                                var inst = Activator.CreateInstance(type);
                            }
                            catch (Exception)
                            {
                                // ignored
                            }

                            if (IsSupportedComponentType(type))
                                AddComponentTypeToListIfSupported(componentTypeSet, type);
                        }
                    }
                }
#endif

                // Register ComponentData concrete generics
                foreach (var assembly in assemblies)
                {
                    foreach (var registerGenericComponentTypeAttribute in assembly.GetCustomAttributes<RegisterGenericComponentTypeAttribute>())
                    {
                        var type = registerGenericComponentTypeAttribute.ConcreteType;

                        if (IsSupportedComponentType(type))
                            componentTypeSet.Add(type);
                    }
                }

                var componentTypeCount = componentTypeSet.Count;
                var componentTypes = new Type[componentTypeCount];
                componentTypeSet.CopyTo(componentTypes);

                var typeIndexByType = new Dictionary<Type, int>();
                var writeGroupByType = new Dictionary<int, HashSet<int>>();
                var startTypeIndex = s_TypeCount;

                for (int i = 0; i < componentTypes.Length; i++)
                {
                    typeIndexByType[componentTypes[i]] = startTypeIndex + i;
                }

                GatherWriteGroups(componentTypes, startTypeIndex, typeIndexByType, writeGroupByType);
                AddAllComponentTypes(componentTypes, startTypeIndex, writeGroupByType);
            }
            finally
            {
                Profiler.EndSample();
            }
#if UNITY_EDITOR
            // Save the time since profiler might not catch the first frame.
            stopWatch.Stop();
            Console.WriteLine($"TypeManager.Initialize took: {stopWatch.ElapsedMilliseconds}ms");
#endif
        }

        private static void AddAllComponentTypes(Type[] componentTypes, int startTypeIndex, Dictionary<int, HashSet<int>> writeGroupByType)
        {
            var expectedTypeIndex = startTypeIndex;

            for (int i = 0; i < componentTypes.Length; i++)
            {
                var type = componentTypes[i];
                try
                {
                    var index = FindTypeIndex(type);
                    if (index != -1)
                        throw new InvalidOperationException("ComponentType cannot be initialized more than once.");

                    TypeInfo typeInfo;
                    if (writeGroupByType.ContainsKey(expectedTypeIndex))
                    {
                        var writeGroupSet = writeGroupByType[expectedTypeIndex];
                        var writeGroupCount = writeGroupSet.Count;
                        var writeGroupArray = new int[writeGroupCount];
                        writeGroupSet.CopyTo(writeGroupArray);

                        typeInfo = BuildComponentType(type, writeGroupArray);
                    }
                    else
                    {
                        typeInfo = BuildComponentType(type);
                    }

                    var typeIndex = typeInfo.TypeIndex & TypeManager.ClearFlagsMask;
                    if (expectedTypeIndex != typeIndex)
                        throw new InvalidOperationException("ComponentType.TypeIndex does not match precalculated index.");

                    AddTypeInfoToTables(type, typeInfo, type.FullName);
                    expectedTypeIndex += 1;
                }
                catch (Exception e)
                {
                    if (type != null)
                    {
                        // Explicitly clear the shared type index.
                        // This is a workaround for a bug in burst where the shared static doesn't get reset to zero on domain reload.
                        // Can be removed once it is fixed in burst.
                        SharedTypeIndex.Get(type) = 0;
                        s_FailedTypeBuildException[type] = e;
                    }

                    Debug.LogException(e);
                }
            }
        }

        private static void GatherWriteGroups(Type[] componentTypes, int startTypeIndex, Dictionary<Type, int> typeIndexByType,
            Dictionary<int, HashSet<int>> writeGroupByType)
        {
            for (int i = 0; i < componentTypes.Length; i++)
            {
                var type = componentTypes[i];
                var typeIndex = startTypeIndex + i;

                foreach (var attribute in type.GetCustomAttributes(typeof(WriteGroupAttribute)))
                {
                    var attr = (WriteGroupAttribute)attribute;
                    if (!typeIndexByType.ContainsKey(attr.TargetType))
                    {
                        Debug.LogError($"GatherWriteGroups: looking for {attr.TargetType} but it hasn't been set up yet");
                    }

                    int targetTypeIndex = typeIndexByType[attr.TargetType];

                    if (!writeGroupByType.ContainsKey(targetTypeIndex))
                    {
                        var targetList = new HashSet<int>();
                        writeGroupByType.Add(targetTypeIndex, targetList);
                    }

                    writeGroupByType[targetTypeIndex].Add(typeIndex);
                }
            }
        }

        public static bool IsAssemblyReferencingEntities(Assembly assembly)
        {
            const string kEntitiesAssemblyName = "Unity.Entities";
            if (assembly.GetName().Name.Contains(kEntitiesAssemblyName))
                return true;

            var referencedAssemblies = assembly.GetReferencedAssemblies();
            foreach (var referenced in referencedAssemblies)
                if (referenced.Name.Contains(kEntitiesAssemblyName))
                    return true;
            return false;
        }

        internal static void IsAssemblyReferencingEntitiesOrUnityEngine(Assembly assembly, out bool referencesEntities, out bool referencesUnityEngine)
        {
            const string kEntitiesAssemblyName = "Unity.Entities";
            const string kUnityEngineAssemblyName = "UnityEngine";
            var assemblyName = assembly.GetName().Name;

            referencesEntities = false;
            referencesUnityEngine = false;

            if (assemblyName.Contains(kEntitiesAssemblyName))
                referencesEntities = true;

            if (assemblyName.Contains(kUnityEngineAssemblyName))
                referencesUnityEngine = true;

            var referencedAssemblies = assembly.GetReferencedAssemblies();
            foreach (var referencedAssembly in referencedAssemblies)
            {
                var referencedAssemblyName = referencedAssembly.Name;

                if (!referencesEntities && referencedAssemblyName.Contains(kEntitiesAssemblyName))
                    referencesEntities = true;
                if (!referencesUnityEngine && referencedAssemblyName.Contains(kUnityEngineAssemblyName))
                    referencesUnityEngine = true;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckIsAllowedAsComponentData(Type type, string baseTypeDesc)
        {
            if (UnsafeUtility.IsUnmanaged(type))
                return;

            // it can't be used -- so we expect this to find and throw
            ThrowOnDisallowedComponentData(type, type, baseTypeDesc);

            // if something went wrong and the above didn't throw, then throw
            throw new ArgumentException($"{type} cannot be used as component data for unknown reasons (BUG)");
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckIsAllowedAsManagedComponentData(Type type, string baseTypeDesc)
        {
            if (type.IsClass && typeof(IComponentData).IsAssignableFrom(type))
            {
                ThrowOnDisallowedManagedComponentData(type, type, baseTypeDesc);
                return;
            }

            // if something went wrong and the above didn't throw, then throw
            throw new ArgumentException($"{type} cannot be used as managed component data for unknown reasons (BUG)");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void ThrowOnDisallowedManagedComponentData(Type type, Type baseType, string baseTypeDesc)
        {
            // Validate the class IComponentData is usable:
            // - Has a default constructor
            if (type.GetConstructor(Type.EmptyTypes) == null)
                throw new ArgumentException($"{type} is a class based IComponentData. Class based IComponentData must implement a default constructor.");
        }

#endif

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void ThrowOnDisallowedComponentData(Type type, Type baseType, string baseTypeDesc)
        {
            if (type.IsPrimitive)
                return;

            // if it's a pointer, we assume you know what you're doing
            if (type.IsPointer)
                return;

            if (!type.IsValueType || type.IsByRef || type.IsClass || type.IsInterface || type.IsArray)
            {
                if (type == baseType)
                    throw new ArgumentException(
                        $"{type} is a {baseTypeDesc} and thus must be a struct containing only primitive or blittable members.");

                throw new ArgumentException($"{baseType} contains a field of {type}, which is neither primitive nor blittable.");
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                ThrowOnDisallowedComponentData(field.FieldType, baseType, baseTypeDesc);
            }
        }

        // https://stackoverflow.com/a/27851610
        static bool IsZeroSizeStruct(Type t)
        {
            return t.IsValueType && !t.IsPrimitive &&
                t.GetFields((BindingFlags)0x34).All(fi => IsZeroSizeStruct(fi.FieldType));
        }

        internal static TypeInfo BuildComponentType(Type type)
        {
            return BuildComponentType(type, null);
        }

        internal static TypeInfo BuildComponentType(Type type, int[] writeGroups)
        {
            var sizeInChunk = 0;
            TypeCategory category;
            var typeInfo = FastEquality.TypeInfo.Null;
            int bufferCapacity = -1;
            var memoryOrdering = TypeHash.CalculateMemoryOrdering(type, out var hasCustomMemoryOrder);
            // The stable type hash is the same as the memory order if the user hasn't provided a custom memory ordering
            var stableTypeHash = !hasCustomMemoryOrder ? memoryOrdering : TypeHash.CalculateStableTypeHash(type);
            bool isManaged = type.IsClass;
            var maxChunkCapacity = MaximumChunkCapacity;
            var valueTypeSize = 0;

            var maxCapacityAttribute = type.GetCustomAttribute<MaximumChunkCapacityAttribute>();
            if (maxCapacityAttribute != null)
                maxChunkCapacity = maxCapacityAttribute.Capacity;

            int entityOffsetIndex = s_EntityOffsetList.Length;
            int blobAssetRefOffsetIndex = s_BlobAssetRefOffsetList.Length;

            int elementSize = 0;
            int alignmentInBytes = 0;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (type.IsInterface)
                throw new ArgumentException($"{type} is an interface. It must be a concrete type.");
#endif
            bool hasEntityReferences = false;
            bool hasBlobReferences = false;

            if (typeof(IComponentData).IsAssignableFrom(type) && !isManaged)
            {
                CheckIsAllowedAsComponentData(type, nameof(IComponentData));

                category = TypeCategory.ComponentData;

                valueTypeSize = UnsafeUtility.SizeOf(type);
                alignmentInBytes = CalculateAlignmentInChunk(valueTypeSize);

                if (TypeManager.IsZeroSizeStruct(type))
                    sizeInChunk = 0;
                else
                    sizeInChunk = valueTypeSize;

                typeInfo = FastEquality.CreateTypeInfo(type);
                EntityRemapUtility.CalculateEntityAndBlobOffsetsUnmanaged(type, out hasEntityReferences, out hasBlobReferences, ref s_EntityOffsetList, ref s_BlobAssetRefOffsetList);
            }
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            else if (typeof(IComponentData).IsAssignableFrom(type) && isManaged)
            {
                CheckIsAllowedAsManagedComponentData(type, nameof(IComponentData));

                category = TypeCategory.ComponentData;
                sizeInChunk = sizeof(int);
                typeInfo = FastEquality.CreateTypeInfo(type);
                EntityRemapUtility.HasEntityReferencesManaged(type, out hasEntityReferences, out hasBlobReferences);
            }
#endif
            else if (typeof(IBufferElementData).IsAssignableFrom(type))
            {
                CheckIsAllowedAsComponentData(type, nameof(IBufferElementData));

                category = TypeCategory.BufferData;

                valueTypeSize = UnsafeUtility.SizeOf(type);
                // TODO: Implement UnsafeUtility.AlignOf(type)
                alignmentInBytes = CalculateAlignmentInChunk(valueTypeSize);

                elementSize = valueTypeSize;

                var capacityAttribute = (InternalBufferCapacityAttribute)type.GetCustomAttribute(typeof(InternalBufferCapacityAttribute));
                if (capacityAttribute != null)
                    bufferCapacity = capacityAttribute.Capacity;
                else
                    bufferCapacity = DefaultBufferCapacityNumerator / elementSize; // Rather than 2*cachelinesize, to make it cross platform deterministic

                sizeInChunk = sizeof(BufferHeader) + bufferCapacity * elementSize;
                typeInfo = FastEquality.CreateTypeInfo(type);
                EntityRemapUtility.CalculateEntityAndBlobOffsetsUnmanaged(type, out hasEntityReferences, out hasBlobReferences, ref s_EntityOffsetList, ref s_BlobAssetRefOffsetList);
            }
            else if (typeof(ISharedComponentData).IsAssignableFrom(type))
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!type.IsValueType)
                    throw new ArgumentException($"{type} is an ISharedComponentData, and thus must be a struct.");
#endif
                valueTypeSize = UnsafeUtility.SizeOf(type);

                EntityRemapUtility.HasEntityReferencesManaged(type, out hasEntityReferences, out hasBlobReferences);
                // Shared components explicitly do not allow patching of entity references
                hasEntityReferences = false;

                category = TypeCategory.ISharedComponentData;
                typeInfo = FastEquality.CreateTypeInfo(type);
                isManaged = !UnsafeUtility.IsUnmanaged(type);
            }
            else if (type.IsClass)
            {
                category = TypeCategory.UnityEngineObject;
                sizeInChunk = sizeof(int);
                alignmentInBytes = sizeof(int);
                hasEntityReferences = false;
                hasBlobReferences = false;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (type.FullName == "Unity.Entities.GameObjectEntity")
                    throw new ArgumentException(
                        "GameObjectEntity cannot be used from EntityManager. The component is ignored when creating entities for a GameObject.");
                if (UnityEngineObjectType == null)
                    throw new ArgumentException(
                        $"{type} cannot be used from EntityManager. If it inherits UnityEngine.Component, you must first register TypeManager.UnityEngineObjectType or include the Unity.Entities.Hybrid assembly in your build.");
                if (!UnityEngineObjectType.IsAssignableFrom(type))
                    throw new ArgumentException($"{type} must inherit {UnityEngineObjectType}.");
#endif
            }
            else
            {
                throw new ArgumentException($"{type} is not a valid component.");
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CheckComponentType(type);
#endif
            int fastEqIndex = 0;
            if (!FastEquality.TypeInfo.Null.Equals(typeInfo))
            {
                fastEqIndex = s_FastEqualityTypeInfoList.Count;
                s_FastEqualityTypeInfoList.Add(typeInfo);
            }

            int entityOffsetCount = s_EntityOffsetList.Length - entityOffsetIndex;
            int blobAssetRefOffsetCount = s_BlobAssetRefOffsetList.Length - blobAssetRefOffsetIndex;

            int writeGroupIndex = s_WriteGroupList.Length;
            int writeGroupCount = writeGroups == null ? 0 : writeGroups.Length;
            if (writeGroups != null)
            {
                foreach (var wgTypeIndex in writeGroups)
                    s_WriteGroupList.Add(wgTypeIndex);
            }

            int typeIndex = s_TypeCount;
            // System state shared components are also considered system state components
            bool isSystemStateSharedComponent = typeof(ISystemStateSharedComponentData).IsAssignableFrom(type);
            bool isSystemStateBufferElement = typeof(ISystemStateBufferElementData).IsAssignableFrom(type);
            bool isSystemStateComponent = isSystemStateSharedComponent || isSystemStateBufferElement || typeof(ISystemStateComponentData).IsAssignableFrom(type);

            if (typeIndex != 0)
            {
                if (sizeInChunk == 0)
                    typeIndex |= ZeroSizeInChunkTypeFlag;

                if (category == TypeCategory.ISharedComponentData)
                    typeIndex |= SharedComponentTypeFlag;

                if (isSystemStateComponent)
                    typeIndex |= SystemStateTypeFlag;

                if (isSystemStateSharedComponent)
                    typeIndex |= SystemStateSharedComponentTypeFlag;

                if (bufferCapacity >= 0)
                    typeIndex |= BufferComponentTypeFlag;

                if (!hasEntityReferences)
                    typeIndex |= HasNoEntityReferencesFlag;

                if (isManaged)
                    typeIndex |= ManagedComponentTypeFlag;
            }

            return new TypeInfo(typeIndex, category, entityOffsetCount, entityOffsetIndex,
                memoryOrdering, stableTypeHash, bufferCapacity, sizeInChunk,
                elementSize > 0 ? elementSize : sizeInChunk, alignmentInBytes,
                maxChunkCapacity, writeGroupCount, writeGroupIndex,
                hasBlobReferences, blobAssetRefOffsetCount, blobAssetRefOffsetIndex, fastEqIndex,
                valueTypeSize);
        }

 #if UNITY_EDITOR
        /// <summary>
        /// This function allows for unregistered component types to be added to the TypeManager allowing for their use
        /// across the ECS apis _after_ TypeManager.Initialize() may have been called. Importantly, this function must
        /// be called from the main thread and will create a synchronization point across all worlds. If a type which
        /// is already registered with the TypeManager is passed in, this function will throw.
        /// </summary>
        /// <remarks>Types with [WriteGroup] attributes will be accepted for registration however their
        /// write group information will be ignored.</remarks>
        /// <param name="types"></param>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static void AddNewComponentTypes(params Type[] types)
        {
            if (!UnityEditorInternal.InternalEditorUtility.CurrentThreadIsMainThread())
                throw new InvalidOperationException("Must be called from the main thread");

            // We might invalidate the SharedStatics ptr so we must synchronize all jobs that might be using those ptrs
            foreach (var world in World.All)
                world.EntityManager.BeforeStructuralChange();

            // Is this a new type, or are we replacing an existing one?
            foreach (var type in types)
            {
                if (s_ManagedTypeToIndex.ContainsKey(type))
                    continue;

                var typeInfo = BuildComponentType(type);
                AddTypeInfoToTables(type, typeInfo, type.FullName);
            }

            // We may have added enough types to cause the underlying containers to resize so re-fetch their ptrs
            SharedEntityOffsetInfo.Ref.Data = new IntPtr(s_EntityOffsetList.GetUnsafePtr());
            SharedBlobAssetRefOffset.Ref.Data = new IntPtr(s_BlobAssetRefOffsetList.GetUnsafePtr());
            SharedWriteGroup.Ref.Data = new IntPtr(s_WriteGroupList.GetUnsafePtr());

            // Since the ptrs may have changed we need to ensure all entity component stores are using the correct ones
            foreach (var w in World.All)
            {
                var access = w.EntityManager.GetCheckedEntityDataAccess();
                var ecs = access->EntityComponentStore;
                ecs->InitializeTypeManagerPointers();
            }
        }

#endif

        private sealed class SharedTypeIndex
        {
            public static ref int Get(Type componentType)
            {
                return ref SharedStatic<int>.GetOrCreate(typeof(TypeManagerKeyContext), componentType).Data;
            }
        }
#endif // #if !UNITY_DOTSRUNTIME

        private sealed class TypeManagerKeyContext
        {
            private TypeManagerKeyContext()
            {
            }
        }

        private sealed class SharedTypeInfo
        {
            private SharedTypeInfo()
            {
            }

            public static readonly SharedStatic<IntPtr> Ref = SharedStatic<IntPtr>.GetOrCreate<TypeManagerKeyContext, SharedTypeInfo>();
        }

        private sealed class SharedEntityOffsetInfo
        {
            public static readonly SharedStatic<IntPtr> Ref = SharedStatic<IntPtr>.GetOrCreate<TypeManagerKeyContext, SharedEntityOffsetInfo>();
        }

        private sealed class SharedBlobAssetRefOffset
        {
            public static readonly SharedStatic<IntPtr> Ref = SharedStatic<IntPtr>.GetOrCreate<TypeManagerKeyContext, SharedBlobAssetRefOffset>();
        }

        private sealed class SharedWriteGroup
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal static AtomicSafetyHandle AtomicSafetyHandle;
#endif
            public static readonly SharedStatic<IntPtr> Ref = SharedStatic<IntPtr>.GetOrCreate<TypeManagerKeyContext, SharedWriteGroup>();
        }

        // Marked as internal as this is used by StaticTypeRegistryILPostProcessor
        internal sealed class SharedTypeIndex<TComponent>
        {
            public static readonly SharedStatic<int> Ref = SharedStatic<int>.GetOrCreate<TypeManagerKeyContext, TComponent>();
        }

#if UNITY_DOTSRUNTIME
        static SpinLock sLock = new SpinLock();

        internal static void RegisterStaticAssemblyTypes()
        {
            throw new CodegenShouldReplaceException("To be replaced by codegen");
        }

        static List<int> s_TypeDelegateIndexRanges = new List<int>();
        static List<TypeRegistry.GetBoxedEqualsFn> s_AssemblyBoxedEqualsFn = new List<TypeRegistry.GetBoxedEqualsFn>();
        static List<TypeRegistry.GetBoxedEqualsPtrFn> s_AssemblyBoxedEqualsPtrFn = new List<TypeRegistry.GetBoxedEqualsPtrFn>();
        static List<TypeRegistry.BoxedGetHashCodeFn> s_AssemblyBoxedGetHashCodeFn = new List<TypeRegistry.BoxedGetHashCodeFn>();
        static List<TypeRegistry.ConstructComponentFromBufferFn> s_AssemblyConstructComponentFromBufferFn = new List<TypeRegistry.ConstructComponentFromBufferFn>();

        internal static bool GetBoxedEquals(object lhs, object rhs, int typeIndexNoFlags)
        {
            int offset = 0;
            for (int i = 0; i < s_TypeDelegateIndexRanges.Count; ++i)
            {
                if (typeIndexNoFlags < s_TypeDelegateIndexRanges[i])
                    return s_AssemblyBoxedEqualsFn[i](lhs, rhs, typeIndexNoFlags - offset);
                offset = s_TypeDelegateIndexRanges[i];
            }

            throw new ArgumentException("No function was generated for the provided type.");
        }

        internal static bool GetBoxedEquals(object lhs, void* rhs, int typeIndexNoFlags)
        {
            int offset = 0;
            for (int i = 0; i < s_TypeDelegateIndexRanges.Count; ++i)
            {
                if (typeIndexNoFlags < s_TypeDelegateIndexRanges[i])
                    return s_AssemblyBoxedEqualsPtrFn[i](lhs, rhs, typeIndexNoFlags - offset);
                offset = s_TypeDelegateIndexRanges[i];
            }

            throw new ArgumentException("No function was generated for the provided type.");
        }

        internal static int GetBoxedHashCode(object obj, int typeIndexNoFlags)
        {
            int offset = 0;
            for (int i = 0; i < s_TypeDelegateIndexRanges.Count; ++i)
            {
                if (typeIndexNoFlags < s_TypeDelegateIndexRanges[i])
                    return s_AssemblyBoxedGetHashCodeFn[i](obj, typeIndexNoFlags - offset);
                offset = s_TypeDelegateIndexRanges[i];
            }

            throw new ArgumentException("No function was generated for the provided type.");
        }

        internal static object ConstructComponentFromBuffer(void* buffer, int typeIndexNoFlags)
        {
            int offset = 0;
            for (int i = 0; i < s_TypeDelegateIndexRanges.Count; ++i)
            {
                if (typeIndexNoFlags < s_TypeDelegateIndexRanges[i])
                    return s_AssemblyConstructComponentFromBufferFn[i](buffer, typeIndexNoFlags - offset);
                offset = s_TypeDelegateIndexRanges[i];
            }

            throw new ArgumentException("No function was generated for the provided type.");
        }

        static bool EntityBoxedEquals(object lhs, object rhs, int typeIndexNoFlags)
        {
            Assert.IsTrue(typeIndexNoFlags == 1);
            Entity e0 = (Entity)lhs;
            Entity e1 = (Entity)rhs;
            return e0.Equals(e1);
        }

        static bool EntityBoxedEqualsPtr(object lhs, void* rhs, int typeIndexNoFlags)
        {
            Assert.IsTrue(typeIndexNoFlags == 1);
            Entity e0 = (Entity)lhs;
            Entity e1 = *(Entity*)rhs;
            return e0.Equals(e1);
        }

        static int EntityBoxedGetHashCode(object obj, int typeIndexNoFlags)
        {
            Assert.IsTrue(typeIndexNoFlags == 1);
            Entity e0 = (Entity)obj;
            return e0.GetHashCode();
        }

        static object EntityConstructComponentFromBuffer(void* obj, int typeIndexNoFlags)
        {
            Assert.IsTrue(typeIndexNoFlags == 1);
            return *(Entity*)obj;
        }

        /// <summary>
        /// Registers, all at once, the type registry information generated for each assembly.
        /// </summary>
        /// <param name="registries"></param>
        internal unsafe static void RegisterAssemblyTypes(TypeRegistry[] registries)
        {
            // The standard doesn't guarantee that we will not call this method concurrently so we need to
            // ensure the code here can handle multiple modules registering their types at once
            bool lockTaken = false;
            try
            {
                sLock.Enter(ref lockTaken);
                int initializeTypeIndexOffset = s_TypeCount;
                s_TypeDelegateIndexRanges.Add(s_TypeCount);

                s_AssemblyBoxedEqualsFn.Add(EntityBoxedEquals);
                s_AssemblyBoxedEqualsPtrFn.Add(EntityBoxedEqualsPtr);
                s_AssemblyBoxedGetHashCodeFn.Add(EntityBoxedGetHashCode);
                s_AssemblyConstructComponentFromBufferFn.Add(EntityConstructComponentFromBuffer);
                foreach (var typeRegistry in registries)
                {
                    int typeIndexOffset = s_TypeCount;
                    int entityOffsetsOffset = s_EntityOffsetList.Length;
                    int blobOffsetsOffset = s_BlobAssetRefOffsetList.Length;
                    int fieldInfosOffset = s_FieldInfos.Length;
                    int fieldTypesOffset = s_FieldTypes.Count;
                    int fieldNamesOffset = s_FieldNames.Count;

                    foreach (var type in typeRegistry.Types)
                        s_Types.Add(type);

                    foreach (var typeName in typeRegistry.TypeNames)
                        s_TypeNames.Add(typeName);

                    foreach (var type in typeRegistry.FieldTypes)
                        s_FieldTypes.Add(type);

                    foreach (var fieldName in typeRegistry.FieldNames)
                        s_FieldNames.Add(fieldName);

                    s_EntityOffsetList.AddRange(typeRegistry.EntityOffsetsPtr, typeRegistry.EntityOffsetsCount);
                    s_BlobAssetRefOffsetList.AddRange(typeRegistry.BlobAssetReferenceOffsetsPtr, typeRegistry.BlobAssetReferenceOffsetsCount);
                    {
                        var typeInfoOffset = ((TypeInfo*)s_TypeInfos.GetUnsafePtr()) + s_TypeCount;
                        UnsafeUtility.MemCpy(typeInfoOffset, typeRegistry.TypeInfosPtr, typeRegistry.TypeInfosCount * UnsafeUtility.SizeOf<TypeInfo>());

                        int* newTypeIndices = stackalloc int[typeRegistry.TypeInfosCount];
                        for (int i = 0; i < typeRegistry.TypeInfosCount; ++i)
                        {
                            TypeInfo* pTypeInfo = ((TypeInfo*)s_TypeInfos.GetUnsafePtr()) + i + s_TypeCount;
                            *(&pTypeInfo->TypeIndex) += typeIndexOffset;
                            *(&pTypeInfo->EntityOffsetStartIndex) += entityOffsetsOffset;
                            *(&pTypeInfo->BlobAssetRefOffsetStartIndex) += blobOffsetsOffset;

                            // we will adjust these values when we recalculate the writegroups below
                            *(&pTypeInfo->WriteGroupCount) = 0;
                            *(&pTypeInfo->WriteGroupStartIndex) = -1;

                            s_StableTypeHashToTypeIndex.Add(pTypeInfo->StableTypeHash, pTypeInfo->TypeIndex);
                            newTypeIndices[i] = pTypeInfo->TypeIndex;
                        }
                        // Setup our new TypeIndices into the appropriately types SharedTypeIndex<TComponent> shared static
                        typeRegistry.SetSharedTypeIndices(newTypeIndices, typeRegistry.TypeInfosCount);
                        s_TypeCount += typeRegistry.TypeInfosCount;
                    }

                    for (int i = 0; i < typeRegistry.FieldInfos.Length; ++i)
                    {
                        var fieldInfo = typeRegistry.FieldInfos[i];
                        fieldInfo.FieldNameIndex += fieldNamesOffset;
                        fieldInfo.FieldTypeIndex += fieldTypesOffset;

                        s_FieldInfos.Add(fieldInfo);
                    }

                    for (int i = 0; i < typeRegistry.FieldInfoLookups.Length; ++i)
                    {
                        var lookup = typeRegistry.FieldInfoLookups[i];

                        lookup.FieldTypeIndex += fieldTypesOffset;
                        lookup.Index += fieldInfosOffset;
                        var fieldType = s_FieldTypes[lookup.FieldTypeIndex];

                        if (!s_TypeToFieldInfosMap.ContainsKey(fieldType))
                            s_TypeToFieldInfosMap.Add(fieldType, lookup);
                    }

                    if (typeRegistry.Types.Length > 0)
                    {
                        s_TypeDelegateIndexRanges.Add(s_TypeCount);

                        s_AssemblyBoxedEqualsFn.Add(typeRegistry.BoxedEquals);
                        s_AssemblyBoxedEqualsPtrFn.Add(typeRegistry.BoxedEqualsPtr);
                        s_AssemblyBoxedGetHashCodeFn.Add(typeRegistry.BoxedGetHashCode);
                        s_AssemblyConstructComponentFromBufferFn.Add(typeRegistry.ConstructComponentFromBuffer);
                    }

                    // Register system types
                    RegisterAssemblySystemTypes(typeRegistry);
                }
                GatherAndInitializeWriteGroups(initializeTypeIndexOffset, registries);
            }
            finally
            {
                if (lockTaken)
                {
                    sLock.Exit(true);
                }
            }
        }

        static unsafe void GatherAndInitializeWriteGroups(int typeIndexOffset, TypeRegistry[] registries)
        {
            // A this point we have loaded all Types and know all TypeInfos. Now we need to
            // go back through each assembly, determine if a type has a write group, and if so
            // translate the Type of the writegroup component to a TypeIndex. But, we must do this incrementally
            // for all assemblies since AssemblyA can add to the writegroup list of a type defined in AssemblyB.
            // Once we have a complete mapping, generate the s_WriteGroup array and fixup all writegroupStart
            // indices in our type infos

            // We create a list of hashmaps here since we can't put a NativeHashMap inside of a NativeHashMap in debug builds due to DisposeSentinels being managed
            var hashSetList = new List<NativeHashMap<int, byte>>();
            NativeHashMap<int, int> writeGroupMap = new NativeHashMap<int, int>(1024, Allocator.Temp);
            foreach (var typeRegistry in registries)
            {
                for (int i = 0; i < typeRegistry.TypeInfosCount; ++i)
                {
                    var typeInfo = typeRegistry.TypeInfosPtr[i];
                    if (typeInfo.WriteGroupCount > 0)
                    {
                        var typeIndex = typeInfo.TypeIndex + typeIndexOffset;

                        for (int wgIndex = 0; wgIndex < typeInfo.WriteGroupCount; ++wgIndex)
                        {
                            var targetType = typeRegistry.WriteGroups[typeInfo.WriteGroupStartIndex + wgIndex];
                            // targetType isn't necessarily from this assembly (it could be from one of its references)
                            // so lookup the actual typeIndex since we loaded all assembly types above
                            var targetTypeIndex = GetTypeIndex(targetType);

                            if (!writeGroupMap.TryGetValue(targetTypeIndex, out var targetSetIndex))
                            {
                                targetSetIndex = hashSetList.Count;
                                writeGroupMap.Add(targetTypeIndex, targetSetIndex);
                                hashSetList.Add(new NativeHashMap<int, byte>(typeInfo.WriteGroupCount, Allocator.Temp));
                            }
                            var targetSet = hashSetList[targetSetIndex];
                            targetSet.TryAdd(typeIndex, 0); // We don't have a NativeSet, so just push 0
                        }
                    }
                }

                typeIndexOffset += typeRegistry.TypeInfosCount;
            }

            using (var keys = writeGroupMap.GetKeyArray(Allocator.Temp))
            {
                foreach (var typeIndex in keys)
                {
                    var index = typeIndex & ClearFlagsMask;
                    var typeInfo = (TypeInfo*)s_TypeInfos.GetUnsafePtr() + index;

                    var valueIndex = writeGroupMap[typeIndex];
                    var valueSet = hashSetList[valueIndex];
                    using (var values = valueSet.GetKeyArray(Allocator.Temp))
                    {
                        *(&typeInfo->WriteGroupStartIndex) = s_WriteGroupList.Length;
                        *(&typeInfo->WriteGroupCount) = values.Length;

                        foreach (var ti in values)
                            s_WriteGroupList.Add(ti);
                    }

                    valueSet.Dispose();
                }
            }
            writeGroupMap.Dispose();
        }

        static ulong GetEntityStableTypeHash()
        {
            throw new CodegenShouldReplaceException("This call should have been replaced by codegen");
        }

#endif
    }

#if UNITY_DOTSRUNTIME
    internal unsafe class TypeRegistry
    {
        // TODO: Have Burst generate a native function ptr we can invoke instead of using a delegate
        public delegate bool GetBoxedEqualsFn(object lhs, object rhs, int typeIndexNoFlags);
        public unsafe delegate bool GetBoxedEqualsPtrFn(object lhs, void* rhs, int typeIndexNoFlags);
        public delegate int BoxedGetHashCodeFn(object obj, int typeIndexNoFlags);
        public unsafe delegate object ConstructComponentFromBufferFn(void* buffer, int typeIndexNoFlags);
        public unsafe delegate void SetSharedTypeIndicesFn(int* typeInfoArray, int count);
        public delegate Attribute[] GetSystemAttributesFn(Type system);
        public delegate object CreateSystemFn(Type system);

        public GetBoxedEqualsFn BoxedEquals;
        public GetBoxedEqualsPtrFn BoxedEqualsPtr;
        public BoxedGetHashCodeFn BoxedGetHashCode;
        public ConstructComponentFromBufferFn ConstructComponentFromBuffer;
        public SetSharedTypeIndicesFn SetSharedTypeIndices;
        public GetSystemAttributesFn GetSystemAttributes;
        public CreateSystemFn CreateSystem;

#pragma warning disable 0649
        public string AssemblyName;

        public TypeManager.TypeInfo* TypeInfosPtr;
        public int TypeInfosCount;
        public int* EntityOffsetsPtr;
        public int EntityOffsetsCount;
        public int* BlobAssetReferenceOffsetsPtr;
        public int BlobAssetReferenceOffsetsCount;

        public Type[] Types;
        public string[] TypeNames;
        public Type[] WriteGroups;

        public Type[] SystemTypes;
        public WorldSystemFilterFlags[] SystemFilterFlags;
        public string[] SystemTypeNames;
        public bool[] IsSystemGroup;

        public Type[] FieldTypes;
        public string[] FieldNames;
        public TypeManager.FieldInfo[] FieldInfos;
        public FieldInfoLookup[] FieldInfoLookups;

        public struct FieldInfoLookup
        {
            public FieldInfoLookup(int typeIndex, int infoIndex, int count)
            {
                FieldTypeIndex = typeIndex;
                Index = infoIndex;
                Count = count;
            }

            public int FieldTypeIndex;
            public int Index;
            public int Count;
        }
#pragma warning restore 0649
    }
#endif
}
