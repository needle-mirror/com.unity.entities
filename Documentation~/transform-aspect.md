# Transform aspect

The [aspect](aspects-intro.md) system has the built in [`TransformAspect`](xref:Unity.Transforms.TransformAspect), which contains references to all three transform components of a child entity:

* [`LocalTransform`](xref:Unity.Transforms.LocalTransform)
* [`WorldTransform`](xref:Unity.Transforms.WorldTransform)
* [`ParentTransform`](xref:Unity.Transforms.ParentTransform) 

For any root entities, `TransformAspect`'s `LocalTransform` will be the same as `WorldTransform`.

`TransformAspect` is a convenient way to work with transforms in your project. It contains logic that keeps `LocalTransform` and `WorldTransform` in sync with each other. It allows you to manipulate an entity in world space, even if it is part of a hierarchy.

This example illustrates using `TransformAspect` to rotate the turret of a tank:

```c#
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
partial struct TurretRotationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // The amount of rotation around Y required to do 360 degrees in 2 seconds.
        var rotation = quaternion.RotateY(state.Time.DeltaTime * math.PI);

        // The classic C# foreach is what we often refer to as "Idiomatic foreach" (IFE).
        // Aspects provide a higher level interface than directly accessing component data.
        // Using IFE with aspects is a powerful and expressive way of writing main thread code.
        foreach (var transform in SystemAPI.Query<TransformAspect>())
        {
            transform.RotateWorld(rotation);
        }
    }
}
```
