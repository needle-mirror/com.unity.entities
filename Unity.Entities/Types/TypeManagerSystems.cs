using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Burst;
using static Unity.Burst.BurstRuntime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Profiling;
using CreateOrderSystemSorter = Unity.Entities.ComponentSystemSorter<Unity.Entities.CreateBeforeAttribute, Unity.Entities.CreateAfterAttribute>;

namespace Unity.Entities
{
    static public unsafe partial class TypeManager
    {
        static int s_SystemCount;
        static List<Type> s_SystemTypes;
        static Dictionary<Type, int> s_ManagedSystemTypeToIndex;


        internal readonly struct SystemTypeInfo
        {
            public const int kIsSystemGroupFlag = 1 << 30;
            public const int kIsSystemManagedFlag = 1 << 29;
            public const int kIsSystemISystemStartStopFlag = 1 << 28;
            public const int kClearSystemTypeFlagsMask = 0x0FFFFFFF;

            public readonly int TypeIndex;
            public readonly int Size;
            public readonly long Hash;

            public SystemTypeInfo(int typeIndex, int size, long hash)
            {
                TypeIndex = typeIndex;
                Size = size;
                Hash = hash;
            }

            public bool IsSystemGroup => (TypeIndex & kIsSystemGroupFlag) != 0;
            public Type Type => TypeManager.GetSystemType(TypeIndex);

            [GenerateTestsForBurstCompatibility]
            public NativeText.ReadOnly DebugTypeName
            {
                get
                {
                    var pUnsafeText = GetSystemNameInternal(TypeIndex);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeText.ReadOnly ro = new NativeText.ReadOnly(pUnsafeText, SharedSafetyHandle.Ref.Data);
#else
                    NativeText.ReadOnly ro = new NativeText.ReadOnly(pUnsafeText);
#endif
                    return ro;
                }
            }
        }

        static List<int> s_SystemTypeSizes;
        static List<long> s_SystemTypeHashes;
        struct LookupFlags
        {
            public WorldSystemFilterFlags OptionalFlags;
            public WorldSystemFilterFlags RequiredFlags;
        }
        static Dictionary<LookupFlags, IReadOnlyList<Type>> s_SystemFilterTypeMap;

        static UnsafeList<int> s_SystemTypeFlagsList;
        static UnsafeList<WorldSystemFilterFlags> s_SystemFilterFlagsList;
        static UnsafeList<UnsafeText> s_SystemTypeNames;


#if UNITY_DOTSRUNTIME
        static List<int> s_SystemTypeDelegateIndexRanges;
        static List<TypeRegistry.CreateSystemFn> s_AssemblyCreateSystemFn;
        static List<TypeRegistry.GetSystemAttributesFn> s_AssemblyGetSystemAttributesFn;
#endif

        // While we provide a public interface for the TypeManager the init/shutdown
        // of the TypeManager owned by the TypeManager so we mark these functions as internal
        private static void InitializeSystemsState()
        {
            const int kInitialSystemCount = 1024;
            s_SystemCount = 0;
            s_SystemTypes = new List<Type>(kInitialSystemCount);
            s_ManagedSystemTypeToIndex = new Dictionary<Type, int>(kInitialSystemCount);
            s_SystemTypeSizes = new List<int>(kInitialSystemCount);
            s_SystemTypeHashes = new List<long>(kInitialSystemCount);
            s_SystemFilterTypeMap = new Dictionary<LookupFlags, IReadOnlyList<Type>>(kInitialSystemCount);

            s_SystemTypeFlagsList = new UnsafeList<int>(kInitialSystemCount, Allocator.Persistent);
            s_SystemFilterFlagsList = new UnsafeList<WorldSystemFilterFlags>(kInitialSystemCount, Allocator.Persistent);

            s_SystemTypeNames = new UnsafeList<UnsafeText>(kInitialSystemCount, Allocator.Persistent);

#if UNITY_DOTSRUNTIME
            s_SystemTypeDelegateIndexRanges = new List<int>(kInitialSystemCount);
            s_AssemblyCreateSystemFn = new List<TypeRegistry.CreateSystemFn>(kInitialSystemCount);
            s_AssemblyGetSystemAttributesFn = new List<TypeRegistry.GetSystemAttributesFn>(kInitialSystemCount);
#endif
        }

        private static void ShutdownSystemsState()
        {
            s_SystemCount = 0;
            s_SystemTypes.Clear();
            s_ManagedSystemTypeToIndex.Clear();
            s_SystemTypeSizes.Clear();
            s_SystemTypeHashes.Clear();
            s_SystemFilterTypeMap.Clear();

            s_SystemTypeFlagsList.Dispose();
            s_SystemFilterFlagsList.Dispose();

            foreach (var name in s_SystemTypeNames)
                name.Dispose();
            s_SystemTypeNames.Dispose();

#if UNITY_DOTSRUNTIME
            s_SystemTypeDelegateIndexRanges.Clear();
            s_AssemblyCreateSystemFn.Clear();
            s_AssemblyGetSystemAttributesFn.Clear();
#endif
        }

        private static void RegisterSpecialSystems()
        {
            // Reserve index 0 for a null sentinel so we always have at least _some_ system type info
            Assert.IsTrue(s_SystemCount == 0);
            AddSystemTypeToTables(null, "null", 0, 0, 0, 0);
            Assert.IsTrue(s_SystemCount == 1);
        }

        internal static void InitializeAllSystemTypes()
        {
            // DOTS Runtime registers all system info when registering component info
#if !UNITY_DOTSRUNTIME
            try
            {
                Profiler.BeginSample(nameof(InitializeAllSystemTypes));
                foreach (var systemType in GetTypesDerivedFrom(typeof(ISystem)))
                {
                    if (!systemType.IsValueType)
                        continue;

                    var name = systemType.FullName;
                    var size = UnsafeUtility.SizeOf(systemType);
                    var hash = GetHashCode64(systemType);
                    // isystems can't be groups
                    var flags = GetSystemTypeFlags(systemType);
                    var filterFlags = MakeWorldFilterFlags(systemType);

                    AddSystemTypeToTables(systemType, name, size, hash, flags, filterFlags);
                }

                foreach (var systemType in GetTypesDerivedFrom(typeof(ComponentSystemBase)))
                {
                    if (systemType.IsAbstract || systemType.ContainsGenericParameters)
                        continue;

                    var name = systemType.FullName;
                    var size = -1; // Don't get a type size for a managed type
                    var hash = GetHashCode64(systemType);
                    var flags = GetSystemTypeFlags(systemType);

                    var filterFlags = MakeWorldFilterFlags(systemType);

                    AddSystemTypeToTables(systemType, name, size, hash, flags, filterFlags);
                }
            }
            finally
            {
                Profiler.EndSample();
            }
#endif
        }
#if !UNITY_DOTSRUNTIME
        private static int GetSystemTypeFlags(Type systemType)
        {
            var flags = 0;
            if (systemType.IsSubclassOf(typeof(ComponentSystemGroup)))
                flags |= SystemTypeInfo.kIsSystemGroupFlag;
            if (!UnsafeUtility.IsUnmanaged(systemType))
                flags |= SystemTypeInfo.kIsSystemManagedFlag;
            if (typeof(ISystemStartStop).IsAssignableFrom(systemType))
                flags |= SystemTypeInfo.kIsSystemISystemStartStopFlag;
            return flags;
        }
#endif

        /// <summary>
        /// Construct a System from a Type. Uses the same list in <see cref="GetSystems"/>.
        /// </summary>
        /// <param name="systemType">The system type.</param>
        /// <returns>Returns the new system.</returns>
        public static ComponentSystemBase ConstructSystem(Type systemType)
        {
#if !NET_DOTS
            if (!typeof(ComponentSystemBase).IsAssignableFrom(systemType))
                throw new ArgumentException($"'{systemType.FullName}' cannot be constructed as it does not inherit from ComponentSystemBase");

            // In cases where we are dealing with generic types, we may need to calculate typeinformation that wasn't collected upfront
            AddSystemTypeToTables(systemType);

            return (ComponentSystemBase)Activator.CreateInstance(systemType);
#else
            Assertions.Assert.IsTrue(s_Initialized, "The TypeManager must be initialized before the TypeManager can be used.");

            var obj = CreateSystem(systemType);
            if (!(obj is ComponentSystemBase))
                throw new ArgumentException("Null casting in Construct System. Bug in TypeManager.");
            return obj as ComponentSystemBase;
#endif
        }

        /// <summary>
        /// Creates an instance of a system of type T.
        /// </summary>
        /// <typeparam name="T">System type to create</typeparam>
        /// <returns>Returns the newly created system instance</returns>
        public static T ConstructSystem<T>() where T : ComponentSystemBase
        {
            return (T)ConstructSystem(typeof(T));
        }

        /// <summary>
        /// Creates an instance of a system of System.Type.
        /// </summary>
        /// <typeparam name="T">System type to create</typeparam>
        /// <param name="systemType">System type to create</param>
        /// <returns>Returns the newly created system instance</returns>
        public static T ConstructSystem<T>(Type systemType) where T : ComponentSystemBase
        {
            return (T)ConstructSystem(systemType);
        }


        /// <summary>
        /// Return an array of all System types available to the runtime matching the WorldSystemFilterFlags. By default,
        /// all systems available to the runtime is returned.
        /// </summary>
        /// <param name="filterFlags">Flags the returned systems can have</param>
        /// <param name="requiredFlags">Flags the returned systems must have</param>
        /// <returns>Returns a list of systems meeting the flag requirements provided</returns>
        public static IReadOnlyList<Type> GetSystems(WorldSystemFilterFlags filterFlags = WorldSystemFilterFlags.All, WorldSystemFilterFlags requiredFlags = 0)
        {
            // Expand default to proper types
            if ((filterFlags & WorldSystemFilterFlags.Default) != 0)
            {
                filterFlags &= ~WorldSystemFilterFlags.Default;
                filterFlags |= WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.Presentation;
            }

            Assertions.Assert.IsTrue(s_Initialized, "The TypeManager must be initialized before the TypeManager can be used.");
            // By default no flags are required
            requiredFlags &= ~WorldSystemFilterFlags.Default;
            LookupFlags lookupFlags = new LookupFlags() { OptionalFlags = filterFlags, RequiredFlags = requiredFlags };

            if (s_SystemFilterTypeMap.TryGetValue(lookupFlags, out var systemTypes))
                return systemTypes;

#if !UNITY_DOTSRUNTIME
            var filteredSystemTypes = new List<Type>();

            // Skip index 0 since that is always null
            for (int i = 1; i < s_SystemTypes.Count;++i)
            {
                var systemType = s_SystemTypes[i];
                if (FilterSystemType(systemType, lookupFlags))
                    filteredSystemTypes.Add(systemType);
            }

            SortSystemTypes(filteredSystemTypes);

            s_SystemFilterTypeMap[lookupFlags] = filteredSystemTypes;
            return filteredSystemTypes;
#else

            var filteredSystemTypes = new List<Type>();
            if (lookupFlags.OptionalFlags == WorldSystemFilterFlags.All)
            {
#if !NET_DOTS
                // 0 is reserved for null, don't return it
                filteredSystemTypes = s_SystemTypes.GetRange(1, s_SystemTypes.Count-1);
#else
                for(int i = 1; i < s_SystemTypes.Count; ++i)
                    filteredSystemTypes.Add(s_SystemTypes[i]);
#endif
            }
            else
            {
                for (int i = 1; i < s_SystemTypes.Count; ++i)
                {
                    if (!IsSystemDisabledForCreation(s_SystemTypes[i]) && (s_SystemFilterFlagsList[i] & lookupFlags.OptionalFlags) != 0 && (s_SystemFilterFlagsList[i] & lookupFlags.RequiredFlags) == lookupFlags.RequiredFlags)
                        filteredSystemTypes.Add(s_SystemTypes[i]);
                }
            }

            SortSystemTypes(filteredSystemTypes);
            s_SystemFilterTypeMap[lookupFlags] = filteredSystemTypes;
            return filteredSystemTypes;
#endif
        }


#if !NET_DOTS
        internal static void AddSystemTypeToTables(Type systemType)
        {
            if (systemType == null || s_ManagedSystemTypeToIndex.ContainsKey(systemType))
                return;

            var size = -1; // Don't get a type size for a managed type
#if !UNITY_DOTSRUNTIME
            if(systemType.IsValueType)
                size = UnsafeUtility.SizeOf(systemType);
#else
            // In non NET_DOTS DOTS Runtime builds we should only be calling this function for
            // types that are not statically known
            Assert.IsTrue(systemType.IsGenericType);
#endif
            var hash = GetHashCode64(systemType);
            var name = systemType.FullName;
#if UNITY_DOTSRUNTIME
            var flags = 0; //this is very possibly wrong, but it's also hard to find out
#else
            var flags = GetSystemTypeFlags(systemType);
#endif

            var filterFlags = default(WorldSystemFilterFlags);
            AddSystemTypeToTables(systemType, name, size, hash, flags, filterFlags);
        }
#endif

        internal static void SortSystemTypes(List<Type> systemTypes)
        {
            var systemElements = new CreateOrderSystemSorter.SystemElement[systemTypes.Count];

            for (int i = 0; i < systemTypes.Count; ++i)
            {
                systemElements[i] = new CreateOrderSystemSorter.SystemElement
                {
                    Type = systemTypes[i],
                    Index = new UpdateIndex(i, false),
                    OrderingBucket = 0,
                    updateBefore = new List<Type>(),
                    nAfter = 0,
                };
            }

            // Find & validate constraints between systems in the group
            CreateOrderSystemSorter.FindConstraints(null, systemElements);
            CreateOrderSystemSorter.Sort(systemElements);

            for (int i = 0; i < systemElements.Length; ++i)
                systemTypes[i] = systemElements[i].Type;
        }

        internal static void AddSystemTypeToTables(Type type, string typeName, int typeSize, long typeHash, int systemTypeFlags, WorldSystemFilterFlags filterFlags)
        {
            if (type != null && s_ManagedSystemTypeToIndex.ContainsKey(type))
                return;

            int systemIndex = s_SystemCount++;
            if (type != null)
            {
                s_ManagedSystemTypeToIndex.Add(type, systemIndex);
                SharedSystemTypeIndex.Get(type) = systemIndex;
            }

            s_SystemTypes.Add(type);
            s_SystemTypeSizes.Add(typeSize);
            s_SystemTypeHashes.Add(typeHash);

            var unsafeName = new UnsafeText(-1, Allocator.Persistent);
            var utf8Bytes = Encoding.UTF8.GetBytes(typeName);
            unsafe
            {
                fixed (byte* b = utf8Bytes)
                    unsafeName.Append(b, (ushort) utf8Bytes.Length);
            }
            s_SystemTypeNames.Add(unsafeName);

            s_SystemTypeFlagsList.Add(systemTypeFlags);
            s_SystemFilterFlagsList.Add(filterFlags);
        }

        internal static WorldSystemFilterFlags GetSystemFilterFlags(Type type)
        {
            var systemIndex = GetSystemTypeIndex(type) & SystemTypeInfo.kClearSystemTypeFlagsMask;
            var flags = s_SystemFilterFlagsList[systemIndex];
#if !UNITY_DOTSRUNTIME
            if (flags == 0)
            {
                flags = MakeWorldFilterFlags(type);
                s_SystemFilterFlagsList[systemIndex] = flags;
            }
#endif
            return flags;
        }

        /// <summary>
        /// Determines if a given type is a System type (e.g. ISystem, SystemBase).
        /// </summary>
        /// <param name="type">Type to check</param>
        /// <returns>Returns true if the input type is a System. False otherwise</returns>
        public static bool IsSystemType(Type type)
        {
            return GetSystemTypeIndexNoThrow(type) != -1;
        }

        internal static UnsafeText* GetSystemTypeNamesPointer()
        {
            return (UnsafeText*) SharedSystemTypeNames.Ref.Data;
        }

        internal static UnsafeText* GetSystemNameInternal(int systemIndex)
        {
            return GetSystemTypeNamesPointer() + (systemIndex & ClearFlagsMask);
        }

        /// <summary>
        /// Retrieve the name of a system via its type.
        /// </summary>
        /// <param name="type">Input type</param>
        /// <returns>System name for the type</returns>
        public static NativeText.ReadOnly GetSystemName(Type type)
        {
            var systemIndex = Math.Max(0, GetSystemTypeIndexNoThrow(type));
            return GetSystemName(systemIndex);
        }


        /// <summary>
        /// Retrives the name of a system via its system index.
        /// </summary>
        /// <param name="systemIndex">System index to lookup the name for</param>
        /// <returns>Returns the name of the system. Otherwise an empty string</returns>
        public static NativeText.ReadOnly GetSystemName(int systemIndex)
        {
            var pUnsafeText = GetSystemNameInternal(systemIndex);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeText.ReadOnly ro = new NativeText.ReadOnly(pUnsafeText, SharedSafetyHandle.Ref.Data);
#else
            NativeText.ReadOnly ro = new NativeText.ReadOnly(pUnsafeText);
#endif
            return ro;
        }

        /// <summary>
        /// Returns the system index for System T.
        /// </summary>
        /// <typeparam name="T">System type</typeparam>
        /// <returns>Returns the System index for the input type T</returns>
        public static int GetSystemTypeIndex<T>()
        {
            var index = SharedSystemTypeIndex<T>.Ref.Data;

            if (index <= 0)
            {
                ManagedException<T>();
                BurstException<T>();
            }

            return index;
        }

        internal static long GetSystemTypeHash(Type type)
        {
            var systemIndex = GetSystemTypeIndex(type);
            return s_SystemTypeHashes[systemIndex];
        }

        internal static long GetSystemTypeHash<T>()
        {
            return GetHashCode64<T>();
        }

        internal static int GetSystemTypeSize(Type type)
        {
            int systemIndex = GetSystemTypeIndex(type);
            return s_SystemTypeSizes[systemIndex];
        }
        internal static int GetSystemTypeSize<T>()
        {
            int systemIndex = GetSystemTypeIndex<T>();
            return s_SystemTypeSizes[systemIndex];
        }


        internal static int GetSystemTypeIndexNoThrow(Type type)
        {
            if (type == null)
                return 0;

            int res;
            if (s_ManagedSystemTypeToIndex.TryGetValue(type, out res))
                return res;
            else
                return -1;
        }

        internal static int GetSystemTypeIndex(Type type)
        {
            int index = GetSystemTypeIndexNoThrow(type);
            if (index == -1)
                throw new ArgumentException($"The passed-in Type is not a type that derives from SystemBase or ISystem");
            return index;
        }

        internal static Type GetSystemType(int systemTypeIndex)
        {
            int typeIndexNoFlags = systemTypeIndex & SystemTypeInfo.kClearSystemTypeFlagsMask;
            Assertions.Assert.IsTrue(typeIndexNoFlags < s_SystemTypes.Count);
            return s_SystemTypes[typeIndexNoFlags];
        }

        /// <summary>
        /// Check if the provided type is a SystemGroup.
        /// </summary>
        /// <param name="type">Type to check</param>
        /// <returns>Returns true if the provided type is a SystemGroup (inherits from  <see cref="ComponentSystemGroup"/> or a sub-class of <see cref="ComponentSystemGroup"/>)</returns>
        public static bool IsSystemAGroup(Type type)
        {
            Assertions.Assert.IsTrue(s_Initialized, "The TypeManager must be initialized before the TypeManager can be used.");

            int index = GetSystemTypeIndex(type);
            return (s_SystemTypeFlagsList[index] & SystemTypeInfo.kIsSystemGroupFlag) != 0;
        }

        /// <summary>
        /// Check if the provided type is a managed system.
        /// </summary>
        /// <param name="type">Type to check</param>
        /// <returns>Returns true if the system type is managed. Otherwise false.</returns>
        public static bool IsSystemManaged(Type type)
        {
            Assertions.Assert.IsTrue(s_Initialized, "The TypeManager must be initialized before the TypeManager can be used.");

            int index = GetSystemTypeIndex(type);
            return (s_SystemTypeFlagsList[index] & SystemTypeInfo.kIsSystemManagedFlag) != 0;
        }

        /// <summary>
        /// Retrieve type flags for a system index.
        /// </summary>
        /// <param name="systemTypeIndex">System index to use for flag lookup</param>
        /// <returns>System type flags</returns>
        public static int GetSystemTypeFlags(int systemTypeIndex)
        {
            return s_SystemTypeFlagsList[systemTypeIndex];
        }

        /// <summary>
        /// Get all the attribute objects of Type attributeType for a System.
        /// </summary>
        /// <param name="systemType">System type</param>
        /// <param name="attributeType">Attribute type to return</param>
        /// <returns>Returns all attributes of type attributeType decorating systemType</returns>
        public static Attribute[] GetSystemAttributes(Type systemType, Type attributeType)
        {
            Assertions.Assert.IsTrue(s_Initialized, "The TypeManager must be initialized before the TypeManager can be used.");

#if !NET_DOTS
            Attribute[] attributes;
            var kDisabledCreationAttribute = typeof(DisableAutoCreationAttribute);
            if (attributeType == kDisabledCreationAttribute)
            {
                // We do not want to inherit DisableAutoCreation from some parent type (as that attribute explicitly states it should not be inherited)
                var objArr = systemType.GetCustomAttributes(attributeType, false);
                var attrList = new List<Attribute>();

                var alreadyDisabled = false;
                for (int i = 0; i < objArr.Length; i++)
                {
                    var attr = objArr[i] as Attribute;
                    attrList.Add(attr);

                    if (attr.GetType() == kDisabledCreationAttribute)
                        alreadyDisabled = true;
                }

                if (!alreadyDisabled && systemType.Assembly.GetCustomAttribute(attributeType) != null)
                {
                    attrList.Add(new DisableAutoCreationAttribute());
                }
                attributes = attrList.ToArray();
            }
            else
            {
                var objArr = systemType.GetCustomAttributes(attributeType, true);
                attributes = new Attribute[objArr.Length];
                for (int i = 0; i < objArr.Length; i++)
                {
                    attributes[i] = objArr[i] as Attribute;
                }
            }

            return attributes;
#else
            Attribute[] attr = GetSystemAttributes(systemType);
            int count = 0;
            for (int i = 0; i < attr.Length; ++i)
            {
                if (attr[i].GetType() == attributeType)
                {
                    ++count;
                }
            }
            Attribute[] result = new Attribute[count];
            count = 0;
            for (int i = 0; i < attr.Length; ++i)
            {
                if (attr[i].GetType() == attributeType)
                {
                    result[count++] = attr[i];
                }
            }
            return result;
#endif
        }

#if !UNITY_DOTSRUNTIME
        internal static IEnumerable<Type> GetTypesDerivedFrom(Type type)
        {
#if UNITY_EDITOR
            return UnityEditor.TypeCache.GetTypesDerivedFrom(type);
#else

            var types = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!TypeManager.IsAssemblyReferencingEntities(assembly))
                    continue;

                try
                {
                    var assemblyTypes = assembly.GetTypes();
                    foreach (var t in assemblyTypes)
                    {
                        if (type.IsAssignableFrom(t))
                            types.Add(t);
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    foreach (var t in e.Types)
                    {
                        if (t != null && type.IsAssignableFrom(t))
                            types.Add(t);
                    }

                    Debug.LogWarning($"DefaultWorldInitialization failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
                }
            }

            return types;
#endif
        }

        static bool FilterSystemType(Type type, LookupFlags lookupFlags)
        {
            // These types cannot be instantiated
            if (type.IsAbstract || type.ContainsGenericParameters)
                return false;

            // Only derivatives of ComponentSystemBase and structs implementing ISystem are systems
            if (!type.IsSubclassOf(typeof(ComponentSystemBase)) && !typeof(ISystem).IsAssignableFrom(type))
                throw new System.ArgumentException($"{type} must already be filtered by ComponentSystemBase or ISystem");

            // The auto-creation system instantiates using the default ctor, so if we can't find one, exclude from list
            if (type.IsClass && type.GetConstructor(Type.EmptyTypes) == null)
            {
                // the entire assembly can be marked for no-auto-creation (test assemblies are good candidates for this)
                var localDisableAllAutoCreation = Attribute.IsDefined(type.Assembly, typeof(DisableAutoCreationAttribute));
                var localDisableTypeAutoCreation = Attribute.IsDefined(type, typeof(DisableAutoCreationAttribute), false);
                if (!localDisableAllAutoCreation && !localDisableTypeAutoCreation)
                    Debug.LogWarning($"Missing default ctor on {type.FullName} (or if you don't want this to be auto-creatable, tag it with [DisableAutoCreation])");
                return false;
            }

            if (lookupFlags.OptionalFlags == WorldSystemFilterFlags.All)
                return true;

            // the entire assembly can be marked for no-auto-creation (test assemblies are good candidates for this)
            var disableAllAutoCreation = Attribute.IsDefined(type.Assembly, typeof(DisableAutoCreationAttribute));
            var disableTypeAutoCreation = Attribute.IsDefined(type, typeof(DisableAutoCreationAttribute), false);

            if (disableTypeAutoCreation || disableAllAutoCreation)
            {
                if (disableTypeAutoCreation && disableAllAutoCreation)
                    Debug.LogWarning($"Redundant [DisableAutoCreation] on {type.FullName} (attribute is already present on assembly {type.Assembly.GetName().Name}");

                return false;
            }

            var systemFlags = GetSystemFilterFlags(type);

            if ((lookupFlags.RequiredFlags & WorldSystemFilterFlags.Editor) != 0)
                lookupFlags.OptionalFlags |= WorldSystemFilterFlags.Editor;

            return (lookupFlags.OptionalFlags & systemFlags) != 0 && (lookupFlags.RequiredFlags & systemFlags) == lookupFlags.RequiredFlags;
        }

        static WorldSystemFilterFlags GetParentGroupDefaultFilterFlags(Type type)
        {
            if (!Attribute.IsDefined(type, typeof(UpdateInGroupAttribute), true))
            {
                // Fallback default
                return WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation;
            }
            var attrs = type.GetCustomAttributes<UpdateInGroupAttribute>(true);
            WorldSystemFilterFlags systemFlags = default;
            foreach (var uig in attrs)
            {
                var groupType = ((UpdateInGroupAttribute)uig).GroupType;
                var groupFlags = WorldSystemFilterFlags.Default;
                if (Attribute.IsDefined(groupType, typeof(WorldSystemFilterAttribute), true))
                {
                    groupFlags = groupType.GetCustomAttribute<WorldSystemFilterAttribute>(true).ChildDefaultFilterFlags;
                }
                if ((groupFlags & WorldSystemFilterFlags.Default) != 0)
                {
                    groupFlags &= ~WorldSystemFilterFlags.Default;
                    groupFlags |= GetParentGroupDefaultFilterFlags(groupType);
                }
                systemFlags |= groupFlags;
            }
            return systemFlags;
        }
        static WorldSystemFilterFlags MakeWorldFilterFlags(Type type)
        {
            // IMPORTANT: keep this logic in sync with SystemTypeGen.cs for DOTS Runtime
            WorldSystemFilterFlags systemFlags = WorldSystemFilterFlags.Default;

            if (Attribute.IsDefined(type, typeof(WorldSystemFilterAttribute), true))
                systemFlags = type.GetCustomAttribute<WorldSystemFilterAttribute>(true).FilterFlags;

            if ((systemFlags & WorldSystemFilterFlags.Default) != 0)
            {
                systemFlags &= ~WorldSystemFilterFlags.Default;
                systemFlags |= GetParentGroupDefaultFilterFlags(type);
            }

            if (Attribute.IsDefined(type, typeof(ExecuteInEditMode)))
                Debug.LogWarning($"{type} is decorated with {typeof(ExecuteInEditMode)}. Support for this attribute on systems is deprecated as it is meant for MonoBehaviours only. " +
                    $"Please use [WorldSystemFilter({nameof(WorldSystemFilterFlags.Editor)})] instead to ensure your system is added and runs in the Editor's default world.");

            if (Attribute.IsDefined(type, typeof(ExecuteAlways)))
            {
                // Until we formally deprecate ExecuteAlways, add in the Editor flag as this has the same meaning
                // When we deprecate uncomment the log error below
                Debug.LogWarning($"{type} is decorated with {typeof(ExecuteAlways)}. Support for this attribute on systems is deprecated as it is meant for MonoBehaviours only. " +
                    $"Please use the [WorldSystemFilter] attribute to specify if you want to ensure your system is added and runs in the Editor ({nameof(WorldSystemFilterFlags.Editor)}) " +
                    $"and/or Player ({nameof(WorldSystemFilterFlags.Default)}) default world. You can specify [WorldSystemFilter({nameof(WorldSystemFilterFlags.Editor)} | {nameof(WorldSystemFilterFlags.Default)}] for both.");

                systemFlags |= WorldSystemFilterFlags.Editor;
            }

            return systemFlags;
        }
#else
        static bool IsSystemDisabledForCreation(Type system)
        {
            return GetSystemAttributes(system, typeof(DisableAutoCreationAttribute)).Length > 0;
        }

        static object CreateSystem(Type systemType)
        {
            int systemIndex = 1; // 0 is reserved for null
            for (; systemIndex < s_SystemTypes.Count; ++systemIndex)
            {
                if (s_SystemTypes[systemIndex] == systemType)
                    break;
            }

            for (int i = 0; i < s_SystemTypeDelegateIndexRanges.Count; ++i)
            {
                if (systemIndex < s_SystemTypeDelegateIndexRanges[i])
                    return s_AssemblyCreateSystemFn[i](systemType);
            }

            throw new ArgumentException("No function was generated for the provided type.");
        }

        internal static Attribute[] GetSystemAttributes(Type system)
        {
            int typeIndexNoFlags = 1; // 0 is reserved for null
            for (; typeIndexNoFlags < s_SystemTypes.Count; ++typeIndexNoFlags)
            {
                if (s_SystemTypes[typeIndexNoFlags] == system)
                    break;
            }

            for (int i = 0; i < s_SystemTypeDelegateIndexRanges.Count; ++i)
            {
                if (typeIndexNoFlags < s_SystemTypeDelegateIndexRanges[i])
                    return s_AssemblyGetSystemAttributesFn[i](system);
            }

            throw new ArgumentException("No function was generated for the provided type.");
        }

        internal static void RegisterAssemblySystemTypes(TypeRegistry typeRegistry)
        {
            // Todo: consolidate this all to a SystemInfo Struct
            for(int i = 0; i < typeRegistry.SystemTypes.Length; ++i)
            {
                var type = typeRegistry.SystemTypes[i];
                var typeName = typeRegistry.SystemTypeNames[i];
                var typeSize = typeRegistry.SystemTypeSizes[i];
                var typeHash = typeRegistry.SystemTypeHashes[i];
                AddSystemTypeToTables(type, typeName, typeSize, typeHash, typeRegistry.SystemTypeFlags[i], typeRegistry.SystemFilterFlags[i]);
            }

            if (typeRegistry.SystemTypes.Length > 0)
            {
                s_SystemTypeDelegateIndexRanges.Add(s_SystemCount);

                s_AssemblyCreateSystemFn.Add(typeRegistry.CreateSystem);
                s_AssemblyGetSystemAttributesFn.Add(typeRegistry.GetSystemAttributes);
            }
        }

#endif
    }
}
