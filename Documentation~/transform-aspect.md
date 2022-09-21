# Transform aspect

The [aspect](aspects-intro.md) system has the built in [`TransformAspect`](xref:Unity.Transforms.TransformAspect), which contains references to all three transform components of a child entity:

* [`LocalToWorldTransform`](xref:Unity.Transforms.LocalToWorldTransform)
* [`LocalToParentTransform`](xref:Unity.Transforms.LocalToParentTransform)
* [`ParentToWorldTransform`](xref:Unity.Transforms.ParentToWorldTransform) 

For any root entities, `TransformAspect` only contains a reference to `LocalToWorldTransform`.

`TransformAspect` is a convenient way to manage the transforms in your project because it contains logic which keeps all of these components in sync with each other. For example, if you want to control the world-space position of a child component without using `TransformAspect`, you have to update both the `LocalToWorldTransform` and `LocalToParentTransform`, and then use `ParentToWorldTransform` in that calculation. 

However, `TransformAspect` manages this for you. This is a convenient way to move Entities that might have a parent.

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