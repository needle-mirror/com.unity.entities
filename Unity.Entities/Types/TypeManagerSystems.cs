using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Unity.Entities
{
    static public unsafe partial class TypeManager
    {
#pragma warning disable 414
        static int s_SystemCount;
#pragma warning restore 414
        static List<Type> s_SystemTypes = new List<Type>();
        static List<string> s_SystemTypeNames = new List<string>();
        static NativeList<bool> s_SystemIsGroupList;
        static NativeList<WorldSystemFilterFlags> s_SystemFilterFlagsList;
#if UNITY_DOTSRUNTIME
        static List<int> s_SystemTypeDelegateIndexRanges = new List<int>();
        static List<TypeRegistry.CreateSystemFn> s_AssemblyCreateSystemFn = new List<TypeRegistry.CreateSystemFn>();
        static List<TypeRegistry.GetSystemAttributesFn> s_AssemblyGetSystemAttributesFn = new List<TypeRegistry.GetSystemAttributesFn>();
#endif
        struct LookupFlags
        {
            public WorldSystemFilterFlags OptionalFlags;
            public WorldSystemFilterFlags RequiredFlags;
        }
        static Dictionary<LookupFlags, IReadOnlyList<Type>> s_SystemFilterTypeMap;

        // While we provide a public interface for the TypeManager the init/shutdown
        // of the TypeManager owned by the TypeManager so we mark these functions as internal
        private static void InitializeSystemsState()
        {
            s_SystemTypes = new List<Type>();
            s_SystemTypeNames = new List<string>();
            s_SystemIsGroupList = new NativeList<bool>(Allocator.Persistent);
            s_SystemFilterFlagsList = new NativeList<WorldSystemFilterFlags>(Allocator.Persistent);
            s_SystemFilterTypeMap = new Dictionary<LookupFlags, IReadOnlyList<Type>>();
            s_SystemCount = 0;
        }

        private static void ShutdownSystemsState()
        {
            s_SystemTypes.Clear();
            s_SystemTypeNames.Clear();
            s_SystemIsGroupList.Dispose();
            s_SystemFilterFlagsList.Dispose();
            s_SystemCount = 0;
        }

        /// <summary>
        /// Construct a System from a Type. Uses the same list in GetSystems()
        /// </summary>
        ///
        public static ComponentSystemBase ConstructSystem(Type systemType)
        {
#if !NET_DOTS
            if (!typeof(ComponentSystemBase).IsAssignableFrom(systemType))
                throw new ArgumentException($"'{systemType.FullName}' cannot be constructed as it does not inherit from ComponentSystemBase");
            return (ComponentSystemBase)Activator.CreateInstance(systemType);
#else
            Assertions.Assert.IsTrue(s_Initialized, "The TypeManager must be initialized before the TypeManager can be used.");

            var obj = CreateSystem(systemType);
            if (!(obj is ComponentSystemBase))
                throw new ArgumentException("Null casting in Construct System. Bug in TypeManager.");
            return obj as ComponentSystemBase;
#endif
        }

        public static T ConstructSystem<T>() where T : ComponentSystemBase
        {
            return (T)ConstructSystem(typeof(T));
        }

        public static T ConstructSystem<T>(Type systemType) where T : ComponentSystemBase
        {
            return (T)ConstructSystem(systemType);
        }

        /// <summary>
        /// Return an array of all System types available to the runtime matching the WorldSystemFilterFlags. By default,
        /// all systems available to the runtime is returned.
        /// </summary>
        public static IReadOnlyList<Type> GetSystems(WorldSystemFilterFlags filterFlags = WorldSystemFilterFlags.All, WorldSystemFilterFlags requiredFlags = WorldSystemFilterFlags.Default)
        {
            Assertions.Assert.IsTrue(requiredFlags > 0, "Must use a 'requiredFlags' greater than 0. If you want to get all systems with any flag, pass a filterFlag of WorldSystemFilterFlags.All");
            LookupFlags lookupFlags = new LookupFlags() { OptionalFlags = filterFlags, RequiredFlags = requiredFlags };

            if (s_SystemFilterTypeMap.TryGetValue(lookupFlags, out var systemTypes))
                return systemTypes;

#if !UNITY_DOTSRUNTIME
            var filteredSystemTypes = new List<Type>();
            foreach (var systemType in GetTypesDerivedFrom(typeof(ComponentSystemBase)))
            {
                if (FilterSystemType(systemType, lookupFlags))
                    filteredSystemTypes.Add(systemType);
            }

            foreach (var unmanagedSystemType in GetTypesDerivedFrom(typeof(ISystemBase)))
            {
                if (!unmanagedSystemType.IsValueType)
                    continue;

                if (FilterSystemType(unmanagedSystemType, lookupFlags))
                    filteredSystemTypes.Add(unmanagedSystemType);
            }

            s_SystemFilterTypeMap[lookupFlags] = filteredSystemTypes;
            return filteredSystemTypes;
#else
            Assertions.Assert.IsTrue(s_Initialized, "The TypeManager must be initialized before the TypeManager can be used.");
            var filteredSystemTypes = new List<Type>();
            if (lookupFlags.OptionalFlags == WorldSystemFilterFlags.All)
            {
                filteredSystemTypes = s_SystemTypes;
            }
            else
            {
                for (int i = 0; i < s_SystemTypes.Count; ++i)
                {
                    if (!IsSystemDisabledForCreation(s_SystemTypes[i]) && (s_SystemFilterFlagsList[i] & lookupFlags.OptionalFlags) >= lookupFlags.RequiredFlags)
                        filteredSystemTypes.Add(s_SystemTypes[i]);
                }
            }

            s_SystemFilterTypeMap[lookupFlags] = filteredSystemTypes;
            return filteredSystemTypes;
#endif
        }

        // Internal function used for tests
        internal static WorldSystemFilterFlags GetSystemFilterFlags(Type type)
        {
            WorldSystemFilterFlags systemFlags = WorldSystemFilterFlags.Default;
#if !NET_DOTS
            if (Attribute.IsDefined(type, typeof(WorldSystemFilterAttribute), true))
                systemFlags = type.GetCustomAttribute<WorldSystemFilterAttribute>(true).FilterFlags;

            if (Attribute.IsDefined(type, typeof(ExecuteAlways)))
            {
                // Until we formally deprecate ExecuteAlways, add in the Editor flag as this has the same meaning
                // When we deprecate uncomment the log error below
                //Debug.LogError($"{type} is decorated with {typeof(ExecuteAlways)}. Support for this attribute will be deprecated. Please use [WorldSystemFilter(WorldSystemFilterFlags.EditTime)] instead.");
                systemFlags |= WorldSystemFilterFlags.Editor;
            }
#else
            systemFlags = s_SystemFilterFlagsList[GetSystemTypeIndex(type)];
#endif
            return systemFlags;
        }

        public static bool IsSystemType(Type t)
        {
            return GetSystemTypeIndexNoThrow(t) != -1;
        }

        public static string GetSystemName(Type t)
        {
#if !NET_DOTS
            return t.FullName;
#else
            Assertions.Assert.IsTrue(s_Initialized, "The TypeManager must be initialized before the TypeManager can be used.");

            int index = GetSystemTypeIndex(t);
            return s_SystemTypeNames[index];
#endif
        }

        internal static int GetSystemTypeIndexNoThrow(Type t)
        {
            Assertions.Assert.IsTrue(s_Initialized, "The TypeManager must be initialized before the TypeManager can be used.");

            for (int i = 0; i < s_SystemTypes.Count; ++i)
            {
                if (t == s_SystemTypes[i]) return i;
            }
            return -1;
        }

        internal static int GetSystemTypeIndex(Type t)
        {
            int index = GetSystemTypeIndexNoThrow(t);
            if (index == -1)
                throw new ArgumentException($"The passed-in Type is not a type that derives from SystemBase or ISystemBase");
            return index;
        }

        public static bool IsSystemAGroup(Type t)
        {
#if !NET_DOTS
            return t.IsSubclassOf(typeof(ComponentSystemGroup));
#else
            Assertions.Assert.IsTrue(s_Initialized, "The TypeManager must be initialized before the TypeManager can be used.");

            int index = GetSystemTypeIndex(t);
            var isGroup = s_SystemIsGroupList[index];
            return isGroup;
#endif
        }

        /// <summary>
        /// Get all the attribute objects of Type attributeType for a System.
        /// </summary>
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
            // IMPORTANT: keep this logic in sync with SystemTypeGen.cs for DOTS Runtime
            WorldSystemFilterFlags systemFlags = WorldSystemFilterFlags.Default;

            // the entire assembly can be marked for no-auto-creation (test assemblies are good candidates for this)
            var disableAllAutoCreation = Attribute.IsDefined(type.Assembly, typeof(DisableAutoCreationAttribute));
            var disableTypeAutoCreation = Attribute.IsDefined(type, typeof(DisableAutoCreationAttribute), false);

            // these types obviously cannot be instantiated
            if (type.IsAbstract || type.ContainsGenericParameters)
            {
                if (disableTypeAutoCreation)
                    Debug.LogWarning($"Invalid [DisableAutoCreation] on {type.FullName} (only concrete types can be instantiated)");

                return false;
            }

            // only derivatives of ComponentSystemBase and structs implementing ISystemBase are systems
            if (!type.IsSubclassOf(typeof(ComponentSystemBase)) && !typeof(ISystemBase).IsAssignableFrom(type))
                throw new System.ArgumentException($"{type} must already be filtered by ComponentSystemBase");

            // the auto-creation system instantiates using the default ctor, so if we can't find one, exclude from list
            if (type.IsClass && type.GetConstructor(Type.EmptyTypes) == null)
            {
                // we want users to be explicit
                if (!disableTypeAutoCreation && !disableAllAutoCreation)
                    Debug.LogWarning($"Missing default ctor on {type.FullName} (or if you don't want this to be auto-creatable, tag it with [DisableAutoCreation])");

                return false;
            }

            if (lookupFlags.OptionalFlags == WorldSystemFilterFlags.All)
                return true;

            if (disableTypeAutoCreation || disableAllAutoCreation)
            {
                if (disableTypeAutoCreation && disableAllAutoCreation)
                    Debug.LogWarning($"Redundant [DisableAutoCreation] on {type.FullName} (attribute is already present on assembly {type.Assembly.GetName().Name}");

                return false;
            }

            if (Attribute.IsDefined(type, typeof(WorldSystemFilterAttribute), true))
                systemFlags = type.GetCustomAttribute<WorldSystemFilterAttribute>(true).FilterFlags;

            if ((lookupFlags.RequiredFlags & WorldSystemFilterFlags.Editor) != 0)
            {
                lookupFlags.OptionalFlags |= WorldSystemFilterFlags.Editor;

#if !UNITY_DOTSRUNTIME
                if (Attribute.IsDefined(type, typeof(ExecuteInEditMode)))
                    Debug.LogError($"{type} is decorated with {typeof(ExecuteInEditMode)}. Support for this attribute will be deprecated. Please use [WorldSystemFilter(WorldSystemFilterFlags.EditTime)] instead.");
#endif
                if (Attribute.IsDefined(type, typeof(ExecuteAlways)))
                {
                    // Until we formally deprecate ExecuteAlways, add in the Editor flag as this has the same meaning
                    // When we deprecate uncomment the log error below
                    //Debug.LogError($"{type} is decorated with {typeof(ExecuteAlways)}. Support for this attribute will be deprecated. Please use [WorldSystemFilter(WorldSystemFilterFlags.EditTime)] instead.");

                    systemFlags |= WorldSystemFilterFlags.Editor;
                }
            }

            return (lookupFlags.OptionalFlags & systemFlags) >= lookupFlags.RequiredFlags;
        }
#else
        static bool IsSystemDisabledForCreation(Type system)
        {
            return GetSystemAttributes(system, typeof(DisableAutoCreationAttribute)).Length > 0;
        }

        static object CreateSystem(Type systemType)
        {
            int systemIndex = 0;
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
            int typeIndexNoFlags = 0;
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
            foreach (var type in typeRegistry.SystemTypes)
            {
                s_SystemTypes.Add(type);
                s_SystemCount++;
            }

            foreach (var typeName in typeRegistry.SystemTypeNames)
            {
                s_SystemTypeNames.Add(typeName);
            }

            foreach (var isSystemGroup in typeRegistry.IsSystemGroup)
            {
                s_SystemIsGroupList.Add(isSystemGroup);
            }

            foreach (var flags in typeRegistry.SystemFilterFlags)
            {
                s_SystemFilterFlagsList.Add(flags);
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
