namespace Doc.CodeSamples.Tests.GettingStarted
{
    #region example
    using Unity.Burst;
    using Unity.Entities;
    using Unity.Transforms;

    // This example defines an unmanaged system based on the ISystem interface.
    // ECS uses code generation, which is why the struct must be declared as partial.
    public partial struct RotationSystem : ISystem
    {
        // The BurstCompile attribute indicates that the method should be compiled
        // with the Burst compiler into highly-optimized native CPU code.
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // In ECS, use the DeltaTime property from Entities.SystemAPI.Time.
            float deltaTime = SystemAPI.Time.DeltaTime;

            // Create a query that selects all entities that have a LocalTransform
            // component and a RotationSpeed component.
            // In each loop iteration, the transform variable is assigned 
            // a read-write reference to LocalTransform, and the speed variable is
            // assigned a read-only reference to the RotationSpeed component.
            foreach (var (transform, speed) in
                        SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>>())
            {
                // ValueRW and ValueRO both return a reference to the actual component
                // value. The difference is that ValueRW does a safety check for 
                // read-write access while ValueRO does a safety check for read-only
                // access.
                transform.ValueRW = transform.ValueRO.RotateY(
                    speed.ValueRO.RadiansPerSecond * deltaTime);
            }
        }
    }
    #endregion
}