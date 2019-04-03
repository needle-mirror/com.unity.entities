using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Transforms
{
    /// <summary>
    /// User specified (or calculated) World position
    /// 1. If a TransformParent exists and no LocalPosition exists, the value
    /// will be used as the object to World translation irrespective of the
    /// parent object to World matrix.
    /// 2. If a TransformParent exists and a LocalPosition exists, the calculated
    /// World position will be stored in this value by the TransformSystem.
    /// 3. If a TransformMatrix exists, the value will be stored as the translation
    /// part of the matrix.
    /// </summary>
    [Serializable]
    public struct Position : IComponentData
    {
        public float3 Value;

        public Position(float3 position)
        {
            Value = position;
        }
    }
}

namespace Unity.Transforms
{
    public class PositionComponent : ComponentDataWrapper<Position> { } 
}
