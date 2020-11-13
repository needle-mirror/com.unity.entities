using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities.Conversion;
using Unity.Jobs;
using UnityEngine;

namespace Unity.Entities
{
    /// <summary>
    /// This group can be used for systems that need to handle incremental conversion in a custom manner. It is updated
    /// before the actual conversion takes place.
    /// This allows you to query the data from the <see cref="IncrementalChangesSystem"/>, inspect changes to the
    /// scene that have happened since the last conversion, and queue up additional GameObjects for conversion.
    ///
    /// ATTENTION: This is future public API.
    /// </summary>
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    internal class ConversionSetupGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// This system contains all the information about what changed since the last conversion, allows you to reconvert
    /// additional objects, and access information about the current state of objects.
    ///
    /// ATTENTION: This is future public API.
    /// </summary>
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    [UpdateInGroup(typeof(ConversionSetupGroup))]
    internal class IncrementalChangesSystem : SystemBase
    {
        /// <summary>
        /// Contains all changes that have happened in the scene since the last conversion.
        /// </summary>
        public IncrementalConversionChanges IncomingChanges;

        /// <summary>
        /// Contains information about the state of the GameObject hierarchy in the scene in a way that can be easily
        /// queried using Jobs and Burst.
        /// </summary>
        public SceneHierarchyWithTransforms SceneHierarchy;

        /// <summary>
        /// Allows you to retrieve the existing converted entities for a GameObject. This can be used to patch entities
        /// in place instead of reconverting GameObjects entirely.
        /// </summary>
        public ConvertedEntitiesAccessor ConvertedEntities;

        public EntityManager DstEntityManager => MappingSystem.DstEntityManager;
        internal Hash128 SceneGUID => MappingSystem.Settings.SceneGUID;

        private NativeList<int> _convertSingleRequests = new NativeList<int>(Allocator.Persistent);
        private NativeList<int> _convertHierarchyRequests = new NativeList<int>(Allocator.Persistent);
        private readonly List<NativeList<int>> _singleRequests = new List<NativeList<int>>();
        private readonly List<NativeList<int>> _hierarchyRequests = new List<NativeList<int>>();
        private NativeList<JobHandle> _requestsHandles = new NativeList<JobHandle>(Allocator.Persistent);

        private GameObjectConversionMappingSystem _mappingSystem;
        private GameObjectConversionMappingSystem MappingSystem
        {
            get
            {
                if (_mappingSystem == null)
                    _mappingSystem = World.GetExistingSystem<GameObjectConversionMappingSystem>();
                return _mappingSystem;
            }
        }

        internal void ExtractRequests(IncrementalConversionData data)
        {
            data.ReconvertSingleRequests.AddRange(_convertSingleRequests);
            data.ReconvertHierarchyRequests.AddRange(_convertHierarchyRequests);
            JobHandle.CompleteAll(_requestsHandles);
            foreach (var request in _singleRequests)
            {
                data.ReconvertSingleRequests.AddRange(request);
                request.Dispose();
            }

            foreach (var request in _hierarchyRequests)
            {
                data.ReconvertHierarchyRequests.AddRange(request);
                request.Dispose();
            }

            _convertSingleRequests.Clear();
            _convertHierarchyRequests.Clear();
            _singleRequests.Clear();
            _hierarchyRequests.Clear();
            _requestsHandles.Clear();
        }

        protected override void OnCreate()
        {
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _requestsHandles.Dispose();
            _convertSingleRequests.Dispose();
            _convertHierarchyRequests.Dispose();
        }

        protected override void OnUpdate()
        {
        }

        /// <summary>
        /// Add a request to reconvert a single game object.
        /// </summary>
        /// <param name="instanceId">The instance id of the game object to reconvert.</param>
        public void AddConversionRequest(int instanceId) =>
            _convertSingleRequests.Add(instanceId);

        /// <summary>
        /// Add a request to reconvert a game object and the entire hierarchy of game objects below it.
        /// </summary>
        /// <param name="instanceId">The instance id of the game object to reconvert.</param>
        public void AddHierarchyConversionRequest(int instanceId) =>
            _convertHierarchyRequests.Add(instanceId);

        /// <summary>
        /// Add a request to reconvert game objects.
        /// </summary>
        /// <param name="instanceIds">The instance ids of the game objects to reconvert.</param>
        public void AddConversionRequest(NativeArray<int> instanceIds) =>
            _convertSingleRequests.AddRange(instanceIds);

        /// <summary>
        /// Add a request to reconvert game objects and the entire hierarchy of game objects below them.
        /// </summary>
        /// <param name="instanceIds">The instance ids of the game objects to reconvert.</param>
        public void AddHierarchyConversionRequest(NativeArray<int> instanceIds) =>
            _convertHierarchyRequests.AddRange(instanceIds);

        /// <summary>
        /// Add a request to reconvert game objects. The instance ids will only be accessed when the associated job
        /// handle is complete.
        /// </summary>
        /// <param name="instanceIds">The instance ids of the game objects to reconvert.</param>
        /// <param name="handle">The handle of the job that needs to finish before any dependencies are added.</param>
        public void AddConversionRequest(NativeList<int> instanceIds, JobHandle handle)
        {
            _singleRequests.Add(instanceIds);
            _requestsHandles.Add(handle);
        }

        /// <summary>
        /// Add a request to reconvert game objects and the entire hierarchy of game objects below them. The instance
        /// ids will only be accessed when the associated job handle is complete.
        /// </summary>
        /// <param name="instanceIds">The instance ids of the game objects to reconvert.</param>
        /// <param name="handle">The handle of the job that needs to finish before any dependencies are added.</param>
        public void AddHierarchyConversionRequest(NativeList<int> instanceIds, JobHandle handle)
        {
            _hierarchyRequests.Add(instanceIds);
            _requestsHandles.Add(handle);
        }

        /// <summary>
        /// Registers a component type for fine-grained tracking.
        ///
        /// This means that when a dependency is registered on a component on this type, it is not registered against
        /// the GameObject of that component but against the component itself. This allows you to query for dependencies
        /// on that component type later on when you are incrementally converting changes.
        /// </summary>
        /// <typeparam name="T">The Component type to register tracking for.</typeparam>
        public void DeclareComponentDependencyTracking<T>() where T : Component =>
            MappingSystem.Dependencies.RegisterComponentTypeForDependencyTracking<T>();

        /// <summary>
        /// Registers a component type for fine-grained tracking.
        ///
        /// This means that when a dependency is registered on a component on this type, it is not registered against
        /// the GameObject of that component but against the component itself. This allows you to query for dependencies
        /// on that component type later on when you are incrementally converting changes.
        /// </summary>
        /// <param name="typeIndex">The type index of the component type to register tracking for.</param>
        public void DeclareComponentDependencyTracking(int typeIndex) =>
            MappingSystem.Dependencies.RegisterComponentTypeForDependencyTracking(typeIndex);

        /// <summary>
        /// Try to get the component dependency tracker for a given component type. This call only succeeds if you
        /// previously used DeclareComponentDependencyTracking for this particular component type.
        /// </summary>
        /// <param name="tracker">The tracker associated with the component type.</param>
        /// <typeparam name="T">The component type to get the tracker for.</typeparam>
        /// <returns>True if there is a component dependency tracker, false otherwise.</returns>
        public bool TryGetComponentDependencyTracker<T>(out DependencyTracker tracker) where T : Component =>
            MappingSystem.Dependencies.TryGetComponentDependencyTracker(TypeManager.GetTypeIndex<T>(), out tracker);

        /// <summary>
        /// Try to get the component dependency tracker for a given component type. This call only succeeds if you
        /// previously used DeclareComponentDependencyTracking for this particular component type.
        /// </summary>
        /// <param name="typeIndex">The type index of the component type to get the tracker for.</param>
        /// <param name="tracker">The tracker associated with the component type.</param>
        /// <returns>True if there is a component dependency tracker, false otherwise.</returns>
        public bool TryGetComponentDependencyTracker(int typeIndex, out DependencyTracker tracker) =>
            MappingSystem.Dependencies.TryGetComponentDependencyTracker(typeIndex, out tracker);
    }

    /// <summary>
    /// Contains a summary of all changes that happened since the last conversion.
    /// ATTENTION: This is future public API.
    /// </summary>
    internal struct IncrementalConversionChanges
    {
        /// <summary>
        /// Contains all GameObjects that were changed in some way since the last conversion. This includes changes
        /// to the name, enabled/disabled state, addition or removal of components, and newly created GameObjects.
        /// This does not include GameObjects for which only the data on a component was changed or whose place in the
        /// hierarchy has changed.
        /// </summary>
        public IReadOnlyList<GameObject> ChangedGameObjects;

        /// <summary>
        /// Contains the instance ID of all GameObjects in <see cref="ChangedGameObjects"/>.
        /// </summary>
        public NativeArray<int>.ReadOnly ChangedGameObjectsInstanceIds;

        /// <summary>
        /// Contains all Components that were changed in some way since the last conversion. This does not include new
        /// components by default, only components that were actually changed.
        /// </summary>
        public IReadOnlyList<Component> ChangedComponents;

        /// <summary>
        /// Contains the instance IDs of all GameObjects whose parents have changed.
        /// </summary>
        public NativeArray<ParentChange>.ReadOnly ParentChanges;

        /// <summary>
        /// Describes how a game object's parenting has changed.
        /// </summary>
        public struct ParentChange
        {
            /// <summary>
            /// The instance id of the game object whose parenting has changed.
            /// </summary>
            public int InstanceId;
            /// <summary>
            /// The instance if of the game object that was the previous parent.
            /// </summary>
            public int PreviousParentInstanceId;
            /// <summary>
            /// The instance if of the game object that is the new parent.
            /// </summary>
            public int NewParentInstanceId;
        }

        public void CollectGameObjectsWithComponentChange<T>(NativeList<int> instanceIDs) where T : Component
        {
            var changes = ChangedComponents;
            for (int i = 0; i < changes.Count; i++)
            {
                if (changes[i] is T)
                {
                    instanceIDs.Add(changes[i].gameObject.GetInstanceID());
                }
            }
        }

        /// <summary>
        /// Contains the instance IDs of all GameObjects that were removed since the last conversion.
        /// An object might be removed because it was deleted or moved to another scene.
        /// </summary>
        public NativeArray<int>.ReadOnly RemovedGameObjectInstanceIds;
    }
}
