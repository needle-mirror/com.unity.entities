#if !UNITY_DISABLE_MANAGED_COMPONENTS
using System;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities
{
    class CompanionLink : IComponentData, IEquatable<CompanionLink>, IDisposable, ICloneable
    {
        public GameObject Companion;
        public bool Equals(CompanionLink other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Companion, other.Companion);
        }

        public override int GetHashCode()
        {
            return ReferenceEquals(Companion, null) ? 0 : Companion.GetHashCode();
        }

        public void Dispose()
        {
            UnityObject.DestroyImmediate(Companion);
        }

        public object Clone()
        {
            var cloned = new CompanionLink { Companion = UnityObject.Instantiate(Companion) };
#if UNITY_EDITOR
            CompanionGameObjectUtility.MoveToCompanionScene(cloned.Companion, true);
#endif
            return cloned;
        }
    }
}

#endif
