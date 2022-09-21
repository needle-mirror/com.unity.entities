using System;
using Unity.Entities;

#if !ENABLE_TRANSFORM_V1
#else

namespace Unity.Transforms
{
    [Serializable]
    [WriteGroup(typeof(LocalToWorld))]
    [WriteGroup(typeof(LocalToParent))]
    [WriteGroup(typeof(CompositeScale))]
    [WriteGroup(typeof(ParentScaleInverse))]
    public struct Scale : IComponentData
    {
        public float Value;
    }
}

#endif
