# Using transforms

To use transforms in your project, use the [`Unity.Transforms`](xref:Unity.Transforms) namespace to control the position, rotation, and scale of any entity in your project.

There are three transform components. They are structurally identical, but their purpose is different. The most commonly used component is `LocalTransform`. It represents the relative position, rotation, and scale of the component. If there is a parent, the transform is relative to that parent. If there is no parent, the transform is relative to the world origin. You can read and write to this component.

```c#
public struct LocalTransform : IComponentData, ITransformData
{
    public float3 Position;
    public float Scale;
    public quaternion Rotation;
}
```

The second component is `WorldTransform`. It has the same properties as `LocalTransform`, but this transform is always relative to the world origin. Because the value is derived from `LocalTransform`, writing to it has no effect. You should treat this as read-only. It is used internally in `TransformAspect`.

```c#
public struct WorldTransform : IComponentData, ITransformData
{
    public float3 Position;
    public float Scale;
    public quaternion Rotation;
}
```

The third component is `ParentTransform`. It also has the same properties as `LocalTransform`. This is a copy of the parent's `WorldTransform`, so that you don't have to look up the parent if you only need its transform. This is also a derived value, so writing to it has no effect. You should treat it as read-only. It is used internally in the `TransformAspect`.

```c#
public struct ParentTransform : IComponentData, ITransformData
{
    public float3 Position;
    public float Scale;
    public quaternion Rotation;
}
```

## Using the API

All three transform components (`LocalTransform`, `WorldTransform`, and `ParentTransform`) have the same API. This API is implemented as extension methods. There are no methods in the API to modify the transform. All methods return a new value, and do not change the transform itself. So if you want to modify the transform, you must use the assignment operator. For example, to rotate a transform around the Z axis:

```c#
    myTransform = myTransform.RotateZ(someAngle);
```

The only way to modify the transform directly is by writing to the Position, Rotation, and Scale properties. For example:

```c#
    transform.Position += math.up();
```

For the complete non-static API, see [`Unity.Transforms.TransformDataHelpers`](xref:Unity.Transforms.TransformDataHelpers)

There is also a static API, which mainly constructs transforms for you. So if you want to create a `LocalTransform` with a specified position, but using default rotation and scale, use this:

```c#
    var myNewTransform = LocalTransform.FromPosition(1, 2, 3);
```

All three transform components have the same static API. See [`Unity.Transforms.LocalTransform`](xref:Unity.Transforms.LocalTransform)

## Using a hierarchy

You can use `LocalTransform` by itself. However, if you want to use a hierarchy of Entities, you must also use `Parent`. To set a parent of a child entity use [`Parent`](xref:Unity.Transforms.Parent):

```c#
public struct Parent : IComponentData
{
    public Entity Value;
}
```

To make sure that the parents find their children, and to set up their child component, run [ParentSystem](xref:Unity.Transforms.ParentSystem).

To specify how to position, rotate, and scale the child relative to its parent use [`LocalTransform`](xref:Unity.Transforms.LocalTransform). For example, this is how you could rotate the wheels on a parent car object:

```c#
void Execute(ref LocalTransform transform)
{
    transform = transform.RotateZ(angleChange);
}
```
