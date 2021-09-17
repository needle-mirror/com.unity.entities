#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
using NUnit.Framework;
using System;
using System.Collections;
using System.Linq;
using Unity.Collections;
using static Unity.Entities.EntitiesJournaling;

namespace Unity.Entities.Tests
{
    [TestFixture]
    unsafe partial class EntitiesJournalingTests : ECSTestsFixture
    {
        public partial class TestComponentSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .ForEach((ref EcsTestData writable1, ref EcsTestData2 writable2, in EcsTestData3 readOnly) =>
                    {
                        writable1.value = 1;
                    }).Run();
            }
        }

        public partial class TestComponentWithoutBurstSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithoutBurst()
                    .ForEach((ref EcsTestData writable1, ref EcsTestData2 writable2, in EcsTestData3 readOnly) =>
                    {
                        writable1.value = 1;
                    }).Run();
            }
        }

        public partial class TestComponentWithStructuralChangesSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithStructuralChanges()
                    .ForEach((ref EcsTestData writable1, ref EcsTestData2 writable2, in EcsTestData3 readOnly) =>
                    {
                        writable1.value = 1;
                    }).Run();
            }
        }

        public partial class TestSharedComponentWithoutBurstSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithoutBurst()
                    .ForEach((EcsTestSharedComp writable1, EcsTestSharedComp2 writable2, in EcsTestSharedComp3 readOnly) =>
                    {
                        writable1.value = 1;
                    }).Run();
            }
        }

        public partial class TestSharedComponentWithStructuralChangesSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithStructuralChanges()
                    .ForEach((EcsTestSharedComp writable1, EcsTestSharedComp2 writable2, in EcsTestSharedComp3 readOnly) =>
                    {
                        writable1.value = 1;
                    }).Run();
            }
        }

        public partial class TestBufferElementSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .ForEach((ref DynamicBuffer<EcsIntElement> writable1, ref DynamicBuffer<EcsIntElement2> writable2, in DynamicBuffer<EcsIntElement3> readOnly) =>
                    {
                        writable1.Add(new EcsIntElement { Value = 1 });
                    }).Run();
            }
        }

        public partial class TestBufferElementWithoutBurstSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithoutBurst()
                    .ForEach((ref DynamicBuffer<EcsIntElement> writable1, ref DynamicBuffer<EcsIntElement2> writable2, in DynamicBuffer<EcsIntElement3> readOnly) =>
                    {
                        writable1.Add(new EcsIntElement { Value = 1 });
                    }).Run();
            }
        }

        public partial class TestBufferElementWithStructuralChangesSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithStructuralChanges()
                    .ForEach((ref DynamicBuffer<EcsIntElement> writable1, ref DynamicBuffer<EcsIntElement2> writable2, in DynamicBuffer<EcsIntElement3> readOnly) =>
                    {
                        writable1.Add(new EcsIntElement { Value = 1 });
                    }).Run();
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        public partial class TestManagedComponentWithoutBurstSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithoutBurst()
                    .ForEach((EcsTestManagedComponent writable1, EcsTestManagedComponent2 writable2, in EcsTestManagedComponent3 readOnly) =>
                    {
                        writable1.value = "hello";
                    }).Run();
            }
        }

        public partial class TestManagedComponentWithStructuralChangesSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithStructuralChanges()
                    .ForEach((EcsTestManagedComponent writable1, EcsTestManagedComponent2 writable2, in EcsTestManagedComponent3 readOnly) =>
                    {
                        writable1.value = "hello";
                    }).Run();
            }
        }
#endif

        static RecordView MakeRecord(RecordType recordType, World world, SystemHandleUntyped originSystem = default) =>
            new RecordView(0, recordType, 0, world.SequenceNumber, world.Unmanaged.ExecutingSystem, originSystem, Array.Empty<Entity>(), Array.Empty<ComponentType>(), null);

        static RecordView MakeRecord(RecordType recordType, World world, object data, SystemHandleUntyped originSystem = default) =>
            new RecordView(0, recordType, 0, world.SequenceNumber, world.Unmanaged.ExecutingSystem, originSystem, Array.Empty<Entity>(), Array.Empty<ComponentType>(), data);

        static RecordView MakeRecord(RecordType recordType, World world, Entity entity, object data, SystemHandleUntyped originSystem = default) =>
            new RecordView(0, recordType, 0, world.SequenceNumber, world.Unmanaged.ExecutingSystem, originSystem, new[] { entity }, Array.Empty<ComponentType>(), data);

        static RecordView MakeRecord(RecordType recordType, World world, Entity entity, ComponentType type, object data, SystemHandleUntyped originSystem = default) =>
            new RecordView(0, recordType, 0, world.SequenceNumber, world.Unmanaged.ExecutingSystem, originSystem, new[] { entity }, new[] { type }, data);

        static RecordView MakeRecord(RecordType recordType, World world, SystemHandleUntyped executingSystem, Entity entity, ComponentType type, object data, SystemHandleUntyped originSystem = default) =>
            new RecordView(0, recordType, 0, world.SequenceNumber, executingSystem, originSystem, new[] { entity }, new[] { type }, data);

        static RecordView MakeRecord(RecordType recordType, World world, Entity entity, ComponentType[] types, object data, SystemHandleUntyped originSystem = default) =>
            new RecordView(0, recordType, 0, world.SequenceNumber, world.Unmanaged.ExecutingSystem, originSystem, new[] { entity }, types, data);

        static RecordView MakeRecord(RecordType recordType, World world, NativeArray<Entity> entities, object data, SystemHandleUntyped originSystem = default) =>
            new RecordView(0, recordType, 0, world.SequenceNumber, world.Unmanaged.ExecutingSystem, originSystem, entities.ToArray(), Array.Empty<ComponentType>(), data);

        static RecordView MakeRecord(RecordType recordType, World world, NativeArray<Entity> entities, ComponentType type, object data, SystemHandleUntyped originSystem = default) =>
            new RecordView(0, recordType, 0, world.SequenceNumber, world.Unmanaged.ExecutingSystem, originSystem, entities.ToArray(), new[] { type }, data);

        static RecordView MakeRecord(RecordType recordType, World world, SystemHandleUntyped executingSystem, NativeArray<Entity> entities, ComponentType type, object data, SystemHandleUntyped originSystem = default) =>
            new RecordView(0, recordType, 0, world.SequenceNumber, executingSystem, originSystem, entities.ToArray(), new[] { type }, data);

        static RecordView MakeRecord(RecordType recordType, World world, NativeArray<Entity> entities, ComponentType[] types, object data, SystemHandleUntyped originSystem = default) =>
            new RecordView(0, recordType, 0, world.SequenceNumber, world.Unmanaged.ExecutingSystem, originSystem, entities.ToArray(), types, data);


        static T[] MakeArray<T>(int length, T value)
        {
            var array = new T[length];
            for (var i = 0; i < length; ++i)
                array[i] = value;
            return array;
        }

        static void CheckRecords(RecordView[] expectedRecords)
        {
            AssertAreEquals(expectedRecords, GetRecords().ToArray());
        }

        static void AssertAreEquals(object expected, object actual)
        {
            if (expected is IList expectedList && actual is IList actualList)
                AssertAreEquals(expectedList, actualList);
            else if(expected is RecordView expectedRecord && actual is RecordView actualRecord)
                AssertAreEquals(expectedRecord, actualRecord);
            else
                Assert.IsTrue(Equals(expected, actual));
        }

        static void AssertAreEquals(IList expected, IList actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; ++i)
                AssertAreEquals(expected[i], actual[i]);
        }

        static void AssertAreEquals(RecordView expected, RecordView actual)
        {
            AssertAreEquals(expected.RecordType, actual.RecordType);
            AssertAreEquals(expected.FrameIndex, actual.FrameIndex);
            AssertAreEquals(expected.World, actual.World);
            AssertAreEquals(expected.ExecutingSystem, actual.ExecutingSystem);
            AssertAreEquals(expected.Entities, actual.Entities);
            AssertAreEquals(expected.ComponentTypes, actual.ComponentTypes);
            AssertAreEquals(expected.Data, actual.Data);
            AssertAreEquals(expected.OriginSystem, actual.OriginSystem);
        }

        bool m_LastEnabled;
        int m_LastTotalMemoryMB;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            m_LastEnabled = Preferences.Enabled;
            m_LastTotalMemoryMB = Preferences.TotalMemoryMB;
            Shutdown();
            Preferences.Enabled = true;
            Preferences.TotalMemoryMB = 64;
            Initialize();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Shutdown();
            Preferences.Enabled = m_LastEnabled;
            Preferences.TotalMemoryMB = m_LastTotalMemoryMB;
            Initialize();
        }

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            Clear();
        }

        [TearDown]
        public override void TearDown()
        {
            Clear();
            base.TearDown();
        }

        [Test]
        public void CreateEntity()
        {
            var entity = m_Manager.CreateEntity();

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
            });
        }

        [Test]
        public void CreateEntity_WithArchetype()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entity = m_Manager.CreateEntity(archetype);

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, typeof(EcsTestData), null),
            });
        }

        [Test]
        public void CreateEntity_WithComponentTypes()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }, null),
            });
        }

        [Test]
        public void CreateEntity_WithArchetypeAndEntityArray()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using (var entities = new NativeArray<Entity>(10, Allocator.Temp))
            {
                m_Manager.CreateEntity(archetype, entities);

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.CreateEntity, World, entities, typeof(EcsTestData), null),
                });
            }
        }

        [Test]
        public void CreateEntity_WithArchetypeAndEntityCount()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using (var entities = m_Manager.CreateEntity(archetype, 10, Allocator.Temp))
            {
                CheckRecords(new[]
                {
                    MakeRecord(RecordType.CreateEntity, World, entities, typeof(EcsTestData), null),
                });
            }
        }

        [Test]
        public void DestroyEntity()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.DestroyEntity(entity);

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.DestroyEntity, World, entity, null),
            });
        }

        [Test]
        public void DestroyEntity_WithQuery()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                m_Manager.DestroyEntity(query);
            }

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, typeof(EcsTestData), null),
                MakeRecord(RecordType.DestroyEntity, World, entity, null),
            });
        }

        [Test]
        public void DestroyEntity_WithEntityArray()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using (var entities = m_Manager.CreateEntity(archetype, 10, Allocator.Temp))
            {
                m_Manager.DestroyEntity(entities);

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.CreateEntity, World, entities, typeof(EcsTestData), null),
                    MakeRecord(RecordType.DestroyEntity, World, entities, null),
                });
            }
        }

        [Test]
        public void AddComponent()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponent(entity, typeof(EcsTestData));

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestData), null),
            });
        }

        [Test]
        public void AddComponents()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponents(entity, new ComponentTypes(new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }));

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }, null),
            });
        }

        [Test]
        public void AddComponent_WithQuery()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                m_Manager.AddComponent(query, typeof(EcsTestData2));
            }

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, typeof(EcsTestData), null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestData2), null),
            });
        }

        [Test]
        public void AddComponent_WithEntityArray()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using (var entities = m_Manager.CreateEntity(archetype, 10, Allocator.Temp))
            {
                m_Manager.AddComponent(entities, typeof(EcsTestData2));

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.CreateEntity, World, entities, typeof(EcsTestData), null),
                    MakeRecord(RecordType.AddComponent, World, entities, typeof(EcsTestData2), null),
                });
            }
        }

        [Test]
        public void AddComponentData()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentData(entity, new EcsTestData(42));

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestData), null),
                MakeRecord(RecordType.SetComponentData, World, entity, typeof(EcsTestData), new EcsTestData(42)),
            });
        }

        [Test]
        public void AddSharedComponent()
        {
            var entity = m_Manager.CreateEntity();
            using (var chunkArray = new NativeArray<ArchetypeChunk>(new[] { m_Manager.GetChunk(entity) }, Allocator.Temp))
            {
                m_Manager.AddSharedComponent(chunkArray, new EcsTestSharedComp(42));
            }

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestSharedComp), null),
                MakeRecord(RecordType.SetSharedComponentData, World, entity, typeof(EcsTestSharedComp), null),
            });
        }

        [Test]
        public void AddSharedComponentData()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddSharedComponentData(entity, new EcsTestSharedComp(42));

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestSharedComp), null),
                MakeRecord(RecordType.SetSharedComponentData, World, entity, typeof(EcsTestSharedComp), null),
            });
        }

        [Test]
        public void AddSharedComponentData_WithQuery()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                m_Manager.AddSharedComponentData(query, new EcsTestSharedComp(42));
            }

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, typeof(EcsTestData), null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestSharedComp), null),
                MakeRecord(RecordType.SetSharedComponentData, World, entity, typeof(EcsTestSharedComp), null),
            });
        }

        [Test]
        public void VerifyOriginSystemID()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentData(entity, new EcsTestData(42));

            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var entity2 = m_Manager.CreateEntity();
            ecb.AddComponent(entity2, new EcsTestData(42));
            ecb.Playback(m_Manager);

            var ecboriginSystem = ecb.SystemID > 0 ? World.Unmanaged.GetExistingUnmanagedSystem(TypeManager.GetType(ecb.SystemID)) : World.Unmanaged.ExecutingSystem;
            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null, default),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestData), null, default),
                MakeRecord(RecordType.SetComponentData, World, entity, typeof(EcsTestData), new EcsTestData(42), default),
                MakeRecord(RecordType.CreateEntity, World, entity2, null, default),
                MakeRecord(RecordType.AddComponent, World, entity2, typeof(EcsTestData), null, ecboriginSystem),
                MakeRecord(RecordType.SetComponentData, World, entity2, typeof(EcsTestData), new EcsTestData(42), ecboriginSystem)
            });

            ecb.Dispose();
        }

        [Test]
        public void EntityJournalErrorAdded_DoesNotExist_EntityManager()
        {
            var e = m_Manager.CreateEntity();
            m_Manager.DestroyEntity(e);
            Assert.That(() => m_Manager.AddComponent<EcsTestData>(e),
                Throws.InvalidOperationException.With.Message.Contains("(" + e.Index + ":" + e.Version + ")"));
            Clear();

            var arch = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entities = m_Manager.CreateEntity(arch, 5, Allocator.TempJob);
            var destroyedEntity = entities[0];
            m_Manager.DestroyEntity(entities[0]);
            Assert.That(() => m_Manager.AddComponent<EcsTestData>(entities),
                Throws.ArgumentException.With.Message.Contains("(" + destroyedEntity.Index + ":" + destroyedEntity.Version + ")"));
            entities.Dispose();
        }

        [Test]
        public void EntityJournalErrorAdded_DoesNotHaveComponent_EntityManager()
        {
            var e = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.RemoveComponent<EcsTestData>(e);
            Assert.That(() => m_Manager.SetComponentData<EcsTestData>(e, new EcsTestData {value = 10}),
                Throws.ArgumentException.With.Message.Contains("(" + e.Index + ":" + e.Version + ")"));
        }

        class TestECBPlaybackSystem : EntityCommandBufferSystem {}
        partial class TestECBRecordingSystem_DestroyedErrorMessage : SystemBase
        {
            protected override void OnUpdate()
            {
                // NOTE: ECB playback being bursted does not add to the error message, so we are disabling it for the test
                var ecb = World.GetOrCreateSystem<TestECBPlaybackSystem>().CreateCommandBuffer();
                ecb.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
                var e = ecb.CreateEntity();
                ecb.DestroyEntity(e);
                ecb.AddComponent<EcsTestData>(e);
            }
        }

        partial class TestECBRecordingSystem_RemovedErrorMessage : SystemBase
        {
            protected override void OnUpdate()
            {
                // NOTE: ECB playback being bursted does not add to the error message, so we are disabling it for the test
                var ecb = World.GetOrCreateSystem<TestECBPlaybackSystem>().CreateCommandBuffer();
                ecb.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
                var e = ecb.CreateEntity();
                ecb.AddComponent<EcsTestData>(e);
                ecb.RemoveComponent<EcsTestData>(e);
                ecb.SetComponent(e, new EcsTestData{value = 42});
            }
        }

        [Test]
        public void EntityJournalErrorAdded_ValidOriginSystem_DestroyedEntity()
        {
            using (var world = new World("World A"))
            {
                var ecbRecordingSystem = world.GetOrCreateSystem<TestECBRecordingSystem_DestroyedErrorMessage>();
                var ecbPlaybackSystem = world.GetOrCreateSystem<TestECBPlaybackSystem>();

                ecbRecordingSystem.Update();
                Assert.That(() => ecbPlaybackSystem.Update(),
                    Throws.ArgumentException.With.Message.Contains(
                        $"This command was requested from {ecbRecordingSystem}."));
            }
        }

        [Test]
        public void EntityJournalErrorAdded_ValidOriginSystem_RemovedComponent()
        {
            using (var world = new World("World A"))
            {
                var ecbRecordingSystem = world.GetOrCreateSystem<TestECBRecordingSystem_RemovedErrorMessage>();
                var ecbPlaybackSystem = world.GetOrCreateSystem<TestECBPlaybackSystem>();

                ecbRecordingSystem.Update();
                Assert.That(() => ecbPlaybackSystem.Update(),
                    Throws.ArgumentException.With.Message.Contains(
                        $"This command was requested from {ecbRecordingSystem}."));
            }
        }

        [Test]
        public void EntityJournalErrorAdded_DoesNotExist_EntityCommandBuffer()
        {
            // NOTE: ECB playback being bursted does not add to the error message, so we are disabling it for the test
            var e = m_Manager.CreateEntity();
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
            ecb.AddComponent<EcsTestData>(e);
            m_Manager.DestroyEntity(e);
            Assert.That(() => ecb.Playback(m_Manager),
                Throws.InvalidOperationException.With.Message.Contains("(" + e.Index + ":" + e.Version + ")"));
            ecb.Dispose();

            var arch = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entities = m_Manager.CreateEntity(arch, 5, Allocator.TempJob);
            var destroyedEntity = entities[0];

            var ecb2 = new EntityCommandBuffer(Allocator.TempJob);
            ecb2.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                ecb2.AddComponentForEntityQuery(query, typeof(EcsTestData));
            }

            m_Manager.DestroyEntity(entities[0]);
            Assert.That(() => ecb2.Playback(m_Manager),
                Throws.InvalidOperationException.With.Message.Contains("(" + destroyedEntity.Index + ":" + destroyedEntity.Version + ")"));
            entities.Dispose();
            ecb2.Dispose();
        }

        [Test]
        public void EntityJournalErrorAdded_DoesNotHaveComponent_EntityCommandBuffer()
        {
            // NOTE: ECB playback being bursted does not add to the error message, so we are disabling it for the test
            var e = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.RemoveComponent<EcsTestData>(e);
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            ecb.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
            ecb.SetComponent(e, new EcsTestData {value = 10});
            Assert.That(() => ecb.Playback(m_Manager),
                Throws.ArgumentException.With.Message.Contains("(" + e.Index + ":" + e.Version + ")"));
            ecb.Dispose();

            var arch = m_Manager.CreateArchetype(typeof(EcsTestSharedComp));
            var entities = m_Manager.CreateEntity(arch, 5, Allocator.TempJob);
            var removedEntity = entities[0];

            var ecb2 = new EntityCommandBuffer(Allocator.TempJob);
            ecb2.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            {
                ecb2.SetSharedComponentForEntityQuery(query, new EcsTestSharedComp {value = 10});
            }

            m_Manager.RemoveComponent<EcsTestSharedComp>(entities[0]);
            Assert.That(() => ecb2.Playback(m_Manager),
                Throws.ArgumentException.With.Message.Contains("(" + removedEntity.Index + ":" + removedEntity.Version + ")"));
            entities.Dispose();
            ecb2.Dispose();
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void AddComponentObject()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentObject(entity, new EcsTestManagedComponent { value = "hi" });

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestManagedComponent), null),
                MakeRecord(RecordType.SetComponentObject, World, entity, typeof(EcsTestManagedComponent), null),
            });
        }
#endif

        [Test]
        public void AddChunkComponentData()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddChunkComponentData<EcsTestData>(entity);

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, ComponentType.ChunkComponent<EcsTestData>(), null),
            });
        }

        [Test]
        public void AddChunkComponentData_WithQuery()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                m_Manager.AddChunkComponentData(query, new EcsTestData2(42));
            }

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, typeof(EcsTestData), null),
                MakeRecord(RecordType.AddComponent, World, entity, ComponentType.ChunkComponent<EcsTestData2>(), null),
                MakeRecord(RecordType.SetComponentData, World, entity, ComponentType.ChunkComponent<EcsTestData2>(), new EcsTestData2(42)),
            });
        }

        [Test]
        public void AddBuffer()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddBuffer<EcsIntElement>(entity);

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsIntElement), null),
            });
        }

        [Test]
        public void RemoveComponent()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.RemoveComponent(entity, typeof(EcsTestData));

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, typeof(EcsTestData), null),
                MakeRecord(RecordType.RemoveComponent, World, entity, typeof(EcsTestData), null),
            });
        }

        [Test]
        public void RemoveComponents()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            m_Manager.RemoveComponent(entity, new ComponentTypes(new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }));

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }, null),
                MakeRecord(RecordType.RemoveComponent, World, entity, new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }, null),
            });
        }

        [Test]
        public void RemoveComponent_WithQuery()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                m_Manager.RemoveComponent(query, typeof(EcsTestData));
            }

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, typeof(EcsTestData), null),
                MakeRecord(RecordType.RemoveComponent, World, entity, typeof(EcsTestData), null),
            });
        }

        [Test]
        public void RemoveComponents_WithQuery()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2)))
            {
                m_Manager.RemoveComponent(query, new ComponentTypes(new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }));
            }

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }, null),
                MakeRecord(RecordType.RemoveComponent, World, entity, new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }, null),
            });
        }

        [Test]
        public void RemoveComponent_WithEntityArray()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using (var entities = m_Manager.CreateEntity(archetype, 10, Allocator.Temp))
            {
                m_Manager.RemoveComponent(entities, typeof(EcsTestData));

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.CreateEntity, World, entities, typeof(EcsTestData), null),
                    MakeRecord(RecordType.RemoveComponent, World, entities, typeof(EcsTestData), null),
                });
            }
        }

        [Test]
        public void SetComponentData()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponent(entity, typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestData), null),
                MakeRecord(RecordType.SetComponentData, World, entity, typeof(EcsTestData), new EcsTestData(42)),
            });
        }

        [Test]
        public void SetSharedComponentData()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponent(entity, typeof(EcsTestSharedComp));
            m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(42));

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestSharedComp), null),
                MakeRecord(RecordType.SetSharedComponentData, World, entity, typeof(EcsTestSharedComp), null),
            });
        }

        [Test]
        public void SetSharedComponentData_WithQuery()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponent(entity, typeof(EcsTestSharedComp));
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            {
                m_Manager.SetSharedComponentData(query, new EcsTestSharedComp(42));
            }

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestSharedComp), null),
                MakeRecord(RecordType.RemoveComponent, World, entity, typeof(EcsTestSharedComp), null),
                MakeRecord(RecordType.AddComponent, World, entity, typeof(EcsTestSharedComp), null),
                MakeRecord(RecordType.SetSharedComponentData, World, entity, typeof(EcsTestSharedComp), null),
            });
        }

        [Test]
        public void SetChunkComponentData()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddChunkComponentData<EcsTestData>(entity);

            var chunk = m_Manager.GetChunk(entity);
            m_Manager.SetChunkComponentData(chunk, new EcsTestData(42));

            CheckRecords(new[]
            {
                MakeRecord(RecordType.CreateEntity, World, entity, null),
                MakeRecord(RecordType.AddComponent, World, entity, ComponentType.ChunkComponent<EcsTestData>(), null),
                MakeRecord(RecordType.SetComponentData, World, chunk.m_Chunk->metaChunkEntity, typeof(EcsTestData), new EcsTestData(42)),
            });
        }

        [Test]
        public void EntitiesForEach_SetComponentData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3));
            using (var entities = m_Manager.CreateEntity(archetype, 3, Allocator.Temp))
            {
                var system = World.GetOrCreateSystem<TestComponentSystem>();

                Clear();
                system.Update();

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.SetComponentData, World, system.SystemHandleUntyped, entities, typeof(EcsTestData), MakeArray(entities.Length, new EcsTestData { value = 1 })),
                    MakeRecord(RecordType.SetComponentData, World, system.SystemHandleUntyped, entities, typeof(EcsTestData2), MakeArray(entities.Length, new EcsTestData2()))
                });
            }
        }

        [Test]
        public void EntitiesForEach_WithoutBurst_SetComponentData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3));
            using (var entities = m_Manager.CreateEntity(archetype, 3, Allocator.Temp))
            {
                var system = World.GetOrCreateSystem<TestComponentWithoutBurstSystem>();

                Clear();
                system.Update();

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.SetComponentData, World, system.SystemHandleUntyped, entities, typeof(EcsTestData), MakeArray(entities.Length, new EcsTestData { value = 1 })),
                    MakeRecord(RecordType.SetComponentData, World, system.SystemHandleUntyped, entities, typeof(EcsTestData2), MakeArray(entities.Length, new EcsTestData2()))
                });
            }
        }

        [Test]
        public void EntitiesForEach_WithStructuralChanges_SetComponentData()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3));
            using (var entities = m_Manager.CreateEntity(archetype, 3, Allocator.Temp))
            {
                var system = World.GetOrCreateSystem<TestComponentWithStructuralChangesSystem>();

                Clear();
                system.Update();

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.SetComponentData, World, system.SystemHandleUntyped, entities[0], typeof(EcsTestData), new EcsTestData { value = 1 }),
                    MakeRecord(RecordType.SetComponentData, World, system.SystemHandleUntyped, entities[1], typeof(EcsTestData), new EcsTestData { value = 1 }),
                    MakeRecord(RecordType.SetComponentData, World, system.SystemHandleUntyped, entities[2], typeof(EcsTestData), new EcsTestData { value = 1 })
                });
            }
        }

        [Test]
        public void EntitiesForEach_WithoutBurst_SetSharedComponentData()
        {
            var componentTypes = new ComponentType[] { typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2), typeof(EcsTestSharedComp3) };
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var entities = m_Manager.CreateEntity(archetype, 3, Allocator.Temp))
            {
                var system = World.GetOrCreateSystem<TestSharedComponentWithoutBurstSystem>();

                Clear();
                system.Update();

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.SetSharedComponentData, World, system.SystemHandleUntyped, entities, typeof(EcsTestSharedComp), null),
                    MakeRecord(RecordType.SetSharedComponentData, World, system.SystemHandleUntyped, entities, typeof(EcsTestSharedComp2), null),
                });
            }
        }

        [Test]
        public void EntitiesForEach_WithStructuralChanges_SetSharedComponentData()
        {
            var componentTypes = new ComponentType[] { typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2), typeof(EcsTestSharedComp3) };
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var entities = m_Manager.CreateEntity(archetype, 3, Allocator.Temp))
            {
                var system = World.GetOrCreateSystem<TestSharedComponentWithStructuralChangesSystem>();

                Clear();
                system.Update();

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.SetSharedComponentData, World, system.SystemHandleUntyped, entities[0], typeof(EcsTestSharedComp), null),
                    MakeRecord(RecordType.SetSharedComponentData, World, system.SystemHandleUntyped, entities[0], typeof(EcsTestSharedComp2), null),
                    MakeRecord(RecordType.SetSharedComponentData, World, system.SystemHandleUntyped, entities[1], typeof(EcsTestSharedComp), null),
                    MakeRecord(RecordType.SetSharedComponentData, World, system.SystemHandleUntyped, entities[1], typeof(EcsTestSharedComp2), null),
                    MakeRecord(RecordType.SetSharedComponentData, World, system.SystemHandleUntyped, entities[2], typeof(EcsTestSharedComp), null),
                    MakeRecord(RecordType.SetSharedComponentData, World, system.SystemHandleUntyped, entities[2], typeof(EcsTestSharedComp2), null),
                });
            }
        }

        [Test]
        public void EntitiesForEach_SetBuffer()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsIntElement), typeof(EcsIntElement2), typeof(EcsIntElement3));
            using (var entities = m_Manager.CreateEntity(archetype, 3, Allocator.Temp))
            {
                var system = World.GetOrCreateSystem<TestBufferElementSystem>();

                Clear();
                system.Update();

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.SetBuffer, World, system.SystemHandleUntyped, entities, typeof(EcsIntElement), null),
                    MakeRecord(RecordType.SetBuffer, World, system.SystemHandleUntyped, entities, typeof(EcsIntElement2), null)
                });
            }
        }

        [Test]
        public void EntitiesForEach_WithoutBurst_SetBuffer()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsIntElement), typeof(EcsIntElement2), typeof(EcsIntElement3));
            using (var entities = m_Manager.CreateEntity(archetype, 3, Allocator.Temp))
            {
                var system = World.GetOrCreateSystem<TestBufferElementWithoutBurstSystem>();

                Clear();
                system.Update();

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.SetBuffer, World, system.SystemHandleUntyped, entities, typeof(EcsIntElement), null),
                    MakeRecord(RecordType.SetBuffer, World, system.SystemHandleUntyped, entities, typeof(EcsIntElement2), null)
                });
            }
        }

        [Test]
        public void EntitiesForEach_WithStructuralChanges_SetBuffer()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsIntElement), typeof(EcsIntElement2), typeof(EcsIntElement3));
            using (var entities = m_Manager.CreateEntity(archetype, 3, Allocator.Temp))
            {
                var system = World.GetOrCreateSystem<TestBufferElementWithStructuralChangesSystem>();

                Clear();
                system.Update();

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.SetBuffer, World, system.SystemHandleUntyped, entities[0], typeof(EcsIntElement), null),
                    MakeRecord(RecordType.SetBuffer, World, system.SystemHandleUntyped, entities[0], typeof(EcsIntElement2), null),
                    MakeRecord(RecordType.SetBuffer, World, system.SystemHandleUntyped, entities[1], typeof(EcsIntElement), null),
                    MakeRecord(RecordType.SetBuffer, World, system.SystemHandleUntyped, entities[1], typeof(EcsIntElement2), null),
                    MakeRecord(RecordType.SetBuffer, World, system.SystemHandleUntyped, entities[2], typeof(EcsIntElement), null),
                    MakeRecord(RecordType.SetBuffer, World, system.SystemHandleUntyped, entities[2], typeof(EcsIntElement2), null),
                });
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void EntitiesForEach_WithoutBurst_SetComponentObject()
        {
            var componentTypes = new ComponentType[] { typeof(EcsTestManagedComponent), typeof(EcsTestManagedComponent2), typeof(EcsTestManagedComponent3) };
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var entities = m_Manager.CreateEntity(archetype, 3, Allocator.Temp))
            {
                var system = World.GetOrCreateSystem<TestManagedComponentWithoutBurstSystem>();
                foreach (var entity in entities)
                {
                    m_Manager.SetComponentData(entity, new EcsTestManagedComponent());
                    m_Manager.SetComponentData(entity, new EcsTestManagedComponent2());
                    m_Manager.SetComponentData(entity, new EcsTestManagedComponent3());
                }

                Clear();
                system.Update();

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.SetComponentObject, World, system.SystemHandleUntyped, entities, typeof(EcsTestManagedComponent), null),
                    MakeRecord(RecordType.SetComponentObject, World, system.SystemHandleUntyped, entities, typeof(EcsTestManagedComponent2), null),
                });
            }
        }

        [Test]
        public void EntitiesForEach_WithStructuralChanges_SetComponentObject()
        {
            var componentTypes = new ComponentType[] { typeof(EcsTestManagedComponent), typeof(EcsTestManagedComponent2), typeof(EcsTestManagedComponent3) };
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var entities = m_Manager.CreateEntity(archetype, 3, Allocator.Temp))
            {
                var system = World.GetOrCreateSystem<TestManagedComponentWithStructuralChangesSystem>();
                foreach (var entity in entities)
                {
                    m_Manager.SetComponentData(entity, new EcsTestManagedComponent());
                    m_Manager.SetComponentData(entity, new EcsTestManagedComponent2());
                    m_Manager.SetComponentData(entity, new EcsTestManagedComponent3());
                }

                Clear();
                system.Update();

                CheckRecords(new[]
                {
                    MakeRecord(RecordType.SetComponentObject, World, system.SystemHandleUntyped, entities[0], typeof(EcsTestManagedComponent), null),
                    MakeRecord(RecordType.SetComponentObject, World, system.SystemHandleUntyped, entities[0], typeof(EcsTestManagedComponent2), null),
                    MakeRecord(RecordType.SetComponentObject, World, system.SystemHandleUntyped, entities[1], typeof(EcsTestManagedComponent), null),
                    MakeRecord(RecordType.SetComponentObject, World, system.SystemHandleUntyped, entities[1], typeof(EcsTestManagedComponent2), null),
                    MakeRecord(RecordType.SetComponentObject, World, system.SystemHandleUntyped, entities[2], typeof(EcsTestManagedComponent), null),
                    MakeRecord(RecordType.SetComponentObject, World, system.SystemHandleUntyped, entities[2], typeof(EcsTestManagedComponent2), null),
                });
            }
        }
#endif
    }
}
#endif
