# Aspect overview

An aspect is an object-like wrapper that you can use to group together a subset of an entity's components into a single C# struct. Aspects are useful for organizing component code and simplifying queries in your systems. 

For example, the [`TransformAspect`](xref:Unity.Transforms.TransformAspect) groups together the individual position, rotation, and scale of components and enables you to access these components from a query that includes the `TransformAspect`. You can also define your own aspects with the [`IAspect`](xref:Unity.Entities.IAspect) interface.

Aspects can include items such as:

* A single entity field to store the entity's ID
* `RefRW<T>` and `RefRO<T>` fields to access component data of type T, which implements [`IComponentData`](xref:Unity.Entities.IComponentData).
* `EnabledRefRW` and `EnabledRefRO` fields to access the enabled state of components that implement [`IEnableableComponent`](xref:Unity.Entities.IEnableableComponent).
* `DynamicBuffer<T>` fields
* Other aspect types

## Further information

* [Create an aspect](aspects-create.md)
* [`IAspect` API documentation](xref:Unity.Entities.IAspect)
* [`TransformAspect` API documentation](xref:Unity.Transforms.TransformAspect)