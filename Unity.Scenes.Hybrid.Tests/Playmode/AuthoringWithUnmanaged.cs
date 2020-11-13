using System;
using Unity.Entities;
using Unity.Scenes.Hybrid.Tests;
using UnityEngine;

namespace Unity.Scenes.Hybrid.Tests
{
    public struct RuntimeUnmanaged : IComponentData
    {
        public int Value;
    }

    [DisallowMultipleComponent]
    public class AuthoringWithUnmanaged : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int Value;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity, new RuntimeUnmanaged {Value = Value});
        }
    }
}
