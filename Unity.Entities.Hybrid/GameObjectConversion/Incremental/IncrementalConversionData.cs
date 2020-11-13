using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.Conversion
{
    struct IncrementalConversionData : IDisposable
    {
        public NativeList<int> ChangedAssets;
        public NativeList<int> DeletedAssets;
        public NativeList<int> RemovedInstanceIds;
        public List<GameObject> ChangedGameObjects;
        public List<Component> ChangedComponents;

        public NativeList<int> ReconvertHierarchyRequests;
        public NativeList<int> ReconvertSingleRequests;
        private NativeList<int> _changedGameObjectInstanceIds;
        public NativeList<IncrementalConversionChanges.ParentChange> ParentChangeInstanceIds;

        public static IncrementalConversionData Create()
        {
            return new IncrementalConversionData
            {
                RemovedInstanceIds = new NativeList<int>(Allocator.Persistent),
                ChangedGameObjects = new List<GameObject>(),
                ChangedComponents = new List<Component>(),
                ReconvertHierarchyRequests = new NativeList<int>(Allocator.Persistent),
                ReconvertSingleRequests = new NativeList<int>(Allocator.Persistent),
                ChangedAssets = new NativeList<int>(Allocator.Persistent),
                DeletedAssets = new NativeList<int>(Allocator.Persistent),
                _changedGameObjectInstanceIds = new NativeList<int>(Allocator.Persistent),
                ParentChangeInstanceIds = new NativeList<IncrementalConversionChanges.ParentChange>(Allocator.Persistent)
            };
        }

        public IncrementalConversionChanges ToChanges()
        {
            foreach (var go in ChangedGameObjects)
                _changedGameObjectInstanceIds.Add(go.GetInstanceID());
            return new IncrementalConversionChanges
            {
                ChangedGameObjects = ChangedGameObjects,
                ChangedGameObjectsInstanceIds = _changedGameObjectInstanceIds.AsParallelReader(),
                RemovedGameObjectInstanceIds = RemovedInstanceIds.AsParallelReader(),
                ChangedComponents = ChangedComponents,
                ParentChanges = ParentChangeInstanceIds.AsParallelReader()
            };
        }

        public void Clear()
        {
            ChangedAssets.Clear();
            DeletedAssets.Clear();
            RemovedInstanceIds.Clear();
            ChangedGameObjects.Clear();
            ChangedComponents.Clear();
            ReconvertHierarchyRequests.Clear();
            ReconvertSingleRequests.Clear();
            _changedGameObjectInstanceIds.Clear();
            ParentChangeInstanceIds.Clear();
        }

        public void Dispose()
        {
            if (RemovedInstanceIds.IsCreated)
                RemovedInstanceIds.Dispose();
            if (ReconvertHierarchyRequests.IsCreated)
                ReconvertHierarchyRequests.Dispose();
            if (ReconvertSingleRequests.IsCreated)
                ReconvertSingleRequests.Dispose();
            if (ChangedAssets.IsCreated)
                ChangedAssets.Dispose();
            if (DeletedAssets.IsCreated)
                DeletedAssets.Dispose();
            if (_changedGameObjectInstanceIds.IsCreated)
                _changedGameObjectInstanceIds.Dispose();
            if (ParentChangeInstanceIds.IsCreated)
                ParentChangeInstanceIds.Dispose();
        }
    }
}
