using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Transforms
{
    [Serializable]
    public struct Heading : IComponentData
    {
        public float3 Value;

        public Heading(float3 heading)
        {
            Value = heading;
        }
    }

    public class HeadingComponent : ComponentDataWrapper<Heading> { } 
}
