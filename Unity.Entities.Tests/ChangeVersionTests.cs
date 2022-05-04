using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.Entities.Tests
{
    partial class ChangeVersionTests : ECSTestsFixture
    {
#if !UNITY_DOTSRUNTIME
        partial class BumpVersionSystemInJob : SystemBase
        {
            JobHandle UpdateEcsTestData2()
            {
                return
                    Entities
                        .ForEach((ref EcsTestData data, ref EcsTestData2 data2) =>
                        {
                            data2 = new EcsTestData2 { value0 = 10 };
                        })
                        .Schedule(default);
            }

            protected override void OnUpdate()
            {
                UpdateEcsTestData2().Complete();
            }

            protected override void OnCreate()
            {
            }
        }
#endif

        class BumpVersionSystem : ComponentSystem
        {
            public EntityQuery m_Group;

            protected override void OnUpdate()
            {
                var data = m_Group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
                var data2 = m_Group.ToComponentDataArray<EcsTestData2>(World.UpdateAllocator.ToAllocator);

                for (int i = 0; i < data.Length; ++i)
                {
                    var d2 = data2[i];
                    d2.value0 = 10;
                    data2[i] = d2;
                }

                m_Group.CopyFromComponentDataArray(data);
                m_Group.CopyFromComponentDataArray(data2);
            }

            protected override void OnCreate()
            {
                m_Group = GetEntityQuery(ComponentType.ReadWrite<EcsTestData>(),
                    ComponentType.ReadWrite<EcsTestData2>());
            }
        }

        partial class BumpChunkTypeVersionSystem : SystemBase
        {
            struct UpdateChunks : IJobParallelFor
            {
                public NativeArray<ArchetypeChunk> Chunks;
                public ComponentTypeHandle<EcsTestData> EcsTestDataTypeHandle;

                public void Execute(int chunkIndex)
                {
                    var chunk = Chunks[chunkIndex];
                    var ecsTestData = chunk.GetNativeArray(EcsTestDataTypeHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        ecsTestData[i] = new EcsTestData {value = ecsTestData[i].value + 1};
                    }
                }
            }

            EntityQuery m_Group;
            private bool m_LastAllChanged;

            protected override void OnCreate()
            {
                m_Group = GetEntityQuery(typeof(EcsTestData));
                m_LastAllChanged = false;
            }

            protected override void OnUpdate()
            {
                var chunks = m_Group.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);
                var ecsTestDataType = GetComponentTypeHandle<EcsTestData>();
                var updateChunksJob = new UpdateChunks
                {
                    Chunks = chunks,
                    EcsTestDataTypeHandle = ecsTestDataType
                };
                var updateChunksJobHandle = updateChunksJob.Schedule(chunks.Length, 32, Dependency);
                updateChunksJobHandle.Complete();

                // LastSystemVersion bumped after update. Check for change
                // needs to occur inside system update.
                m_LastAllChanged = true;
                for (int i = 0; i < chunks.Length; i++)
                {
                    m_LastAllChanged &= chunks[i].DidChange(ecsTestDataType, LastSystemVersion);
                }
            }

            public bool AllEcsTestDataChunksChanged()
            {
                return m_LastAllChanged;
            }
        }

        [Test]
        public void CHG_BumpValueChangesChunkTypeVersion()
        {
            m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));

            var bumpChunkTypeVersionSystem = World.CreateSystem<BumpChunkTypeVersionSystem>();

            bumpChunkTypeVersionSystem.Update();
            Assert.AreEqual(true, bumpChunkTypeVersionSystem.AllEcsTestDataChunksChanged());

            bumpChunkTypeVersionSystem.Update();
            Assert.AreEqual(true, bumpChunkTypeVersionSystem.AllEcsTestDataChunksChanged());
        }

        [Test]
        public void CHG_SystemVersionZeroWhenNotRun()
        {
            m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            var system = World.CreateSystem<BumpVersionSystem>();
            Assert.AreEqual(0, system.LastSystemVersion);
            system.Update();
            Assert.AreNotEqual(0, system.LastSystemVersion);
        }

        partial class ChunkDidChangeManagedSystem : SystemBase
        {
            ComponentTypeHandle<EcsTestData> m_ComponentTypeHandle;
            EntityQuery m_Query;

            public bool DidChange { get; private set; }

            protected override void OnCreate()
            {
                m_ComponentTypeHandle = GetComponentTypeHandle<EcsTestData>();
                m_Query = GetEntityQuery(typeof(EcsTestData));
            }

            protected override void OnUpdate()
            {
                using (var chunks = m_Query.CreateArchetypeChunkArray(Allocator.Temp))
                {
                    Assert.That(chunks.Length, Is.EqualTo(1));
                    DidChange = chunks[0].DidChange(m_ComponentTypeHandle, LastSystemVersion);
                }
            }
        }

        [Test]
        public void Chunk_DidChange_ManagedSystemDetectsChange()
        {
            var system = World.GetOrCreateSystem<ChunkDidChangeManagedSystem>();
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));

            system.Update();
            Assert.That(system.DidChange, Is.True);

            system.Update();
            Assert.That(system.DidChange, Is.False);

            system.Update();
            Assert.That(system.DidChange, Is.False);

            m_Manager.SetComponentData(entity, new EcsTestData { value = 1 });

            system.Update();
            Assert.That(system.DidChange, Is.True);

            system.Update();
            Assert.That(system.DidChange, Is.False);

            system.Update();
            Assert.That(system.DidChange, Is.False);
        }

        partial struct ChunkDidChangeUnmanagedSystem : ISystem
        {
            ComponentTypeHandle<EcsTestData> m_ComponentTypeHandle;
            EntityQuery m_Query;

            public bool DidChange { get; private set; }

            public void OnCreate(ref SystemState state)
            {
                m_ComponentTypeHandle = state.GetComponentTypeHandle<EcsTestData>();
                m_Query = state.GetEntityQuery(typeof(EcsTestData));
            }

            public void OnDestroy(ref SystemState state)
            {
            }

            public void OnUpdate(ref SystemState state)
            {
                using (var chunks = m_Query.CreateArchetypeChunkArray(Allocator.Temp))
                {
                    Assert.That(chunks.Length, Is.EqualTo(1));
                    DidChange = chunks[0].DidChange(m_ComponentTypeHandle, state.LastSystemVersion);
                }
            }
        }

        [Test]
        public void Chunk_DidChange_UnmanagedSystemDetectsChange()
        {
            var system = World.GetOrCreateSystem<ChunkDidChangeUnmanagedSystem>();
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));

            system.Update(World.Unmanaged);
            Assert.That(system.Struct.DidChange, Is.True);

            system.Update(World.Unmanaged);
            Assert.That(system.Struct.DidChange, Is.False);

            system.Update(World.Unmanaged);
            Assert.That(system.Struct.DidChange, Is.False);

            m_Manager.SetComponentData(entity, new EcsTestData { value = 1 });

            system.Update(World.Unmanaged);
            Assert.That(system.Struct.DidChange, Is.True);

            system.Update(World.Unmanaged);
            Assert.That(system.Struct.DidChange, Is.False);

            system.Update(World.Unmanaged);
            Assert.That(system.Struct.DidChange, Is.False);
        }

        partial class DidChangeTestSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var bfe = GetBufferFromEntity<EcsIntElement>(true);
                var cdfe = GetComponentDataFromEntity<EcsTestData>(true);
                uint lastSysVersion = LastSystemVersion;
                Entities
                    .WithAll<EcsTestData, EcsIntElement>()
                    .ForEach((Entity e, ref EcsTestData2 changed) =>
                    {
                        changed.value0 = cdfe.DidChange(e, lastSysVersion) ? 1 : 0;
                        changed.value1 = bfe.DidChange(e, lastSysVersion) ? 1 : 0;
                    }).Run();
            }
        }

        partial class ChangeEntitiesWithTag : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithAll<EcsTestTag>()
                    .ForEach((Entity e, ref EcsTestData testData, ref DynamicBuffer<EcsIntElement> buf) =>
                    {
                        testData.value += 10;
                        buf.Add(new EcsIntElement{Value=17});
                    }).Run();
            }
        }

        [Test]
        public void ComponentDataFromEntity_DidChange_DetectsChanges()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsIntElement), typeof(EcsTestData2));
            int entityCount = 10;
            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp);
            for(int i=0; i<entityCount; ++i)
            {
                if (i % 2 == 0)
                    m_Manager.AddComponent<EcsTestTag>(entities[i]);
            }

            var detectChangesSys = World.CreateSystem<DidChangeTestSystem>();
            var changeEntitiesWithTagSys = World.CreateSystem<ChangeEntitiesWithTag>();

            // First update: all elements "changed"
            detectChangesSys.Update();
            foreach(var ent in entities)
                Assert.AreEqual(1, m_Manager.GetComponentData<EcsTestData2>(ent).value0);

            // Second update: no changes
            detectChangesSys.Update();
            foreach(var ent in entities)
                Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestData2>(ent).value0);

            // Modify only entities with the EcsTestTag.
            changeEntitiesWithTagSys.Update();

            // Third update: has EcsTestTag -> non-zero EcsTestData2.value0
            detectChangesSys.Update();
            foreach (var ent in entities)
            {
                bool hasTag = m_Manager.HasComponent<EcsTestTag>(ent);
                Assert.AreEqual(hasTag, m_Manager.GetComponentData<EcsTestData2>(ent).value0 != 0);
            }

            entities.Dispose();
        }

        [Test]
        public void BufferFromEntity_DidChange_DetectsChanges()
        {
            var archetype =
                m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsIntElement), typeof(EcsTestData2));
            int entityCount = 10;
            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp);
            for(int i=0; i<entityCount; ++i)
            {
                if (i % 2 == 0)
                    m_Manager.AddComponent<EcsTestTag>(entities[i]);
            }

            var detectChangesSys = World.CreateSystem<DidChangeTestSystem>();
            var changeEntitiesWithTagSys = World.CreateSystem<ChangeEntitiesWithTag>();

            // First update: all elements "changed"
            detectChangesSys.Update();
            foreach(var ent in entities)
                Assert.AreEqual(1, m_Manager.GetComponentData<EcsTestData2>(ent).value1);

            // Second update: no changes
            detectChangesSys.Update();
            foreach(var ent in entities)
                Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestData2>(ent).value1);

            // Modify only entities with the EcsTestTag.
            changeEntitiesWithTagSys.Update();

            // Third update: has EcsTestTag -> non-zero EcsTestData2.value1
            detectChangesSys.Update();
            foreach (var ent in entities)
            {
                bool hasTag = m_Manager.HasComponent<EcsTestTag>(ent);
                Assert.AreEqual(hasTag, m_Manager.GetComponentData<EcsTestData2>(ent).value1 != 0);
            }

            entities.Dispose();
        }

#if !UNITY_DOTSRUNTIME
        [Test]
        public void CHG_SystemVersionJob()
        {
            m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            var system = World.CreateSystem<BumpVersionSystemInJob>();
            Assert.AreEqual(0, system.LastSystemVersion);
            system.Update();
            Assert.AreNotEqual(0, system.LastSystemVersion);
        }

#endif
    }
}
