using System;
using Unity.Entities;
using Unity.Mathematics;

#if !ENABLE_TRANSFORM_V1
#else

namespace Unity.Transforms
{
    [Serializable]
    [WriteGroup(typeof(LocalToWorld))]
    [WriteGroup(typeof(LocalToParent))]
    [WriteGroup(typeof(CompositeRotation))]
    public struct Rotation : IComponentData
    {
        public quaternion Value;
    }
}

#endif
