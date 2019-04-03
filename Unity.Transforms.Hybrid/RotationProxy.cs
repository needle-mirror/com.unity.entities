using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Transforms
{
    [UnityEngine.DisallowMultipleComponent]
    public class RotationProxy : ComponentDataProxy<Rotation>
    {
        protected override void ValidateSerializedData(ref Rotation serializedData)
        {
            serializedData.Value = math.normalizesafe(serializedData.Value);
        }
    }
}
