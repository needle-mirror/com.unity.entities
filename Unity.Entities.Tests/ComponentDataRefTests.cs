using System;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
    partial class ComponentDataRefTests : ECSTestsFixture
    {
        [Test]
        public void TestDataRefFromEntity([Values]bool optional)
        {
            var e = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(e, new EcsTestData(1));

            var testData = optional ? EmptySystem.GetComponentLookup<EcsTestData>().GetRefRWOptional(e, isReadOnly: false) : EmptySystem.GetComponentLookup<EcsTestData>().GetRefRW(e, isReadOnly: false);

            testData.ValueRW.value = 5;
            Assert.AreEqual(5, testData.ValueRO.value);
            Assert.AreEqual(5, m_Manager.GetComponentData<EcsTestData>(e).value);

            Assert.IsTrue(testData.IsValid);
        }

        [Test]
        public void TestMissingGetDataRef()
        {
            var e = m_Manager.CreateEntity();

            Assert.Throws<ArgumentException>(() => EmptySystem.GetComponentLookup<EcsTestData>().GetRefRW(e, isReadOnly: false));
            Assert.Throws<ArgumentException>(() => EmptySystem.GetComponentLookup<EcsTestData>().GetRefRW(Entity.Null, isReadOnly: false));
        }

        [Test]
        public void TestOptionalMissing()
        {
            var e = m_Manager.CreateEntity();

            var missingData = EmptySystem.GetComponentLookup<EcsTestData>().GetRefRWOptional(e, isReadOnly: false);
            var missingData2 = EmptySystem.GetComponentLookup<EcsTestData>().GetRefRWOptional(Entity.Null, isReadOnly: false);

            Assert.IsFalse(missingData.IsValid);
            Assert.IsFalse(missingData2.IsValid);

            //NOTE: it would be better if we can throw a objectdisposedexception.
            // But right now there is no simple way to constructing a safety handle that is invalid but not null.
            Assert.Throws<NullReferenceException>(() => { missingData.ValueRW.value = 5; });
            Assert.Throws<NullReferenceException>(() => { Debug.Log(missingData.ValueRO.value); });

            Assert.Throws<NullReferenceException>(() => { missingData2.ValueRW.value = 5; });
            Assert.Throws<NullReferenceException>(() => { Debug.Log(missingData2.ValueRO.value); });
        }
    }
}
