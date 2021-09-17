#define DETAIL_MARKERS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Serialization;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using ConversionFlags = Unity.Entities.GameObjectConversionUtility.ConversionFlags;
using UnityLogType = UnityEngine.LogType;
using UnityObject = UnityEngine.Object;
using UnityComponent = UnityEngine.Component;
using static Unity.Debug;

namespace Unity.Entities.Conversion
{
    enum ConversionState
    {
        NotConverting,
        Discovering,
        Converting,
    }

    [DisableAutoCreation]
    class GameObjectConversionMappingSystem : ComponentSystem
    {
        const int k_StaticMask = 1;
        const int k_EntityGUIDMask = 2;
        const int k_DisabledMask = 4;
        const int k_SceneSectionMask = 8;
        const int k_AllMask = 15;

        static List<UnityComponent>             s_ComponentsCache = new List<UnityComponent>();

        internal GameObjectConversionSettings   Settings { get; private set; }
        private IncrementalConversionContext    m_LiveConversionContext;
        private IncrementalConversionData       m_IncrementalConversionData;
        readonly EntityManager                  m_DstManager;
        readonly JournalingUnityLogger          m_JournalingUnityLogger;

        ConversionState                         m_ConversionState;
        int                                     m_BeginConvertingRefCount;

        ConversionJournalData                   m_JournalData;
        internal ref ConversionDependencies     Dependencies => ref m_LiveConversionContext.Dependencies;
        internal ref Scene                      Scene => ref m_LiveConversionContext.Scene;

        EntityArchetype[]                       m_Archetypes;

        // prefabs and everything they contain will be stored in this set, to be tagged with the Prefab component in dst world
        HashSet<GameObject>                     m_DstPrefabs = new HashSet<GameObject>();
        // each will be marked as a linked entity group containing all of its converted descendants in the dst world
        HashSet<GameObject>                     m_DstLinkedEntityGroups = new HashSet<GameObject>();
        // assets that were declared via DeclareReferencedAssets
        HashSet<UnityObject>                    m_DstAssets = new HashSet<UnityObject>();

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        // Used to reverse look-up GameObjects to process hybrid components at the end of conversion
        EntityQuery                             m_CompanionComponentsQuery;
        HashSet<ComponentType>                  m_CompanionTypeSet = new HashSet<ComponentType>();
        bool                                    m_CompanionQueryDirty = true;
        internal static string                  k_CreateCompanionGameObjectsMarkerName = "Create Companion GameObjects";
        ProfilerMarker                          m_CreateCompanionGameObjectsMarker = new ProfilerMarker(k_CreateCompanionGameObjectsMarkerName);
#endif

        internal ref ConversionJournalData      JournalData => ref m_JournalData;

        #if UNITY_EDITOR
        // Used for both systems and component types
        Dictionary<Type, bool>                  m_ConversionTypeLookupCache = new Dictionary<Type, bool>();
        #endif

        // Fast lookup for entity names while converting
        Dictionary<int, string>                 m_FastUnityObjectNameLookup = new Dictionary<int, string>();

        private EntityQuery                     m_SceneSectionEntityQuery;

        public bool  AddEntityGUID              => (Settings.ConversionFlags & ConversionFlags.AddEntityGUID) != 0;
        public bool  ForceStaticOptimization    => (Settings.ConversionFlags & ConversionFlags.ForceStaticOptimization) != 0;
        public bool  AssignName                 => (Settings.ConversionFlags & ConversionFlags.AssignName) != 0;
        public bool  IsLiveConversion                 => (Settings.ConversionFlags & (ConversionFlags.SceneViewLiveConversion | ConversionFlags.GameViewLiveConversion)) != 0;

        public EntityManager   DstEntityManager => Settings.DestinationWorld.EntityManager;
        public ConversionState ConversionState  => m_ConversionState;

        struct CachedCollections
        {
            public List<GameObject> OldGameObjects;
            public List<GameObject> NewGameObjects;
            public List<UnityEngine.Object> TmpObjects;
            public Stack<Transform> Transforms;
            public HashSet<GameObject> GameObjectsWithEntities;

            public void Init()
            {
                if (OldGameObjects != null)
                {
                    OldGameObjects.Clear();
                    NewGameObjects.Clear();
                    TmpObjects.Clear();
                    Transforms.Clear();
                    GameObjectsWithEntities.Clear();
                }
                else
                {
                    OldGameObjects = new List<GameObject>();
                    NewGameObjects = new List<GameObject>();
                    TmpObjects = new List<UnityEngine.Object>();
                    Transforms = new Stack<Transform>();
                    GameObjectsWithEntities = new HashSet<GameObject>();
                }
            }
        }

        private CachedCollections _cachedCollections;

        public GameObjectConversionMappingSystem(GameObjectConversionSettings settings)
        {
            Settings = settings;
            m_DstManager = Settings.DestinationWorld.EntityManager;
            m_JournalingUnityLogger = new JournalingUnityLogger(this);
            m_JournalData.Init();
            m_IncrementalConversionData = IncrementalConversionData.Create();
            _cachedCollections = default;
            _cachedCollections.Init();
            m_LiveConversionContext = new IncrementalConversionContext(IsLiveConversion);

            InitArchetypes();
        }

        public void PrepareForLiveConversion(Scene scene)
        {
            var gameObjects = scene.GetRootGameObjects();
            using (new ProfilerMarker("Build Incremental Hierarchy").Auto())
            {
                m_LiveConversionContext.InitializeHierarchy(scene, gameObjects);
            }
        }

        protected override void OnUpdate() {}

        protected override void OnDestroy()
        {
            if (m_BeginConvertingRefCount > 0)
                CleanupConversion();
            if (EntityManager.IsQueryValid(m_SceneSectionEntityQuery))
                m_SceneSectionEntityQuery.Dispose();
            if (m_EntityIdsChached.IsCreated)
                m_EntityIdsChached.Dispose();
            m_JournalData.Dispose();
            m_LiveConversionContext.Dispose();
            m_IncrementalConversionData.Dispose();
        }

        void CleanupConversion()
        {
            m_JournalingUnityLogger.Unhook();
            s_ComponentsCache.Clear();

            m_ConversionState = ConversionState.NotConverting;
            m_BeginConvertingRefCount = 0;
        }

        void InitArchetypes()
        {
            m_Archetypes = new EntityArchetype[k_AllMask + 1];
            var types = new List<ComponentType>();

            for (int i = 0; i <= k_AllMask; i++)
            {
                types.Clear();
                if ((i & k_StaticMask) != 0)
                    types.Add(typeof(Static));
                if ((i & k_EntityGUIDMask) != 0)
                    types.Add(typeof(EntityGuid));
                if ((i & k_DisabledMask) != 0)
                    types.Add(typeof(Disabled));
                if ((i & k_SceneSectionMask) != 0)
                    types.Add(typeof(SceneSection));

                m_Archetypes[i] = m_DstManager.CreateArchetype(types.ToArray());
            }
        }

        public void BeginConversion()
        {
            if (ConversionState == ConversionState.Converting)
                throw new InvalidOperationException("Cannot BeginConversion after conversion has started (call EndConversion first)");

            ++m_BeginConvertingRefCount;

            if (ConversionState == ConversionState.NotConverting)
            {
                m_ConversionState = ConversionState.Discovering;

                m_JournalingUnityLogger.Hook();
            }
        }

        public void EndConversion()
        {
            if (m_BeginConvertingRefCount == 0)
                throw new InvalidOperationException("Conversion has not started");

            if (--m_BeginConvertingRefCount == 0)
                CleanupConversion();
        }

#if DETAIL_MARKERS
        static ProfilerMarker m_CreateEntity = new ProfilerMarker("GameObjectConversion.CreateEntity");
        static ProfilerMarker m_CreatePrimaryEntities = new ProfilerMarker("GameObjectConversion.CreatePrimaryEntities");
        static ProfilerMarker m_CreateAdditional = new ProfilerMarker("GameObjectConversionCreateAdditionalEntity");
        static readonly ProfilerMarker IncrementalClearEntities = new ProfilerMarker("IncrementalClearEntities");
        static readonly ProfilerMarker IncrementalCreateEntitiesOld = new ProfilerMarker("IncrementalCreateEntitiesOld");
        static readonly ProfilerMarker IncrementalCreateEntitiesNew = new ProfilerMarker("IncrementalCreateEntitiesNew");
        static readonly ProfilerMarker IncrementalDeleteEntities = new ProfilerMarker("IncrementalDeleteEntities");
        static readonly ProfilerMarker IncrementalCollectDependencies = new ProfilerMarker("IncrementalCollectDependencies");
#endif

        int ComputeArchetypeFlags(UnityObject uobject)
        {
            int flags = 0;
            if (AddEntityGUID)
                flags |= k_EntityGUIDMask;

            var go = uobject as GameObject;
            if (go != null)
            {
                if (ForceStaticOptimization || go.GetComponentInParent<StaticOptimizeEntity>(true) != null)
                    flags |= k_StaticMask;
                if (!go.IsActiveIgnorePrefab())
                    flags |= k_DisabledMask;
            }
            else if (uobject is UnityComponent)
                throw new ArgumentException("Object must be a GameObject, Prefab, or Asset", nameof(uobject));

            if (Settings.SceneGUID != default)
                flags |= k_SceneSectionMask;
            return flags;
        }

        Entity CreateDstEntity(UnityObject uobject, int serial)
        {
            Entity returnValue;
            unsafe
            {
                var arr = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(&returnValue, 1, Allocator.Invalid);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref arr, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
                CreateDstEntity(uobject, arr, serial);
            }
            return returnValue;
        }

        void CreateDstEntity(UnityObject uobject, NativeArray<Entity> outEntities, int serial)
        {
            if (outEntities.Length == 0)
                return;
            #if DETAIL_MARKERS
            using (m_CreateEntity.Auto())
            #endif
            {
                int flags = ComputeArchetypeFlags(uobject);
                m_DstManager.CreateEntity(m_Archetypes[flags], outEntities);
                if ((flags & k_EntityGUIDMask) != 0)
                {
#pragma warning disable 0618
                    uint namespaceId = Settings.NamespaceID ^ (uint)m_LiveConversionContext.Scene.handle;
                    for (int i = 0; i < outEntities.Length; i++)
                        m_DstManager.SetComponentData(outEntities[i], uobject.ComputeEntityGuid(namespaceId, serial + i));
#pragma warning restore 0618
                }

                if (Settings.SceneGUID != default)
                {
                    int sectionIndex = 0;
                    var go = uobject as GameObject;
                    if (go != null)
                    {
                        var section = go.GetComponentInParent<SceneSectionComponent>(true);
                        if (section != null)
                        {
                            sectionIndex = section.SectionIndex;
                        }
                    }

                    for (int i = 0; i < outEntities.Length; i++)
                    {
                        m_DstManager.AddSharedComponentData(outEntities[i], new SceneSection { SceneGUID = Settings.SceneGUID, Section = sectionIndex });
                    }
                }

#if !DOTS_DISABLE_DEBUG_NAMES
                if (AssignName)
                {
                    for (int i = 0; i < outEntities.Length; i++)
                        m_DstManager.SetName(outEntities[i], GetUnityObjectName(uobject));
                }
#endif
            }
        }

        public string GetUnityObjectName(UnityObject uobj)
        {
            var iid = uobj.GetInstanceID();
            if (m_FastUnityObjectNameLookup.TryGetValue(iid, out var name))
                return name;
            name = uobj.name;
            m_FastUnityObjectNameLookup[iid] = name;
            return name;
        }

        public Entity GetSceneSectionEntity(Entity entity)
        {
            var sceneSection = m_DstManager.GetSharedComponentData<SceneSection>(entity);
            return SerializeUtility.GetSceneSectionEntity(sceneSection.Section, m_DstManager, ref m_SceneSectionEntityQuery);
        }

        public Entity CreatePrimaryEntity(UnityObject uobject)
        {
            if (uobject == null)
                throw new ArgumentNullException(nameof(uobject), $"{nameof(CreatePrimaryEntity)} must be called with a valid UnityEngine.Object");

            var entity = CreateDstEntity(uobject, 0);
            m_JournalData.RecordPrimaryEntity(uobject.GetInstanceID(), entity);
            return entity;
        }

        void CreatePrimaryEntitiesRecurse(GameObject go)
        {
            var stack = _cachedCollections.Transforms;
            stack.Clear();
            stack.Push(go.transform);
            while (stack.Count > 0)
            {
                var top = stack.Pop();
                // Check that we do not already have an entity. Might be the case when both a parent and a child are
                // separately added to the conversion.
                if (!m_JournalData.TryGetPrimaryEntity(top.gameObject.GetInstanceID(), out _))
                    CreatePrimaryEntity(top.gameObject);

                for (int i = 0, n = top.childCount; i < n; i++)
                    stack.Push(top.GetChild(i));
            }
        }

        public void CreatePrimaryEntities()
        {
            if (ConversionState != ConversionState.Discovering)
                throw new InvalidOperationException("Unexpected conversion state transition");

            m_ConversionState = ConversionState.Converting;

            #if DETAIL_MARKERS
            using (m_CreatePrimaryEntities.Auto())
            #endif
            {
                Entities.WithIncludeAll().ForEach((Transform transform) =>
                {
                    CreatePrimaryEntity(transform.gameObject);
                });

                Entities.WithIncludeAll().ForEach((RectTransform transform) =>
                {
                    CreatePrimaryEntity(transform.gameObject);
                });

                Entities.WithAll<Asset>().ForEach(entity =>
                {
                    using (var types = EntityManager.GetComponentTypes(entity))
                    {
                        var derivedType = types.FirstOrDefault(t => typeof(UnityObject).IsAssignableFrom(t.GetManagedType()));
                        if (derivedType.TypeIndex == 0)
                            throw new Exception("Expected to find a UnityEngine.Object-derived component type in this entity");

                        var asset = EntityManager.GetComponentObject<UnityObject>(entity, derivedType);
                        CreatePrimaryEntity(asset);
                    }
                });
            }
        }

#if UNITY_EDITOR
        public T GetBuildConfigurationComponent<T>() where T : Build.IBuildComponent
        {
            if (Settings.BuildConfiguration == null)
            {
                return default;
            }
            return Settings.BuildConfiguration.GetComponent<T>();
        }

        public bool TryGetBuildConfigurationComponent<T>(out T component) where T : Build.IBuildComponent
        {
            if (Settings.BuildConfiguration == null)
            {
                component = default;
                return false;
            }
            return Settings.BuildConfiguration.TryGetComponent(out component);
        }

        /// <summary>
        /// Returns whether a GameObjectConversionSystem of the given type, or a IConvertGameObjectToEntity
        /// MonoBehaviour, should execute its conversion methods. Typically used in an implementation
        /// of GameObjectConversionSystem.ShouldRunConversionSystem
        /// </summary>
        public bool ShouldRunConversion(Type conversionSystemType)
        {
            if (!m_ConversionTypeLookupCache.TryGetValue(conversionSystemType, out var shouldRun))
            {
                var hasFilter = TryGetBuildConfigurationComponent<ConversionSystemFilterSettings>(out var filter);

                shouldRun = (!hasFilter || filter.ShouldRunConversionSystem(conversionSystemType));

                m_ConversionTypeLookupCache[conversionSystemType] = shouldRun;
            }
            return shouldRun;
        }

        /// <summary>
        /// Returns whether the current build configuration includes the given types at runtime.
        /// Typically used in an implementation of GameObjectConversionSystem.ShouldRunConversionSystem,
        /// but can also be used to make more detailed decisions.
        /// </summary>
        public bool BuildHasType(Type componentType)
        {
            if (!m_ConversionTypeLookupCache.TryGetValue(componentType, out var hasType))
            {
                // TODO -- check using a TypeCache obtained from the build configuration
                hasType = true;

                m_ConversionTypeLookupCache[componentType] = hasType;
            }

            return hasType;
        }

        /// <summary>
        /// Returns whether the current build configuration includes the given types at runtime.
        /// Typically used in an implementation of GameObjectConversionSystem.ShouldRunConversionSystem,
        /// but can also be used to make more detailed decisions.
        /// </summary>
        public bool BuildHasType(params Type[] componentTypes)
        {
            foreach (var type in componentTypes)
            {
                if (!BuildHasType(type))
                    return false;
            }

            return true;
        }

        public bool IsBuildingForEditor
        {
            get
            {
                return (Settings.ConversionFlags & ConversionFlags.IsBuildingForPlayer) == 0;
            }
        }

        public UnityEditor.GUID BuildConfigurationGUID
        {
            get { return Settings.BuildConfigurationGUID; }
        }

#endif // UNITY_EDITOR

        public Entity TryGetPrimaryEntity(UnityObject uobject)
        {
            if (uobject == null)
                return Entity.Null;

            if (!m_JournalData.TryGetPrimaryEntity(uobject.GetInstanceID(), out var entity))
                uobject.CheckObjectIsNotComponent();

            return entity;
        }

        static string MakeUnknownObjectMessage<T>(T uobject, [CallerMemberName] string methodName = "")
            where T : UnityObject
        {
            uobject.CheckObjectIsNotComponent(); // cannot get here by user code - all front end API's should auto-fetch owning GameObject

            var sb = new StringBuilder();

            sb.Append(methodName);
            sb.Append($"({typeof(T).Name} '{uobject.name}')");

            if (uobject.IsAsset())
            {
                sb.Append(" is an Asset that was not declared for conversion and will be ignored. ");
                sb.Append($"(Did you forget to declare it using a [UpdateInGroup(typeof({nameof(GameObjectDeclareReferencedObjectsGroup)})] system?)");
            }
            else if (uobject.IsPrefab())
            {
                sb.Append(" is a Prefab that was not declared for conversion and will be ignored. ");
                sb.Append($"(Did you forget to declare it using {nameof(IDeclareReferencedPrefabs)} or via a [UpdateInGroup(typeof({nameof(GameObjectDeclareReferencedObjectsGroup)})] system?)");
            }
            else
                sb.Append(" is a GameObject that was not included in the conversion and will be ignored.");

            return sb.ToString();
        }

        public Entity GetPrimaryEntity(UnityObject uobject)
        {
            var entity = TryGetPrimaryEntity(uobject);
            if (entity == Entity.Null && uobject != null)
                LogWarning(MakeUnknownObjectMessage(uobject), uobject);

            return entity;
        }

        Entity GetPrimaryEntity(int instanceId)
        {
            m_JournalData.TryGetPrimaryEntity(instanceId, out var entity);
            return entity;
        }

        public Entity CreateAdditionalEntity(UnityObject uobject)
        {
            #if DETAIL_MARKERS
            using (m_CreateAdditional.Auto())
            #endif
            {
                if (uobject == null)
                    throw new ArgumentNullException(nameof(uobject), $"{nameof(CreateAdditionalEntity)} must be called with a valid UnityEngine.Object");

                var(id, serial) = m_JournalData.ReserveAdditionalEntity(uobject.GetInstanceID());
                if (serial == 0)
                    throw new ArgumentException(MakeUnknownObjectMessage(uobject), nameof(uobject));

                var entity = CreateDstEntity(uobject, serial);
                m_JournalData.RecordAdditionalEntityAt(id, entity);
                return entity;
            }
        }

        private UnsafeList<int> m_EntityIdsChached;
        public unsafe void CreateAdditionalEntities(UnityObject uobject, NativeArray<Entity> outEntities)
        {
            #if DETAIL_MARKERS
            using (m_CreateAdditional.Auto())
            #endif
            {
                if (uobject == null)
                    throw new ArgumentNullException(nameof(uobject), $"{nameof(CreateAdditionalEntity)} must be called with a valid UnityEngine.Object");
                if (!m_EntityIdsChached.IsCreated)
                    m_EntityIdsChached = new UnsafeList<int>(outEntities.Length, Allocator.Persistent);
                else
                    m_EntityIdsChached.Resize(outEntities.Length);

                int* ids = m_EntityIdsChached.Ptr;
                int serial = m_JournalData.ReserveAdditionalEntities(uobject.GetInstanceID(), ids, outEntities.Length);
                if (serial == 0)
                    throw new ArgumentException(MakeUnknownObjectMessage(uobject), nameof(uobject));

                CreateDstEntity(uobject, outEntities, serial);
                for (int i = 0; i < outEntities.Length; i++)
                {
                    m_JournalData.RecordAdditionalEntityAt(ids[i], outEntities[i]);
                }
            }
        }

        public void BeginIncrementalConversionPreparation(ConversionFlags flags, ref IncrementalConversionBatch arguments)
        {
            if (Settings.ConversionFlags != flags)
                throw new ArgumentException("Conversion flags don't match");

            if (!IsLiveConversion)
                throw new InvalidOperationException(
                    "Incremental conversion can only be used when the conversion world was specifically created for it");

            if (!EntityManager.UniversalQuery.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("Conversion world is expected to be empty");

            m_DstLinkedEntityGroups.Clear();

            m_IncrementalConversionData.Clear();
            m_LiveConversionContext.UpdateHierarchy(arguments, ref m_IncrementalConversionData);

            var incrementalSystem = World.GetExistingSystem<IncrementalChangesSystem>();
            incrementalSystem.SceneHierarchy.Hierarchy = m_LiveConversionContext.Hierarchy.AsReadOnly();
            incrementalSystem.SceneHierarchy.TransformAccessArray = m_LiveConversionContext.Hierarchy.TransformArray;
            incrementalSystem.ConvertedEntities = m_JournalData.GetConvertedEntitiesAccessor();
            incrementalSystem.IncomingChanges = m_IncrementalConversionData.ToChanges();
        }

        public void FinishIncrementalConversionPreparation()
        {
            var incrementalSystem = World.GetExistingSystem<IncrementalChangesSystem>();
            incrementalSystem.ExtractRequests(m_IncrementalConversionData);

#if DETAIL_MARKERS
            using (IncrementalDeleteEntities.Auto())
#endif
            {
                // TODO: Once hybrid components have been refactored, this whole thing can go through Burst (DOTS-3420)
                var toRemoveLinkedEntityGroup = new NativeList<Entity>(m_IncrementalConversionData.RemovedInstanceIds.Length, Allocator.Temp);
                var toDestroy = new NativeList<Entity>(m_IncrementalConversionData.RemovedInstanceIds.Length, Allocator.Temp);
                foreach (var instanceId in m_IncrementalConversionData.RemovedInstanceIds)
                    DeleteIncrementalConversion(instanceId, toRemoveLinkedEntityGroup, toDestroy);
                m_DstManager.RemoveComponent<LinkedEntityGroup>(toRemoveLinkedEntityGroup);
                m_DstManager.DestroyEntity(toDestroy);
            }

            _cachedCollections.Init();
            List<GameObject> oldGameObjects = _cachedCollections.OldGameObjects;
            List<GameObject> newGameObjects = _cachedCollections.NewGameObjects;
            NativeArray<int> dependentInstances;
#if DETAIL_MARKERS
            using (IncrementalCollectDependencies.Auto())
#endif
            {
                using (var instancesToConvert =
                    m_LiveConversionContext.CollectAndClearDependencies(m_IncrementalConversionData))
                {
                    dependentInstances = instancesToConvert.ToNativeArray(Allocator.TempJob);
                }
            }

            using (dependentInstances)
            {
#if DETAIL_MARKERS
                using (IncrementalClearEntities.Auto())
#endif
                {
                    var objs = _cachedCollections.TmpObjects;
                    Resources.InstanceIDToObjectList(dependentInstances, objs);
                    var scene = m_LiveConversionContext.Scene;
                    for (int i = 0; i < dependentInstances.Length; i++)
                    {
                        var go = objs[i] as GameObject;
                        if (go == null || go.scene != scene)
                        {
                            // This case can happen e.g. when a GameObject is reparented to another object but that
                            // new parent is then deleted later or moved to another scene.
                            // Alternatively, this might happen for example when using prefabs: Deleting a child that
                            // has a dependency on the root of the prefab would trigger this as well.
                            continue;
                        }
                        if (ClearIncrementalConversion(dependentInstances[i], go))
                            oldGameObjects.Add(go);
                        else
                            newGameObjects.Add(go);
                    }
                }
            }

#if UNITY_EDITOR
            if (LiveConversionSettings.IsDebugLoggingEnabled)
                LogIncrementalConversion(newGameObjects, oldGameObjects);
#endif

            m_IncrementalConversionData.Clear();

#if DETAIL_MARKERS
            using (IncrementalCreateEntitiesOld.Auto())
#endif
            {
                foreach (var go in oldGameObjects)
                {
                    // this check is necessary so enabling/disabling GOs works as expected
                    if (!go.activeSelf)
                        DeclareLinkedEntityGroup(go);

                    _cachedCollections.GameObjectsWithEntities.Add(go);
                    CreateGameObjectEntity(EntityManager, go, s_ComponentsCache);
                }
            }

#if DETAIL_MARKERS
            using (IncrementalCreateEntitiesNew.Auto())
#endif
            {
                foreach (var go in newGameObjects)
                {
                    if (_cachedCollections.GameObjectsWithEntities.Contains(go))
                        continue;
                    CreatePrimaryEntitiesRecurse(go);
                    CreateEntitiesForGameObjectsRecurse(go.transform, _cachedCollections.GameObjectsWithEntities);
                }
            }
        }

        static void LogIncrementalConversion(List<GameObject> newObjects, List<GameObject> oldObjects)
        {
            var sb = new StringBuilder();
            sb.Append("Reconverting ");
            sb.Append(newObjects.Count + oldObjects.Count);
            sb.AppendLine(" GameObjects:");
            foreach (var go in newObjects)
                sb.AppendLine(go.name);
            foreach (var go in oldObjects)
                sb.AppendLine(go.name);
            Debug.Log(sb.ToString());
        }

        static void CheckCanConvertIncrementally(EntityManager manager, Entity entity)
        {
            if (manager.HasComponent<Prefab>(entity))
                throw new ArgumentException("An Entity with a Prefab tag cannot be destroyed during incremental conversion");
        }

        void DeleteIncrementalConversion(int instanceId, NativeList<Entity> toRemoveLinkedEntityGroup, NativeList<Entity> toDestroy)
        {
            if (m_JournalData.GetEntities(instanceId, out var entities))
            {
                entities.MoveNext();
                var primaryEntity = entities.Current;
                CheckCanConvertIncrementally(m_DstManager, primaryEntity);

                while (entities.MoveNext())
                {
                    var entity = entities.Current;
                    CheckCanConvertIncrementally(m_DstManager, entity);
                    toDestroy.Add(entity);
                }

                m_JournalData.RemoveForIncremental(instanceId, null);
                m_JournalData.RemovePrimaryEntity(instanceId);
                // If this entity had a linked entity group, we should remove it before destroying it. It could be that
                // one of the entities in the linked entity group is getting re-converted some other way, i.e. the data
                // in the linked entity group is stale at this point.
                toRemoveLinkedEntityGroup.Add(primaryEntity);
                toDestroy.Add(primaryEntity);
            }
        }

        bool ClearIncrementalConversion(int instanceId, GameObject go)
        {
            if (m_JournalData.GetEntities(instanceId, out var entities))
            {
                entities.MoveNext();
                var primaryEntity = entities.Current;
                CheckCanConvertIncrementally(m_DstManager, primaryEntity);

                var archetype = m_Archetypes[ComputeArchetypeFlags(go)];
                m_DstManager.SetArchetype(primaryEntity, archetype);

                while (entities.MoveNext())
                {
                    var entity = entities.Current;
                    CheckCanConvertIncrementally(m_DstManager, entity);
                    m_DstManager.DestroyEntity(entity);
                }

                m_JournalData.RemoveForIncremental(instanceId, go);
                return true;
            }

            m_JournalData.RemoveForIncremental(instanceId, go);
            return false;
        }

        /// <summary>
        /// Is the game object included in the set of converted objects.
        /// </summary>
        public bool HasPrimaryEntity(UnityObject uobject)
        {
            if (uobject == null)
                return false;

            var found = m_JournalData.HasPrimaryEntity(uobject.GetInstanceID());
            if (!found)
                uobject.CheckObjectIsNotComponent();

            return found;
        }

        public MultiListEnumerator<Entity> GetEntities(UnityObject uobject)
        {
            if (uobject == null)
                return MultiListEnumerator<Entity>.Empty;

            if (!m_JournalData.GetEntities(uobject.GetInstanceID(), out var iter))
                uobject.CheckObjectIsNotComponent();

            return iter;
        }

        public void AddGameObjectOrPrefab(GameObject gameObjectOrPrefab)
        {
            if (ConversionState != ConversionState.Discovering)
                throw new InvalidOperationException("AddGameObjectOrPrefab can only be called from a System using [UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup))].");

            if (gameObjectOrPrefab == null)
                return;
            if (m_DstPrefabs.Contains(gameObjectOrPrefab))
                return;

            m_DstLinkedEntityGroups.Add(gameObjectOrPrefab);

            var outDiscoveredGameObjects = gameObjectOrPrefab.IsPrefab() ? m_DstPrefabs : null;
            CreateEntitiesForGameObjectsRecurse(gameObjectOrPrefab.transform, outDiscoveredGameObjects);
        }

        /// <summary>
        /// Adds a LinkedEntityGroup to the primary entity of this GameObject, for all entities that are created from this and all child game objects.
        /// As a result EntityManager.Instantiate and EntityManager.SetEnabled will work on those entities as a group.
        /// </summary>
        public void DeclareLinkedEntityGroup(GameObject gameObject)
        {
            m_DstLinkedEntityGroups.Add(gameObject);
        }

        public void ConfigureEditorRenderData(Entity entity, GameObject pickableObject, bool hasGameObjectBasedRenderingRepresentation)
        {
            #if UNITY_EDITOR
            //@TODO: Dont apply to prefabs (runtime instances should just be pickable by the scene ... Custom one should probably have a special culling mask though?)

            bool liveConversionScene = (Settings.ConversionFlags & ConversionFlags.SceneViewLiveConversion) != 0;
            //bool liveConversionGameView = (Settings.ConversionFlags & ConversionFlags.GameViewLiveConversion) != 0;

            // NOTE: When no live link is present all entities will simply be batched together for the whole scene

            // In SceneView Live Link mode we want to show the original MeshRenderer
            if (hasGameObjectBasedRenderingRepresentation && liveConversionScene)
            {
                var sceneCullingMask = UnityEditor.GameObjectUtility.ModifyMaskIfGameObjectIsHiddenForPrefabModeInContext(
                    UnityEditor.SceneManagement.EditorSceneManager.DefaultSceneCullingMask,
                    pickableObject);
                m_DstManager.AddSharedComponentData(entity, new EditorRenderData
                {
                    PickableObject = pickableObject,
                    SceneCullingMask = sceneCullingMask
                });
            }
            // Code never hit currently so outcommented:
            // When live linking game view, we still want custom renderers to be pickable.
            // Otherwise they will not even be visible in scene view at all.
            //else if (!hasGameObjectBasedRenderingRepresentation && liveLinkGameView)
            //{
            //    m_DstManager.AddSharedComponentData(entity, new EditorRenderData
            //    {
            //        PickableObject = pickableObject,
            //        SceneCullingMask = EditorRenderData.LiveLinkEditGameViewMask | EditorRenderData.LiveLinkEditSceneViewMask
            //    });
            //}
            #endif
        }

        /// <summary>
        /// DeclareReferencedPrefab includes the referenced Prefab in the conversion process.
        /// Once it has been declared you can use GetPrimaryEntity to find the Entity for the GameObject.
        /// All entities in the Prefab will be made part of the LinkedEntityGroup, thus Instantiate will clone the whole group.
        /// All entities in the Prefab will be tagged with the Prefab component thus will not be picked up by an EntityQuery by default.
        /// </summary>
        public void DeclareReferencedPrefab(GameObject prefab)
        {
            if (ConversionState != ConversionState.Discovering)
                throw new InvalidOperationException($"{nameof(DeclareReferencedPrefab)} can only be called from a System using [UpdateInGroup(typeof({nameof(GameObjectDeclareReferencedObjectsGroup)}))].");

            if (prefab == null)
                return;

            if (m_DstPrefabs.Contains(prefab))
                return;

            if (!prefab.IsPrefab())
            {
                LogWarning($"Object {prefab.name} is not a Prefab", prefab);
                return;
            }

            m_DstLinkedEntityGroups.Add(prefab);
            CreateEntitiesForGameObjectsRecurse(prefab.transform, m_DstPrefabs);
        }

        public void DeclareReferencedAsset(UnityObject asset)
        {
            if (ConversionState != ConversionState.Discovering)
                throw new InvalidOperationException($"{nameof(DeclareReferencedAsset)} can only be called from a System using [UpdateInGroup(typeof({nameof(GameObjectDeclareReferencedObjectsGroup)}))].");

            if (asset == null)
                return;

            if (m_DstAssets.Contains(asset))
                return;

            if (!asset.IsAsset())
            {
                LogWarning($"Object {asset.name} is not an Asset", asset);
                return;
            }

            m_DstAssets.Add(asset);

            var entity = EntityManager.CreateEntity(typeof(Asset), asset.GetType());

            EntityManager.SetComponentObject(entity, asset.GetType(), asset);
        }

        public Hash128 GetGuidForAssetExport(UnityObject asset)
        {
            if (!asset.IsAsset())
                throw new ArgumentException("Object is not an Asset", nameof(asset));

            return Settings.GetGuidForAssetExport(asset);
        }

        public Stream TryCreateAssetExportWriter(UnityObject asset)
            => Settings.TryCreateAssetExportWriter(asset);

        public void GenerateLinkedEntityGroups()
        {
            // Create LinkedEntityGroup for each root GameObject entity
            // Instantiate & Destroy will destroy the entity as a group.
            foreach (var dstLinkedEntityGroup in m_DstLinkedEntityGroups)
            {
                var selfAndChildren = dstLinkedEntityGroup.GetComponentsInChildren<Transform>(true);

                var entityGroupRoot = GetPrimaryEntity(dstLinkedEntityGroup);

                if (entityGroupRoot == Entity.Null)
                {
                    LogWarning($"Missing entity for root GameObject '{dstLinkedEntityGroup.name}', check for warnings/errors reported during conversion.", dstLinkedEntityGroup);
                    continue;
                }

                if (m_DstManager.HasComponent<LinkedEntityGroup>(entityGroupRoot))
                    continue;

                var buffer = m_DstManager.AddBuffer<LinkedEntityGroup>(entityGroupRoot);
                foreach (var transform in selfAndChildren)
                {
                    Dependencies.DependOnGameObject(dstLinkedEntityGroup, transform.gameObject);

                    foreach (var entity in GetEntities(transform.gameObject))
                    {
                        if (m_DstManager.Exists(entity))
                            buffer.Add(entity);
                    }
                }

                Assert.AreEqual(buffer[0], entityGroupRoot);
            }
        }

        // Add prefab tag to all entities that were converted from a prefab game object source
        public void AddPrefabComponentDataTag()
        {
            foreach (var dstPrefab in m_DstPrefabs)
            {
                foreach (var entity in GetEntities(dstPrefab))
                {
                    if (m_DstManager.Exists(entity))
                        m_DstManager.AddComponent<Prefab>(entity);
                }
            }
        }

        internal static unsafe Entity CreateGameObjectEntity(EntityManager entityManager, GameObject gameObject, List<UnityComponent> componentsCache)
        {
            var componentTypes = stackalloc ComponentType[128];
            if (!gameObject.GetComponents(componentTypes, 128, componentsCache))
                return Entity.Null;

            EntityArchetype archetype = entityManager.CreateArchetype(componentTypes, componentsCache.Count);

            var entity = entityManager.CreateEntity(archetype);

            for (var i = 0; i != componentsCache.Count; i++)
            {
                var com = componentsCache[i];
                if (com != null)
                {
                    entityManager.SetComponentObject(entity, componentTypes[i], com);
                }
            }

            return entity;
        }

        void CreateEntitiesForGameObjectsRecurse(Transform transform, HashSet<GameObject> outDiscoveredGameObjects)
        {
            var go = transform.gameObject;
            if (outDiscoveredGameObjects != null && !outDiscoveredGameObjects.Add(go))
                return;

            // We don't want things marked to not save, converted
            var hiddenFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            if ((go.hideFlags & hiddenFlags) != 0)
                return;

            // If a game object is disabled, we add a linked entity group so that EntityManager.SetEnabled() on the primary entity will result in the whole hierarchy becoming enabled.
            if (!go.activeSelf)
                DeclareLinkedEntityGroup(go);

            CreateGameObjectEntity(EntityManager, go, s_ComponentsCache);

            int childCount = transform.childCount;
            for (int i = 0; i != childCount; i++)
                CreateEntitiesForGameObjectsRecurse(transform.GetChild(i), outDiscoveredGameObjects);
        }

        public void CreateEntitiesForGameObjects(Scene scene)
        {
            var gameObjects = scene.GetRootGameObjects();
            foreach (var go in gameObjects)
                CreateEntitiesForGameObjectsRecurse(go.transform, null);
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        private void UpdateCompanionQuery()
        {
            if (!m_CompanionQueryDirty)
                return;

            foreach (var type in CompanionComponentSupportedTypes.Types)
            {
                m_CompanionTypeSet.Add(type);
            }

            var entityQueryDesc = new EntityQueryDesc
            {
                Any = m_CompanionTypeSet.ToArray(),
                None = new ComponentType[] {typeof(CompanionLink)},
                Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
            };
            m_CompanionComponentsQuery = DstEntityManager.CreateEntityQuery(entityQueryDesc);
            m_CompanionComponentsQuery.SetOrderVersionFilter();

            m_CompanionQueryDirty = false;
        }

        internal void AddTypeToCompanionWhiteList(ComponentType newType)
        {
            if (m_CompanionTypeSet.Add(newType.GetManagedType()))
            {
                m_CompanionQueryDirty = true;
            }
        }

        internal unsafe void CreateCompanionGameObjects()
        {
            using (m_CreateCompanionGameObjectsMarker.Auto())
            {
                UpdateCompanionQuery();

                var mcs = DstEntityManager.GetCheckedEntityDataAccess()->ManagedComponentStore;

                var archetypeChunkArray = m_CompanionComponentsQuery.CreateArchetypeChunkArray(Allocator.Temp);

                Archetype* prevArchetype = null;
                var unityObjectTypeOffset = -1;
                var unityObjectTypeSizeOf = -1;
                var unityObjectTypes = new NativeList<TypeManager.TypeInfo>(10, Allocator.Temp);

                foreach(var archetypeChunk in archetypeChunkArray)
                {
                    var archetype = archetypeChunk.Archetype.Archetype;

                    if (prevArchetype != archetype)
                    {
                        unityObjectTypeOffset = -1;
                        unityObjectTypeSizeOf = -1;
                        unityObjectTypes.Clear();

                        for (int i = 0; i < archetype->TypesCount; i++)
                        {
                            var typeInfo = TypeManager.GetTypeInfo(archetype->Types[i].TypeIndex);
                            if (typeInfo.Category == TypeManager.TypeCategory.UnityEngineObject)
                            {
                                if (unityObjectTypeOffset == -1)
                                {
                                    unityObjectTypeOffset = archetype->Offsets[i];
                                    unityObjectTypeSizeOf = archetype->SizeOfs[i];
                                }

                                unityObjectTypes.Add(typeInfo);
                            }
                        }

                        // For some reason, this archetype had no UnityEngineObjects
                        if (unityObjectTypeOffset == -1)
                            throw new InvalidOperationException("CompanionComponent Query produced Archetype without a Unity Engine Object");
                    }

                    var chunk = archetypeChunk.m_Chunk;
                    var count = chunk->Count;
                    var entities = (Entity*)chunk->Buffer;

                    for (int entityIndex = 0; entityIndex < count; entityIndex++)
                    {
                        var entity = entities[entityIndex];

                        var managedIndex = *(int*)(chunk->Buffer + (unityObjectTypeOffset + unityObjectTypeSizeOf * entityIndex));
                        var obj = (UnityComponent)mcs.GetManagedComponent(managedIndex);
                        var authoringGameObject = obj.gameObject;
                        bool wasActive = authoringGameObject.activeSelf;

                        try
                        {
                            if(wasActive)
                                authoringGameObject.SetActive(false);

                            // Replicate the authoringGameObject, we then strip Components we don't care about
                            var companionGameObject = UnityObject.Instantiate(authoringGameObject);
                            #if UNITY_EDITOR
                            CompanionGameObjectUtility.SetCompanionName(entity, companionGameObject);
                            #endif

                            var components = authoringGameObject.GetComponents<UnityComponent>();
                            foreach (var component in components)
                            {
                                // This is possible if a MonoBehaviour is added but the Script cannot be found
                                // We can remove this if we disallow custom Hybrid Components
                                if (component == null)
                                    continue;

                                var type = component.GetType();
                                if (type == typeof(Transform))
                                    continue;

                                var typeIndex = TypeManager.GetTypeIndex(type);
                                bool foundType = false;
                                for (int i = 0; i < unityObjectTypes.Length; i++)
                                {
                                    if (unityObjectTypes[i].TypeIndex == typeIndex)
                                    {
                                        foundType = true;
                                        break;
                                    }
                                }

                                if (foundType)
                                {
                                    m_DstManager.AddComponentObject(entity, companionGameObject.GetComponent(type));
                                }
                                else
                                {
                                    // Remove all instances of this component from the companion, we don't want them
                                    var unwantedComponents = companionGameObject.GetComponents(type);
                                    foreach (var unwanted in unwantedComponents)
                                    {
                                        //@TODO some components have [RequireComponent] dependencies to each other, and will require special handling for deletion
                                        UnityObject.DestroyImmediate(unwanted);
                                    }
                                }
                            }
                            m_DstManager.AddComponentData(entity, new CompanionLink { Companion = companionGameObject });

                            // We only move into the companion scene if we're currently doing live conversion.
                            // Otherwise this is handled by de-serialisation.
                            #if UNITY_EDITOR
                            if (IsLiveConversion)
                            {
                                CompanionGameObjectUtility.MoveToCompanionScene(companionGameObject, true);
                            }
                            #endif

                            // Can't detach children before instantiate because that won't work with a prefab
                            for (int child = companionGameObject.transform.childCount - 1; child >= 0; child -= 1)
                                UnityObject.DestroyImmediate(companionGameObject.transform.GetChild(child).gameObject);
                        }
                        catch (Exception exception)
                        {
                            LogException(exception, authoringGameObject);
                        }
                        finally
                        {
                            if (wasActive)
                                authoringGameObject.SetActive(true);
                        }
                    }
                }

                archetypeChunkArray.Dispose();
                unityObjectTypes.Dispose();
            }
        }

        #endif // !UNITY_DISABLE_MANAGED_COMPONENTS

        public BlobAssetStore GetBlobAssetStore() => Settings.BlobAssetStore;
    }
}
