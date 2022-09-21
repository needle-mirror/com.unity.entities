# Transform concepts

You can use the [`Unity.Transforms`](xref:Unity.Transforms) namespace to control the world-space position, rotation, and scale of any entity in your project. 

You can also use the built-in [aspect](aspects-intro.md) `TransformAspect`, to move entities and their parents together, and keep the entity data in sync. For more information, see the [TransformAspect](transform-aspect.md) documentation. 

The main Transform components are:

* [`LocalToWorldTransform`](xref:Unity.Transforms.LocalToWorldTransform): Modify these values to change an entity's world-space position. Every entity that represents an object in world-space has this component. **Important:** If the entity also contains `LocalToParentTransform` and `ParentToWorldTransform`, these take preference and overwrite the `LocalToWorldTransform` values you enter.
* [`LocalToParentTransform`](xref:Unity.Transforms.LocalToParentTransform): Represents the transform from local-space to parent-space. Defines how a child entity transforms relative to its parent.
* [`ParentToWorldTransform`](xref:Unity.Transforms.ParentToWorldTransform): A copy of the `LocalToWorldTransform` of the parent entity.

If all three Transform components are all present on an entity, ECS computes `LocalToWorldTransform` as:

```c#
LocalToWorldTransform = LocalToParentTransform * ParentToWorldTransform
```

You can use the Convert To Entity script to create and initialize all the components for you. To use this script, in the EntitiesSamples project, open HelloCube and select the large cube. In the Inspector, select the Convert To Entity component, which turns a GameObject into an entity. 

## Transform hierarchy 

`Unity.Transforms` is hierarchical, which means that you can transform Entities based on their relationship to each other. 

For example, a car body can be the parent of its wheels. The wheels are children of the car body. When the car body moves, the wheels move with it. You can also move and rotate the wheels relative to the car body.

An entity can have multiple children, but only one parent. Children can also be parents of their own child entities. These multiple levels of parent-child relationships form a transform hierarchy. The entity at the top of a hierarchy, without a parent, is the **root**.

To declare a Transform hierarchy, you must do this from the bottom up. This means that you use [`Parent`](xref:Unity.Transforms.Parent) to declare an Entity's parent, rather than declare its children. If you want to declare a child of an Entity, find the Entities that you want to be children and set their parent to be the target Entity. For more information, see the [Using a hierarchy](transforms-using.md#using-a-hierarchy) documentation.
