using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.Entities.Tests
{
    [BurstCompile]
    partial class EnabledMaskTests : ECSTestsFixture
    {
        void AssertEnabledMaskIsConsistent<T>(EnabledMask mask, in NativeArray<Entity> entities,
            in NativeArray<bool> expectedEnabledEntities) where T: unmanaged, IEnableableComponent
        {
            for (int i = 0; i < entities.Length; ++i)
            {
                bool expectedEnabled = expectedEnabledEntities[i];
                bool actualEnabled1 = mask.GetBit(i);
                bool actualEnabled2 = m_Manager.IsComponentEnabled<T>(entities[i]);
                bool actualEnabled3 = mask.GetEnabledRefRO<T>(i).ValueRO;
                bool actualEnabled4 = mask.GetEnabledRefRW<T>(i).ValueRO;
                bool actualEnabled5 = mask.GetEnabledRefRW<T>(i).ValueRW;
                Assert.AreEqual(expectedEnabled, actualEnabled1,
                    $"mismatch in enabled bits for entity {i}: expected={expectedEnabled} mask={actualEnabled1}");
                Assert.AreEqual(expectedEnabled, actualEnabled2,
                    $"mismatch in enabled bits for entity {i}: expected={expectedEnabled} mask={actualEnabled2}");
                Assert.AreEqual(expectedEnabled, actualEnabled3,
                    $"mismatch in enabled bits for entity {i}: expected={expectedEnabled} mask={actualEnabled3}");
                Assert.AreEqual(expectedEnabled, actualEnabled4,
                    $"mismatch in enabled bits for entity {i}: expected={expectedEnabled} mask={actualEnabled4}");
                Assert.AreEqual(expectedEnabled, actualEnabled5,
                    $"mismatch in enabled bits for entity {i}: expected={expectedEnabled} mask={actualEnabled5}");
            }
        }

        public enum TypeHandleVariant
        {
            Component,
            Buffer,
            Dynamic,
        }

        struct DummyWriteJob : IJob
        {
            public ComponentTypeHandle<EcsTestDataEnableable> CompTypeHandle;
            public BufferTypeHandle<EcsIntElementEnableable> BufTypeHandle;
            public void Execute()
            {
            }
        }

        [Test]
        [TestRequiresCollectionChecks]
        public void GetEnabledMask_WithJobWritingToType_SafetyError([Values] TypeHandleVariant typeHandleVariant)
        {
            ComponentType componentType = (typeHandleVariant == TypeHandleVariant.Buffer)
                ? ComponentType.ReadOnly<EcsIntElementEnableable>()
                : ComponentType.ReadOnly<EcsTestDataEnableable>();

            var archetype = m_Manager.CreateArchetype(componentType);
            using var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity, Allocator.Temp);
            Assert.AreEqual(1, archetype.ChunkCount);
            var chunk = m_Manager.GetChunk(entities[0]);

            var writeJobHandle = new DummyWriteJob
            {
                CompTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(isReadOnly:false),
                BufTypeHandle = m_Manager.GetBufferTypeHandle<EcsIntElementEnableable>(isReadOnly:false),
            }.Schedule(default);

            if (typeHandleVariant == TypeHandleVariant.Component)
            {
                var componentTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(isReadOnly: true);
                Assert.That(() => chunk.GetEnabledMask(ref componentTypeHandle),
                    Throws.InvalidOperationException.With.Message.Contains("The previously scheduled job"));
            }
            else if (typeHandleVariant == TypeHandleVariant.Dynamic)
            {
                var dynamicComponentTypeHandle = m_Manager.GetDynamicComponentTypeHandle(componentType);
                Assert.That(() => chunk.GetEnabledMask(ref dynamicComponentTypeHandle),
                    Throws.InvalidOperationException.With.Message.Contains("The previously scheduled job"));
            }
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
            {
                var bufferTypeHandle = m_Manager.GetBufferTypeHandle<EcsIntElementEnableable>(isReadOnly: true);
                Assert.That(() => chunk.GetEnabledMask(ref bufferTypeHandle),
                    Throws.InvalidOperationException.With.Message.Contains("The previously scheduled job"));
            }

            // After completing the job, the same operation should succeed.
            writeJobHandle.Complete();
            EnabledMask enabledMask = default;
            if (typeHandleVariant == TypeHandleVariant.Component)
            {
                var componentTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(isReadOnly: true);
                Assert.DoesNotThrow(() => enabledMask = chunk.GetEnabledMask(ref componentTypeHandle));
            }
            else if (typeHandleVariant == TypeHandleVariant.Dynamic)
            {
                var dynamicComponentTypeHandle = m_Manager.GetDynamicComponentTypeHandle(componentType);
                Assert.DoesNotThrow(() => enabledMask = chunk.GetEnabledMask(ref dynamicComponentTypeHandle));
            }
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
            {
                var bufferTypeHandle = m_Manager.GetBufferTypeHandle<EcsIntElementEnableable>(isReadOnly: true);
                Assert.DoesNotThrow(() => enabledMask = chunk.GetEnabledMask(ref bufferTypeHandle));
            }

            var expectedEnabledEntities = new NativeArray<bool>(128, Allocator.Temp);
            for (int i = 0; i < entities.Length; ++i)
            {
                bool enabled = (i % 3) == 0;
                m_Manager.SetComponentEnabled(entities[i], componentType, enabled);
                expectedEnabledEntities[i] = enabled;
            }
            if (typeHandleVariant is TypeHandleVariant.Component or TypeHandleVariant.Dynamic)
                AssertEnabledMaskIsConsistent<EcsTestDataEnableable>(enabledMask, entities, expectedEnabledEntities);
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
                AssertEnabledMaskIsConsistent<EcsIntElementEnableable>(enabledMask, entities, expectedEnabledEntities);
        }

        [Test]
        public void GetEnabledMask_TypeNotInChunk_ReturnsInvalidMask([Values] TypeHandleVariant typeHandleVariant)
        {
            ComponentType componentType = (typeHandleVariant == TypeHandleVariant.Buffer)
                ? ComponentType.ReadOnly<EcsIntElementEnableable>()
                : ComponentType.ReadOnly<EcsTestDataEnableable>();

            var archetype = m_Manager.CreateArchetype(componentType);
            using var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity, Allocator.Temp);
            Assert.AreEqual(1, archetype.ChunkCount);

            // TODO(DOTS-10236): failing to create an EnabledMask should be a more obvious error than "mask.EnabledBit.IsValid is false".
            // This test validates the existing behaviour, but should not be seen as an endorsement of this approach.
            var chunk = m_Manager.GetChunk(entities[0]);
            EnabledMask enabledMask = default;
            if (typeHandleVariant == TypeHandleVariant.Component)
            {
                var componentTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable2>(isReadOnly: true);
                enabledMask = chunk.GetEnabledMask(ref componentTypeHandle);
            }
            else if (typeHandleVariant == TypeHandleVariant.Dynamic)
            {
                var dynamicComponentTypeHandle = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly<EcsTestDataEnableable2>());
                enabledMask = chunk.GetEnabledMask(ref dynamicComponentTypeHandle);
            }
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
            {
                var bufferTypeHandle = m_Manager.GetBufferTypeHandle<EcsIntElementEnableable2>(isReadOnly: true);
                enabledMask = chunk.GetEnabledMask(ref bufferTypeHandle);
            }
            Assert.IsFalse(enabledMask.EnableBit.IsValid);
        }

        [Test]
        public void EnabledMask_GetEnabledRefFromInvalidMask_RefIsInvalid([Values] TypeHandleVariant typeHandleVariant)
        {
            ComponentType componentType = (typeHandleVariant == TypeHandleVariant.Buffer)
                ? ComponentType.ReadOnly<EcsIntElementEnableable>()
                : ComponentType.ReadOnly<EcsTestDataEnableable>();

            var archetype = m_Manager.CreateArchetype(componentType);
            using var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity, Allocator.Temp);
            Assert.AreEqual(1, archetype.ChunkCount);

            // TODO(DOTS-10236): failing to create an EnabledMask should be a more obvious error than "mask.EnabledBit.IsValid is false".
            // This test validates the existing behaviour, but should not be seen as an endorsement of this approach.
            var chunk = m_Manager.GetChunk(entities[0]);
            EnabledMask enabledMask = default;
            if (typeHandleVariant == TypeHandleVariant.Component)
            {
                var componentTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable2>(isReadOnly: true);
                enabledMask = chunk.GetEnabledMask(ref componentTypeHandle);
            }
            else if (typeHandleVariant == TypeHandleVariant.Dynamic)
            {
                var dynamicComponentTypeHandle = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly<EcsTestDataEnableable2>());
                enabledMask = chunk.GetEnabledMask(ref dynamicComponentTypeHandle);
            }
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
            {
                var bufferTypeHandle = m_Manager.GetBufferTypeHandle<EcsIntElementEnableable2>(isReadOnly: true);
                enabledMask = chunk.GetEnabledMask(ref bufferTypeHandle);
            }
            Assert.IsFalse(enabledMask.EnableBit.IsValid);

            // Now that we have an invalid EnabledMask, make sure the EnabledRefs we get from it are invalid.
            // TODO(DOTS-10236): an invalid EnabledRef should be more obvious. For example, GetBit() on an invalid ref currently is most likely a null-pointer dereference.
            if (typeHandleVariant is TypeHandleVariant.Component or TypeHandleVariant.Dynamic)
            {
                Assert.IsFalse(enabledMask.GetEnabledRefRO<EcsTestDataEnableable2>(0).IsValid);
                Assert.IsFalse(enabledMask.GetEnabledRefRW<EcsTestDataEnableable2>(0).IsValid);
            }
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
            {
                Assert.IsFalse(enabledMask.GetEnabledRefRO<EcsIntElementEnableable2>(0).IsValid);
                Assert.IsFalse(enabledMask.GetEnabledRefRW<EcsIntElementEnableable2>(0).IsValid);
            }
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks]
        public void GetEnabledMask_TypeNotEnableable_Throws()
        {
            // This is only possible with a DynamicComponentTypeHandle; other GetEnabledMask() calls constrain T to be
            // an enableable type at compile time.
            ComponentType componentType = ComponentType.ReadOnly<EcsTestDataEnableable>();

            var archetype = m_Manager.CreateArchetype(componentType, ComponentType.ReadOnly<EcsTestData>());
            using var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity, Allocator.Temp);
            Assert.AreEqual(1, archetype.ChunkCount);

            var chunk = m_Manager.GetChunk(entities[0]);
            var dynamicComponentTypeHandle = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly<EcsTestData>());
            Assert.That(() => chunk.GetEnabledMask(ref dynamicComponentTypeHandle),
                Throws.ArgumentException.With.Message.Contains("must implement"));
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks]
        public void EnabledMask_WriteThroughReadOnlyMask_Throws([Values] TypeHandleVariant typeHandleVariant)
        {
            ComponentType componentType = (typeHandleVariant == TypeHandleVariant.Buffer)
                ? ComponentType.ReadOnly<EcsIntElementEnableable>()
                : ComponentType.ReadOnly<EcsTestDataEnableable>();

            var archetype = m_Manager.CreateArchetype(componentType);
            using var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity, Allocator.Temp);
            Assert.AreEqual(1, archetype.ChunkCount);

            var chunk = m_Manager.GetChunk(entities[0]);
            EnabledMask enabledMask = default;
            if (typeHandleVariant == TypeHandleVariant.Component)
            {
                var componentTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(isReadOnly: true);
                enabledMask = chunk.GetEnabledMask(ref componentTypeHandle);
                Assert.That(() => enabledMask.GetEnabledRefRW<EcsTestDataEnableable>(0).ValueRW = false,
                    Throws.InvalidOperationException);
            }
            else if (typeHandleVariant == TypeHandleVariant.Dynamic)
            {
                var dynamicComponentTypeHandle = m_Manager.GetDynamicComponentTypeHandle(componentType);
                enabledMask = chunk.GetEnabledMask(ref dynamicComponentTypeHandle);
                Assert.That(() => enabledMask.GetEnabledRefRW<EcsTestDataEnableable>(0).ValueRW = false,
                    Throws.InvalidOperationException);
            }
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
            {
                var bufferTypeHandle = m_Manager.GetBufferTypeHandle<EcsIntElementEnableable>(isReadOnly: true);
                enabledMask = chunk.GetEnabledMask(ref bufferTypeHandle);
                Assert.That(() => enabledMask.GetEnabledRefRW<EcsIntElementEnableable>(0).ValueRW = false,
                    Throws.InvalidOperationException);
            }

            Assert.That(() => enabledMask[0] = false,
                Throws.InvalidOperationException.With.Message.Contains("This EnabledMask was created from a read-only type handle or is uninitialized"));
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks]
        [Ignore("TODO: DOTS-10236 This error is not currently detected")]
        public void EnabledMask_GetEnabledRefToDifferentType_Throws([Values] TypeHandleVariant typeHandleVariant)
        {
            ComponentType componentType = (typeHandleVariant == TypeHandleVariant.Buffer)
                ? ComponentType.ReadOnly<EcsIntElementEnableable>()
                : ComponentType.ReadOnly<EcsTestDataEnableable>();

            var archetype = m_Manager.CreateArchetype(componentType);
            using var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity, Allocator.Temp);
            Assert.AreEqual(1, archetype.ChunkCount);

            var chunk = m_Manager.GetChunk(entities[0]);
            EnabledMask enabledMask = default;
            if (typeHandleVariant == TypeHandleVariant.Component)
            {
                var componentTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(isReadOnly: true);
                enabledMask = chunk.GetEnabledMask(ref componentTypeHandle);
            }
            else if (typeHandleVariant == TypeHandleVariant.Dynamic)
            {
                var dynamicComponentTypeHandle = m_Manager.GetDynamicComponentTypeHandle(componentType);
                enabledMask = chunk.GetEnabledMask(ref dynamicComponentTypeHandle);
            }
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
            {
                var bufferTypeHandle = m_Manager.GetBufferTypeHandle<EcsIntElementEnableable>(isReadOnly: true);
                enabledMask = chunk.GetEnabledMask(ref bufferTypeHandle);
            }

            Assert.That(() => enabledMask.GetEnabledRefRO<EcsTestDataEnableable3>(0),
                Throws.InvalidOperationException);
            Assert.That(() => enabledMask.GetEnabledRefRW<EcsTestDataEnableable3>(0),
                Throws.InvalidOperationException);
        }

        [Test]
        public void GetEnabledMask_FromTypeHandle_Works([Values] TypeHandleVariant typeHandleVariant)
        {
            ComponentType componentType = (typeHandleVariant == TypeHandleVariant.Buffer)
                ? ComponentType.ReadWrite<EcsIntElementEnableable>()
                : ComponentType.ReadWrite<EcsTestDataEnableable>();

            var archetype = m_Manager.CreateArchetype(componentType);
            using var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity, Allocator.Temp);
            Assert.AreEqual(1, archetype.ChunkCount);

            var chunk = m_Manager.GetChunk(entities[0]);
            EnabledMask enabledMask = default;
            if (typeHandleVariant == TypeHandleVariant.Component)
            {
                var componentTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(isReadOnly: false);
                enabledMask = chunk.GetEnabledMask(ref componentTypeHandle);
            }
            else if (typeHandleVariant == TypeHandleVariant.Dynamic)
            {
                var dynamicComponentTypeHandle = m_Manager.GetDynamicComponentTypeHandle(componentType);
                enabledMask = chunk.GetEnabledMask(ref dynamicComponentTypeHandle);
            }
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
            {
                var bufferTypeHandle = m_Manager.GetBufferTypeHandle<EcsIntElementEnableable>(isReadOnly: false);
                enabledMask = chunk.GetEnabledMask(ref bufferTypeHandle);
            }

            // Make sure EnabledMask gives consistent results vs. other methods of checking enabled bits
            var expectedEnabledEntities = new NativeArray<bool>(128, Allocator.Temp);
            // 1. Set through SetComponentEnabled
            for (int i = 0; i < entities.Length; ++i)
            {
                bool enabled = (i % 3) == 0;
                m_Manager.SetComponentEnabled(entities[i], componentType, enabled);
                expectedEnabledEntities[i] = enabled;
            }
            if (typeHandleVariant is TypeHandleVariant.Component or TypeHandleVariant.Dynamic)
                AssertEnabledMaskIsConsistent<EcsTestDataEnableable>(enabledMask, entities, expectedEnabledEntities);
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
                AssertEnabledMaskIsConsistent<EcsIntElementEnableable>(enabledMask, entities, expectedEnabledEntities);
            // 2. Set through EnabledMask.SetBit()
            for (int i = 0; i < entities.Length; ++i)
            {
                bool enabled = (i % 5) == 0;
                enabledMask[i] = enabled;
                expectedEnabledEntities[i] = enabled;
            }
            if (typeHandleVariant is TypeHandleVariant.Component or TypeHandleVariant.Dynamic)
                AssertEnabledMaskIsConsistent<EcsTestDataEnableable>(enabledMask, entities, expectedEnabledEntities);
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
                AssertEnabledMaskIsConsistent<EcsIntElementEnableable>(enabledMask, entities, expectedEnabledEntities);
            // 3. Set through EnabledRefRW.ValueRW
            for (int i = 0; i < entities.Length; ++i)
            {
                bool enabled = (i % 7) == 0;
                if (typeHandleVariant is TypeHandleVariant.Component or TypeHandleVariant.Dynamic)
                    enabledMask.GetEnabledRefRW<EcsTestDataEnableable>(i).ValueRW = enabled;
                else if (typeHandleVariant == TypeHandleVariant.Buffer)
                    enabledMask.GetEnabledRefRW<EcsIntElementEnableable>(i).ValueRW = enabled;
                expectedEnabledEntities[i] = enabled;
            }
            if (typeHandleVariant is TypeHandleVariant.Component or TypeHandleVariant.Dynamic)
                AssertEnabledMaskIsConsistent<EcsTestDataEnableable>(enabledMask, entities, expectedEnabledEntities);
            else if (typeHandleVariant == TypeHandleVariant.Buffer)
                AssertEnabledMaskIsConsistent<EcsIntElementEnableable>(enabledMask, entities, expectedEnabledEntities);
        }

        [Test]
        public void EnabledMask_UpdatesDisabledCounts([Values(0,1,2,3)] int bit)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var e0 = m_Manager.CreateEntity(archetype);
            var e1 = m_Manager.CreateEntity(archetype);
            var e2 = m_Manager.CreateEntity(archetype);
            var e3 = m_Manager.CreateEntity(archetype);
            // components are enabled by default, so turn a few off to start.
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(e0, false);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(e1, false);
            var chunk = m_Manager.GetChunk(e0);
            Assert.AreEqual(4, chunk.Count);
            var typeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);
            var enabledMask = chunk.GetEnabledMask(ref typeHandle);
            // Test all four transitions. Can't test them all in the same run, or the errors would cancel each other out!
            if (bit == 0)
                enabledMask[0] = true; // off -> on;
            else if (bit == 1)
                enabledMask[1] = false; // off -> off;
            else if (bit == 2)
                enabledMask[2] = false; // on -> off;
            else if (bit == 3)
                enabledMask[3] = true; // on -> on;
            // If the disabled counts are not updated correctly, we'll get an internal consistency
            // failure when the EntityManager is destroyed during TearDown.
        }
    }
}
