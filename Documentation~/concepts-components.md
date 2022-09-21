# Component concepts

In the Entity Component System (ECS) architecture, **components** contain [entity](concepts-entities.md) data that [systems](concepts-systems.md) can read or write. 

Use the [`IComponentData`](xref:Unity.Entities.IComponentData) interface, which has no methods, to mark a struct as a component type. This component type can only contain unmanaged data, and they can contain methods, but it's best practice for them to just be pure data. If you want to create a managed component, you define this as a class. For more information, see [Managed components](components-managed.md).

There are different types of component that serve different purposes. Depending on how you want to manage the data in your project, certain components allow for more fine-tuned control over the performance of your application. For more information, see [Component types](components-type.md).

A unique set of an entity's components is called an **archetype**. The ECS architecture stores component data by archetype in 16KiB blocks of memory called chunks. For more information on how ECS stores component data see the documentation on [Archetype concepts](concepts-archetypes.md).

## Additional resources

* [Working with components](components-intro.md)
* [Component types](components-type.md)
* [Entity concepts](concepts-entities.md)
* [Archetype concepts](concepts-archetypes.md)
