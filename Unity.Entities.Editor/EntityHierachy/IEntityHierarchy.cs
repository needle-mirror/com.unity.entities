using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Unity.Entities.Editor
{
    interface IEntityHierarchy
    {
        IEntityHierarchyState State { get; }
        IEntityHierarchyGroupingStrategy GroupingStrategy { get; }
        EntityQueryDesc QueryDesc { get; }
        World World { get; }
    }

    interface IEntityHierarchyState : IDisposable
    {
        // Cheap operation: Gets all the nodes in an unpredictable order
        IReadOnlyCollection<EntityHierarchyNodeId> GetAllNodesUnordered();
        // Expensive operation: Gets all the nodes in the hierarchical order they would be presented in a TreeView or ListView
        IReadOnlyList<EntityHierarchyNodeId> GetAllNodesOrdered();
        IEnumerable<EntityHierarchyNodeId> GetAllDescendants(in EntityHierarchyNodeId node);
        bool Exists(in EntityHierarchyNodeId nodeId);
        bool TryGetChildren(in EntityHierarchyNodeId nodeId, out HashSet<EntityHierarchyNodeId> children);
        bool HasChildren(in EntityHierarchyNodeId nodeId);
        void GetChildren(in EntityHierarchyNodeId nodeId, List<EntityHierarchyNodeId> childrenList);
        HashSet<EntityHierarchyNodeId> GetChildren(in EntityHierarchyNodeId nodeId);
        int GetDepth(in EntityHierarchyNodeId nodeId);
        EntityHierarchyNodeId GetParent(in EntityHierarchyNodeId nodeId);
        uint GetNodeVersion(in EntityHierarchyNodeId nodeId);

        // TODO More explicit Name (this is an expensive operation): ComputeNodeName / RetrieveNodeName / FetchNodeName / BuildNodeName / etc.
        string GetNodeName(in EntityHierarchyNodeId nodeId);

        void GetNodesBeingAdded(List<EntityHierarchyNodeId> nodesBeingAdded);
        void GetNodesBeingAdded(HashSet<EntityHierarchyNodeId> nodesBeingAdded);
        void GetNodesBeingRemoved(List<EntityHierarchyNodeId> nodesBeingRemoved);
        void GetNodesBeingRemoved(HashSet<EntityHierarchyNodeId> nodesBeingRemoved);
        void GetNodesBeingMoved(List<EntityHierarchyNodeId> nodesBeingMoved);
        void GetNodesBeingMoved(HashSet<EntityHierarchyNodeId> nodesBeingMoved);
        bool TryGetFutureParent(in EntityHierarchyNodeId node, out EntityHierarchyNodeId nextParent);

        // TODO More explicit Name: EnqueueOperation? StageOperation?
        void RegisterAddEntityOperation(Entity entity, out EntityHierarchyNodeId generatedNode);
        void RegisterAddSceneOperation(int sceneId, out EntityHierarchyNodeId generatedNode);
        void RegisterAddSubSceneOperation(int subSceneId, out EntityHierarchyNodeId generatedNode);
        void RegisterAddDynamicSubSceneOperation(int subSceneId, string name, out EntityHierarchyNodeId generatedNode);
        void RegisterAddCustomNodeOperation(FixedString64Bytes name, out EntityHierarchyNodeId generatedNode);
        void RegisterRemoveOperation(in EntityHierarchyNodeId node);
        void RegisterMoveOperation(in EntityHierarchyNodeId toNode, in EntityHierarchyNodeId node);

        // TODO More explicit Name: Commit? Push? Submit? ApplyAndClear?
        bool FlushOperations(IEntityHierarchyGroupingContext context);
    }

    interface IEntityHierarchyGroupingStrategy : IDisposable
    {
        ComponentType[] ComponentsToWatch { get; }

        void BeginApply(IEntityHierarchyGroupingContext context);
        void ApplyEntityChanges(NativeArray<Entity> newEntities, NativeArray<Entity> removedEntities, IEntityHierarchyGroupingContext context);
        void ApplyComponentDataChanges(ComponentType componentType, in ComponentDataDiffer.ComponentChanges componentChanges, IEntityHierarchyGroupingContext context);
        void ApplySharedComponentDataChanges(ComponentType componentType, in SharedComponentDataDiffer.ComponentChanges componentChanges, IEntityHierarchyGroupingContext context);
        bool EndApply(IEntityHierarchyGroupingContext context);
    }

    interface IEntityHierarchyGroupingContext
    {
        uint Version { get; }
        ISceneMapper SceneMapper { get; }
    }
}
