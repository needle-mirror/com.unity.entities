using System;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public class MockDynamicBufferDataAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddBuffer<MockDynamicBufferData>(entity);
        }
    }

    [Serializable]
    public struct MockDynamicBufferData : IBufferElementData
    {
        public int Value;
    }
}
