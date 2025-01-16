# Manage structural changes with enableable components

Enableable components don't create [structural changes](concepts-structural-changes.md), unlike adding and removing components. ECS treats a disabled component as though the entity doesn't have that component when determining if an entity matches an [entity query](systems-entityquery.md). This means that an entity with a disabled component doesn't match a query that requires the component, and matches a query that excludes the component, assuming it meets all other query criteria.

The semantics of existing component operations don't change.  `EntityManager` considers an entity with a disabled component to still have the component. 

For example, if component `T` is disabled on entity `E` these methods do the following:

|**Method**|**Outcome**|
|---|---|
|`HasComponent<T>(E)` |Returns true.|
|`GetComponent<T>(E)` |Returns component `T`’s current value.|
|`SetComponent<T>(E,value)`| Updates the component `T`’s value.|
|`RemoveComponent<T>(E)`| Removes component `T` from `E`.|
|`AddComponent<T>(E)`| Quietly does nothing, because the component already exists.|

## When to use enableable components for structural changes

Not every scenario is suitable for enableable components. Enableable components might affect the performance of jobs and systems that access the archetypes that contain them. If entity changes are extremely infrequent in your project, or you want to optimize chunk fragmentation and CPU cache usage, then adding and removing components might be the preferable option.

When you [iterate entities](systems-iterating-data-intro.md) with a specific enableable component, if all the components of that type within a chunk are disabled, the chunk is skipped, which saves time. If all the components in the chunk are enabled, the job can iterate efficiently over the entities in the chunk, which allows [Burst](https://docs.unity3d.com/Packages/com.unity.burst@latest) to process multiple components simultaneously.

Entities with many components in general take up more space within a chunk, and using enableable components heavily can sometimes mean you have entities with more components on them. Fewer entities that fit into a chunk might mean an increase in the total runtime memory usage because they need more chunks to store all the entities. This scenario might contribute to [chunk fragmentation](performance-chunk-allocations.md). 

Entities that use a lot of components and that are serialized in prefabs or subscenes occupy extra storage space on the disk, which can impact the time it takes to load the data.

For more information, refer to [Optimize structural changes](optimize-structural-changes.md).

## Additional resources

* [Enableable components introduction](components-enableable-intro.md)
* [Using enableable components](components-enableable-use.md)
* [Manage structural changes](systems-manage-structural-changes-intro.md)