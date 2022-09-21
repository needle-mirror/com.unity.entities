using System;
using System.Collections.Generic;
using Unity.Transforms;

namespace Unity.Entities.Editor
{
    class EntityInspectorAspectsComparer: IComparer<string>
    {
        static readonly string[] k_TopAspects =
        {
            nameof(TransformAspect)
        };

        public static EntityInspectorAspectsComparer Instance { get; } = new();

        public int Compare(string x, string y)
            => InspectorUtility.Compare(x, y, k_TopAspects);
    }
}
