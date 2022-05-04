#if !UNITY_DISABLE_MANAGED_COMPONENTS
using System.Linq;
using NUnit.Framework;

using Unity.Entities.Tests.Conversion;
using Unity.Scenes;

namespace Unity.Entities.Tests
{
    class CompanionComponentSerializeTests : ConversionTestFixtureBase
    {
        [Test]
        public void CompanionComponentSerialize()
        {
            var root = CreateGameObject();
            var values = new[] { 123, 234, 345 };

            foreach (var value in values)
            {
                var gameObject = CreateGameObject().ParentTo(root);
                gameObject.AddComponent<ConversionTestCompanionComponent>().SomeValue = value;
            }

            GameObjectConversionUtility.ConvertGameObjectHierarchy(root, MakeDefaultSettings().WithExtraSystem<CompanionComponentConversionTests.MonoBehaviourComponentConversionSystem>());

            var world = new World("temp");

            ReferencedUnityObjects objRefs = null;
            var writer = new TestBinaryWriter(world.UpdateAllocator.ToAllocator);
            SerializeUtilityHybrid.Serialize(m_Manager, writer, out objRefs);

            var reader = new TestBinaryReader(writer);
            SerializeUtilityHybrid.Deserialize(world.EntityManager, reader, objRefs);

            var query = world.EntityManager.CreateEntityQuery(typeof(ConversionTestCompanionComponent));
            var components = query.ToComponentArray<ConversionTestCompanionComponent>();

            CollectionAssert.AreEquivalent(components.Select(c => c.SomeValue), values);

            query.Dispose();

            world.Dispose();
        }
    }
}
#endif
