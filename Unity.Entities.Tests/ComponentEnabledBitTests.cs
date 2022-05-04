using System;
using System.Collections.Generic;
#if !NET_DOTS && !UNITY_DOTSRUNTIME // DOTS Runtimes does not support regex
using System.Text.RegularExpressions;
#endif
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Entities.Tests
{
    partial class ComponentEnabledBitTests : ECSTestsFixture
    {
        [Test]
        public unsafe void IsComponentEnabled_NewEntities_IsTrue()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));
            var maxEntitiesPerChunk = archetype.ChunkCapacity;
            using (var types = archetype.GetComponentTypes(World.UpdateAllocator.ToAllocator))
            using (var entities = m_Manager.CreateEntity(archetype, maxEntitiesPerChunk, World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(1, archetype.ChunkCount);

                // Test ComponentType variant
                foreach (var t in types)
                {
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype, t.TypeIndex);
                    Assert.AreEqual(0, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));
                    foreach (var ent in entities)
                    {
                        Assert.IsTrue(m_Manager.IsComponentEnabled(ent, t), $"Component {t} in Entity {ent} is should be enabled, but isn't");
                    }
                }
                // Test generic interface
                foreach (var ent in entities)
                {
                    Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestDataEnableable>(ent), $"Component {nameof(EcsTestDataEnableable)} in Entity {ent} is should be enabled, but isn't");
                    Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestDataEnableable2>(ent), $"Component {nameof(EcsTestDataEnableable2)} in Entity {ent} is should be enabled, but isn't");
                }

            }
        }

        [Test]
        public unsafe void IsComponentEnabled_ImmediatelyAfterSet_HasCorrectValue()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));
            var maxEntitiesPerChunk = archetype.ChunkCapacity;
            using (var types = archetype.GetComponentTypes(World.UpdateAllocator.ToAllocator))
            using (var entities = m_Manager.CreateEntity(archetype, maxEntitiesPerChunk, World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(1, archetype.ChunkCount);

                // Test ComponentType interface
                foreach (var t in types)
                {
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype, t.TypeIndex);
                    foreach (var ent in entities)
                    {
                        m_Manager.SetComponentEnabled(ent, t, false);
                        Assert.IsFalse(m_Manager.IsComponentEnabled(ent, t), $"Component {t} in Entity {ent} is should be disabled, but isn't");

                        Assert.AreEqual(1, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));

                        m_Manager.SetComponentEnabled(ent, t, true);
                        Assert.IsTrue(m_Manager.IsComponentEnabled(ent, t), $"Component {t} in Entity {ent} is should be enabled, but isn't");

                        Assert.AreEqual(0, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));
                    }
                }

                // Test generic interface
                {
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype,
                        TypeManager.GetTypeIndex<EcsTestDataEnableable>());
                    foreach (var ent in entities)
                    {
                        m_Manager.SetComponentEnabled<EcsTestDataEnableable>(ent, false);
                        Assert.IsFalse(m_Manager.IsComponentEnabled<EcsTestDataEnableable>(ent),
                            $"Component {nameof(EcsTestDataEnableable)} in Entity {ent} is should be disabled, but isn't");
                        Assert.AreEqual(1, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));

                        m_Manager.SetComponentEnabled<EcsTestDataEnableable>(ent, true);
                        Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestDataEnableable>(ent),
                            $"Component {nameof(EcsTestDataEnableable)} in Entity {ent} is should be enabled, but isn't");
                        Assert.AreEqual(0, archetype.Archetype->Chunks.GetChunkDisabledCountForType(indexInTypeArray, 0));
                    }
                }
            }
        }

        partial class DummySystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((in EcsTestDataEnableable2 testData2) => { }).Run();
            }
        }

        [Test]
        public unsafe void SetComponentEnabled_ChunkChangeVersion_IsChanged()
        {
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ent = m_Manager.CreateEntity(archetype);
            var typeIndex = TypeManager.GetTypeIndex(typeof(EcsTestDataEnableable));
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(archetype.Archetype, typeIndex);
            var versionBefore = ecs->GetChunk(ent)->GetChangeVersion(typeIndexInArchetype);
            // Force a system update on unrelated entities to bump the global system version
            var ent2 = m_Manager.CreateEntity(typeof(EcsTestDataEnableable2));
            var sys = World.CreateSystem<DummySystem>();
            sys.Update();
            var versionAfterUpdate = ecs->GetChunk(ent)->GetChangeVersion(typeIndexInArchetype);
            Assert.AreEqual(versionBefore, versionAfterUpdate, "Chunk's change version should be the same after unrelated system update");
            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(ent, false);
            var versionAfterSet = ecs->GetChunk(ent)->GetChangeVersion(typeIndexInArchetype);
            Assert.AreNotEqual(versionBefore, versionAfterSet, "Chunk's change version should be different after SetComponentEnabled()");
        }

        [Test]
        public unsafe void SetEnabled_ThrowsWithNonIEnableableComponent()
        {
            var type = ComponentType.ReadOnly<EcsTestData>();
            var archetype = m_Manager.CreateArchetype(type);
            var entity = m_Manager.CreateEntity(archetype);
            Assert.Throws<ArgumentException>(() =>
            {
                m_Manager.SetComponentEnabled(entity, type, false);
            });
        }

        static bool GetTestEntityShouldBeEnabled(int entityIndex, int chunkIndex)
        {
            return entityIndex % (2 + chunkIndex) == 0;
        }

        static unsafe void SetupChunkWithEnabledBits(ref EntityManager manager, ComponentType enableableType, Allocator allocator, out NativeArray<Entity> outEntities, out UnsafeParallelHashMap<Entity, bool> outMap, out EntityArchetype outArchetype, int chunkCount = 1, params ComponentType[] additionalTypes)
        {
            var types = new List<ComponentType>();
            types.Add(enableableType);
            types.Add(ComponentType.ReadOnly<EcsTestData>());
            types.AddRange(additionalTypes);

            outArchetype = manager.CreateArchetype(types.ToArray());

            outMap = new UnsafeParallelHashMap<Entity, bool>(outArchetype.ChunkCapacity * chunkCount, allocator);
            outEntities = manager.CreateEntity(outArchetype, outArchetype.ChunkCapacity * chunkCount, allocator);

            for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
            {
                for (int i = 0; i < outArchetype.ChunkCapacity; ++i)
                {
                    var entityIndex = chunkIndex * outArchetype.ChunkCapacity + i;
                    var value = GetTestEntityShouldBeEnabled(entityIndex, chunkIndex);
                    manager.SetComponentEnabled(outEntities[entityIndex], enableableType, value);
                    outMap.Add(outEntities[entityIndex], value);
                }
            }
        }

        static unsafe void CheckChunkDataAndMapConsistency(EntityManager manager, ComponentType enableableType, NativeArray<Entity> entities, UnsafeParallelHashMap<Entity, bool> map, int skipStartIndex = -1, int skipCount = 0)
        {
            for (int i = 0; i < entities.Length; ++i)
            {
                if(skipStartIndex != -1 && i >= skipStartIndex && i < skipStartIndex + skipCount)
                    continue;

                var ecsIsComponentEnabled = manager.IsComponentEnabled(entities[i], enableableType);
                var mapIsComponentEnabled = map[entities[i]];

                Assert.AreEqual(mapIsComponentEnabled, ecsIsComponentEnabled);
            }
        }

        static unsafe void CheckChunkDataAndMapConsistency_WithRemapping(EntityManager dstManager, ComponentType enableableType, NativeArray<Entity> srcEntities, NativeArray<Entity> dstEntities, UnsafeParallelHashMap<Entity, bool> map, int skipIndex = -1)
        {
            for (int i = 0; i < srcEntities.Length; ++i)
            {
                if(skipIndex != -1 && i == skipIndex)
                    continue;

                var ecsIsComponentEnabled = dstManager.IsComponentEnabled(dstEntities[i], enableableType);
                var mapIsComponentEnabled = map[srcEntities[i]];

                Assert.AreEqual(mapIsComponentEnabled, ecsIsComponentEnabled);
            }
        }

        [Test]
        public void AddDataComponent_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddComponent<EcsTestData>(entities[7]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddTagComponent_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddComponent<EcsTestTag>(entities[7]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddTagComponent_Query_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            var query = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestDataEnableable>());
            m_Manager.AddComponent<EcsTestTag>(query);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();

            query.Dispose();
        }

        [Test]
        public void AddBuffer_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddBuffer<EcsIntElement>(entities[7]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddSharedComponentData_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddSharedComponentData<EcsTestSharedComp>(entities[7], new EcsTestSharedComp(0));
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void SetSharedComponentData_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount, typeof(EcsTestSharedComp));

            m_Manager.AddSharedComponentData<EcsTestSharedComp>(entities[7], new EcsTestSharedComp(10));
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddChunkComponentData_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddChunkComponentData<EcsTestData2>(entities[7]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddChunkComponentData_Query_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.AddChunkComponentData<EcsTestData2>(m_Manager.UniversalQuery, new EcsTestData2(7));
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void RemoveComponentData_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.RemoveComponent<EcsTestData>(entities[7]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void RemoveComponentData_Batched_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.RemoveComponent<EcsTestData>(entities);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void CreateEntity_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            m_Manager.CreateEntity(m_Manager.CreateArchetype(enableableType));
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public unsafe void InstantiatePrefab_One_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            var archetype = m_Manager.CreateArchetype(enableableType, ComponentType.ReadOnly<Prefab>());
            var prefabEntity = m_Manager.CreateEntity(archetype);

            m_Manager.SetComponentEnabled(prefabEntity, enableableType, false);
            var entity = m_Manager.Instantiate(prefabEntity);
            Assert.AreEqual(false, m_Manager.IsComponentEnabled(entity, enableableType));

            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public unsafe void InstantiatePrefab_Many_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            var archetype = m_Manager.CreateArchetype(enableableType, ComponentType.ReadOnly<Prefab>());
            var prefabEntity = m_Manager.CreateEntity(archetype);

            m_Manager.SetComponentEnabled(prefabEntity, enableableType, false);
            using (var entities = m_Manager.Instantiate(prefabEntity, archetype.ChunkCapacity * chunkCount, m_Manager.World.UpdateAllocator.ToAllocator))
            {
                for (int i = 0; i < entities.Length; ++i)
                {
                    Assert.AreEqual(false, m_Manager.IsComponentEnabled(entities[i], enableableType));
                }
            }

            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void DestroyEntityFromMiddleOfChunk_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            var destroyIndex = 10;
            m_Manager.DestroyEntity(entities[destroyIndex]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map, destroyIndex, 1);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void DestroyEntityFromEndOfChunk_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            var destroyIndex = entities.Length - 1;
            m_Manager.DestroyEntity(entities[destroyIndex]);
            CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map, destroyIndex, 1);
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void MoveEntitiesFrom_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var dstWorld = new World("CopyWorld");
            var dstManager = dstWorld.EntityManager;
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            dstManager.MoveEntitiesFrom(out var copyWorldEntities, m_Manager);
            CheckChunkDataAndMapConsistency_WithRemapping(dstManager, enableableType, entities, copyWorldEntities, map);

            m_Manager.Debug.CheckInternalConsistency();
            dstManager.Debug.CheckInternalConsistency();

            copyWorldEntities.Dispose();
            dstWorld.Dispose();
        }

        [Test]
        public void MoveEntitiesFrom_WithQuery_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var dstWorld = new World("CopyWorld");
            var dstManager = dstWorld.EntityManager;
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            var query = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestDataEnableable>());
            dstManager.MoveEntitiesFrom(out var copyWorldEntities, m_Manager, query);
            CheckChunkDataAndMapConsistency_WithRemapping(dstManager, enableableType, entities, copyWorldEntities, map);

            m_Manager.Debug.CheckInternalConsistency();
            dstManager.Debug.CheckInternalConsistency();

            copyWorldEntities.Dispose();
            dstWorld.Dispose();
        }

        [Test]
        public void CopyEntitiesFrom_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var dstWorld = new World("CopyWorld");
            var dstManager = dstWorld.EntityManager;
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, chunkCount);

            var copyWorldEntities = new NativeArray<Entity>(entities.Length, Allocator.Persistent);
            dstManager.CopyEntitiesFrom(m_Manager, entities, copyWorldEntities);
            CheckChunkDataAndMapConsistency_WithRemapping(dstManager, enableableType, entities, copyWorldEntities, map);

            m_Manager.Debug.CheckInternalConsistency();
            dstManager.Debug.CheckInternalConsistency();

            copyWorldEntities.Dispose();
            dstWorld.Dispose();
        }

        [Test]
        public unsafe void Serialization_PreservesBitValues([Values(1, 4)] int chunkCount)
        {
            var archetype = m_Manager.CreateArchetype(ComponentType.ReadOnly<EcsTestDataEnableable>(), ComponentType.ReadOnly<EcsTestData>());
            using (var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount, Allocator.TempJob))
            {
                for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                {
                    for (int i = 0; i < archetype.ChunkCapacity; ++i)
                    {
                        var entityIndex = chunkIndex * archetype.ChunkCapacity + i;
                        var value = GetTestEntityShouldBeEnabled(entityIndex, chunkIndex);
                        m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[entityIndex], value);
                    }
                }
            }

            // disposed via reader
            var writer = new TestBinaryWriter(World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);

            using (var deserializeWorld = new World("DeserializeWorld"))
            using (var deserializeQuery = deserializeWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestDataEnableable>()))
            using (var reader = new TestBinaryReader(writer))
            {
                var deserializeManager = deserializeWorld.EntityManager;
                SerializeUtility.DeserializeWorld(deserializeWorld.EntityManager.BeginExclusiveEntityTransaction(), reader);
                deserializeManager.EndExclusiveEntityTransaction();

                var queryData = deserializeQuery._GetImpl()->_QueryData;
                Assert.AreEqual(1, queryData->MatchingArchetypes.Length);
                var newArchetype = queryData->MatchingArchetypes.Ptr[0]->Archetype;
                var chunks = newArchetype->Chunks;
                Assert.AreEqual(chunkCount, chunks.Count);

                for (int chunkIndex = 0; chunkIndex < chunks.Count; ++chunkIndex)
                {
                    var chunk = chunks[chunkIndex];
                    for (int i = 0; i < chunk->Count; ++i)
                    {
                        var entityIndexInQuery = chunkIndex * archetype.ChunkCapacity + i;
                        var value = GetTestEntityShouldBeEnabled(entityIndexInQuery, chunkIndex);

                        var entityArray = (Entity*) chunk->Buffer;
                        Assert.AreEqual(value, deserializeManager.IsComponentEnabled<EcsTestDataEnableable>(entityArray[i]));
                    }

                }
                deserializeManager.Debug.CheckInternalConsistency();
            }
        }

        [Test]
        public unsafe void ArchetypeStoresEnableableTypes()
        {
            var enableableTypeA = ComponentType.ReadOnly<EcsTestDataEnableable>();
            var enableableTypeB = ComponentType.ReadOnly<EcsTestDataEnableable2>();
            var archetype = m_Manager.CreateArchetype(enableableTypeA, typeof(EcsTestData), typeof(EcsTestData2), enableableTypeB);

            Assert.AreEqual(5, archetype.Archetype->TypesCount);
            Assert.AreEqual(2, archetype.Archetype->EnableableTypesCount); // Entity + EcsTestDataEnableable

            var types = archetype.Archetype->Types;
            Assert.AreEqual(enableableTypeA.TypeIndex, types[archetype.Archetype->EnableableTypeIndexInArchetype[0]].TypeIndex);
            Assert.AreEqual(enableableTypeB.TypeIndex, types[archetype.Archetype->EnableableTypeIndexInArchetype[1]].TypeIndex);
        }

        [Test]
        public void MoveChunkWithinArchetype_PreservesEnabledBits()
        {
            var enableableType = ComponentType.ReadOnly<EcsTestDataEnableable>();
            SetupChunkWithEnabledBits(ref m_Manager, enableableType, World.UpdateAllocator.ToAllocator, out var entities, out var map, out var archetype, 4);

            using(var query = m_Manager.CreateEntityQuery(enableableType))
            using (var chunks = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator))
            {
                var entityType = m_Manager.GetEntityTypeHandle();
                var destroyChunkIndex = 1;
                m_Manager.DestroyEntity(chunks[destroyChunkIndex].GetNativeArray(entityType));

                var destroyStartIndex = archetype.ChunkCapacity * destroyChunkIndex;
                var destroyCount = archetype.ChunkCapacity;
                CheckChunkDataAndMapConsistency(m_Manager, enableableType, entities, map, destroyStartIndex, destroyCount);

                m_Manager.Debug.CheckInternalConsistency();
            }
        }


        struct WriteBitsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [NativeDisableContainerSafetyRestriction]public ComponentDataFromEntity<EcsTestDataEnableable> EnableableTypeRW;

            public void Execute(int index)
            {
                var entity = Entities[index];
                var setValue = index % 2 == 0;
                EnableableTypeRW.SetComponentEnabled(entity, setValue);
            }
        }

        [Test]
        public void ParallelWrites_PreservesMetadataCount([Values(1, 4)] int chunkCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            using (var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount, m_Manager.World.UpdateAllocator.ToAllocator))
            {
                new WriteBitsJob
                {
                    Entities = entities,
                    EnableableTypeRW = m_Manager.GetComponentDataFromEntity<EcsTestDataEnableable>(false)
                }.Schedule(entities.Length, 1, default).Complete();

                m_Manager.Debug.CheckInternalConsistency();
            }
        }

        [Test]
        public unsafe void EntityQueryCreatesCorrectMatchingArchetypes()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable));
            var archetypeC = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>(),},
                None = new ComponentType[0],
                Any = new ComponentType[0]
            }))
            {
                var queryData = query._GetImpl()->_QueryData;
                var matchingArchetypes = queryData->MatchingArchetypes;
                Assert.AreEqual(3, matchingArchetypes.Length);

                var a = matchingArchetypes.Ptr[0];
                Assert.AreEqual(0, a->EnableableComponentsCount_All);
                Assert.AreEqual(0, a->EnableableComponentsCount_None);

                var b = matchingArchetypes.Ptr[1];
                Assert.AreEqual(0, b->EnableableComponentsCount_All);
                Assert.AreEqual(0, b->EnableableComponentsCount_None);

                var c = matchingArchetypes.Ptr[2];
                Assert.AreEqual(0, b->EnableableComponentsCount_All);
                Assert.AreEqual(0, b->EnableableComponentsCount_None);
            }

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadOnly<EcsTestDataEnableable>(), },
                None = new ComponentType[0],
                Any = new ComponentType[0]
            }))
            {
                var queryData = query._GetImpl()->_QueryData;
                var matchingArchetypes = queryData->MatchingArchetypes;
                Assert.AreEqual(2, matchingArchetypes.Length);

                var a = matchingArchetypes.Ptr[0];
                Assert.AreEqual(1, a->EnableableComponentsCount_All);
                Assert.AreEqual(0, a->EnableableComponentsCount_None);

                var b = matchingArchetypes.Ptr[1];
                Assert.AreEqual(1, b->EnableableComponentsCount_All);
                Assert.AreEqual(0, b->EnableableComponentsCount_None);
            }

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>(), },
                None = new [] {ComponentType.ReadOnly<EcsTestDataEnableable>()},
                Any = new ComponentType[0]
            }))
            {
                var queryData = query._GetImpl()->_QueryData;
                var matchingArchetypes = queryData->MatchingArchetypes;
                Assert.AreEqual(3, matchingArchetypes.Length);

                var a = matchingArchetypes.Ptr[0];
                Assert.AreEqual(0, a->EnableableComponentsCount_All);
                Assert.AreEqual(0, a->EnableableComponentsCount_None);

                var b = matchingArchetypes.Ptr[1];
                Assert.AreEqual(0, b->EnableableComponentsCount_All);
                Assert.AreEqual(1, b->EnableableComponentsCount_None);

                var c = matchingArchetypes.Ptr[1];
                Assert.AreEqual(0, c->EnableableComponentsCount_All);
                Assert.AreEqual(1, c->EnableableComponentsCount_None);
            }

            // None on non-enableable types does not result in extra archetypes added to MatchingArchetypes
            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestDataEnableable>(), },
                None = new [] {ComponentType.ReadOnly<EcsTestData>()},
                Any = new ComponentType[0]
            }))
            {
                var queryData = query._GetImpl()->_QueryData;
                var matchingArchetypes = queryData->MatchingArchetypes;
                Assert.AreEqual(0, matchingArchetypes.Length);
            }
        }

        private unsafe NativeArray<ArchetypeChunk> CreateChunks(ref EntityManager manager, EntityArchetype archetype, int chunkCount, Allocator allocator)
        {
            manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount);

            var ret = new NativeArray<ArchetypeChunk>(chunkCount, allocator);
            for (int i = 0; i < chunkCount; ++i)
            {
                ret[i] = new ArchetypeChunk(archetype.Archetype->Chunks[i], manager.GetCheckedEntityDataAccess()->EntityComponentStore);
            }

            return ret;
        }

        [Test]
        public unsafe void Batching_OneEntityDisabled()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1360, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);

                var firstChunk = chunks[0];
                firstChunk.SetComponentEnabled(enableableTypeHandle, 10, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(2, batchCount);

                Assert.AreEqual(0, batches[0].m_BatchStartEntityIndex);
                Assert.AreEqual(10, batches[0].Count);

                //// archetype.ChunkCapacity - 11 = 1349.
                Assert.AreEqual(11, batches[1].m_BatchStartEntityIndex);
                Assert.AreEqual(1349, batches[1].Count);
            }
        }

        [Test]
        public unsafe void Batching_GapContinuesIntoNextStride()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1360, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);

                var firstChunk = chunks[0];
                firstChunk.SetComponentEnabled(enableableTypeHandle, 61, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle,62, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle, 63, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle,64, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(2, batchCount);

                Assert.AreEqual(0, batches[0].m_BatchStartEntityIndex);
                Assert.AreEqual(61, batches[0].Count);

                //// archetype.ChunkCapacity - 61 - 4 = 1295.
                Assert.AreEqual(65, batches[1].m_BatchStartEntityIndex);
                Assert.AreEqual(1295, batches[1].Count);
            }
        }

        [Test]
        public unsafe void Batching_GapEndsAtFirstStride()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1360, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);

                var firstChunk = chunks[0];
                firstChunk.SetComponentEnabled(enableableTypeHandle, 62, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle,63, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle, 127, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(3, batchCount);

                Assert.AreEqual(0, batches[0].m_BatchStartEntityIndex);
                Assert.AreEqual(62, batches[0].Count);

                Assert.AreEqual(64, batches[1].m_BatchStartEntityIndex);
                Assert.AreEqual(63, batches[1].Count);

                //// archetype.ChunkCapacity - 62 - 63 - 3 = 1232.
                Assert.AreEqual(128, batches[2].m_BatchStartEntityIndex);
                Assert.AreEqual(1232, batches[2].Count);
            }
        }

        [Test]
        public unsafe void Batching_GapLastsForFullStride()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1360, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);

                var firstChunk = chunks[0];
                for (int i = 64; i < 128; ++i)
                {
                    firstChunk.SetComponentEnabled(enableableTypeHandle, i, false);
                }

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(2, batchCount);

                Assert.AreEqual(0, batches[0].m_BatchStartEntityIndex);
                Assert.AreEqual(64, batches[0].Count);

                //// archetype.ChunkCapacity - 64 - 64 = 1232.
                Assert.AreEqual(128, batches[1].m_BatchStartEntityIndex);
                Assert.AreEqual(1232, batches[1].Count);
            }
        }

        [Test]
        public unsafe void Batching_GapStartsAtOneStrideAndLastsForNextFullStride()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1360, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);

                var firstChunk = chunks[0];
                for (int i = 63; i < 128; ++i)
                {
                    firstChunk.SetComponentEnabled(enableableTypeHandle, i, false);
                }

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(2, batchCount);

                Assert.AreEqual(0, batches[0].m_BatchStartEntityIndex);
                Assert.AreEqual(63, batches[0].Count);

                //// archetype.ChunkCapacity - 64 - 64 = 1231.
                Assert.AreEqual(128, batches[1].m_BatchStartEntityIndex);
                Assert.AreEqual(1232, batches[1].Count);
            }
        }

        [Test]
        public unsafe void Batching_GapEndsAtFirstStride_NextBatchIsFull()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1360, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);

                var firstChunk = chunks[0];
                firstChunk.SetComponentEnabled(enableableTypeHandle, 62, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle,63, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(2, batchCount);

                Assert.AreEqual(0, batches[0].m_BatchStartEntityIndex);
                Assert.AreEqual(62, batches[0].Count);

                //// archetype.ChunkCapacity - 62 - 2 = 1296.
                Assert.AreEqual(64, batches[1].m_BatchStartEntityIndex);
                Assert.AreEqual(1296, batches[1].Count);
            }
        }

        [Test]
        public unsafe void Batching_BitstringEndsWithGap()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1360, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);

                var firstChunk = chunks[0];
                var lastEntityIndex = archetype.ChunkCapacity - 1;
                firstChunk.SetComponentEnabled(enableableTypeHandle, lastEntityIndex, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(1, batchCount);

                Assert.AreEqual(0, batches[0].m_BatchStartEntityIndex);
                Assert.AreEqual(lastEntityIndex, batches[0].Count);
            }
        }

        [Test]
        public unsafe void Batching_FirstGapIsNextAfterFirstStride()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1360, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);

                var firstChunk = chunks[0];
                firstChunk.SetComponentEnabled(enableableTypeHandle, 64, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(2, batchCount);

                Assert.AreEqual(0, batches[0].m_BatchStartEntityIndex);
                Assert.AreEqual(64, batches[0].Count);

                //// archetype.ChunkCapacity - 64 - 1 = 1295.
                Assert.AreEqual(65, batches[1].m_BatchStartEntityIndex);
                Assert.AreEqual(1295, batches[1].Count);
            }
        }

        [Test]
        public unsafe void Batching_FirstGapIsAfterFirstStride()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1360, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);

                var firstChunk = chunks[0];
                firstChunk.SetComponentEnabled(enableableTypeHandle, 65, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(2, batchCount);

                Assert.AreEqual(0, batches[0].m_BatchStartEntityIndex);
                Assert.AreEqual(65, batches[0].Count);

                //// archetype.ChunkCapacity - 65 - 1 = 1294.
                Assert.AreEqual(66, batches[1].m_BatchStartEntityIndex);
                Assert.AreEqual(1294, batches[1].Count);
            }
        }

        [Test]
        public unsafe void Batching_SingleDisabledEntity()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;
            var entity = m_Manager.CreateEntity(archetype);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            {
                var chunkCache = query.__impl->_QueryData->GetMatchingChunkCache();
                Assert.AreEqual(1, chunkCache.Length);
                m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entity, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];
                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(chunkCache.Ptr[0], matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(0, batchCount);
            }
        }

        [Test]
        public unsafe void Batching_AllEntitiesDisabled()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1360, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);
                var firstChunk = chunks[0];
                for (int i = 0; i < archetype.ChunkCapacity; ++i)
                {
                    firstChunk.SetComponentEnabled(enableableTypeHandle, i, false);
                }

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(0, batchCount);
            }
        }

        [Test]
        public unsafe void Batching_QueryNone()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable));
            var ecs = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore;

            Assert.AreEqual(1016, archetype.ChunkCapacity);

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>()},
                None = new[] {ComponentType.ReadOnly<EcsTestDataEnableable>()}
            }))
            using (var chunks = CreateChunks(ref m_Manager, archetype, 1, Allocator.TempJob))
            {
                var enableableTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false);
                var firstChunk = chunks[0];
                firstChunk.SetComponentEnabled(enableableTypeHandle, 0, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle, 32, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle, 33, false);
                firstChunk.SetComponentEnabled(enableableTypeHandle, 34, false);

                var matchingArchetype = query._GetImpl()->_QueryData->MatchingArchetypes.Ptr[0];

                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                ChunkIterationUtility.FindBatchesForChunk(firstChunk.m_Chunk, matchingArchetype, ecs, batches, out var batchCount);
                Assert.AreEqual(2, batchCount);

                Assert.AreEqual(0, batches[0].m_BatchStartEntityIndex);
                Assert.AreEqual(1, batches[0].Count);

                Assert.AreEqual(32, batches[1].m_BatchStartEntityIndex);
                Assert.AreEqual(3, batches[1].Count);
            }
        }

#if !NET_DOTS && !UNITY_DOTSRUNTIME // DOTS Runtimes does not support regex
        struct DataJob_WriteBits_ComponentDataFromEntity : IJobEntityBatch
        {
            [ReadOnly]public ComponentDataFromEntity<EcsTestDataEnableable> EnableableType;
            [ReadOnly]public EntityTypeHandle EntityType;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var entities = batchInChunk.GetNativeArray(EntityType);
                EnableableType.SetComponentEnabled(entities[0], false);
            }
        }

        struct BufferJob_WriteBits_BufferDataFromEntity : IJobEntityBatch
        {
            [ReadOnly]public BufferFromEntity<EcsIntElementEnableable> EnableableType;
            [ReadOnly]public EntityTypeHandle EntityType;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var entities = batchInChunk.GetNativeArray(EntityType);
                EnableableType.SetComponentEnabled(entities[0], false);
            }
        }

        struct DataJob_WritesBitsToNonEnableable : IJobEntityBatch
        {
            public ComponentDataFromEntity<EcsTestData> NonEnableableType;
            [ReadOnly]public EntityTypeHandle EntityType;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var entities = batchInChunk.GetNativeArray(EntityType);
                NonEnableableType.SetComponentEnabled(entities[0], false);
            }
        }

        struct BufferJob_WritesBitsToNonEnableable : IJobEntityBatch
        {
            public BufferFromEntity<EcsIntElement> NonEnableableType;
            [ReadOnly]public EntityTypeHandle EntityType;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var entities = batchInChunk.GetNativeArray(EntityType);
                NonEnableableType.SetComponentEnabled(entities[0], false);
            }
        }

        [Test]
        public void WritingBitsToReadOnlyData_TriggersSafetySystem_ComponentDataFromEntity()
        {
            m_Manager.CreateEntity(typeof(EcsTestDataEnableable));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestDataEnableable>());

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            new DataJob_WriteBits_ComponentDataFromEntity{EntityType = m_Manager.GetEntityTypeHandle(), EnableableType = m_Manager.GetComponentDataFromEntity<EcsTestDataEnableable>(true)}.Run(queryRO);
        }

        [Test]
        public void WritingBitsToReadOnlyBuffer_TriggersSafetySystem_ComponentDataFromEntity()
        {
            m_Manager.CreateEntity(typeof(EcsIntElementEnableable));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsIntElementEnableable>());

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            new BufferJob_WriteBits_BufferDataFromEntity(){EntityType = m_Manager.GetEntityTypeHandle(), EnableableType = m_Manager.GetBufferFromEntity<EcsIntElementEnableable>(true)}.Run(queryRO);
        }

        [Test]
        public void WritingBitsForNonEnableableDataType_Throws()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestData>());

            LogAssert.Expect(LogType.Exception, new Regex("ArgumentException"));
            new DataJob_WritesBitsToNonEnableable{EntityType = m_Manager.GetEntityTypeHandle(), NonEnableableType = m_Manager.GetComponentDataFromEntity<EcsTestData>(false)}.Run(queryRO);
        }

        [Test]
        public void WritingBitsForNonEnableableBufferType_Throws()
        {
            m_Manager.CreateEntity(typeof(EcsIntElement));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsIntElement>());

            LogAssert.Expect(LogType.Exception, new Regex("ArgumentException"));
            new BufferJob_WritesBitsToNonEnableable{EntityType = m_Manager.GetEntityTypeHandle(), NonEnableableType = m_Manager.GetBufferFromEntity<EcsIntElement>(false)}.Run(queryRO);
        }

        struct DataJob_WritesBits_ArchetypeChunk : IJobEntityBatch
        {
            [ReadOnly]public ComponentTypeHandle<EcsTestDataEnableable> EnableableType;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                batchInChunk.SetComponentEnabled(EnableableType, 0, true);
            }
        }

        struct BufferJob_WritesBits_ArchetypeChunk : IJobEntityBatch
        {
            [ReadOnly]public BufferTypeHandle<EcsIntElementEnableable> EnableableType;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                batchInChunk.SetComponentEnabled(EnableableType, 0, true);
            }
        }

        [Test]
        public void WritingBitsToReadOnlyData_TriggersSafetySystem_ArchetypeChunk()
        {
            m_Manager.CreateEntity(typeof(EcsTestDataEnableable));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestDataEnableable>());

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            new DataJob_WritesBits_ArchetypeChunk(){ EnableableType = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(true)}.Run(queryRO);
        }

        [Test]
        public void WritingBitsToReadOnlyBuffer_TriggersSafetySystem_ArchetypeChunk()
        {
            m_Manager.CreateEntity(typeof(EcsIntElementEnableable));
            var queryRO = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsIntElementEnableable>());

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            new BufferJob_WritesBits_ArchetypeChunk(){ EnableableType = m_Manager.GetBufferTypeHandle<EcsIntElementEnableable>(true)}.Run(queryRO);
        }
#endif
    }
}
