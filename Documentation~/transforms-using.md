# Use transforms

To use transforms in your project, use the [`Unity.Transforms`](xref:Unity.Transforms) namespace to control the position, rotation, and scale of any entity in your project.

`LocalTransform` represents the relative position, rotation, and scale of the entity. If there is a parent, the transform is relative to that parent. If there is no parent, the transform is relative to the world origin. You can read and write to this component.

```c#
public struct LocalTransform : IComponentData
{
    public float3 Position;
    public float Scale;
    public quaternion Rotation;
}
```

## Using the API

There are no methods in the API to modify `LocalTransform`. All methods return a new value, and do not change the transform itself. So if you want to modify the transform, you must use the assignment operator. For example, to rotate a transform around the Z axis:

```c#
    myTransform = myTransform.RotateZ(someAngle);
```

The only way to modify `LocalTransform` directly is by writing to the Position, Rotation, and Scale properties. For example:

```c#
    myTransform.Position += math.up();
```

This code is equivalent to:

```c#
    myTransform = myTransform.Translate(math.up());
```

There are several methods to construct a transform for you. So if you want to create a `LocalTransform` with a specified position, but using default rotation and scale, use this:

```c#
    var myTransform = LocalTransform.FromPosition(1, 2, 3);
```

## Using a hierarchy

You can use `LocalTransform` by itself. However, if you want to use a hierarchy of Entities, you must also use `Parent`. To set a parent of a child entity use [`Parent`](xref:Unity.Transforms.Parent):

```c#
public struct Parent : IComponentData
{
    public Entity Value;
}
```

To make sure that the parents find their children, and to set up their child component, [`ParentSystem`](xref:Unity.Transforms.ParentSystem) must run.

Use the `static` flag for everything that isn't going to move. This improves performance and reduces memory consumption.

The transform system is optimized for large numbers of hierarchies at the root level. A root level transform is a transform with no parent. Avoid having large hierarchies under a single root. The work of processing hierarchical transforms is divided across jobs at the root level. 

When working with entity transform hierarchies, you should keep the following points in mind:

* The set of hierarchy components on an entity is determined by its position in its transform hierarchy, if any:
  * The entities at the root of a transform hierarchy will have the `Child` component, but not `Parent`.
  * The entities at the "leaves" of a hierarchy will have the `Parent` component, but not `Child`.
  * The entities in the interior of a hierarchy will have both `Parent` and `Child` components.
  * Entities not in a hierarchy will have neither `Parent` nor `Child`.
* The `Child` and `PreviousParent` components are always managed by the `ParentSystem`. Application code should never directly add, remove, or change the values of these components.
* After adding, removing, or modifying the `Parent` component on an entity, the hierarchy will be in an inconsistent state until the next `ParentSystem` update: the entity will still appear in its previous parent's `Child` buffer, and will not appear in its new parent's `Child` buffer.
* The `LocalToWorld` component value is not kept up-to-date with the entity's `LocalTransform` over the course of the frame; it is only guaranteed to be valid immediately after the `LocalToWorldSystem` updates. It is not safe to use this component as an entity's world-space transform during simulation code; the value may be stale, or even completely invalid.