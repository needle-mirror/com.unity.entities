#if !UNITY_DOTSRUNTIME
using System;
using System.Collections.Generic;
using Unity.Entities;

namespace Unity.Scenes
{
    [Serializable]
    internal struct SceneSectionBundle : ISharedComponentData, IEquatable<SceneSectionBundle>, IRefCounted
    {
        private List<SceneBundleHandle> _sceneBundleHandles;

        public SceneSectionBundle(IEnumerable<SceneBundleHandle> bundles)
        {
            _sceneBundleHandles = new List<SceneBundleHandle>(bundles);
        }

        public void Release()
        {
            foreach (var b in _sceneBundleHandles)
                b.Release();
        }

        public void Retain()
        {
            foreach (var b in _sceneBundleHandles)
                b.Retain();
        }

        public bool Equals(SceneSectionBundle other)
        {
            if (GetHashCode() != other.GetHashCode())
                return false;

            if (other._sceneBundleHandles == null)
                return false;

            if (_sceneBundleHandles.Count != other._sceneBundleHandles.Count)
                return false;

            for (int i = 0; i < _sceneBundleHandles.Count; i++)
                if (!_sceneBundleHandles[i].Equals(other._sceneBundleHandles[i]))
                    return false;

            return true;
        }

        public override int GetHashCode()
        {
            if (_sceneBundleHandles == null || _sceneBundleHandles.Count == 0)
                return 0;

            int hash = 0;
            for (int i = 0; i < _sceneBundleHandles.Count; i++)
                hash ^= _sceneBundleHandles[i].GetHashCode();
            return hash;
        }
    }
}
#endif
