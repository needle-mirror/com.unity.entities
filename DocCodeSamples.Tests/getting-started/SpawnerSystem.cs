namespace Doc.CodeSamples.Tests.GettingStarted
{
    #region example
    using Unity.Entities;
    using Unity.Transforms;
    using Unity.Burst;
    using Unity.Mathematics;

    public partial struct SpawnerSystem : ISystem
    {
        private float nextSpawn;

        // The Random struct is from the Unity Mathematics package, which provides types
        // and functions optimized for Burst.
        private Random random;

        public void OnCreate(ref SystemState state)
        {
            // This call prevents the system from updating unless at least one entity with
            // the Spawner component exists in the ECS world.
            // This also prevents GetSingleton from throwing an exception if it doesn't find
            // an object of type Spawner.
            state.RequireForUpdate<Spawner>();

            random = new Random((uint)System.DateTime.Now.Ticks);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Use the GetSingleton method when there is only one entity of a 
            // specific type in the ECS world.
            Spawner spawner = SystemAPI.GetSingleton<Spawner>();

            if (nextSpawn < SystemAPI.Time.ElapsedTime)
            {
                // The Prefab field of the spawner variable contains a reference to 
                // the entity prefab which ECS converts during the baking stage.
                Entity newEntity = state.EntityManager.Instantiate(spawner.Prefab);

                float3 randomOffset = (random.NextFloat3() - 0.5f) * 10f;
                randomOffset.y = 0;

                float3 newPosition = spawner.SpawnPosition + randomOffset;

                state.EntityManager.SetComponentData(newEntity,
                                            LocalTransform.FromPosition(newPosition));

                nextSpawn = (float)SystemAPI.Time.ElapsedTime + spawner.SpawnRate;
            }
        }
    }
    #endregion
}