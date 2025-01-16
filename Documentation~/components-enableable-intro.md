# Enableable components introduction

You can use enableable components on [IComponentData](xref:Unity.Entities.IComponentData) and [IBufferElementData](xref:Unity.Entities.IBufferElementData) components to disable or enable individual components on an entity at runtime. To make components enableable, inherit them from [IEnableableComponent](xref:Unity.Entities.IEnableableComponent).

Enableable components are ideal for states that you expect to change often and unpredictably, or where the number of state permutations are high on a frame-by-frame basis. [Adding](components-add-to-entity.md) and [removing components](components-remove-from-entity.md) is the preferable way to manage components for low-frequency state changes, where you expect the state to persist for many frames. You can use enableable components to avoid structural changes in some situations. For more information, refer to [Manage structural changes with enableable components](structural-changes-enableable-components.md).

You can also use enableable components instead of a set of zero-size [tag components](components-tag.md) to represent entity states. This reduces the number of unique entity archetypes, and encourages better [chunk](concepts-archetypes.md#archetype-chunks) usage to reduce memory consumption.


## Additional resources

* [Use enableable components](components-enableable-use.md)
* [Look up arbitrary data](systems-looking-up-data.md)
* [Manage structural changes with enableable components](structural-changes-enableable-components.md)