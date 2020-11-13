using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Entities.Conversion
{
    struct ConversionDependencies : IDisposable
    {
        [NativeDisableContainerSafetyRestriction]
        internal DependencyTracker GameObjectDependencyTracker;
#if UNITY_EDITOR
        internal DependencyTracker AssetDependencyTracker;
#endif

        private NativeHashMap<int, DependencyTracker> _componentDependenciesByTypeIndex;
        private NativeHashSet<int> _unresolvedComponentInstanceIds;
        internal bool HasUnresolvedComponentInstanceIds => !_unresolvedComponentInstanceIds.IsEmpty;
        readonly bool _isLiveLink;

        internal ConversionDependencies(bool isLiveLink)
        {
            _isLiveLink = isLiveLink;
            if (_isLiveLink)
            {
                GameObjectDependencyTracker = new DependencyTracker(Allocator.Persistent);
                _componentDependenciesByTypeIndex = new NativeHashMap<int, DependencyTracker>(0, Allocator.Persistent);
                _unresolvedComponentInstanceIds = new NativeHashSet<int>(0, Allocator.Persistent);
            }
            else
            {
                GameObjectDependencyTracker = default;
                _componentDependenciesByTypeIndex = default;
                _unresolvedComponentInstanceIds = default;
            }
#if UNITY_EDITOR
            AssetDependencyTracker = new DependencyTracker(Allocator.Persistent);
#endif
        }

        internal void RegisterComponentTypeForDependencyTracking<T>() where T : Component
            => RegisterComponentTypeForDependencyTracking(TypeManager.GetTypeIndex<T>());

        internal void RegisterComponentTypeForDependencyTracking(int typeIndex)
        {
            if (!_componentDependenciesByTypeIndex.ContainsKey(typeIndex))
                _componentDependenciesByTypeIndex.Add(typeIndex, new DependencyTracker(Allocator.Persistent));
        }

        internal void DependOnGameObject(GameObject dependent, GameObject dependsOn)
        {
            if (!_isLiveLink)
            {
                // this dependency only needs to be tracked when using LiveLink, since otherwise subscenes are converted
                // as a whole.
                return;
            }


            if (dependent == null)
                throw new ArgumentNullException(nameof(dependent));
            if (ReferenceEquals(dependsOn, null))
            {
                // It is essential that we early out here. Due to the way that null-ness works in Unity, we can still
                // work with the data we get even if it is null to extract the instance ID. This means that we should
                // *not* blame the user when they pass a null-value in here, because they will probably not use
                // ReferenceEquals but stick to == null, which then means we cannot extract the instance id anymore.
                return;
            }
            GameObjectDependencyTracker.AddDependency(dependent.GetInstanceID(), dependsOn.GetInstanceID());
        }

        internal void DependOnAsset(GameObject dependent, Object dependsOn)
        {
#if UNITY_EDITOR
            if (dependent == null)
                throw new ArgumentNullException(nameof(dependent));
            if (ReferenceEquals(dependsOn, null))
                return;
            if (dependsOn != null && !dependsOn.IsAsset() && !dependsOn.IsPrefab())
            {
                return;
            }

            int dependentId = dependent.GetInstanceID();
            int assetId = dependsOn.GetInstanceID();
            AssetDependencyTracker.AddDependency(dependentId, assetId);
#endif
        }

        internal void DependOnComponent(GameObject dependent, Component dependsOn)
        {
            if (!_isLiveLink)
            {
                // this dependency only needs to be tracked when using LiveLink, since otherwise subscenes are converted
                // as a whole.
                return;
            }
            if (dependent == null)
                throw new ArgumentNullException(nameof(dependent));
            if (ReferenceEquals(dependsOn, null))
            {
                // It is essential that we early out here. Due to the way that null-ness works in Unity, we can still
                // work with the data we get even if it is null to extract the instance ID. This means that we should
                // *not* blame the user when they pass a null-value in here, because they will probably not use
                // ReferenceEquals but stick to == null, which then means we cannot extract the instance id anymore.
                return;
            }

            // Figure out what to depend on. We generally speaking prefer to depend on GameObjects instead of components
            // but if a component is destroyed, we only have its instance id to go by and need to migrate it to a
            // GameObject dependency later.
            int dependsOnId;
            if (dependsOn == null)
            {
                dependsOnId = dependsOn.GetInstanceID();
                _unresolvedComponentInstanceIds.Add(dependsOnId);
            }
            else
                dependsOnId = dependsOn.gameObject.GetInstanceID();

            int dependentId = dependent.GetInstanceID();
            GameObjectDependencyTracker.AddDependency(dependentId, dependsOnId);
            var typeIndex = TypeManager.GetTypeIndex(dependsOn.GetType());
            if (_componentDependenciesByTypeIndex.TryGetValue(typeIndex, out var dependencyTracker))
            {
                dependencyTracker.AddDependency(dependentId, dependsOnId);
                _componentDependenciesByTypeIndex[typeIndex] = dependencyTracker;
            }
        }

        internal void ResolveComponentInstanceIds(int gameObjectInstanceId, List<Component> components)
        {
            NativeList<int> dependentList = new NativeList<int>(32, Allocator.Temp);
            foreach (var c in components)
            {
                int componentInstanceId = c.GetInstanceID();
                if (!_unresolvedComponentInstanceIds.Remove(componentInstanceId))
                    continue;

                GameObjectDependencyTracker.RemapInstanceId(componentInstanceId, gameObjectInstanceId);
                var typeIndex = TypeManager.GetTypeIndex(c.GetType());
                if (_componentDependenciesByTypeIndex.TryGetValue(typeIndex, out var dependencyTracker))
                    dependencyTracker.RemapInstanceId(componentInstanceId, gameObjectInstanceId);
                dependentList.Clear();
            }
        }

        internal void ClearDependencies(NativeArray<int> instanceIds)
        {
            GameObjectDependencyTracker.ClearDependencies(instanceIds);
#if UNITY_EDITOR
            AssetDependencyTracker.ClearDependencies(instanceIds);
#endif

            var componentTrackers = _componentDependenciesByTypeIndex.GetKeyValueArrays(Allocator.Temp);
            for (int i = 0; i < componentTrackers.Keys.Length; i++)
            {
                var key = componentTrackers.Keys[i];
                var dependencyTracker = componentTrackers.Values[i];
                dependencyTracker.ClearDependencies(instanceIds);
                _componentDependenciesByTypeIndex[key] = dependencyTracker;
            }

        }

        internal void CalculateAssetDependents(NativeArray<int> assetInstanceIds, NativeHashSet<int> outDependents)
        {
#if UNITY_EDITOR
            if (assetInstanceIds.Length == 0)
                return;
            var toBeProcessed = new NativeList<int>(0, Allocator.Temp);
            AssetDependencyTracker.CalculateDirectDependents(assetInstanceIds, toBeProcessed);
            CalculateDependents(toBeProcessed, outDependents);
#endif
        }

        internal bool TryGetComponentDependencyTracker<T>(out DependencyTracker tracker)
            => _componentDependenciesByTypeIndex.TryGetValue(TypeManager.GetTypeIndex<T>(), out tracker);
        internal bool TryGetComponentDependencyTracker(int typeIndex, out DependencyTracker tracker)
            => _componentDependenciesByTypeIndex.TryGetValue(typeIndex, out tracker);

        internal void CalculateDependents(NativeArray<int> instanceIds, NativeHashSet<int> outDependents)
            => GameObjectDependencyTracker.CalculateDependents(instanceIds, outDependents);


        public void Dispose()
        {
            GameObjectDependencyTracker.Dispose();
            if (_unresolvedComponentInstanceIds.IsCreated)
                _unresolvedComponentInstanceIds.Dispose();

            if (_componentDependenciesByTypeIndex.IsCreated)
            {
                var componentTrackers = _componentDependenciesByTypeIndex.GetValueArray(Allocator.Temp);
                for (int i = 0; i < componentTrackers.Length; i++)
                    componentTrackers[i].Dispose();
                _componentDependenciesByTypeIndex.Dispose();
            }

#if UNITY_EDITOR
            AssetDependencyTracker.Dispose();
#endif
        }
    }
}
