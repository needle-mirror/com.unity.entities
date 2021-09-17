using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// Helper type to contain the component order of a given entity.
    /// </summary>
    class EntityInspectorComponentOrder
    {
        public bool Equals(EntityInspectorComponentOrder other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return this == (EntityInspectorComponentOrder)obj;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Components != null ? Components.GetHashCode() : 0) * 397) ^
                    (Tags != null ? Tags.GetHashCode() : 0);
            }
        }

        public readonly List<string> Components = new List<string>();
        public readonly List<string> Tags = new List<string>();

        public void Reset()
        {
            Components.Clear();
            Tags.Clear();
        }

        public static bool operator ==(EntityInspectorComponentOrder lhs, EntityInspectorComponentOrder rhs)
        {
            if (ReferenceEquals(null, lhs))
                return ReferenceEquals(null, rhs);

            if (ReferenceEquals(null, rhs))
                return false;

            return lhs.Components.SequenceEqual(rhs.Components)
                && lhs.Tags.SequenceEqual(rhs.Tags);
        }

        public static bool operator !=(EntityInspectorComponentOrder lhs, EntityInspectorComponentOrder rhs)
        {
            return !(lhs == rhs);
        }
    }
}
