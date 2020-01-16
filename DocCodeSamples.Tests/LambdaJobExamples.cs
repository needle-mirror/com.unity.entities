
using Unity.Entities.UniversalDelegates;

namespace Doc.CodeSamples.Tests
{
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Transforms;
    using Unity.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using Random = Unity.Mathematics.Random;

    #region entities-foreach-example
    class ApplyVelocitySystem : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            var jobHandle = Entities
                .ForEach((ref Translation translation,
                          in Velocity velocity) =>
                {
                    translation.Value += velocity.Value;
                })
                .Schedule(inputDependencies);

            return jobHandle;
        }
    }
    #endregion
    #region job-with-code-example
    public class RandomSumJob : JobComponentSystem
    {
        private uint seed = 1;

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            Random randomGen = new Random(seed++);
            NativeArray<float> randomNumbers
                = new NativeArray<float>(500, Allocator.TempJob);

            JobHandle generateNumbers = Job.WithCode(() =>
            {
                for (int i = 0; i < randomNumbers.Length; i++)
                {
                    randomNumbers[i] = randomGen.NextFloat();
                }
            }).Schedule(inputDeps);


            NativeArray<float> result
                = new NativeArray<float>(1, Allocator.TempJob);

            JobHandle sumNumbers = Job.WithCode(() =>
            {
                for (int i = 0; i < randomNumbers.Length; i++)
                {
                    result[0] += randomNumbers[i];
                }
            }).Schedule(generateNumbers);

            sumNumbers.Complete();
            UnityEngine.Debug.Log("The sum of "
                                  + randomNumbers.Length + " numbers is " + result[0]);

            randomNumbers.Dispose();
            result.Dispose();

            return sumNumbers;
        }
    }

    #endregion

    //Used to verify the BuffersByEntity example (not shown in docs)
    public class MakeData : ComponentSystem
    {
        protected override void OnCreate()
        {
            var sum = 0;
            for (int i = 0; i < 100; i++)
            {
                var ent = EntityManager.CreateEntity(typeof(IntBufferElement));
                var buff = EntityManager.GetBuffer<IntBufferElement>(ent).Reinterpret<int>();
                for (int j = 0; j < 5; j++)
                {
                    buff.Add(j);
                    sum += j;
                }
            }

            UnityEngine.Debug.Log("Sum should equal " + sum);
        }

        protected override void OnUpdate()
        {

        }
    }

    public struct IntBufferData : IBufferElementData
    {
        public int Value;
    }

    #region dynamicbuffer
    public class BufferSum : JobComponentSystem
    {
        private EntityQuery query;

        //Schedules the two jobs with a dependency between them
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            //The query variable can be accessed here because we are
            //using WithStoreEntityQueryInField(query) in the entities.ForEach below
            int entitiesInQuery = query.CalculateEntityCount();

            //Create a native array to hold the intermediate sums
            //(one element per entity)
            NativeArray<int> intermediateSums
                = new NativeArray<int>(entitiesInQuery, Allocator.TempJob);

            //Schedule the first job to add all the buffer elements
            JobHandle bufferSumJob = Entities
                .ForEach((int entityInQueryIndex, in DynamicBuffer<IntBufferData> buffer) =>
                {
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        intermediateSums[entityInQueryIndex] += buffer[i].Value;
                    }
                })
                .WithStoreEntityQueryInField(ref query)
                .WithName("IntermediateSums")
                .Schedule(inputDeps);

            //Schedule the second job, which depends on the first
            JobHandle finalSumJob = Job
                .WithCode(() =>
                {
                    int result = 0;
                    for (int i = 0; i < intermediateSums.Length; i++)
                    {
                        result += intermediateSums[i];
                    }
                    //Not burst compatible:
                    Debug.Log("Final sum is " + result);
                })
                .WithDeallocateOnJobCompletion(intermediateSums)
                .WithoutBurst()
                .WithName("FinalSum")
                .Schedule(bufferSumJob);

            return finalSumJob;
        }
    }
    #endregion

    public struct Source : IComponentData
    {
        public int Value;
    }
    public struct Destination : IComponentData
    {
        public int Value;
    }

    public class WithAllExampleSystem : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            #region entity-query
            return Entities.WithAll<LocalToWorld>()
                .WithAny<Rotation, Translation, Scale>()
                .WithNone<LocalToParent>()
                .ForEach((ref Destination outputData, in Source inputData) =>
                {
                    /* do some work */
                })
                .Schedule(inputDeps);
            #endregion
        }
    }

    public struct Data : IComponentData
    {
        public float Value;
    }
    public class WithStoreQuerySystem : JobComponentSystem
    {
        #region store-query
        private EntityQuery query;
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            int dataCount = query.CalculateEntityCount();
            NativeArray<float> dataSquared
                = new NativeArray<float>(dataCount, Allocator.Temp);
            JobHandle GetSquaredValues = Entities
                .WithStoreEntityQueryInField(ref query)
                .ForEach((int entityInQueryIndex, in Data data) =>
                    {
                        dataSquared[entityInQueryIndex] = data.Value * data.Value;
                    })
                .Schedule(inputDeps);

            return Job
                .WithCode(() =>
                {
                    //Use dataSquared array...
                    var v = dataSquared[dataSquared.Length -1];
                })
                .WithDeallocateOnJobCompletion(dataSquared)
                .Schedule(GetSquaredValues);
        }
        #endregion
    }

    public class WithChangeExampleSystem : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            #region with-change-filter
            return Entities
                .WithChangeFilter<Source>()
                .ForEach((ref Destination outputData,
                    in Source inputData) =>
                {
                    /* Do work */
                })
                .Schedule(inputDeps);
            #endregion
        }
    }

    public struct Cohort : ISharedComponentData
    {
        public int Value;
    }
    public struct DisplayColor : IComponentData
    {
        public int Value;
    }

    public class ColorTable
    {
        public static DisplayColor GetNextColor(int current){return new DisplayColor();}
    }

    #region with-shared-component
    public class ColorCycleJob : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            List<Cohort> cohorts = new List<Cohort>();
            EntityManager.GetAllUniqueSharedComponentData<Cohort>(cohorts);
            JobHandle sequentialDeps = inputDeps; // Chain job dependencies
            foreach (Cohort cohort in cohorts)
            {
                DisplayColor newColor = ColorTable.GetNextColor(cohort.Value);
                JobHandle thisJobHandle =
                    Entities.WithSharedComponentFilter(cohort)
                        .ForEach((ref DisplayColor color) => { color = newColor; })
                        .Schedule(sequentialDeps);
                sequentialDeps = thisJobHandle;
            }
            return sequentialDeps;
        }
    }
    #endregion

    public class ReadWriteModExample : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            #region read-write-modifiers
            return Entities.ForEach(
                    (ref Destination outputData,
                        in Source inputData) =>
                    {
                        outputData.Value = inputData.Value;
                    })
                .Schedule(inputDeps);
            #endregion
        }
    }

    #region basic-ecb
    public class MyJobSystem : JobComponentSystem
    {
        private EndSimulationEntityCommandBufferSystem commandBufferSystem;

        protected override void OnCreate()
        {
            commandBufferSystem = World
                .DefaultGameObjectInjectionWorld
                .GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            EntityCommandBuffer.Concurrent commandBuffer
                = commandBufferSystem.CreateCommandBuffer().ToConcurrent();

            //.. The rest of the job system code
            return inputDeps;
        }
    }
    #endregion
}

namespace Doc.CodeSamples.Tests
{
    #region full-ecb-pt-one
    // ParticleSpawner.cs
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Mathematics;
    using Unity.Transforms;

    public struct Velocity : IComponentData
    {
        public float3 Value;
    }

    public struct TimeToLive : IComponentData
    {
        public float LifeLeft;
    }

    public class ParticleSpawner : JobComponentSystem
    {
        private EndSimulationEntityCommandBufferSystem commandBufferSystem;

        protected override void OnCreate()
        {
            commandBufferSystem = World
                .DefaultGameObjectInjectionWorld
                .GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            EntityCommandBuffer.Concurrent commandBufferCreate
                = commandBufferSystem.CreateCommandBuffer().ToConcurrent();
            EntityCommandBuffer.Concurrent commandBufferCull
                = commandBufferSystem.CreateCommandBuffer().ToConcurrent();

            float dt = Time.DeltaTime;
            Random rnd = new Random();
            rnd.InitState((uint) (dt * 100000));


            JobHandle spawnJobHandle = Entities
                .ForEach((int entityInQueryIndex,
                          in SpawnParticles spawn,
                          in LocalToWorld center) =>
                {
                    int spawnCount = spawn.Rate;
                    for (int i = 0; i < spawnCount; i++)
                    {
                        Entity spawnedEntity = commandBufferCreate
                            .Instantiate(entityInQueryIndex,
                                         spawn.ParticlePrefab);

                        LocalToWorld spawnedCenter = center;
                        Translation spawnedOffset = new Translation()
                        {
                            Value = center.Position +
                                    rnd.NextFloat3(-spawn.Offset, spawn.Offset)
                        };
                        Velocity spawnedVelocity = new Velocity()
                        {
                            Value = rnd.NextFloat3(-spawn.MaxVelocity, spawn.MaxVelocity)
                        };
                        TimeToLive spawnedLife = new TimeToLive()
                        {
                            LifeLeft = spawn.Lifetime
                        };

                        commandBufferCreate.SetComponent(entityInQueryIndex,
                                                         spawnedEntity,
                                                         spawnedCenter);
                        commandBufferCreate.SetComponent(entityInQueryIndex,
                                                         spawnedEntity,
                                                         spawnedOffset);
                        commandBufferCreate.AddComponent(entityInQueryIndex,
                                                         spawnedEntity,
                                                         spawnedVelocity);
                        commandBufferCreate.AddComponent(entityInQueryIndex,
                                                         spawnedEntity,
                                                         spawnedLife);
                    }
                })
                .WithName("ParticleSpawning")
                .Schedule(inputDeps);

            JobHandle MoveJobHandle = Entities
                .ForEach((ref Translation translation, in Velocity velocity) =>
                {
                    translation = new Translation()
                    {
                        Value = translation.Value + velocity.Value * dt
                    };
                })
                .WithName("MoveParticles")
                .Schedule(spawnJobHandle);

            JobHandle cullJobHandle = Entities
                .ForEach((Entity entity, int entityInQueryIndex, ref TimeToLive life) =>
                {
                    life.LifeLeft -= dt;
                    if (life.LifeLeft < 0)
                        commandBufferCull.DestroyEntity(entityInQueryIndex, entity);
                })
                .WithName("CullOldEntities")
                .Schedule(inputDeps);

            JobHandle finalDependencies
                = JobHandle.CombineDependencies(MoveJobHandle, cullJobHandle);

            commandBufferSystem.AddJobHandleForProducer(spawnJobHandle);
            commandBufferSystem.AddJobHandleForProducer(cullJobHandle);

            return finalDependencies;
        }
    }
    #endregion
}

namespace Doc.CodeSamples.Tests
{
    #region full-ecb-pt-two
    // SpawnParticles.cs
    using Unity.Entities;
    using Unity.Mathematics;

    [GenerateAuthoringComponent]
    public struct SpawnParticles : IComponentData
    {
        public Entity ParticlePrefab;
        public int Rate;
        public float3 Offset;
        public float3 MaxVelocity;
        public float Lifetime;
    }
    #endregion
}