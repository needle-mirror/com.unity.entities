using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Tests;
using Unity.Mathematics;
using Unity.PerformanceTesting;

namespace Unity.Entities.PerformanceTests
{
    [TestFixture]
    [Category("Performance")]
    public sealed class SystemUpdatePerformanceTests : EntityPerformanceTestFixture
    {
        unsafe public partial class BenchMarkReferenceClassArrayLoop : SystemBase
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int IterationCount;
            public int LoopsPerSystem;
            public int EntityCount;

            class OtherThing
            {
                public float Value;
            }

            class MyThing
            {
                public float3 Value;

                public OtherThing Reference;

            }
            private MyThing[] MyArray;

            void UpdateThing()
            {
                for (int l = 0; l != LoopsPerSystem; l++)
                {
                    int len = MyArray.Length;
                    for (int i = 0; i != len; i++)
                    {
                        MyThing thing = MyArray[i];
                        thing.Value += thing.Reference.Value + 1;
                    }
                }
            }

            protected override void OnUpdate()
            {
                for (int i = 0; i != IterationCount; i++)
                    UpdateThing();
            }

            protected override void OnStartRunning()
            {
                base.OnStartRunning();

                MyArray = new MyThing[EntityCount];
                for (int i = 0; i != MyArray.Length; i++)
                {
                    MyArray[i] = new MyThing();
                    MyArray[i].Reference = new OtherThing();
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        unsafe public partial class BenchmarkReferenceBurstArrayLoop : SystemBase
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int IterationCount;
            public int LoopsPerSystem;
            public int EntityCount;

            struct Data
            {
                [NoAlias]
                public float3* array;
                [NoAlias]
                public float*  array2;
                public int Count;
            }

            [BurstCompile(CompileSynchronously = true)]
            public static void RunBenchmark(int loopsPerSystem, void* ptr)
            {
                for (int l = 0; l != loopsPerSystem; l++)
                {
                    Data* data = (Data*)ptr;
                    int count = data->Count;
                    for (int i = 0; i != count; i++)
                    {
                        data->array[i] += data->array2[i] + 1;
                    }
                }
            }

            delegate void BenchDelegate(int loopsPerSystem, void* ptr);
            private Data _data;
            private FunctionPointer<BenchDelegate> _BenchFunc;

            protected override void OnStartRunning()
            {
                base.OnCreate();

                _data.Count = EntityCount;
                _data.array = (float3*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<float3>() * _data.Count, 16, Allocator.Persistent);
                _data.array2 = (float*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<float>() * _data.Count, 16, Allocator.Persistent);
                _BenchFunc = BurstCompiler.CompileFunctionPointer<BenchDelegate>(RunBenchmark);
            }

            protected override void OnStopRunning()
            {
                base.OnStopRunning();

                UnsafeUtility.Free(_data.array, Allocator.Persistent);
                UnsafeUtility.Free(_data.array2, Allocator.Persistent);
            }

            protected override void OnUpdate()
            {
                var delegateFunc = _BenchFunc.Invoke;
                fixed (Data* data = &_data)
                {
                    for (int i = 0; i != IterationCount; i++)
                        delegateFunc(LoopsPerSystem, data);
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        unsafe struct MySystemBenchmarkJob : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestFloatData3> RotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<EcsTestFloatData> RotationSpeedTypeHandle;
            public float Singleton;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var chunkRotations = (float3*)batchInChunk.GetComponentDataPtrRW(ref RotationTypeHandle);
                var chunkRotationSpeeds = (float*)batchInChunk.GetComponentDataPtrRO(ref RotationSpeedTypeHandle);
                int count = batchInChunk.Count;
                for (var i = 0; i < count; i++)
                {
                    chunkRotations[i] += chunkRotationSpeeds[i] + Singleton;
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public partial struct MyStructSystem : ISystem
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int LoopsPerSystem;

            private EntityQuery _Query;

            public void OnDestroy(ref SystemState state)
            { }

            ComponentTypeHandle<EcsTestFloatData3> _RotationTypeHandle;
            ComponentTypeHandle<EcsTestFloatData> _RotationSpeedTypeHandle;
            public void OnCreate(ref SystemState state)
            {
                _Query = state.GetEntityQuery(ComponentType.ReadWrite<EcsTestFloatData3>(),
                    ComponentType.ReadOnly<EcsTestFloatData>());
                _RotationTypeHandle = state.GetComponentTypeHandle<EcsTestFloatData3>();
                _RotationSpeedTypeHandle = state.GetComponentTypeHandle<EcsTestFloatData>();
            }

            [BurstDiscard]
            static void CheckRunningBurst()
            {
                throw new ArgumentException("Not running burst");
            }

            [BurstCompile(CompileSynchronously = true)]
            public void OnUpdate(ref SystemState state)
            {
                CheckRunningBurst();

                for (int i = 0; i != LoopsPerSystem; i++)
                {
                    _RotationTypeHandle.Update(ref state);
                    _RotationSpeedTypeHandle.Update(ref state);
                    var job = new MySystemBenchmarkJob
                    {
                        RotationTypeHandle = _RotationTypeHandle,
                        RotationSpeedTypeHandle = _RotationSpeedTypeHandle,
                    };
                    JobEntityBatchExtensions.RunWithoutJobs(ref job, _Query);
                }
            }
        }

        struct SingletonData : IComponentData
        {
            public float Value;
        }

        [BurstCompile(CompileSynchronously = true)]
        public partial struct MyStructSystemWithSingleton : ISystem
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int LoopsPerSystem;

            private EntityQuery _Query;
            private EntityQuery _SingletonData;

            public void OnDestroy(ref SystemState state)
            { }

            ComponentTypeHandle<EcsTestFloatData3> _RotationTypeHandle;
            ComponentTypeHandle<EcsTestFloatData> _RotationSpeedTypeHandle;
            public void OnCreate(ref SystemState state)
            {
                _Query = state.GetEntityQuery(ComponentType.ReadWrite<EcsTestFloatData3>(),
                    ComponentType.ReadOnly<EcsTestFloatData>());
                _SingletonData = state.GetEntityQuery(typeof(SingletonData));
                _RotationTypeHandle = state.GetComponentTypeHandle<EcsTestFloatData3>();
                _RotationSpeedTypeHandle = state.GetComponentTypeHandle<EcsTestFloatData>();
            }

            [BurstDiscard]
            static void CheckRunningBurst()
            {
                throw new ArgumentException("Not running burst");
            }

            [BurstCompile(CompileSynchronously = true)]
            public void OnUpdate(ref SystemState state)
            {
                CheckRunningBurst();

                for (int i = 0; i != LoopsPerSystem; i++)
                {
                    _RotationTypeHandle.Update(ref state);
                    _RotationSpeedTypeHandle.Update(ref state);
                    var singletonData = _SingletonData.GetSingleton<SingletonData>();

                    var job = new MySystemBenchmarkJob
                    {
                        RotationTypeHandle = _RotationTypeHandle,
                        RotationSpeedTypeHandle = _RotationSpeedTypeHandle,
                        Singleton = singletonData.Value
                    };
                    JobEntityBatchExtensions.RunWithoutJobs(ref job, _Query);
                }
            }
        }


        [BurstCompile(CompileSynchronously = true)]
        public partial struct MyStructRunSystem : ISystem
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int LoopsPerSystem;

            private EntityQuery _Query;

            public void OnDestroy(ref SystemState state)
            { }

            ComponentTypeHandle<EcsTestFloatData3> _RotationTypeHandle;
            ComponentTypeHandle<EcsTestFloatData> _RotationSpeedTypeHandle;
            public void OnCreate(ref SystemState state)
            {
                _Query = state.GetEntityQuery(ComponentType.ReadWrite<EcsTestFloatData3>(),
                    ComponentType.ReadOnly<EcsTestFloatData>());
                _RotationTypeHandle = state.GetComponentTypeHandle<EcsTestFloatData3>();
                _RotationSpeedTypeHandle = state.GetComponentTypeHandle<EcsTestFloatData>();
            }

            [BurstDiscard]
            static void CheckRunningBurst()
            {
                throw new ArgumentException("Not running burst");
            }

            [BurstCompile(CompileSynchronously = true)]
            public void OnUpdate(ref SystemState state)
            {
                CheckRunningBurst();

                for (int i = 0; i != LoopsPerSystem; i++)
                {
                    _RotationTypeHandle.Update(ref state);
                    _RotationSpeedTypeHandle.Update(ref state);
                    var job = new MySystemBenchmarkJob
                    {
                        RotationTypeHandle = _RotationTypeHandle,
                        RotationSpeedTypeHandle = _RotationSpeedTypeHandle,
                    };
                    job.RunByRef(_Query);
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public partial struct MyStructScheduleSystem : ISystem
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int LoopsPerSystem;

            private EntityQuery _Query;

            public void OnDestroy(ref SystemState state)
            { }

            ComponentTypeHandle<EcsTestFloatData3> _RotationTypeHandle;
            ComponentTypeHandle<EcsTestFloatData> _RotationSpeedTypeHandle;
            public void OnCreate(ref SystemState state)
            {
                _Query = state.GetEntityQuery(ComponentType.ReadWrite<EcsTestFloatData3>(),
                    ComponentType.ReadOnly<EcsTestFloatData>());
                _RotationTypeHandle = state.GetComponentTypeHandle<EcsTestFloatData3>();
                _RotationSpeedTypeHandle = state.GetComponentTypeHandle<EcsTestFloatData>();
            }

            [BurstDiscard]
            static void CheckRunningBurst()
            {
                throw new ArgumentException("Not running burst");
            }

            [BurstCompile(CompileSynchronously = true)]
            public void OnUpdate(ref SystemState state)
            {
                CheckRunningBurst();

                for (int i = 0; i != LoopsPerSystem; i++)
                {
                    _RotationTypeHandle.Update(ref state);
                    _RotationSpeedTypeHandle.Update(ref state);
                    var job = new MySystemBenchmarkJob
                    {
                        RotationTypeHandle = _RotationTypeHandle,
                        RotationSpeedTypeHandle = _RotationSpeedTypeHandle,
                    };
                    state.Dependency = job.ScheduleByRef(_Query, state.Dependency);
                }
            }
        }

        public partial class MyClassSystem : SystemBase
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int LoopsPerSystem;

            private EntityQuery _Query;

            ComponentTypeHandle<EcsTestFloatData3> _RotationTypeHandle;
            ComponentTypeHandle<EcsTestFloatData> _RotationSpeedTypeHandle;

            protected override void OnCreate()
            {
                _Query = GetEntityQuery(ComponentType.ReadWrite<EcsTestFloatData3>(),
                    ComponentType.ReadOnly<EcsTestFloatData>());
                _RotationTypeHandle = GetComponentTypeHandle<EcsTestFloatData3>();
                _RotationSpeedTypeHandle = GetComponentTypeHandle<EcsTestFloatData>();
            }

            protected override void OnUpdate()
            {
                for (int i = 0; i != LoopsPerSystem; i++)
                {
                    _RotationTypeHandle.Update(this);
                    _RotationSpeedTypeHandle.Update(this);
                    var job = new MySystemBenchmarkJob
                    {
                        RotationTypeHandle = _RotationTypeHandle,
                        RotationSpeedTypeHandle = _RotationSpeedTypeHandle,
                    };

                    JobEntityBatchExtensions.RunWithoutJobs(ref job, _Query);
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        unsafe public partial class MyClassSystemWithBurstForEach : SystemBase
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int LoopsPerSystem;

            EntityQuery _Query;
            ComponentTypeHandle<EcsTestFloatData3> _RotationTypeHandle;
            ComponentTypeHandle<EcsTestFloatData> _RotationSpeedTypeHandle;

            [BurstCompile(CompileSynchronously = true)]
            static void RunBursted(void* job, EntityQuery* query)
            {
                JobEntityBatchExtensions.RunWithoutJobs(ref *(MySystemBenchmarkJob*)job, *query);
            }

            unsafe delegate void RunBurstedCallback(void* job, EntityQuery* query);


            static private RunBurstedCallback _Callback;
            protected override void OnCreate()
            {
                _Query = GetEntityQuery(ComponentType.ReadWrite<EcsTestFloatData3>(),
                    ComponentType.ReadOnly<EcsTestFloatData>());
                _RotationTypeHandle = GetComponentTypeHandle<EcsTestFloatData3>();
                _RotationSpeedTypeHandle = GetComponentTypeHandle<EcsTestFloatData>();

                if (_Callback == null)
                    _Callback = BurstCompiler.CompileFunctionPointer<RunBurstedCallback>(RunBursted).Invoke;
            }

            protected override void OnUpdate()
            {
                for (int i = 0; i != LoopsPerSystem; i++)
                {
                    _RotationTypeHandle.Update(this);
                    _RotationSpeedTypeHandle.Update(this);
                    var job = new MySystemBenchmarkJob
                    {
                        RotationTypeHandle = _RotationTypeHandle,
                        RotationSpeedTypeHandle = _RotationSpeedTypeHandle,
                    };

                    _Callback(UnsafeUtility.AddressOf(ref job), (EntityQuery*)UnsafeUtility.AddressOf(ref _Query));
                }
            }
        }

        public partial class BenchmarkStructSystemGroup : ComponentSystemGroup
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int IterationCount;
            public int LoopsPerSystem;

            protected override void OnStartRunning()
            {
                base.OnStartRunning();

                for (int i = 0; i != IterationCount; i++)
                {
                    var res = World.AddSystem<MyStructSystem>();
                    res.Struct.LoopsPerSystem = LoopsPerSystem;
                    AddUnmanagedSystemToUpdateList(res);
                }
            }
        }

        public partial class BenchmarkStructSystemWithSingletonGroup : ComponentSystemGroup
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int IterationCount;
            public int LoopsPerSystem;

            protected override void OnStartRunning()
            {
                base.OnStartRunning();

                for (int i = 0; i != IterationCount; i++)
                {
                    var res = World.AddSystem<MyStructSystemWithSingleton>();
                    res.Struct.LoopsPerSystem = LoopsPerSystem;
                    AddUnmanagedSystemToUpdateList(res);
                }
            }
        }

        public partial class BenchmarkSystemBaseGroup : ComponentSystemGroup
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int IterationCount;
            public int LoopsPerSystem;

            protected override void OnStartRunning()
            {
                base.OnStartRunning();

                for (int i = 0; i != IterationCount; i++)
                {
                    var sys = World.CreateSystem<MyClassSystem>();
                    sys.LoopsPerSystem = LoopsPerSystem;
                    AddSystemToUpdateList(sys);
                }
            }
        }

        public partial class BenchmarkSystemBaseWithBurstForEachGroup : ComponentSystemGroup
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int IterationCount;
            public int LoopsPerSystem;

            protected override void OnStartRunning()
            {
                base.OnStartRunning();

                for (int i = 0; i != IterationCount; i++)
                {
                    var sys = World.CreateSystem<MyClassSystemWithBurstForEach>();
                    sys.LoopsPerSystem = LoopsPerSystem;
                    AddSystemToUpdateList(sys);
                }
            }
        }

        public partial class BenchmarkRunStructSystemGroup : ComponentSystemGroup
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int IterationCount;
            public int LoopsPerSystem;

            protected override void OnStartRunning()
            {
                base.OnStartRunning();

                for (int i = 0; i != IterationCount; i++)
                {
                    var res = World.AddSystem<MyStructRunSystem>();
                    res.Struct.LoopsPerSystem = LoopsPerSystem;
                    AddUnmanagedSystemToUpdateList(res);
                }
            }

            protected override void OnUpdate()
            {
                base.OnUpdate();
                EntityManager.CompleteAllJobs();
            }
        }

        public partial class BenchmarkScheduleStructSystemGroup : ComponentSystemGroup
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int IterationCount;
            public int LoopsPerSystem;

            protected override void OnStartRunning()
            {
                base.OnStartRunning();

                for (int i = 0; i != IterationCount; i++)
                {
                    var res = World.AddSystem<MyStructScheduleSystem>();
                    res.Struct.LoopsPerSystem = LoopsPerSystem;
                    AddUnmanagedSystemToUpdateList(res);
                }
            }

            protected override void OnUpdate()
            {
                base.OnUpdate();
                EntityManager.CompleteAllJobs();
            }
        }

        public enum EnabledBitsMode
        {
            NoEnableableComponents,
            NoComponentsDisabled,
            FewComponentsDisabled,
            ManyComponentsDisabled,
        }

        void CreateTestEntities(int entityCount, EnabledBitsMode enabledBitsMode)
        {
            // Create the entities that will match the test queries
            var types = new List<ComponentType>
            {
                typeof(EcsTestFloatData3), typeof(EcsTestFloatData),
                typeof(EcsTestData), typeof(EcsTestData2), typeof(SceneTag)
            };
            if (enabledBitsMode != EnabledBitsMode.NoEnableableComponents)
                types.Add(typeof(EcsTestDataEnableable));
            var archPos = m_Manager.CreateArchetype(types.ToArray());
            var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, m_World.UpdateAllocator.ToAllocator);
            m_Manager.CreateEntity(archPos, entities);
            if (enabledBitsMode == EnabledBitsMode.FewComponentsDisabled)
            {
                for (int i = 0; i < entityCount; i += archPos.ChunkCapacity)
                {
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
                }
            }
            else if (enabledBitsMode == EnabledBitsMode.ManyComponentsDisabled)
            {
                for (int i = 0; i < entityCount; i += 2)
                {
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[i], false);
                }
            }

            // Create a bunch of entities in a different archetype that won't match the test queries
            var archNeg = m_Manager.CreateArchetype(typeof(EcsTestData4), typeof(EcsTestData3));
            m_Manager.CreateEntity(archNeg, 100000);

            // Create the singleton used to conditionally execute certain test systems
            m_Manager.CreateEntity(typeof(SingletonData));
        }

        [TestCase(250,10,1)]
        [TestCase(500,5,1)]
        [TestCase(500,5,10)]
        [TestCase(500,5,100)]
        [TestCase(500,5,1000)]
        [Performance]
        public void SystemUpdatePerformance_ReferenceArrayLoop(int iterationCount, int loopsPerSystem, int entityCount)
        {
            var sys = m_World.CreateSystem<BenchMarkReferenceClassArrayLoop>();
            sys.IterationCount = iterationCount;
            sys.LoopsPerSystem = loopsPerSystem;
            sys.EntityCount = entityCount;
            Measure.Method(
                    () =>
                    {
                        sys.Update();
                    })
                .WarmupCount(1)
                .MeasurementCount(100)
                .Run();
        }

        [TestCase(250,10,1)]
        [TestCase(500,5,1)]
        [TestCase(500,5,10)]
        [TestCase(500,5,100)]
        [TestCase(500,5,1000)]
        [Performance]
        public void SystemUpdatePerformance_ReferenceArrayLoop_WithBurst(int iterationCount, int loopsPerSystem, int entityCount)
        {
            var sys = m_World.CreateSystem<BenchmarkReferenceBurstArrayLoop>();
            sys.IterationCount = iterationCount;
            sys.LoopsPerSystem = loopsPerSystem;
            sys.EntityCount = entityCount;
            Measure.Method(
                    () =>
                    {
                        sys.Update();
                    })
                .WarmupCount(1)
                .MeasurementCount(100)
                .Run();
        }

        [TestCase(250,10,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(250,10,1, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,10, EnabledBitsMode.ManyComponentsDisabled)]
        [TestCase(500,5,100, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.FewComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.ManyComponentsDisabled)]
        [Performance]
        public void SystemUpdatePerformance_StructSystem_RunWithoutJobs(int iterationCount, int loopsPerSystem, int entityCount, EnabledBitsMode enabledBitsMode)
        {
            CreateTestEntities(entityCount, enabledBitsMode);
            var group = m_World.CreateSystem<BenchmarkStructSystemGroup>();
            group.IterationCount = iterationCount;
            group.LoopsPerSystem = loopsPerSystem;
            Measure.Method(
                    () =>
                    {
                        group.Update();
                    })
                .WarmupCount(1)
                .MeasurementCount(100)
                .Run();
        }

        [TestCase(250,10,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(250,10,1, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,10, EnabledBitsMode.ManyComponentsDisabled)]
        [TestCase(500,5,100, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.FewComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.ManyComponentsDisabled)]
        [Performance]
        public void SystemUpdatePerformance_StructSystem_RunWithoutJobs_WithSingleton(int iterationCount, int loopsPerSystem, int entityCount, EnabledBitsMode enabledBitsMode)
        {
            CreateTestEntities(entityCount, enabledBitsMode);
            var group = m_World.CreateSystem<BenchmarkStructSystemWithSingletonGroup>();
            group.IterationCount = iterationCount;
            group.LoopsPerSystem = loopsPerSystem;
            Measure.Method(
                    () =>
                    {
                        group.Update();
                    })
                .WarmupCount(1)
                .MeasurementCount(100)
                .Run();
        }

        [TestCase(250,10,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(250,10,1, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,10, EnabledBitsMode.ManyComponentsDisabled)]
        [TestCase(500,5,100, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.FewComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.ManyComponentsDisabled)]
        [Performance]
        public void SystemUpdatePerformance_SystemBase(int iterationCount, int loopsPerSystem, int entityCount, EnabledBitsMode enabledBitsMode)
        {
            CreateTestEntities(entityCount, enabledBitsMode);
            var group = m_World.CreateSystem<BenchmarkSystemBaseGroup>();
            group.IterationCount = iterationCount;
            group.LoopsPerSystem = loopsPerSystem;
            Measure.Method(
                    () =>
                    {
                        group.Update();
                    })
                .WarmupCount(1)
                .MeasurementCount(100)
                .Run();
        }

        [TestCase(250,10,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(250,10,1, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,10, EnabledBitsMode.ManyComponentsDisabled)]
        [TestCase(500,5,100, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.FewComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.ManyComponentsDisabled)]
        [Performance]
        public void SystemUpdatePerformance_SystemBase_WithBurstForEach(int iterationCount, int loopsPerSystem, int entityCount, EnabledBitsMode enabledBitsMode)
        {
            CreateTestEntities(entityCount, enabledBitsMode);
            var group = m_World.CreateSystem<BenchmarkSystemBaseWithBurstForEachGroup>();
            group.IterationCount = iterationCount;
            group.LoopsPerSystem = loopsPerSystem;
            Measure.Method(
                    () =>
                    {
                        group.Update();
                    })
                .WarmupCount(1)
                .MeasurementCount(100)
                .Run();
        }

        [TestCase(250,10,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(250,10,1, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,10, EnabledBitsMode.ManyComponentsDisabled)]
        [TestCase(500,5,100, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.FewComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.ManyComponentsDisabled)]
        [Performance]
        public void SystemUpdatePerformance_StructSystem_Run(int iterationCount, int loopsPerSystem, int entityCount, EnabledBitsMode enabledBitsMode)
        {
            CreateTestEntities(entityCount, enabledBitsMode);
            var group = m_World.CreateSystem<BenchmarkRunStructSystemGroup>();
            group.IterationCount = iterationCount;
            group.LoopsPerSystem = loopsPerSystem;
            Measure.Method(
                    () =>
                    {
                        group.Update();
                    })
                .WarmupCount(1)
                .MeasurementCount(100)
                .Run();
        }

        [TestCase(250,10,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(250,10,1, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,10, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,10, EnabledBitsMode.ManyComponentsDisabled)]
        [TestCase(500,5,100, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoEnableableComponents)]
        [TestCase(500,5,1000, EnabledBitsMode.NoComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.FewComponentsDisabled)]
        [TestCase(500,5,1000, EnabledBitsMode.ManyComponentsDisabled)]
        [Performance]
        public void SystemUpdatePerformance_StructSystem_Schedule(int iterationCount, int loopsPerSystem, int entityCount, EnabledBitsMode enabledBitsMode)
        {
            CreateTestEntities(entityCount, enabledBitsMode);
            var group = m_World.CreateSystem<BenchmarkScheduleStructSystemGroup>();
            group.IterationCount = iterationCount;
            group.LoopsPerSystem = loopsPerSystem;
            Measure.Method(
                    () =>
                    {
                        group.Update();
                    })
                .WarmupCount(1)
                .MeasurementCount(100)
                .Run();
        }
    }
}
