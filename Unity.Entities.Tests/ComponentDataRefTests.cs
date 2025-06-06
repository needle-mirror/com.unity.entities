using System;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
    partial class ComponentDataRefTests : ECSTestsFixture
    {
        [Test]
        public void GetRefRW_Works([Values]bool optional)
        {
            var e = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(e, new EcsTestData(1));

            // Getting R/W access to the component should bump its chunk's change version.
            var typeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(isReadOnly: true);
            var startVersion = m_Manager.GetChunk(e).GetChangeVersion(ref typeHandle);
            m_Manager.Debug.IncrementGlobalSystemVersion();

            RefRW<EcsTestData> testData = default;
            var lookup = m_Manager.GetComponentLookup<EcsTestData>(isReadOnly:false);
            if (optional)
                Assert.IsTrue(lookup.TryGetRefRW(e, out testData));
            else
                testData = lookup.GetRefRW(e);

            Assert.IsTrue(ChangeVersionUtility.DidChange(m_Manager.GetChunk(e).GetChangeVersion(ref typeHandle),
                startVersion), "Expected TryGetRefRW to increment component's change version in chunk");
            testData.ValueRW.value = 5;
            Assert.AreEqual(5, testData.ValueRO.value);
            Assert.AreEqual(5, m_Manager.GetComponentData<EcsTestData>(e).value);

            Assert.IsTrue(testData.IsValid);
        }

        [Test]
        public void GetRefRO_Works([Values]bool optional)
        {
            var e = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(e, new EcsTestData(1));

            // Getting R/O access to the component should NOT bump its chunk's change version.
            var typeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(isReadOnly: true);
            var startVersion = m_Manager.GetChunk(e).GetChangeVersion(ref typeHandle);
            m_Manager.Debug.IncrementGlobalSystemVersion();

            RefRO<EcsTestData> testData = default;
            var lookup = m_Manager.GetComponentLookup<EcsTestData>(isReadOnly: true);
            if (optional)
                Assert.IsTrue(lookup.TryGetRefRO(e, out testData));
            else
                testData = lookup.GetRefRO(e);

            Assert.IsFalse(ChangeVersionUtility.DidChange(m_Manager.GetChunk(e).GetChangeVersion(ref typeHandle),
                startVersion), "Expected TryGetRefRO not to increment component's change version in chunk");
            Assert.AreEqual(1, testData.ValueRO.value);
            Assert.AreEqual(1, m_Manager.GetComponentData<EcsTestData>(e).value);
            Assert.IsTrue(testData.IsValid);
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity component data safety checks")]
        public void GetRefRW_ComponentMissing_Fails()
        {
            var e = m_Manager.CreateEntity();
            var lookup = m_Manager.GetComponentLookup<EcsTestData>();

            Assert.Throws<ArgumentException>(() => lookup.GetRefRW(e));
            Assert.Throws<ArgumentException>(() => lookup.GetRefRW(Entity.Null));
            Assert.IsFalse(lookup.TryGetRefRW(e, out var ref1));
            Assert.IsFalse(ref1.IsValid);
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity component data safety checks")]
        public void GetRefRO_ComponentMissing_Fails()
        {
            var e = m_Manager.CreateEntity();
            var lookup = m_Manager.GetComponentLookup<EcsTestData>(isReadOnly:true);

            Assert.Throws<ArgumentException>(() => lookup.GetRefRO(e));
            Assert.Throws<ArgumentException>(() => lookup.GetRefRO(Entity.Null));
            Assert.IsFalse(lookup.TryGetRefRO(e, out var ref1));
            Assert.IsFalse(ref1.IsValid);
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity component data safety checks")]
        public void GetRefRW_InvalidEntity_Fails()
        {
            var e1 = m_Manager.CreateEntity();
            var e2 = m_Manager.CreateEntity();
            m_Manager.DestroyEntity(e2);
            var lookup = m_Manager.GetComponentLookup<Simulate>();

            Assert.Throws<ArgumentException>(() => lookup.GetRefRW(Entity.Null));
            Assert.IsFalse(lookup.TryGetRefRW(Entity.Null, out var ref1));
            Assert.IsFalse(ref1.IsValid);

            Assert.Throws<ArgumentException>(() => lookup.GetRefRW(e2));
            Assert.IsFalse(lookup.TryGetRefRW(e2, out var ref2));
            Assert.IsFalse(ref2.IsValid);
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires entity component data safety checks")]
        public void GetRefRO_InvalidEntity_Fails()
        {
            var e1 = m_Manager.CreateEntity();
            var e2 = m_Manager.CreateEntity();
            m_Manager.DestroyEntity(e2);
            var lookup = m_Manager.GetComponentLookup<Simulate>();

            Assert.Throws<ArgumentException>(() => lookup.GetRefRO(Entity.Null));
            Assert.IsFalse(lookup.TryGetRefRO(Entity.Null, out var ref1));
            Assert.IsFalse(ref1.IsValid);

            Assert.Throws<ArgumentException>(() => lookup.GetRefRO(e2));
            Assert.IsFalse(lookup.TryGetRefRO(e2, out var ref2));
            Assert.IsFalse(ref2.IsValid);
        }

        [Test]
        public void GetRefRW_ZeroSizeComponent_ReturnsTrueWithInvalidRef()
        {
            var e = m_Manager.CreateEntity(typeof(EcsTestTag));
            var lookup = m_Manager.GetComponentLookup<EcsTestTag>();

            var ref1 = lookup.GetRefRW(e);
            Assert.IsFalse(ref1.IsValid);
            Assert.IsTrue(lookup.TryGetRefRW(e, out var ref2));
            Assert.IsFalse(ref2.IsValid);
        }

        [Test]
        public void GetRefRO_ZeroSizeComponent_ReturnsTrueWithInvalidRef()
        {
            var e = m_Manager.CreateEntity(typeof(EcsTestTag));
            var lookup = m_Manager.GetComponentLookup<EcsTestTag>();

            var ref1 = lookup.GetRefRO(e);
            Assert.IsFalse(ref1.IsValid);
            Assert.IsTrue(lookup.TryGetRefRO(e, out var ref2));
            Assert.IsFalse(ref2.IsValid);
        }
    }
}
