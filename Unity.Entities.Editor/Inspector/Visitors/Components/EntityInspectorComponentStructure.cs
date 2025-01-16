using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// Helper type to contain the component order of a given entity.
    /// </summary>
    class EntityInspectorComponentStructure
    {
        public readonly List<IComponentProperty> Components = new List<IComponentProperty>();
        public readonly List<IComponentProperty> Tags = new List<IComponentProperty>();

        public void Reset()
        {
            Components.Clear();
            Tags.Clear();
        }

        public void Sort()
        {
            Tags.Sort(EntityInspectorComponentsComparer.Instance);
            Components.Sort(EntityInspectorComponentsComparer.Instance);
        }

        public void CopyFrom(EntityInspectorComponentStructure other)
        {
            Reset();

            Components.AddRange(other.Components);
            Tags.AddRange(other.Tags);
        }
    }
}
