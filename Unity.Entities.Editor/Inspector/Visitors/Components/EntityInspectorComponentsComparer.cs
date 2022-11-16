using System;
using System.Collections.Generic;
using Unity.Transforms;

namespace Unity.Entities.Editor
{
    class EntityInspectorComponentsComparer: IComparer<string>
    {
#if !ENABLE_TRANSFORM_V1
        static readonly string[] k_TopComponents =
        {
            nameof(WorldTransform),
            nameof(LocalTransform),
            nameof(PostTransformScale),
            nameof(LocalToWorld),
            nameof(Parent),
        };
#else
        static readonly string[] k_TopComponents =
        {
            nameof(Translation),
            nameof(Rotation),
            nameof(NonUniformScale),
            nameof(LocalToWorld),
            nameof(LocalToParent)
        };
#endif

        public static EntityInspectorComponentsComparer Instance { get; } = new();

        public int Compare(string x, string y)
            => InspectorUtility.Compare(x, y, k_TopComponents);
    }
}
