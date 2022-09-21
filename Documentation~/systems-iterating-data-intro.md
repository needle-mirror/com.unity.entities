# Iterating over data

Iterating over data is one of the most common tasks you need to perform when you create a system. A system typically processes a set of entities, reads data from one or more components, performs a calculation, and then writes the result to another component.

The most efficient way to iterate over entities and components is in a job that processes the components in order. This takes advantage of the processing power from all available cores and data locality to avoid CPU cache misses. 

This section explains how to iterate over entity data in the following ways:


|**Topic**|**Description**|
|---|---|
|[Iterate over data with `Entities.ForEach`](iterating-data-entities-foreach.md)|How to process component data entity by entity with [`SystemBase.Entities.ForEach`](xref:Unity.Entities.SystemBase.Entities).|
|[Iterate over data with `IJobEntity`](iterating-data-ijobentity.md)| How to write once and create multiple schedules with [`IJobEntity`](xref:Unity.Entities.IJobEntity).|
|[Iterate over batches of data](iterating-data-ijobentitybatch.md)| How to iterate over [archetype chunks](concepts-archetypes.md#archetype-chunks) that contain matching entities with [`IJobEntityBatch`](xref:Unity.Entities.IJobEntityBatch).|
|[Iterate manually over data](iterating-manually.md)| How to manually iterate over entities or archetype chunks.|

## Additional resources

You can also use the [`EntityQuery`](xref:Unity.Entities.EntityQuery) class to construct a view of your data that contains only the specific data you need for a given algorithm or process. Many of the iteration methods in the list above use an `EntityQuery`, either explicitly or internally. For more information, see [Querying entity data with an entity query](systems-entityquery.md).
