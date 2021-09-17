#if ENABLE_PROFILER && UNITY_EDITOR
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using static Unity.Entities.EntitiesProfiler;
using static Unity.Entities.StructuralChangesProfiler;

namespace Unity.Entities.Tests
{
    [TestFixture]
    unsafe class StructuralChangesProfilerTests : ECSTestsFixture
    {
        static readonly string s_DataFilePath = Path.Combine(Application.temporaryCachePath, "profilerdata");
        static readonly string s_RawDataFilePath = s_DataFilePath + ".raw";

        partial class TestSystem : SystemBase
        {
            public Action<SystemBase> OnUpdateAction;
            protected override void OnUpdate() => OnUpdateAction(this);
        }

        RawFrameDataView GenerateFrameMetaData(Action<SystemBase> onUpdate, bool withSystem = true)
        {
            EntitiesProfiler.Shutdown();
            EntitiesProfiler.Initialize();

            Profiler.logFile = s_DataFilePath;
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;

            if (withSystem)
            {
                var system = World.AddSystem(new TestSystem { OnUpdateAction = onUpdate });
                World.GetOrCreateSystem<SimulationSystemGroup>().AddSystemToUpdateList(system);
                World.Update();
                EntitiesProfiler.Update();
                World.DestroySystem(system);
            }
            else
            {
                onUpdate(null);
                World.Update();
                EntitiesProfiler.Update();
            }

            Profiler.enabled = false;
            Profiler.enableAllocationCallstacks = false;
            Profiler.logFile = "";

            var loaded = ProfilerDriver.LoadProfile(s_RawDataFilePath, false);
            Assert.IsTrue(loaded);
            Assert.AreNotEqual(-1, ProfilerDriver.lastFrameIndex);

            return ProfilerDriver.GetRawFrameDataView(0, 0);
        }

        static WorldData GetWorldData(RawFrameDataView frame, StructuralChangeData structuralChangeData) =>
            GetSessionMetaData<WorldData>(frame, EntitiesProfiler.Guid, (int)DataTag.WorldData).First(x => x.SequenceNumber == structuralChangeData.WorldSequenceNumber);

        static SystemData GetSystemData(RawFrameDataView frame, StructuralChangeData structuralChangeData) =>
            GetSessionMetaData<SystemData>(frame, EntitiesProfiler.Guid, (int)DataTag.SystemData).FirstOrDefault(x => x.System == structuralChangeData.ExecutingSystem);

        static StructuralChangeData GetStructuralChangeData(RawFrameDataView frame, StructuralChangeType type) =>
            GetFrameMetaData<StructuralChangeData>(frame, StructuralChangesProfiler.Guid, 0).First(x => x.Type == type);

        static IEnumerable<T> GetSessionMetaData<T>(RawFrameDataView frame, Guid guid, int tag) where T : unmanaged
        {
            var metaDataCount = frame.GetSessionMetaDataCount(guid, tag);
            for (var metaDataIter = 0; metaDataIter < metaDataCount; ++metaDataIter)
            {
                var metaDataArray = frame.GetSessionMetaData<T>(guid, tag, metaDataIter);
                for (var i = 0; i < metaDataArray.Length; ++i)
                    yield return metaDataArray[i];
            }
        }

        static IEnumerable<T> GetFrameMetaData<T>(RawFrameDataView frame, Guid guid, int tag) where T : unmanaged
        {
            var metaDataCount = frame.GetFrameMetaDataCount(guid, tag);
            for (var metaDataIter = 0; metaDataIter < metaDataCount; ++metaDataIter)
            {
                var metaDataArray = frame.GetFrameMetaData<T>(guid, tag, metaDataIter);
                for (var i = 0; i < metaDataArray.Length; ++i)
                    yield return metaDataArray[i];
            }
        }

        [Test]
        public void Uninitialized_DoesNotThrow()
        {
            StructuralChangesProfiler.Shutdown();
            Assert.DoesNotThrow(() => StructuralChangesProfiler.BeginCreateEntity(default));
            Assert.DoesNotThrow(() => StructuralChangesProfiler.BeginDestroyEntity(default));
            Assert.DoesNotThrow(() => StructuralChangesProfiler.BeginAddComponent(default));
            Assert.DoesNotThrow(() => StructuralChangesProfiler.BeginRemoveComponent(default));
            Assert.DoesNotThrow(() => StructuralChangesProfiler.Flush());
        }

        [Test]
        public void CreateEntity()
        {
            using (var frame = GenerateFrameMetaData(system => m_Manager.CreateEntity()))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.CreateEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase()]
        [TestCase(typeof(EcsTestData))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void CreateEntity_WithArchetype(params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var frame = GenerateFrameMetaData(system => m_Manager.CreateEntity(archetype)))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.CreateEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase()]
        [TestCase(typeof(EcsTestData))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void CreateEntity_WithComponentTypes(params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            using (var frame = GenerateFrameMetaData(system => m_Manager.CreateEntity(componentTypes)))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.CreateEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(10)]
        [TestCase(20, typeof(EcsTestData))]
        [TestCase(30, typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(40, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void CreateEntity_WithArchetypeAndEntityCount(int entityCount, params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var frame = GenerateFrameMetaData(system => m_Manager.CreateEntity(archetype, entityCount)))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.CreateEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(10)]
        [TestCase(20, typeof(EcsTestData))]
        [TestCase(30, typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(40, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void CreateEntity_WithArchetypeAndEntityCountAndAllocator(int entityCount, params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var frame = GenerateFrameMetaData(system => { using var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp); }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.CreateEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(10)]
        [TestCase(20, typeof(EcsTestData))]
        [TestCase(30, typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(40, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void CreateEntity_WithArchetypeAndEntityArray(int entityCount, params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var entities = new NativeArray<Entity>(entityCount, Allocator.Temp))
            using (var frame = GenerateFrameMetaData(system => m_Manager.CreateEntity(archetype, entities)))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.CreateEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void CreateEntity_FromECB()
        {
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.CreateEntity();
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.CreateEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase()]
        [TestCase(typeof(EcsTestData))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void CreateEntity_FromECB_WithArchetype(params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.CreateEntity(archetype);
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.CreateEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void CreateEntity_WithoutSystem()
        {
            using (var frame = GenerateFrameMetaData(system => m_Manager.CreateEntity(), false))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.CreateEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(string.Empty));
            }
        }

        [Test]
        public void DestroyEntity()
        {
            var entity = m_Manager.CreateEntity();
            using (var frame = GenerateFrameMetaData(system => m_Manager.DestroyEntity(entity)))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.DestroyEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(10)]
        [TestCase(20, typeof(EcsTestData))]
        [TestCase(30, typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(40, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void DestroyEntity_WithEntityArray(int entityCount, params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp))
            using (var frame = GenerateFrameMetaData(system => m_Manager.DestroyEntity(entities)))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.DestroyEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(10)]
        [TestCase(20, typeof(EcsTestData))]
        [TestCase(30, typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(40, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void DestroyEntity_WithEntitySlice(int entityCount, params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp))
            using (var frame = GenerateFrameMetaData(system => m_Manager.DestroyEntity(entities.Slice(0, 1))))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.DestroyEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(10)]
        [TestCase(20, typeof(EcsTestData))]
        [TestCase(30, typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(40, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void DestroyEntity_WithQuery(int entityCount, params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            using (var entities = m_Manager.CreateEntity(m_Manager.CreateArchetype(componentTypes), entityCount, Allocator.Temp))
            using (var query = m_Manager.CreateEntityQuery(componentTypes))
            using (var frame = GenerateFrameMetaData(system => m_Manager.DestroyEntity(query)))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.DestroyEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void DestroyEntity_FromECB()
        {
            var entity = m_Manager.CreateEntity();
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.DestroyEntity(entity);
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.DestroyEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(10)]
        [TestCase(20, typeof(EcsTestData))]
        [TestCase(30, typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(40, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void DestroyEntity_FromECB_WithQuery(int entityCount, params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            using (var entities = m_Manager.CreateEntity(m_Manager.CreateArchetype(componentTypes), entityCount, Allocator.Temp))
            using (var query = m_Manager.CreateEntityQuery(componentTypes))
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.DestroyEntitiesForEntityQuery(query);
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.DestroyEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void DestroyEntity_WithoutSystem()
        {
            var entity = m_Manager.CreateEntity();
            using (var frame = GenerateFrameMetaData(system => m_Manager.DestroyEntity(entity), false))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.DestroyEntity);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(string.Empty));
            }
        }

        [Test]
        public void AddComponent()
        {
            var entity = m_Manager.CreateEntity();
            using (var frame = GenerateFrameMetaData(system => m_Manager.AddComponent(entity, typeof(EcsTestData))))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.AddComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void AddComponent_WithEntityArray()
        {
            using (var entities = m_Manager.CreateEntity(m_Manager.CreateArchetype(), 10, Allocator.Temp))
            using (var frame = GenerateFrameMetaData(system => m_Manager.AddComponent(entities, typeof(EcsTestData))))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.AddComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void AddComponent_WithQuery()
        {
            using (var entities = m_Manager.CreateEntity(m_Manager.CreateArchetype(), 10, Allocator.Temp))
            using (var frame = GenerateFrameMetaData(system => m_Manager.AddComponent(m_Manager.UniversalQuery, typeof(EcsTestData))))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.AddComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(typeof(EcsTestData))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void AddComponent_WithQueryAndComponentTypes(params Type[] types)
        {
            var componentTypes = new ComponentTypes(types.Select(t => new ComponentType(t)).ToArray());
            using (var entities = m_Manager.CreateEntity(m_Manager.CreateArchetype(), 10, Allocator.Temp))
            using (var frame = GenerateFrameMetaData(system => m_Manager.AddComponent(m_Manager.UniversalQuery, componentTypes)))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.AddComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void AddComponent_FromECB()
        {
            var entity = m_Manager.CreateEntity();
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.AddComponent(entity, typeof(EcsTestData));
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.AddComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(typeof(EcsTestData))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void AddComponent_FromECB_WithComponentTypes(params Type[] types)
        {
            var entity = m_Manager.CreateEntity();
            var componentTypes = new ComponentTypes(types.Select(t => new ComponentType(t)).ToArray());
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.AddComponent(entity, componentTypes);
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.AddComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void AddComponent_FromECB_WithQuery()
        {
            using (var entities = m_Manager.CreateEntity(m_Manager.CreateArchetype(), 10, Allocator.Temp))
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.AddComponentForEntityQuery(m_Manager.UniversalQuery, typeof(EcsTestData));
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.AddComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(typeof(EcsTestData))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void AddComponent_FromECB_WithQueryAndComponentTypes(params Type[] types)
        {
            var componentTypes = new ComponentTypes(types.Select(t => new ComponentType(t)).ToArray());
            using (var entities = m_Manager.CreateEntity(m_Manager.CreateArchetype(), 10, Allocator.Temp))
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.AddComponentForEntityQuery(m_Manager.UniversalQuery, componentTypes);
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.AddComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void AddComponent_WithoutSystem()
        {
            var entity = m_Manager.CreateEntity();
            using (var frame = GenerateFrameMetaData(system => m_Manager.AddComponent(entity, typeof(EcsTestData)), false))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.AddComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(string.Empty));
            }
        }

        [Test]
        public void RemoveComponent()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            using (var frame = GenerateFrameMetaData(system => m_Manager.RemoveComponent(entity, typeof(EcsTestData))))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.RemoveComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(typeof(EcsTestData))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void RemoveComponent_WithComponentTypes(params Type[] types)
        {
            var entity = m_Manager.CreateEntity(types.Select(t => new ComponentType(t)).ToArray());
            var componentTypes = new ComponentTypes(types.Select(t => new ComponentType(t)).ToArray());
            using (var frame = GenerateFrameMetaData(system => m_Manager.RemoveComponent(entity, componentTypes)))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.RemoveComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void RemoveComponent_WithQuery()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using (var entities = m_Manager.CreateEntity(archetype, 10, Allocator.Temp))
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var frame = GenerateFrameMetaData(system => m_Manager.RemoveComponent(query, typeof(EcsTestData))))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.RemoveComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(typeof(EcsTestData))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void RemoveComponent_WithQueryAndComponentTypes(params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var entities = m_Manager.CreateEntity(archetype, 10, Allocator.Temp))
            using (var query = m_Manager.CreateEntityQuery(componentTypes))
            using (var frame = GenerateFrameMetaData(system => m_Manager.RemoveComponent(query, new ComponentTypes(componentTypes))))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.RemoveComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void RemoveComponent_FromECB()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.RemoveComponent(entity, typeof(EcsTestData));
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.RemoveComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(typeof(EcsTestData))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void RemoveComponent_FromECB_WithComponentTypes(params Type[] types)
        {
            var entity = m_Manager.CreateEntity(types.Select(t => new ComponentType(t)).ToArray());
            var componentTypes = new ComponentTypes(types.Select(t => new ComponentType(t)).ToArray());
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.RemoveComponent(entity, componentTypes);
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.RemoveComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void RemoveComponent_FromECB_WithQuery()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using (var entities = m_Manager.CreateEntity(archetype, 10, Allocator.Temp))
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.RemoveComponentForEntityQuery(query, typeof(EcsTestData));
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.RemoveComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [TestCase(typeof(EcsTestData))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2))]
        [TestCase(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3))]
        public void RemoveComponent_FromECB_WithQueryAndComponentTypes(params Type[] types)
        {
            var componentTypes = types.Select(t => new ComponentType(t)).ToArray();
            var archetype = m_Manager.CreateArchetype(componentTypes);
            using (var entities = m_Manager.CreateEntity(archetype, 10, Allocator.Temp))
            using (var query = m_Manager.CreateEntityQuery(componentTypes))
            using (var frame = GenerateFrameMetaData(system =>
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                ecb.RemoveComponentForEntityQuery(query, new ComponentTypes(componentTypes));
                ecb.Playback(m_Manager);
                ecb.Dispose();
            }))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.RemoveComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(TypeManager.GetSystemName(typeof(TestSystem))));
            }
        }

        [Test]
        public void RemoveComponent_WithoutSystem()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            using (var frame = GenerateFrameMetaData(system => m_Manager.RemoveComponent(entity, typeof(EcsTestData)), false))
            {
                var structuralChangeData = GetStructuralChangeData(frame, StructuralChangeType.RemoveComponent);
                var worldData = GetWorldData(frame, structuralChangeData);
                var systemData = GetSystemData(frame, structuralChangeData);
                Assert.That(worldData.Name, Is.EqualTo(World.Name));
                Assert.That(systemData.Name, Is.EqualTo(string.Empty));
            }
        }
    }
}
#endif
