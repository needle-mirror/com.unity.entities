using System;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public class MockSharedDataAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int Value;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddSharedComponentData(entity, new MockSharedData
            {
                Value = Value
            });
        }
    }

    [Serializable]
    public struct MockSharedData : ISharedComponentData
    {
        public int Value;
    }
}
