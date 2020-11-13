using NUnit.Framework;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Transforms;

#if !UNITY_DOTSRUNTIME
using System.Text.RegularExpressions;
#endif

// This file contains two user examples of generic jobs that we are trying to keep supporting with generic job type detection.
// Please leave them here.

namespace Unity.Entities.Tests.CustomerProvided.Forum1
{
    public struct Foo
    {
        public int num;
    }

    public struct Bar : IBufferElementData
    {
        public int num;
    }

    public struct Baz : IComponentData
    {
        public int num;
    }

    public interface IJobCustom<T> where T : struct, IBufferElementData
    {
        void Execute(Entity entity, ref T t0, ref Baz baz, NativeArray<Foo> foos);
    }

    public static class TestUtility<TJob, T0> where TJob : struct, IJobCustom<T0> where T0 : struct, IBufferElementData
    {
        [BurstCompile]
        public struct WrapperJob : IJobChunk
        {
            // wrapped job ends up here
            public TJob wrappedJob;

            [ReadOnly]
            [DeallocateOnJobCompletion]
            public NativeArray<Foo> foos;

            [ReadOnly]
            public EntityTypeHandle entityType;
            public ComponentTypeHandle<Baz> bazType;
            public BufferTypeHandle<T0> t0Type;
            public bool isReadOnly_T0;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var entities = chunk.GetNativeArray(entityType);
                var bazArray = chunk.GetNativeArray(bazType);
                var t0Buffers = chunk.GetBufferAccessor(t0Type);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = entities[i];
                    var baz = bazArray[i];
                    var t0Buffer = t0Buffers[i];

                    for (int iT0 = 0; iT0 < t0Buffer.Length; iT0++)
                    {
                        var t0 = t0Buffer[iT0];

                        // wrapped job Execute method is called
                        wrappedJob.Execute(entity, ref t0, ref baz, foos);

                        bazArray[i] = baz;

                        if (!isReadOnly_T0)
                        {
                            t0Buffer[iT0] = t0;
                        }
                    }
                }
            }
        }

        public static JobHandle Schedule(CustomSystemBase system, JobHandle dependentOn, TJob jobData)
        {
            EntityQuery query = GetExistingEntityQuery(system);

            if (query == default)
            {
                ComponentType[] componentTypes;
                if (!componentTypesByJobType.TryGetValue(typeof(TJob), out componentTypes))
                {
                    componentTypes = GetIJobCustomComponentTypes();
                }

                query = system.GetEntityQueryPublic(componentTypes);

                system.RequireForUpdate(query);

                if (query.CalculateChunkCount() == 0)
                {
                    return dependentOn;
                }
            }

            WrapperJob wrapperJob = new WrapperJob
            {
                wrappedJob = jobData,
                // foos = new NativeArray<Foo>(10, Allocator.TempJob), -- this will just leak and cause test failures. we're just testing that the Schedule() will not work in the first place
                entityType = system.EntityManager.GetEntityTypeHandle(),
                bazType = system.EntityManager.GetComponentTypeHandle<Baz>(false),
                t0Type = system.EntityManager.GetBufferTypeHandle<T0>(false),
                isReadOnly_T0 = false
            };

            return wrapperJob.ScheduleParallel(query, dependentOn);
        }

        private static Dictionary<Type, ComponentType[]> componentTypesByJobType = new Dictionary<Type, ComponentType[]>();

        private static EntityQuery GetExistingEntityQuery(ComponentSystemBase system)
        {
            ComponentType[] componentTypes;
            if (!componentTypesByJobType.TryGetValue(typeof(TJob), out componentTypes))
            {
                return default;
            }

            for (var i = 0; i != system.EntityQueries.Length; i++)
            {
                if (system.EntityQueries[i].CompareComponents(componentTypes))
                    return system.EntityQueries[i];
            }

            return default;
        }

        private static ComponentType[] GetIJobCustomComponentTypes()
        {
            // Simplified for testing purposes. The real version uses reflection to get this data, and caches it for later.
            return new ComponentType[]
                {
                ComponentType.ReadWrite<T0>(),
                ComponentType.ReadWrite<Baz>()
                };
        }
    }

    public abstract class CustomSystemBase : SystemBase
    {
        public EntityQuery GetEntityQueryPublic(ComponentType[] componentTypes)
        {
            return GetEntityQuery(componentTypes);
        }
    }
    public class MySystem : CustomSystemBase
    {
        protected override void OnCreate()
        {
            // Creating a single entity for testing purposes
            Entity entity = EntityManager.CreateEntity(ComponentType.ReadWrite<Bar>(), ComponentType.ReadWrite<Baz>());

            DynamicBuffer<Bar> barBuffer = EntityManager.GetBuffer<Bar>(entity);
            barBuffer.Add(new Bar { num = 0 });
        }

        protected override void OnUpdate()
        {
            Dependency = TestUtility<WrappedCustomJob, Bar>.Schedule(this, Dependency, new WrappedCustomJob
            {
                toAdd = 10
            });
        }

        public struct WrappedCustomJob : IJobCustom<Bar>
        {
            public int toAdd;

            public void Execute(Entity entity, ref Bar t0, ref Baz baz, NativeArray<Foo> foos)
            {
                // do custom logic here
                t0.num += toAdd;
            }
        }
    }

#if !NET_DOTS && UNITY_2020_2_OR_NEWER // Regex is not supported in NET_DOTS
    public class UserGenericJobCode1 : ECSTestsFixture
    {
        [Test]
        public void NoReflectionDataForHiddenGenerics()
        {
            // This should throw, as we can't pre-make the reflection data for this type of generic job
            LogAssert.Expect(LogType.Exception, new Regex("Job reflection data"));
            var repro = World.GetOrCreateSystem<MySystem>();
            var simulationGroup = World.GetOrCreateSystem<SimulationSystemGroup>();
            simulationGroup.AddSystemToUpdateList(repro);
            World.Update();
        }
    }
#endif
}

namespace Unity.Entities.Tests.CustomerProvided.Forum2
{
    public struct Foo : IComponentData
    {
        public int value;
    }

    public static class TestUtility<T> where T : struct, IComponentData
    {
        [BurstCompile]
        public struct ProcessChunks : IJobChunk
        {
            public ComponentTypeHandle<T> typeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
            {
                for (int i = 0; i < 10000; i++)
                {
                    var testDataArray = chunk.GetNativeArray(typeHandle);
                    testDataArray[0] = new T();
                }
            }
        }
    }

    public class MySystem : SystemBase
    {
        EntityQuery query;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadWrite<Foo>());

            // for testing purposes. Create an entity with a Foo component
            EntityManager.CreateEntity(ComponentType.ReadWrite<Foo>());
        }

        protected override void OnUpdate()
        {
            Dependency = new TestUtility<Foo>.ProcessChunks
            {
                typeHandle = GetComponentTypeHandle<Foo>()
            }.ScheduleParallel(query, Dependency);
        }
    }

    public class UserGenericJobCode2 : ECSTestsFixture
    {
        [Test, DotsRuntimeFixme]
        public void DoesntCrashEditor()
        {
            var repro = World.GetOrCreateSystem<MySystem>();
            var simulationGroup = World.GetOrCreateSystem<SimulationSystemGroup>();
            simulationGroup.AddSystemToUpdateList(repro);
            World.Update();
        }
    }
}


namespace Unity.Entities.Tests.CustomerProvided.Forum1_Tweaked
{
    public struct Foo
    {
        public int num;
    }

    public struct Bar : IBufferElementData
    {
        public int num;
    }

    public struct Baz : IComponentData
    {
        public int num;
    }

    public interface IJobCustom<T> where T : struct, IBufferElementData
    {
        void Execute(Entity entity, ref T t0, ref Baz baz, NativeArray<Foo> foos);
    }

    public static class TestUtility<TJob, T0> where TJob : struct, IJobCustom<T0> where T0 : struct, IBufferElementData
    {
        public struct BlahBlah<Q> where Q : unmanaged
        {
            public WrapperJob Thing;

            [BurstCompile]
            public struct WrapperJob : IJobChunk
            {
                // wrapped job ends up here
                public TJob wrappedJob;

                [ReadOnly]
                [DeallocateOnJobCompletion]
                public NativeArray<Foo> foos;

                [ReadOnly]
                public EntityTypeHandle entityType;
                public ComponentTypeHandle<Baz> bazType;
                public BufferTypeHandle<T0> t0Type;
                public bool isReadOnly_T0;
                public Q stuff;

                public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
                {
                    var entities = chunk.GetNativeArray(entityType);
                    var bazArray = chunk.GetNativeArray(bazType);
                    var t0Buffers = chunk.GetBufferAccessor(t0Type);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var entity = entities[i];
                        var baz = bazArray[i];
                        var t0Buffer = t0Buffers[i];

                        for (int iT0 = 0; iT0 < t0Buffer.Length; iT0++)
                        {
                            var t0 = t0Buffer[iT0];

                            // wrapped job Execute method is called
                            wrappedJob.Execute(entity, ref t0, ref baz, foos);

                            bazArray[i] = baz;

                            if (!isReadOnly_T0)
                            {
                                t0Buffer[iT0] = t0;
                            }
                        }
                    }
                }
            }
        }

        public static bool MakeJob<Q>(CustomSystemBase system, TJob jobData, out BlahBlah<Q> job, out EntityQuery query) where Q: unmanaged
        {
            query = GetExistingEntityQuery(system);
            job = default;

            if (query == default)
            {
                ComponentType[] componentTypes;
                if (!componentTypesByJobType.TryGetValue(typeof(TJob), out componentTypes))
                {
                    componentTypes = GetIJobCustomComponentTypes();
                }

                query = system.GetEntityQueryPublic(componentTypes);

                system.RequireForUpdate(query);

                if (query.CalculateChunkCount() == 0)
                {
                    return false;
                }
            }

            job = new BlahBlah<Q>
            {
                Thing = new BlahBlah<Q>.WrapperJob
                {
                    wrappedJob = jobData,
                    foos = new NativeArray<Foo>(10, Allocator.TempJob),
                    entityType = system.EntityManager.GetEntityTypeHandle(),
                    bazType = system.EntityManager.GetComponentTypeHandle<Baz>(false),
                    t0Type = system.EntityManager.GetBufferTypeHandle<T0>(false),
                    isReadOnly_T0 = false
                }
            };

            return true;
        }

        private static Dictionary<Type, ComponentType[]> componentTypesByJobType = new Dictionary<Type, ComponentType[]>();

        private static EntityQuery GetExistingEntityQuery(ComponentSystemBase system)
        {
            ComponentType[] componentTypes;
            if (!componentTypesByJobType.TryGetValue(typeof(TJob), out componentTypes))
            {
                return default;
            }

            for (var i = 0; i != system.EntityQueries.Length; i++)
            {
                if (system.EntityQueries[i].CompareComponents(componentTypes))
                    return system.EntityQueries[i];
            }

            return default;
        }

        private static ComponentType[] GetIJobCustomComponentTypes()
        {
            // Simplified for testing purposes. The real version uses reflection to get this data, and caches it for later.
            return new ComponentType[]
                {
                ComponentType.ReadWrite<T0>(),
                ComponentType.ReadWrite<Baz>()
                };
        }
    }

    public abstract class CustomSystemBase : SystemBase
    {
        public EntityQuery GetEntityQueryPublic(ComponentType[] componentTypes)
        {
            return GetEntityQuery(componentTypes);
        }
    }
    public class MySystem : CustomSystemBase
    {
        protected override void OnCreate()
        {
            // Creating a single entity for testing purposes
            Entity entity = EntityManager.CreateEntity(ComponentType.ReadWrite<Bar>(), ComponentType.ReadWrite<Baz>());

            DynamicBuffer<Bar> barBuffer = EntityManager.GetBuffer<Bar>(entity);
            barBuffer.Add(new Bar { num = 0 });
        }

        protected override void OnUpdate()
        {
            if (TestUtility<WrappedCustomJob, Bar>.MakeJob<float>(this, new WrappedCustomJob { toAdd = 10 }, out var resultJob, out var query))
            {
                Dependency = resultJob.Thing.ScheduleParallel(query, Dependency);
            }
        }

        public struct WrappedCustomJob : IJobCustom<Bar>
        {
            public int toAdd;

            public void Execute(Entity entity, ref Bar t0, ref Baz baz, NativeArray<Foo> foos)
            {
                // do custom logic here
                t0.num += toAdd;
            }
        }
    }

    public class UserGenericJobCode3 : ECSTestsFixture
    {
        [Test]
        public void ReflectionDataForVisibleGenerics()
        {
            var repro = World.GetOrCreateSystem<MySystem>();
            var simulationGroup = World.GetOrCreateSystem<SimulationSystemGroup>();
            simulationGroup.AddSystemToUpdateList(repro);
            World.Update();
        }
    }
}

namespace Unity.Entities.Tests.CustomerProvided.Forum3
{
    public abstract partial class CustomSystem<TComponent> : SystemBase where TComponent : struct, IComponentData
    {
        protected interface IProcessor
        {
            void Execute(ref TComponent component);
        }

        [BurstCompile]
        protected struct WrapperJob<TProcessor> : IJobChunk where TProcessor : struct, IProcessor
        {
            public TProcessor processor;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                TComponent component = default;

                processor.Execute(ref component);
            }
        }

        protected WrapperJob<TProcessor> CreateJob<TProcessor>(TProcessor processor) where TProcessor : struct, IProcessor
        {
            return new WrapperJob<TProcessor>
            {
                processor = processor
            };
        }
    }

    public class TestSystem : CustomSystem<Translation>
    {
        protected override void OnUpdate()
        {
            CreateJob(new TestProcessor
            {
            }).Run(EntityManager.UniversalQuery);
        }

        struct TestProcessor : IProcessor
        {
            public void Execute(ref Translation translation)
            {
            }
        }
    }

    public class UserGenericJobCode4 : ECSTestsFixture
    {
        [Test]
        public void ReflectionDataForVisibleGenerics()
        {
            var repro = World.GetOrCreateSystem<TestSystem>();
            repro.Update();
        }
    }

}
