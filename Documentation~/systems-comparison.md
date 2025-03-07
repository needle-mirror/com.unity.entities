# Systems comparison

To create a system, you can use either [`ISystem`](xref:Unity.Entities.ISystem) or [`SystemBase`](xref:Unity.Entities.SystemBase). `ISystem` provides access to unmanaged memory, whereas `SystemBase` is useful for storing managed data. You can use both system types with all of the Entities package and the job system. The following outlines the differences between the two system types

## Differences between systems

[`ISystem`](systems-isystem.md) is compatible with Burst, is faster than `SystemBase`, and has a value-based representation. In general, you should use `ISystem` over `SystemBase` to get better performance benefits. However, [`SystemBase`](systems-systembase.md) has convenient features at the compromise of using garbage collection allocations or increased `SourceGen` compilation time. 

The following table outlines their compatibility:

|**Feature**|**ISystem compatibility**|**SystemBase compatibility**|
|---|---|---|
|Burst compile `OnCreate`, `OnUpdate`, and `OnDestroy`|Yes|No|
|Unmanaged memory allocated|Yes|No|
|GC allocated|No|Yes|
|Can store managed data directly in system type|No|Yes|
|[Idiomatic `foreach`](systems-systemapi-query.md)|Yes|Yes|
|[`Job.WithCode`](xref:Unity.Entities.SystemBase.Job)|No|Yes|
|[`IJobEntity`](xref:Unity.Entities.IJobEntity)|Yes|Yes|
|[`IJobChunk`](xref:Unity.Entities.IJobChunk)|Yes|Yes|
|Supports inheritance|No|Yes|

## System comparison example

Imagine you’re writing a system that moves certain entities along spline paths. The data the system accesses might be the following:

* The system operates on all spline-following entities and identifies them with a `FollowingSplineTag` component. This is included in the [entity query](systems-entityquery.md), but the system doesn’t need to read or write this component.  
* The system needs read-only access to a `SplineFollower` component in the spline follower entities, which contains an `Entity` to reference a spline entity to be followed, and a `float` indicating a distance along that spline.  
* Spline entities contain a [dynamic buffer](components-buffer.md) of SplinePoints called `SplinePointsBuffer`. Given the spline followers use an arbitrary `Entity` handle to reference the spline entity to follow, the system needs read-only random-access to these buffers.  
* Spline entities also contain a `SplineLength` component which the system requires read-only random-access to in order to perform spline position calculations.  
* Finally, the system needs read-write access to the spline-following entities’ `LocalTranform` components in order to update the positions and rotations.

The following declares a stub for a helper method that performs the spline calculation:

```c#
public struct FollowingSplineTag : IComponentData { }

public struct SplineFollower : IComponentData
{
   public Entity Spline;
   public float Distance;
}

public struct SplinePointsBuffer : IBufferElementData
{
   public float3 SplinePoint;
}

public struct SplineLength : IComponentData
{
   public float Value;
}

public struct SplineHelper
{
   public static LocalTransform FollowSpline(
       DynamicBuffer<SplinePointsBuffer> pointsBuf, float length, float distance)
    {
       // Perform spline calculation and return a new LocalTransform here
   }
}
```

You can then use this in a `foreach` statement in an `ISystem` system:

```c#
var lengthLookup = SystemAPI.GetComponentLookup<SplineLength>(true);
var pointsBufferLookup = SystemAPI.GetBufferLookup<SplinePointsBuffer>(true);

// Version with writeable buffer lookup
foreach (var (transform, follower) in
        SystemAPI.Query<RefRW<LocalTransform>, RefRO<SplineFollower>>()
        .WithAll<FollowingSplineTag>())
{
   var splineLength = lengthLookup[follower.ValueRO.Spline].Value;
   var pointsBuf = pointsBufferLookup[follower.ValueRO.Spline];
   transform.ValueRW = SplineHelper.FollowSpline(pointsBuf, splineLength, follower.ValueRO.Distance);
}
```

You could also use multithreaded code to access this information. The following uses `IJobEntity` with an automatically-generated query:

```c#
// Job declaration
[BurstCompile]
[WithAll(typeof(FollowingSplineTag))]
public partial struct FollowSplineJob : IJobEntity
{
   [ReadOnly] public ComponentLookup<SplineLength> LengthLookup;
   [ReadOnly] public BufferLookup<SplinePointsBuffer> PointsBufferLookup;
  
   public void Execute(ref LocalTransform transform, in SplineFollower follower)
   {
       var splineLength = LengthLookup[follower.Spline].Value;
       var pointsBuf = PointsBufferLookup[follower.Spline];
       transform = SplineHelper.FollowSpline(pointsBuf, splineLength, follower.Distance);
   }
}

// in OnUpdate()...
new FollowSplineJob
{
   LengthLookup =  lengthLookup,
   PointsBufferLookup =  pointsBufferLookup
}.ScheduleParallel();
```

## Additional resources

* [`ISystem` overview](systems-isystem.md)
* [`SystemBase` overview](systems-systembase.md)
