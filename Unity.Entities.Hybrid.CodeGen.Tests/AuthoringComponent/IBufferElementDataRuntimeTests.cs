using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Tests.Conversion;
using UnityEngine;

namespace Unity.Entities.Hybrid.CodeGen.Tests
{
    [TestFixture]
    internal class BufferElementDataRuntimeTests : ConversionTestFixtureBase
    {
        [InternalBufferCapacity(0)]
        public struct BufferElementWithZeroCapacity : IBufferElementData
        {
#pragma warning disable 649
            public float Value;
#pragma warning restore 649
        }

        public class BufferElementWithZeroCapacityAuthoring : BufferElementAuthoring
            <BufferElementWithZeroCapacity, float>
        {
        }

        public struct IntBufferElement : IBufferElementData
        {
#pragma warning disable 649
            public int Value;
#pragma warning restore 649
        }

        public class IntBufferElementAuthoring : BufferElementAuthoring<IntBufferElement, int>
        {
        }

        [Test]
        public void AddingCommonlySeenBufferElementDataToDynamicBufferWorks()
        {
            GameObject gameObject = CreateEmptyGameObject();

            const int NumValues = 5;

            int[] values = Enumerable.Range(start: 0, count: NumValues).ToArray();
            gameObject.AddComponent<IntBufferElementAuthoring>().Values = values;

            AwakeConversion(gameObject);

            DynamicBuffer<IntBufferElement> buffer =
                GetDynamicBufferFromConvertedEntity<IntBufferElement>();

            Assert.AreEqual(expected: NumValues, actual: buffer.Length);

            for (int i = 0; i < values.Length; i++)
            {
                Assert.AreEqual(expected: values[i], actual: buffer[i].Value);
            }
        }

        [Test]
        public void AddingBufferElementWithZeroCapacityWorks()
        {
            GameObject gameObject = CreateEmptyGameObject();

            float[] randomFloats = {1.53f, 2.40f, 6.66f, 7.52f, 9.13f, 8.54f, 2.29f, 3.67f};
            gameObject.AddComponent<BufferElementWithZeroCapacityAuthoring>().Values = randomFloats;

            AwakeConversion(gameObject);

            DynamicBuffer<BufferElementWithZeroCapacity> buffer =
                GetDynamicBufferFromConvertedEntity<BufferElementWithZeroCapacity>();
            Assert.AreEqual(expected: randomFloats.Length, actual: buffer.Length);

            for (int i = 0; i < randomFloats.Length; i++)
            {
                Assert.AreEqual(expected: randomFloats[i], actual: buffer[i].Value);
            }
        }

        private GameObject CreateEmptyGameObject()
        {
            GameObject bufferElementDataAuthoringGameObject = CreateGameObject(name: string.Empty, DestructionBy.Test);
            bufferElementDataAuthoringGameObject.AddConvertAndDestroy();

            return bufferElementDataAuthoringGameObject;
        }

        private void AwakeConversion(GameObject bufferElementDataAuthoringComponent)
        {
            ConvertToEntitySystem convertToEntitySystem = BeginAwakeConversion(bufferElementDataAuthoringComponent);
            convertToEntitySystem.Update();
        }

        private ConvertToEntitySystem BeginAwakeConversion(GameObject bufferElementDataAuthoringComponent)
        {
            MethodInfo methodInfo =
                typeof(ConvertToEntity).GetMethod(name: "Awake", BindingFlags.Instance | BindingFlags.NonPublic);

            AwakeConversion(bufferElementDataAuthoringComponent.transform, methodInfo);

            return World.GetOrCreateSystem<ConvertToEntitySystem>();
        }

        private static void AwakeConversion(Transform root, MethodInfo methodInfo)
        {
            foreach (Transform child in root)
            {
                AwakeConversion(child, methodInfo);
            }

            var convert = root.GetComponent<ConvertToEntity>();

            if (convert != null)
            {
                methodInfo.Invoke(convert, parameters: null);
            }
        }

        private DynamicBuffer<T> GetDynamicBufferFromConvertedEntity<T>() where T : struct, IBufferElementData
        {
            Entity convertedEntity;

            using (NativeArray<Entity> entities = m_Manager.UniversalQuery.ToEntityArray(Allocator.TempJob))
            {
                convertedEntity = entities.Single();
            }
            return m_Manager.GetBufferFromEntity<T>()[convertedEntity];
        }
    }
}
