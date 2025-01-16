# Iterate over component data

Iterating over data is one of the most common tasks you need to perform when you create a system. A system typically processes a set of entities, reads data from one or more components, performs a calculation, and then writes the result to another component.

The most efficient way to iterate over entities and components is in a job that processes the components in order. This takes advantage of the processing power from all available cores and data locality to avoid CPU cache misses. 

This section explains how to iterate over entity data in the following ways:

|**Topic**|**Description**|
|---|---|
|[Iterate over component data with SystemAPI.Query](systems-systemapi-query.md)|Iterate through a collection of data on the main thread.|
|[Iterate over component data with IJobEntity](iterating-data-ijobentity.md)| Write once and create multiple schedules with `IJobEntity`.|
| [Iterate over chunks of data with IJobChunk](iterating-data-ijobchunk.md)| Iterate over archetype chunks that contain matching entities with `IJobChunk`. |
|[Iterate manually over data](iterating-manually.md)| Manually iterate over entities or archetype chunks.|
|[Iterate over component data with Entities.ForEach](iterating-data-entities-foreach.md)|Use `Entities.ForEach` in SystemBase to iterate over entities.|
|[Query data with an entity query](systems-entityquery.md)|Find component data with entity queries.|
|[Look up arbitrary data](systems-looking-up-data.md)|Access arbitrary data without using an entity query.|

## Additional resources

You can also use the [`EntityQuery`](xref:Unity.Entities.EntityQuery) class to construct a view of your data that contains only the specific data you need for a given algorithm or process. Many of the iteration methods in the list above use an `EntityQuery`, either explicitly or internally. For more information, see [Querying entity data with an entity query](systems-entityquery.md).
