#if UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP
#define UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_EDITOR_WORLD
#endif

using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Assertions;
using UnityEngine;
using Unity.Collections;
#if !UNITY_DOTSRUNTIME
using UnityEngine.LowLevel;
#endif
#if !NET_DOTS
using System.Linq;
#endif

namespace Unity.Entities
{
    public static class DefaultWorldInitialization
    {
#pragma warning disable 0067 // unused variable
        /// <summary>
        /// Invoked after the default World is initialized.
        /// </summary>
        internal static event Action<World> DefaultWorldInitialized;

        /// <summary>
        /// Invoked after the Worlds are destroyed.
        /// </summary>
        internal static event Action DefaultWorldDestroyed;
#pragma warning restore 0067 // unused variable

#if !UNITY_DOTSRUNTIME
        static bool s_UnloadOrPlayModeChangeShutdownRegistered = false;

        /// <summary>
        /// Destroys Editor World when entering Play Mode without Domain Reload.
        /// RuntimeInitializeOnLoadMethod is called before the new scene is loaded, before Awake and OnEnable of MonoBehaviour.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void CleanupWorldBeforeSceneLoad()
        {
            DomainUnloadOrPlayModeChangeShutdown();
        }
#endif

        /// <summary>
        /// Ensures the current World destruction on shutdown or when entering/exiting Play Mode or Domain Reload.
        /// 1) When switching to Play Mode Editor World (if created) has to be destroyed:
        ///     - after the current scene objects are destroyed and OnDisable/Destroy are called,
        ///     - before game scene is loaded and Awake/OnEnable are called.
        /// 2) When switching to Edit Mode Game World has to be destroyed:
        ///     - after the current scene objects are destroyed and OnDisable/Destroy are called,
        ///     - before backup scene is loaded and Awake/OnEnable are called.
        /// 3) When Unloading Domain (as well as Editor/Player exit) Editor or Game World has to be destroyed:
        ///     - after OnDisable/OnBeforeSerialize are called,
        ///     - before AppDomain.DomainUnload.
        /// Point 1) is covered by RuntimeInitializeOnLoadMethod attribute.
        /// For points 2) and 3) there are no entry point in the Unity API and they have to be handled by a proxy MonoBehaviour
        /// which in OnDisable can drive the World cleanup for both Exit Play Mode and Domain Unload.
        /// </summary>
        static void RegisterUnloadOrPlayModeChangeShutdown()
        {
#if !UNITY_DOTSRUNTIME
            if (s_UnloadOrPlayModeChangeShutdownRegistered)
                return;

            var go = new GameObject { hideFlags = HideFlags.HideInHierarchy };
            if (Application.isPlaying)
                UnityEngine.Object.DontDestroyOnLoad(go);
            else
                go.hideFlags = HideFlags.HideAndDontSave;

            go.AddComponent<DefaultWorldInitializationProxy>().IsActive = true;

            s_UnloadOrPlayModeChangeShutdownRegistered = true;
#endif
        }

        internal static void DomainUnloadOrPlayModeChangeShutdown()
        {
#if !UNITY_DOTSRUNTIME
            if (!s_UnloadOrPlayModeChangeShutdownRegistered)
                return;

            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            foreach (var w in World.s_AllWorlds)
            {
                ScriptBehaviourUpdateOrder.RemoveWorldFromPlayerLoop(w, ref playerLoop);
            }
            PlayerLoop.SetPlayerLoop(playerLoop);

            World.DisposeAllWorlds();

            WordStorage.Instance.Dispose();
            WordStorage.Instance = null;

            s_UnloadOrPlayModeChangeShutdownRegistered = false;

            DefaultWorldDestroyed?.Invoke();
#endif
        }

        /// <summary>
        /// Initializes the default world or runs ICustomBootstrap if one is available.
        /// </summary>
        /// <param name="defaultWorldName">The name of the world that will be created. Unless there is a custom bootstrap.</param>
        /// <param name="editorWorld">Editor worlds by default only include systems with [ExecuteAlways]. If editorWorld is true, ICustomBootstrap will not be used.</param>
        public static World Initialize(string defaultWorldName, bool editorWorld = false)
        {
            RegisterUnloadOrPlayModeChangeShutdown();

            if (!editorWorld)
            {
                var bootStrap = CreateBootStrap();
                if (bootStrap != null && bootStrap.Initialize(defaultWorldName))
                {
                    Assert.IsTrue(World.DefaultGameObjectInjectionWorld != null,
                        $"ICustomBootstrap.Initialize() implementation failed to set " +
                        $"World.DefaultGameObjectInjectionWorld, despite returning true " +
                        $"(indicating the World has been properly initialized)");
                    return World.DefaultGameObjectInjectionWorld;
                }
            }

            var world = new World(defaultWorldName, editorWorld ? WorldFlags.Editor : WorldFlags.Game);
            World.DefaultGameObjectInjectionWorld = world;

            var systemList = GetAllSystems(WorldSystemFilterFlags.Default, editorWorld);
            AddSystemToRootLevelSystemGroupsInternal(world, systemList, systemList.Count);

#if !UNITY_DOTSRUNTIME
            ScriptBehaviourUpdateOrder.AddWorldToCurrentPlayerLoop(world);
#endif

            DefaultWorldInitialized?.Invoke(world);
            return world;
        }

        // NET_DOTS does not support .ToArray()
#if !NET_DOTS
        /// <summary>
        /// Adds the collection of systems to the world by injecting them into the root level system groups
        /// (InitializationSystemGroup, SimulationSystemGroup and PresentationSystemGroup)
        /// </summary>
        public static void AddSystemsToRootLevelSystemGroups(World world, IEnumerable<Type> systemTypes)
        {
            AddSystemsToRootLevelSystemGroups(world, systemTypes.ToArray());
        }
#endif

        /// <summary>
        /// Adds the collection of systems to the world by injecting them into the root level system groups
        /// (InitializationSystemGroup, SimulationSystemGroup and PresentationSystemGroup)
        /// </summary>
        public static void AddSystemsToRootLevelSystemGroups(World world, params Type[] systemTypes)
        {
            AddSystemToRootLevelSystemGroupsInternal(world, systemTypes, systemTypes.Length);
        }

        /// <summary>
        /// Adds the collection of systems to the world by injecting them into the root level system groups
        /// (InitializationSystemGroup, SimulationSystemGroup and PresentationSystemGroup)
        /// </summary>
        public static void AddSystemsToRootLevelSystemGroups(World world, IReadOnlyList<Type> systemTypes)
        {
            AddSystemToRootLevelSystemGroupsInternal(world, systemTypes, systemTypes.Count);
        }

        private static void AddSystemToRootLevelSystemGroupsInternal(World world, IEnumerable<Type> systemTypesOrig, int managedTypesCountOrig)
        {
            var initializationSystemGroup = world.GetOrCreateSystem<InitializationSystemGroup>();
            var simulationSystemGroup = world.GetOrCreateSystem<SimulationSystemGroup>();
            var presentationSystemGroup = world.GetOrCreateSystem<PresentationSystemGroup>();

            var managedTypes = new List<Type>();
            var unmanagedTypes = new List<Type>();

            foreach (var stype in systemTypesOrig)
            {
                if (typeof(ComponentSystemBase).IsAssignableFrom(stype))
                    managedTypes.Add(stype);
                else if (typeof(ISystemBase).IsAssignableFrom(stype))
                    unmanagedTypes.Add(stype);
                else
                    throw new InvalidOperationException("Bad type");
            }

            var systems = world.GetOrCreateSystemsAndLogException(managedTypes, managedTypes.Count);

            // Add systems to their groups, based on the [UpdateInGroup] attribute.
            foreach (var system in systems)
            {
                if (system == null)
                    continue;

                // Skip the built-in root-level system groups
                var type = system.GetType();
                if (type == typeof(InitializationSystemGroup) ||
                    type == typeof(SimulationSystemGroup) ||
                    type == typeof(PresentationSystemGroup))
                {
                    continue;
                }

                var updateInGroupAttributes = TypeManager.GetSystemAttributes(system.GetType(), typeof(UpdateInGroupAttribute));
                if (updateInGroupAttributes.Length == 0)
                {
                    simulationSystemGroup.AddSystemToUpdateList(system);
                }

                foreach (var attr in updateInGroupAttributes)
                {
                    var group = FindGroup(world, type, attr);
                    if (group != null)
                    {
                        group.AddSystemToUpdateList(system);
                    }
                }
            }

#if !UNITY_DOTSRUNTIME
            // Add unmanaged systems
            foreach (var type in unmanagedTypes)
            {
                SystemHandleUntyped sysHandle = world.CreateUnmanagedSystem(type);

                // Add systems to their groups, based on the [UpdateInGroup] attribute.

                var updateInGroupAttributes = TypeManager.GetSystemAttributes(type, typeof(UpdateInGroupAttribute));
                if (updateInGroupAttributes.Length == 0)
                {
                    simulationSystemGroup.AddUnmanagedSystemToUpdateList(sysHandle);
                }

                foreach (var attr in updateInGroupAttributes)
                {
                    ComponentSystemGroup groupSys = FindGroup(world, type, attr);

                    if (groupSys != null)
                    {
                        groupSys.AddUnmanagedSystemToUpdateList(sysHandle);
                    }
                }
            }
#endif


            // Update player loop
            initializationSystemGroup.SortSystems();
            simulationSystemGroup.SortSystems();
            presentationSystemGroup.SortSystems();
        }

        private static ComponentSystemGroup FindGroup(World world, Type systemType, Attribute attr)
        {
            var uga = attr as UpdateInGroupAttribute;

            if (uga == null)
                return null;

            if (!TypeManager.IsSystemAGroup(uga.GroupType))
            {
                throw new InvalidOperationException($"Invalid [UpdateInGroup] attribute for {systemType}: {uga.GroupType} must be derived from ComponentSystemGroup.");
            }
            if (uga.OrderFirst && uga.OrderLast)
            {
                throw new InvalidOperationException($"The system {systemType} can not specify both OrderFirst=true and OrderLast=true in its [UpdateInGroup] attribute.");
            }

            var groupSys = world.GetExistingSystem(uga.GroupType);
            if (groupSys == null)
            {
                // Warn against unexpected behaviour combining DisableAutoCreation and UpdateInGroup
                var parentDisableAutoCreation = TypeManager.GetSystemAttributes(uga.GroupType, typeof(DisableAutoCreationAttribute)).Length > 0;
                if (parentDisableAutoCreation)
                {
                    Debug.LogWarning($"A system {systemType} wants to execute in {uga.GroupType} but this group has [DisableAutoCreation] and {systemType} does not. The system will not be added to any group and thus not update.");
                }
                else
                {
                    Debug.LogWarning(
                        $"A system {systemType} could not be added to group {uga.GroupType}, because the group was not created. Fix these errors before continuing. The system will not be added to any group and thus not update.");
                }
            }

            return groupSys as ComponentSystemGroup;
        }

        /// <summary>
        /// Can be called when in edit mode in the editor to initialize a the default world.
        /// </summary>
        public static void DefaultLazyEditModeInitialize()
        {
#if UNITY_EDITOR
            if (World.DefaultGameObjectInjectionWorld == null)
            {
                // * OnDisable (Serialize monobehaviours in temporary backup)
                // * unload domain
                // * load new domain
                // * OnEnable (Deserialize monobehaviours in temporary backup)
                // * mark entered playmode / load scene
                // * OnDisable / OnDestroy
                // * OnEnable (Loading object from scene...)
                if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    // We are just gonna ignore this enter playmode reload.
                    // Can't see a situation where it would be useful to create something inbetween.
                    // But we really need to solve this at the root. The execution order is kind if crazy.
                    if (UnityEditor.EditorApplication.isPlaying)
                        Debug.LogError("Loading GameObjectEntity in Playmode but there is no active World");
                }
                else
                {
#if !UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_EDITOR_WORLD
                    Initialize("Editor World", true);
#endif
                }
            }
#endif
        }

        /// <summary>
        /// Calculates a list of all systems filtered with WorldSystemFilterFlags, [DisableAutoCreation] etc.
        /// </summary>
        /// <param name="filterFlags"></param>
        /// <param name="requireExecuteAlways">Optionally require that [ExecuteAlways] is present on the system. This is used when creating edit mode worlds.</param>
        /// <returns>The list of filtered systems</returns>
        public static IReadOnlyList<Type> GetAllSystems(WorldSystemFilterFlags filterFlags, bool requireExecuteAlways = false)
        {
            return TypeManager.GetSystems(filterFlags, requireExecuteAlways ? WorldSystemFilterFlags.Editor : WorldSystemFilterFlags.Default);
        }

        static ICustomBootstrap CreateBootStrap()
        {
#if !UNITY_DOTSRUNTIME
            var bootstrapTypes = TypeManager.GetTypesDerivedFrom(typeof(ICustomBootstrap));
            Type selectedType = null;

            foreach (var bootType in bootstrapTypes)
            {
                if (bootType.IsAbstract || bootType.ContainsGenericParameters)
                    continue;

                if (selectedType == null)
                    selectedType = bootType;
                else if (selectedType.IsAssignableFrom(bootType))
                    selectedType = bootType;
                else if (!bootType.IsAssignableFrom(selectedType))
                    Debug.LogError("Multiple custom ICustomBootstrap specified, ignoring " + bootType);
            }
            ICustomBootstrap bootstrap = null;
            if (selectedType != null)
                bootstrap = Activator.CreateInstance(selectedType) as ICustomBootstrap;

            return bootstrap;
#else
            throw new Exception("This method should have been replaced by code-gen.");
#endif
        }
    }
}
