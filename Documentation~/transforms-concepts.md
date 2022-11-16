# Transform concepts

You can use the [`Unity.Transforms`](xref:Unity.Transforms) namespace to control the position, rotation, and scale of any entity in your project.

You can also use the built-in [aspect](aspects-intro.md) `TransformAspect`, which will help you manage entities that are in a hierarchy. For more information, see the [TransformAspect](transform-aspect.md) documentation.

## The LocalTransform component

The `LocalTransform` component has three properties:

```c#
public struct LocalTransform : IComponentData, ITransformData
{
    public float3 Position;
    public float Scale;
    public quaternion Rotation;
}
```

If the entity has a `Parent` component, its Position, Rotation, and Scale are relative to that parent. If the entity doesn't have a `Parent` component, the transform is relative to the origin of the world.

## The WorldTransform component

The `WorldTransform` component has the same three properties as `LocalTransform`. But unlike the latter, they are always relative to the origin of the world.

It is important to note that `WorldTransform` has a derived value, so writing to it has no effect. It is computed by the transform systems from `LocalTransform`, combined with the `WorldTransform` of the parent entity, if there is one. If there is no parent, `WorldTransform` is simply a copy of `LocalTransform`. This gives you access to an entity's final world position, rotation, and scale, regardless of whether it has a parent or not.

## The ParentTransform component

The `ParentTransform` component has the same three properties as `LocalTransform`.

If an entity has a `Parent` component, it will also have a `ParentTransform` component. This is simply a copy of the parent's `WorldTransform`. This is provided so that you don't need to look up the parent when you need the parent's `WorldTransform`. The [TransformAspect](transform-aspect.md) makes use of this to keep `LocalTransform` and `WorldTransform` in sync with each other.

It is important to note that `ParentTransform` has a derived value, so writing to it has no effect.

## The LocalToWorld component

The `LocalToWorld` (`float4x4`) matrix represents the transform from local space to world space. This matrix is what the rendering system will use to render the geometry.

`LocalToWorld` is normally derived from `WorldTransform`, which is itself derived from `LocalTransform`. This behavior may be overridden by using a `[WriteGroup(typeof(LocalToWorld))]` on a component. With this write group, you have complete control over `LocalToWorld`. See documentation on [write groups](systems-write-groups.md).

## The PostTransformScale component

Transform components only support uniform scale. To render nonuniformly scaled geometry, you may use a `PostTransformScale` (`float3x3`). This will be applied in the following manner:

```
LocalToWorld = WorldTransform.ToMatrix() * PostTransformScale
```

## Latency

`WorldTransform`, `ParentTransform`, and `LocalToWorld` are all derived from `LocalTransform`. Because this is done by the transform systems, their values are updated once those systems have run.

## Computation

If `WorldTransform`, `ParentTransform`, and `LocalTransform` are all present on an entity, ECS computes `WorldTransform` as:

```c#
WorldTransform = LocalTransform * ParentTransform
```

## Transform hierarchy

`Unity.Transforms` is hierarchical, which means that you can transform Entities based on their relationship to each other.

For example, a car body can be the parent of its wheels. The wheels are children of the car body. When the car body moves, the wheels move with it. You can also move and rotate the wheels relative to the car body.

An entity can have multiple children, but only one parent. Children can have their own child entities. These multiple levels of parent-child relationships form a transform hierarchy. The entity at the top of a hierarchy, without a parent, is the **root**.

To declare a Transform hierarchy, you must do this from the bottom up. This means that you use [`Parent`](xref:Unity.Transforms.Parent) to declare an Entity's parent, rather than declare its children. If you want to declare a child of an Entity, find the Entities that you want to be children and set their parent to be the target Entity. For more information, see the [Using a hierarchy](transforms-using.md#using-a-hierarchy) documentation.
