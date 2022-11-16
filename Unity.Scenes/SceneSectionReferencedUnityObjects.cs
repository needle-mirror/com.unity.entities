#if !UNITY_DOTSRUNTIME
using System;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Entities.Serialization;

namespace Unity.Scenes
{
    [Serializable]
    internal struct SceneSectionReferencedUnityObjects : ISharedComponentData, IEquatable<SceneSectionReferencedUnityObjects>, IRefCounted
    {
        private UntypedWeakReferenceId _sceneBundleHandles;

        public SceneSectionReferencedUnityObjects(UntypedWeakReferenceId bundles)
        {
            _sceneBundleHandles = bundles;
        }
        public void Release() => RuntimeContentManager.ReleaseObjectAsync(_sceneBundleHandles);
        public void Retain() => RuntimeContentManager.LoadObjectAsync(_sceneBundleHandles);
        public bool Equals(SceneSectionReferencedUnityObjects other) => _sceneBundleHandles.Equals(other._sceneBundleHandles);
        public override int GetHashCode() => _sceneBundleHandles.GetHashCode();
    }
}
#endif

