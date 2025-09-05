namespace Doc.CodeSamples.Tests.GettingStarted
{
    #region example
    using Unity.Entities;
    using Unity.Transforms;
    using Unity.Burst;
    using Unity.Mathematics;

    [BurstCompile]
    public partial struct SpawnerSystemMultithreaded : ISystem
    {
        private Random random;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Spawner>();

            random = new Random((uint)System.DateTime.Now.Ticks);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Initialize an EntityCommandBuffer to record entity operations from
            // multiple threads. You can only create entities on the main thread.
            // Here, we record entity operations in an entity command buffer during
            // parallel job execution and play them back on the main thread to create
            // and configure the entities.
            EntityCommandBuffer.ParallelWriter ecb = GetEntityCommandBuffer(ref state);

            // Creates a new instance of the job, assigns the necessary data, and
            // schedules the job to run in parallel.
            new ProcessSpawnerJob
            {
                ElapsedTime = SystemAPI.Time.ElapsedTime,
                Ecb = ecb,
                RandomSeed = random.NextUInt()
            }.ScheduleParallel();
        }

        private EntityCommandBuffer.ParallelWriter 
                    GetEntityCommandBuffer(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton
                            <BeginSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            return ecb.AsParallelWriter();
        }
    }

    [BurstCompile]
    public partial struct ProcessSpawnerJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter Ecb;
        public double ElapsedTime;
        public uint RandomSeed;

        // IJobEntity generates a component data query based on the parameters of its
        // Execute method. This example queries for Spawner components and uses the ref
        // keyword to specify that the operation requires read and write access. Unity
        // runs the code in the Execute method for each entity that matches the query.
        private void Execute([ChunkIndexInQuery] int chunkIndex, ref Spawner spawner)
        {
            // If the next spawn time has passed.
            if (spawner.NextSpawnTime < ElapsedTime)
            {
                // Create a deterministic random generator for this entity
                var randomGenerator = Random.CreateFromIndex(RandomSeed + (uint)chunkIndex);

                // Spawns a new entity and positions it at the spawner.
                Entity newEntity = Ecb.Instantiate(chunkIndex, spawner.Prefab);

                // Calculate random offset
                float3 randomOffset = (randomGenerator.NextFloat3() - 0.5f) * 10f;
                randomOffset.y = 0;

                float3 newPosition = spawner.SpawnPosition + randomOffset;

                Ecb.SetComponent(chunkIndex, newEntity,
                                            LocalTransform.FromPosition(newPosition));

                // Resets the next spawn time.
                spawner.NextSpawnTime = (float)ElapsedTime + spawner.SpawnRate;
            }
        }
    }
    #endregion
}