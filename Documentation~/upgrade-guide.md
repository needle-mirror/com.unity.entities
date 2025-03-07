# Upgrading to Entities 1.4

Entities 1.4 has some changes that might introduce warnings to your project. To fix those warnings, do the following:

* [Change Entities.ForEach code](#change-entitiesforeach-code)
* [Change Aspects code](#change-aspects-code)

## Change Entities.ForEach code

To consolidate the Entities API and improve iteration time, [`Entities.ForEach`](iterating-entities-foreach-ecb.md) is deprecated in Entities 1.4, and you should use either [`IJobEntity`](#ijobentity) or [`SystemAPI.Query`](#systemapiquery).

### IJobEntity

Because `IJobEntity` `Execute` methods support `ref` and `in` parameters to denote read-only and read-write status, you can often copy the lambda of an `Entities.ForEach`into the `Execute` method for the `IJobEntity` job struct. Additionally, `IJobEntity` supports all the scheduling options that `Entities.ForEach` supports. 

> [!NOTE]
> `IJobEntity` isn't Burst-compiled by default and it can't capture variables because there is no lambda body. Use the `[BurstCompile]` attribute to enable Burst compilation and write captured variables into fields on the job struct.

Code example using `Entities.ForEach`

```c#
public partial class RotationSpeedSystemForEachISystem : SystemBase
{
    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        Entities
            .ForEach((ref LocalTransform transform, in RotationSpeed rotationSpeed) =>
            {
                transform.Rotation = math.mul(
                    math.normalize(transform.Rotation),
                    quaternion.AxisAngle(math.up(), rotationSpeed.RadiansPerSecond * deltaTime));
            })
            .ScheduleParallel();
    }
}

```

Code example using `IJobEntity`

```c#
[BurstCompile]
public partial struct ASampleJob : IJobEntity
{
    public float DeltaTime;
    void Execute(ref LocalTransform transform, in RotationSpeed rotationSpeed)
    {
        transform.Rotation = math.mul(
            math.normalize(transform.Rotation),
            quaternion.AxisAngle(math.up(), rotationSpeed.RadiansPerSecond * DeltaTime));
    }
}

public partial class ASample : SystemBase
{
    protected override void OnUpdate()
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        new ASampleJob{ DeltaTime = deltaTime }.ScheduleParallel();
    }
}
```

For more information about `IJobEntity`, refer to [Iterate over component data with IJobEntity](iterating-data-ijobentity.md)

### SystemAPI.Query

For entity iteration that doesn't have to happen in a job (but can still be Burst compiled), `SystemAPI.Query` can provide a simpler option because`SystemAPI.Query` utilizes `RefRO` and `RefRW` types to wrap type parameters that are accessed as read-only and read-write status respectively. There are additional builder methods on `Query` to indicate `WithAll`, `WithNone`, `WithAny` and other options.

The following changes the previous `Entities.ForEach` example to use `SystemAPI.Query`

```c#
public partial class ASample : SystemBase
{
    protected override void OnUpdate()
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (transform, rotationSpeed) in 
            SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>>())
        {
            transform.ValueRW.Rotation = math.mul(
                math.normalize(transform.ValueRO.Rotation),
                quaternion.AxisAngle(math.up(), rotationSpeed.ValueRO.RadiansPerSecond * deltaTime));
        }
    }
}
```

For more information about `SystemAPI.Query`, refer to [Iterate over component data with SystemAPI.Query](systems-systemapi-query.md).

## Change Aspects code

[Aspects](aspects-intro.md) is deprecated from Entities 1.4, and there's no direct replacement for them. Instead you must replace the abstraction with explicit code that queries for the correct set of components and performs the expected operation on them. The following code provides a simple example of converting an aspect and its usage into an explicit `EntityQuery` and a helper method designed to perform the operation.

Code example using Aspects:

```c#
public partial struct RotationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        var elapsedTime = SystemAPI.Time.ElapsedTime;

        foreach (var movement in SystemAPI.Query<VerticalMovementAspect>())
        {
            movement.Move(elapsedTime);
        }
    }
}

readonly partial struct VerticalMovementAspect : IAspect
{
    readonly RefRW<LocalTransform> m_Transform;
    readonly RefRO<RotationSpeed> m_Speed;

    public void Move(double elapsedTime)
    {
        m_Transform.ValueRW.Position.y = (float)math.sin(elapsedTime * m_Speed.ValueRO.RadiansPerSecond);
    }
}


```

Code example using `EntityQuery`:

```c#
public partial struct RotationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var elapsedTime = SystemAPI.Time.ElapsedTime;

        foreach (var (transform, speed) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>>())
        {
            VerticalMovementHelper.Move(elapsedTime, transform, speed);
        }
    }
}

static class VerticalMovementHelper
{
    public static void Move(double elapsedTime, RefRW<LocalTransform> transform, RefRO<RotationSpeed> speed)
    {
        transform.ValueRW.Position.y = (float)math.sin(elapsedTime * speed.ValueRO.RadiansPerSecond);
    }
}
```
