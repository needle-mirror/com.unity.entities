using System;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public class MockDataAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int Value;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity, new MockData
            {
                Value = Value
            });
        }
    }

    [Serializable]
    public struct MockData : IComponentData
    {
        public int Value;

        public MockData(int value) => Value = value;

        public override string ToString() => Value.ToString();
    }
}
