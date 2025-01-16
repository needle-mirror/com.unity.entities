using System;
using System.Collections.Generic;
using Unity.Transforms;

namespace Unity.Entities.Editor
{
    class EntityInspectorComponentsComparer: IComparer<IComponentProperty>
    {
        static readonly string[] k_TopComponents =
        {
            nameof(LocalTransform),
            nameof(PostTransformMatrix),
            nameof(LocalToWorld),
            nameof(Parent),
        };

        public static EntityInspectorComponentsComparer Instance { get; } = new();

        public int Compare(IComponentProperty x, IComponentProperty y)
            => InspectorUtility.Compare(x.DisplayName, y.DisplayName, k_TopComponents);
    }
}
