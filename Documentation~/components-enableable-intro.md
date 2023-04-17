# Enableable components overview

You can use enableable components on [IComponentData](xref:Unity.Entities.IComponentData) and [IBufferElementData](xref:Unity.Entities.IBufferElementData) components to disable or enable individual components on an entity at runtime. To make components enableable, inherit them from [IEnableableComponent](xref:Unity.Entities.IEnableableComponent).

Enableable components are ideal for states that you expect to change frequently and unpredictably, or where the number of state permutations are high on a frame-by-frame basis. [Adding](components-add-to-entity.md) and [removing components](components-remove-from-entity.md) is the preferable way to manage components for low-frequency state changes, where you expect the state to persist for many frames.

## Structural changes

Enableable components don't create [structural changes](concepts-structural-changes.md), unlike adding and removing components. ECS treats a disabled component as though the entity doesn't have that component when determining if an entity matches an [entity query](systems-entityquery.md). This means that an entity with a disabled component doesn't match a query that requires the component, and matches a query that excludes the component, assuming it meets all other query criteria.

## Tag component alternative

You can also use enableable components instead of a set of zero-size [tag components](components-tag.md) to represent entity states. This reduces the number of unique entity archetypes, and encourages better [chunk](concepts-archetypes.md#archetype-chunks) utilization to reduce memory consumption.

## Enabled component semantics 

The semantics of existing component operations don't change.  `EntityManager` considers an entity with a disabled component to still have the component. 

Specifically, if component `T` is disabled on entity `E`:
* `HasComponent<T>(E)` returns true.
* `GetComponent<T>(E)` returns the component’s current value.
* `SetComponent<T>(E,value)` updates the component’s value.
* `RemoveComponent<T>(E)` removes the component from E.
* `AddComponent<T>(E)` quietly does nothing, because the component already exists.
