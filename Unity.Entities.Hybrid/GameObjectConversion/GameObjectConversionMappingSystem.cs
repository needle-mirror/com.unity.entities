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
using UnityEngine.Profiling;
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
        internal ref ConversionDependencies      Dependencies => ref m_LiveConversionContext.Dependencies;

        EntityArchetype[]                       m_Archetypes;

        // prefabs and everything they contain will be stored in this set, to be tagged with the Prefab component in dst world
        HashSet<GameObject>                     m_DstPrefabs = new HashSet<GameObject>();
        // each will be marked as a linked entity group containing all of its converted descendants in the dst world
        HashSet<GameObject>                     m_DstLinkedEntityGroups = new HashSet<GameObject>();
        // assets that were declared via DeclareReferencedAssets
        HashSet<UnityObject>                    m_DstAssets = new HashSet<UnityObject>();

        internal ref ConversionJournalData      JournalData => ref m_JournalData;

        #if UNITY_EDITOR
        // Used for both systems and component types
        Dictionary<Type, bool>                  m_ConversionTypeLookupCache = new Dictionary<Type, bool>();
        #endif

        private EntityQuery                     m_SceneSectionEntityQuery;

        public bool  AddEntityGUID              => (Settings.ConversionFlags & ConversionFlags.AddEntityGUID) != 0;
        public bool  ForceStaticOptimization    => (Settings.ConversionFlags & ConversionFlags.ForceStaticOptimization) != 0;
        public bool  AssignName                 => (Settings.ConversionFlags & ConversionFlags.AssignName) != 0;
        public bool  IsLiveLink                 => (Settings.ConversionFlags & (ConversionFlags.SceneViewLiveLink | ConversionFlags.GameViewLiveLink)) != 0;

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
#if !UNITY_2020_2_OR_NEWER
            _instanceIdToGameObject = new Dictionary<int, GameObject>();
#endif
            m_LiveConversionContext = new IncrementalConversionContext(IsLiveLink);

            InitArchetypes();
        }

#if !UNITY_2020_2_OR_NEWER
        // There is no API in 2020.1 to map instance IDs to game objects, but the conversion code relies on it. This is
        // working around that.
        private readonly Dictionary<int, GameObject> _instanceIdToGameObject;
        internal void RegisterForInstanceIdMapping(GameObject go)
        {
            if (go != null && !_instanceIdToGameObject.TryGetValue(go.GetInstanceID(), out _))
                _instanceIdToGameObject.Add(go.GetInstanceID(), go);
        }

        void ResolveInstanceIDs(NativeHashSet<int> instanceIDs, HashSet<GameObject> outGameObjects)
        {
            // TODO: cannot use foreach here because it is broken for NativeHashSet.
            var instances = instanceIDs.ToNativeArray(Allocator.Temp);
            for (int i = 0; i < instances.Length; i++)
            {
                if (_instanceIdToGameObject.TryGetValue(instances[i], out var obj))
                    outGameObjects.Add(obj);
            }
        }
#endif

        public void PrepareForLiveLink(Scene scene)
        {
            var gameObjects = scene.GetRootGameObjects();
            using (new ProfilerMarker("Build Incremental Hierarchy").Auto())
            {
                m_LiveConversionContext.InitializeHierarchy(scene, gameObjects);
            }
        }

        [Obsolete("This functionality is no longer supported. (RemovedAfter 2021-01-09).")]
        public GameObjectConversionSettings ForkSettings(byte entityGuidNamespaceID)
            => Settings.Fork(entityGuidNamespaceID);

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
#if UNITY_2020_2_OR_NEWER
        static readonly ProfilerMarker IncrementalClearEntities = new ProfilerMarker("IncrementalClearEntities");
        static readonly ProfilerMarker IncrementalCreateEntitiesOld = new ProfilerMarker("IncrementalCreateEntitiesOld");
        static readonly ProfilerMarker IncrementalCreateEntitiesNew = new ProfilerMarker("IncrementalCreateEntitiesNew");
        static readonly ProfilerMarker IncrementalDeleteEntities = new ProfilerMarker("IncrementalDeleteEntities");
        static readonly ProfilerMarker IncrementalCollectDependencies = new ProfilerMarker("IncrementalCollectDependencies");
#endif
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

#if UNITY_EDITOR
                if (AssignName)
                {
                    for (int i = 0; i < outEntities.Length; i++)
                        m_DstManager.SetName(outEntities[i], uobject.name);
                }
#endif
            }
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

                //@TODO: inherited classes should probably be supported by queries, so we can delete this loop
                Entities.WithIncludeAll().ForEach((RectTransform transform) =>
                {
                    CreatePrimaryEntity(transform.gameObject);
                });

                //@TODO: [slow] implement this using new inherited query feature so we can do
                //       `Entities.WithAll<Asset>().ForEach((UnityObject asset) => ...)`
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
                    m_JournalData.RecordAdditionalEntityAt(ids[i], outEntities[i]);
            }
        }

#if !UNITY_2020_2_OR_NEWER
        public void PrepareIncrementalConversion(IEnumerable<GameObject> gameObjects, NativeList<int> assetChanges, ConversionFlags flags)
        {
            if (Settings.ConversionFlags != flags)
                throw new ArgumentException("Conversion flags don't match");

            if (!IsLiveLink)
                throw new InvalidOperationException("Incremental conversion can only be used when the conversion world was specifically created for it");

            if (!EntityManager.UniversalQuery.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("Conversion world is expected to be empty");

            m_DstLinkedEntityGroups.Clear();

            HashSet<GameObject> dependents = new HashSet<GameObject>();
            using (new ProfilerMarker("CalculateDependencies").Auto())
            {
                var dependentInstances = new NativeHashSet<int>(0, Allocator.Temp);
                var gosToConvert = gameObjects.ToList();
                var toConvert = new NativeArray<int>(gosToConvert.Count, Allocator.Temp);
                for (int i = 0; i < gosToConvert.Count; i++)
                    toConvert[i] = gosToConvert[i].GetInstanceID();
                Dependencies.CalculateDependents(toConvert, dependentInstances);
                if (assetChanges.IsCreated)
                    Dependencies.CalculateAssetDependents(assetChanges, dependentInstances);
                ResolveInstanceIDs(dependentInstances, dependents);
            }

            using (new ProfilerMarker($"ClearIncrementalConversion ({dependents.Count} GameObjects)").Auto())
            {
                foreach (var go in dependents)
                    ClearIncrementalConversion(go.GetInstanceID(), go);
            }

            using (new ProfilerMarker($"CreateGameObjectEntities ({dependents.Count} GameObjects)").Auto())
            {
                foreach (var go in dependents)
                    CreateGameObjectEntity(EntityManager, go, s_ComponentsCache);
            }

            //Debug.Log($"Incremental processing {EntityManager.UniversalQuery.CalculateEntityCount()}");
        }
#else
        public void BeginIncrementalConversionPreparation(ConversionFlags flags, ref IncrementalConversionBatch arguments)
        {
            if (Settings.ConversionFlags != flags)
                throw new ArgumentException("Conversion flags don't match");

            if (!IsLiveLink)
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
                // TODO: Once hybrid components have been refactored, this whole thing can go through Burst
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
#endif

        static void CheckCanConvertIncrementally(EntityManager manager, Entity entity, bool isPrimary)
        {
            if (manager.HasComponent<Prefab>(entity))
                throw new ArgumentException("An Entity with a Prefab tag cannot be destroyed during incremental conversion");
#if !UNITY_2020_2_OR_NEWER
            if (!isPrimary && manager.HasComponent<LinkedEntityGroup>(entity))
                throw new ArgumentException("An Entity with a LinkedEntityGroup component cannot be destroyed during incremental conversion");
#endif
        }

        void DeleteIncrementalConversion(int instanceId, NativeList<Entity> toRemoveLinkedEntityGroup, NativeList<Entity> toDestroy)
        {
            if (m_JournalData.GetEntities(instanceId, out var entities))
            {
                entities.MoveNext();
                var primaryEntity = entities.Current;
                CheckCanConvertIncrementally(m_DstManager, primaryEntity, true);

                while (entities.MoveNext())
                {
                    var entity = entities.Current;
                    CheckCanConvertIncrementally(m_DstManager, entity, false);
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
                CheckCanConvertIncrementally(m_DstManager, primaryEntity, true);

                var archetype = m_Archetypes[ComputeArchetypeFlags(go)];
                m_DstManager.SetArchetype(primaryEntity, archetype);

                while (entities.MoveNext())
                {
                    var entity = entities.Current;
                    CheckCanConvertIncrementally(m_DstManager, entity, false);
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

            bool liveLinkScene = (Settings.ConversionFlags & ConversionFlags.SceneViewLiveLink) != 0;
            //bool liveLinkGameView = (Settings.ConversionFlags & ConversionFlags.GameViewLiveLink) != 0;

            // NOTE: When no live link is present all entities will simply be batched together for the whole scene

            // In SceneView Live Link mode we want to show the original MeshRenderer
            if (hasGameObjectBasedRenderingRepresentation && liveLinkScene)
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

                //@TODO: This optimization caused breakage on terrains.
                // No need for linked root if it ends up being just one entity...
                //if (buffer.Length == 1)
                //    m_DstManager.RemoveComponent<LinkedEntityGroup>(linkedRoot);
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

        public void AddHybridComponent(UnityComponent component)
        {
            //@TODO exception if converting SubScene or doing incremental conversion (requires a conversion flag for SubScene conversion on Settings.ConversionFlags)
            var type = component.GetType();

            if (component.GetComponents(type).Length > 1)
            {
                LogWarning($"AddHybridComponent({type}) requires the GameObject to only contain a single component of this type.", component.gameObject);
                return;
            }

            m_JournalData.AddHybridComponent(component.gameObject, type);
        }

        #if !UNITY_DISABLE_MANAGED_COMPONENTS
        internal void CreateCompanionGameObjects()
        {
            foreach (var kvp in m_JournalData.NewHybridHeadIdIndices)
            {
                var gameObject = kvp.Key;
                var instanceId = gameObject.GetInstanceID();
                var components = m_JournalData.HybridTypes(kvp.Value);
                var entity = GetPrimaryEntity(instanceId);

                bool wasActive = gameObject.activeSelf;
                try
                {
                    if (wasActive)
                        gameObject.SetActive(false);

                    var companion = CompanionLink.InstantiateCompanionObject(entity, gameObject);

                    foreach (var component in gameObject.GetComponents<UnityComponent>())
                    {
                        if (component == null)
                            continue;
                        var type = component.GetType();
                        if (!components.Contains(type))
                        {
                            foreach (var useless in companion.GetComponents(type))
                            {
                                //@TODO some components have [RequireComponent] dependencies to each other, and will require special handling for deletion
                                if (type != typeof(Transform))
                                    UnityObject.DestroyImmediate(useless);
                            }
                        }
                        else
                        {
                            m_DstManager.AddComponentObject(entity, companion.GetComponent(type));
                        }
                    }

                    m_DstManager.AddComponentData(entity, new CompanionLink { Companion = companion });

                    // Can't detach children before instantiate because that won't work with a prefab

                    for (int i = companion.transform.childCount - 1; i >= 0; i -= 1)
                        UnityObject.DestroyImmediate(companion.transform.GetChild(i).gameObject);

                    companion.hideFlags = CompanionLink.CompanionFlags;
                }
                catch (Exception exception)
                {
                    LogException(exception, gameObject);
                }
                finally
                {
                    if (wasActive)
                        gameObject.SetActive(true);
                }
            }

            m_JournalData.ClearNewHybridComponents();
        }

        #endif // !UNITY_DISABLE_MANAGED_COMPONENTS

        public BlobAssetStore GetBlobAssetStore() => Settings.BlobAssetStore;
    }
}

//@TODO: Change of active state is not live linked. Should trigger rebuild?
