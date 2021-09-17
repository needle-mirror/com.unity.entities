---
uid: ecs_ijobentity
---
# Using IJobEntity jobs

IJobEntity, is a way to iterate across component data, like [Entities.ForEach].
This is meant to look like a job, and because it reads like a job, many of the same attributes work, 
and because of source generation it creates the underlying IJobEntityBatch.

You write a struct using the IJobEntity interface, and implement your own custom Execute function. Remember the `partial` keyword, as source generation will create a struct implementing IJobEntityBatch in a separate file found inside project/Temp/GeneratedCode/....
```cs
public partial struct ASampleJobEntity : IJobEntity 
{
    void Execute(ref Translation translation)
    {
        translation.Value += 1f;
    }
}
```

This is a list of allowed attributes:
[Unity.Burst.BurstCompile], [Unity.Collections.DeallocateOnJobCompletion], [Unity.Collections.NativeDisableParallelForRestriction], [Unity.Burst.BurstDiscard], [NativeSetThreadIndex], [NativeDisableParallelForRestriction], [Unity.Burst.NoAlias]

There's also an attribute called [EntityQueryInIndex], which can be used on one parameter of the execute function inside your IJobEntity job.
It is the equivalent to the EntityInQueryIndex found in [Entities.ForEach].
It allows you to get a unique index of the current entity iteration, a sample can be read as follows:
```cs
public partial struct VehicleDespawnJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter EntityCommandBuffer;

    public void Execute(Entity entity, [EntityInQueryIndex] int entityInQueryIndex, in DynamicBuffer<MyBufferInt> myBufferInts, ref Translation translation, in VehiclePathing vehicle)
    {
        translation.Value += entityInQueryIndex + entity.Version + myBufferInts[2].Value + nativeThreadIndex;
        if (vehicle.CurvePos >= 1.0f)
        {
            EntityCommandBuffer.DestroyEntity(entityInQueryIndex, entity);
        }
    }
}

public partial class JobEntity_WithIntParamsAndDynamicBuffer : SystemBase
{
    private EndSimulationEntityCommandBufferSystem _DespawnBarrier;

    protected override void OnCreate()
    {
        base.OnCreate();
        _DespawnBarrier = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        var job = new VehicleDespawnJob
        {
            EntityCommandBuffer = _DespawnBarrier.CreateCommandBuffer().AsParallelWriter()
        };
        Dependency = job.ScheduleParallel(Dependency);
    }
}
```

An equal [Entities.ForEach] would look like:
```cs
public partial class JobEntity_WithIntParamsAndDynamicBuffer : SystemBase
{
    private EndSimulationEntityCommandBufferSystem _DespawnBarrier;

    protected override void OnCreate()
    {
        base.OnCreate();
        _DespawnBarrier = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        var entityCommandBuffer = _DespawnBarrier.CreateCommandBuffer().AsParallelWriter()
        
        Dependency = Entities.ForEach((Entity entity, int entityInQueryIndex, in DynamicBuffer<MyBufferInt> myBufferInts, ref Translation translation, in VehiclePathing vehicle) => 
        {
            translation.Value += entityInQueryIndex + entity.Version + myBufferInts[2].Value + nativeThreadIndex;
            if (vehicle.CurvePos >= 1.0f)
            {
                entityCommandBuffer.DestroyEntity(entityInQueryIndex, entity);
            }
        }).ScheduleParallel(Dependency);
    }
}
```

The core advantage of [IJobEntity] over [Entities.ForEach] is that it enables you to write code once which can be used throughout many systems, instead of only once. An example taken boids is writing:
```cs
[BurstCompile]
partial struct CopyPositionsJob : IJobEntity
{
    public NativeArray<float3> copyPositions;
    public void Execute([EntityInQueryIndex] int entityInQueryIndex, in LocalToWorld localToWorld)
    {
        copyPositions[entityInQueryIndex] = localToWorld.Position;
    }
}
public partial class ExampleSystem : SystemBase 
{
    protected void OnUpdate() 
    {
        var initialCellSeparationJob = new CopyPositionsJob { copyPositions = cellSeparation};
        var initialCellSeparationJobHandle = initialCellSeparationJob.ScheduleParallel(m_BoidQuery, Dependency);
        
        var copyTargetPositionsJob = new CopyPositionsJob { copyPositions = copyTargetPositions};
        var copyTargetPositionsJobHandle = copyTargetPositionsJob.ScheduleParallel(m_TargetQuery, Dependency);
        
        var copyObstaclePositionsJob = new CopyPositionsJob { copyPositions = copyObstaclePositions};
        var copyObstaclePositionsJobHandle = copyObstaclePositionsJob.ScheduleParallel(m_ObstacleQuery, Dependency);
    }
}
```

vs.

```cs
public partial class ExampleSystem : SystemBase 
{
    protected void OnUpdate() 
    {
        var initialCellSeparationJobHandle = Entities
            .WithSharedComponentFilter(settings)
            .WithName("InitialCellSeparationJob")
            .ForEach((int entityInQueryIndex, in LocalToWorld localToWorld) =>
            {
                cellSeparation[entityInQueryIndex] = localToWorld.Position;
            })
            .ScheduleParallel(Dependency);

        var copyTargetPositionsJobHandle = Entities
            .WithName("CopyTargetPositionsJob")
            .WithAll<BoidTarget>()
            .WithStoreEntityQueryInField(ref m_TargetQuery)
            .ForEach((int entityInQueryIndex, in LocalToWorld localToWorld) =>
            {
                copyTargetPositions[entityInQueryIndex] = localToWorld.Position;
            })
            .ScheduleParallel(Dependency);

        var copyObstaclePositionsJobHandle = Entities
            .WithName("CopyObstaclePositionsJob")
            .WithAll<BoidObstacle>()
            .WithStoreEntityQueryInField(ref m_ObstacleQuery)
            .ForEach((int entityInQueryIndex, in LocalToWorld localToWorld) =>
            {
                copyObstaclePositions[entityInQueryIndex] = localToWorld.Position;
            })
            .ScheduleParallel(Dependency);
    }
}
```

Remark:
The IJobForEach class is deprecated in favor of [SystemBase], [IJobEntity] and [Entities.ForEach]. See [Creating systems] for more information on programming systems and read the [Upgrade Guide] for a guide on how to upgrade to IJobEntity.

[Entities.ForEach]: xref:Unity.Entities.SystemBase.Entities
[Creating systems]: ecs_creating_systems.md
[SystemBase]: xref:Unity.Entities.SystemBase
[IJobEntity]: ijobentity.md
[Upgrade Guide]: entities_upgrade_guide.md
