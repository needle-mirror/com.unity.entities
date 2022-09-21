# Using transforms

To use transforms in your project, use the [`Unity.Transforms`](xref:Unity.Transforms) namespace to control the world-space position, rotation, and scale of any entity in your project. 

To store position, rotation, and scale values, use [`UniformScaleTransform`](xref:Unity.Transforms.UniformScaleTransform):

```c#
public struct UniformScaleTransform
{
    public float3     Position
    public float      Scale
    public quaternion Rotation
}
```

Every entity that represents an object in your project has a `LocalToWorldTransform`, which you can use to transform the entity relative to its position in world-space :

```c#
public struct LocalToWorldTransform : IComponentData
{
    public UniformScaleTransform Value;
}
```

## Using a hierarchy

You can use `LocalToWorldTransform` by itself. However, if you want to use a hierarchy of Entities, you must use `Parent`, `LocalToParentTransform`, and `ParentToWorldTransform` to transform them.

To set a parent of a child entity use [`Parent`](xref:Unity.Transforms.Parent):

```c#
public struct Parent : IComponentData
{
    public Entity Value;
}
```

To make sure that the parents find their children, and to set up their child component, run [ParentSystem](xref:Unity.Transforms.ParentSystem).

To specify how to position, rotate, and scale the child relative to its parent use [`LocalToParentTransform`](xref:Unity.Transforms.LocalToParentTransform). For example, this is how you could rotate the wheels on a parent car object:

```c#
public struct LocalToParentTransform : IComponentData
{
    public UniformScaleTransform Value;
}
```

The other important component is `ParentToWorldTransform` which is a copy of the `LocalToWorldTransform` of the parent. You need to make sure the child has this, and that `ParentToWorldTransformSystem` is running. 

```c#
public struct ParentToWorldTransform : IComponentData
{
    public UniformScaleTransform Value;
}
```