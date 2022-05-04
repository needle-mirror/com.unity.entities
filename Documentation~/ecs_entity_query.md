---
uid: ecs-entity-query
---
# Querying data with EntityQuery

To read or write data, you must first find the data you want to change. ECS stores data in Components, which it groups together in memory by the Archetype of the Entity to which they belong. To get a view of the ECS data that contains only the specific data you need for a given algorithm or process, use [EntityQuery]. 

You can use [EntityQuery] to do the following: 

* Run a job to process the selected Entities and Components
* Get a `NativeArray` that contains all the selected Entities
* Get `NativeArray`s of the selected Components (by Component type)

The Entity and Component arrays that [EntityQuery] returns are parallel. This means that the same index value always applies to the same Entity in any array. 

> [!NOTE]
> The SystemBase [Entities.ForEach] constructions create internal [EntityQuery] instances based on the Component types and attributes you specify for these APIs. You can't use a different [EntityQuery] object with [Entities.ForEach]. However, you can get the query object that an [Entities.ForEach] instance creates and use it elsewhere.

## Defining a query

An [EntityQuery] query defines the set of Component types that an Archetype must contain for ECS to include its chunks and Entities in the view. You can also exclude Archetypes that contain specific types of Components.  

For simple queries, you can create an [EntityQuery] based on an array of Component types. The following example defines an [EntityQuery] that finds all Entities with both `RotationQuaternion` and `RotationSpeed` Components: 

[!code-cs[define-query](../DocCodeSamples.Tests/EntityQueryExamples.cs#define-query)]

The query uses [ComponentType.ReadOnly&lt;T&gt;] instead of the [typeof] expression to show that the System doesn't write to `RotationSpeed`. Always specify read-only when possible, because there are fewer constraints on read access to data, which can help the job scheduler execute the jobs more efficiently. 

### EntityQueryDesc

For more complex queries, you can use an [EntityQueryDesc] object to create the [EntityQuery]. An [EntityQueryDesc] provides a flexible query mechanism to specify which Archetypes to select based on the following sets of Components:

* `All`: All Component types in this array must exist in the Archetype
* `Any`: At least one of the Component types in this array must exist in the Archetype
* `None`: None of the Component types in this array can exist in the Archetype

For example, the following query includes Archetypes that contain the `RotationQuaternion` and `RotationSpeed`Components, but excludes any Archetypes that contain the `Frozen` Component:

[!code-cs[query-desc](../DocCodeSamples.Tests/EntityQueryExamples.cs#query-desc)]

> [!NOTE]
> Don't include optional Components in the [EntityQueryDesc]. To handle optional Components, use the [ArchetypeChunk.Has<T>] method to determine whether a chunk contains the optional Component or not. Because all Entities within the same chunk have the same Components, you only need to check whether an optional Component exists once per chunk: not once per Entity.

<a name="query-options"></a>

### Query options

When you create an [EntityQueryDesc], you can set its `Options` variable which is for specialized queries:

* Default: No options set; the query behaves as expected.
* `IncludePrefab`: Includes Archetypes that contain the special Prefab tag Component.
* `IncludeDisabled`: Includes Archetypes that contain the special Disabled tag Component.
* `FilterWriteGroup`: Considers the WriteGroup of any Components in the query.

When you set the `FilterWriteGroup` option, ECS includes only Entities with Components in a `WriteGroup` that are explicitly included in the query in the view. ECS excludes any Entities that have any additional Components from the same `WriteGroup`.

In the following example C2 and C3 are Components in the same `WriteGroup` based on C1. This query uses the `FilterWriteGroup` option that requires C1 and C3:

[!code-cs[query-writegroup](../DocCodeSamples.Tests/EntityQueryExamples.cs#query-writegroup)]

This query excludes any Entities with both C2 and C3 because C2 isn't explicitly included in the query. While you can use `None` to design this into the query, doing it through a Write Group provides an important benefit: you don't need to change the queries other Systems use (as long as these Systems also use Write Groups). 

Write Groups are a mechanism that you can use to extend existing Systems. For example, if C1 and C2 are defined in another System (perhaps part of a library that you don't control), you can put C3 into the same Write Group as C2 to change how C1 is updated. For any Entities which you add to the C3 Component, the System updates C1 and the original System doesn't. For other Entities without C3, the original System updates C1 as before.

For more information, see [Write Groups](ecs_write_groups.md).

### Combining queries

To combine multiple queries, you can pass an array of [EntityQueryDesc] objects rather than a single instance. You must use a logical `OR` operation to combine each query. The following example selects any Archetypes that contain a `RotationQuaternion` Component or a `RotationSpeed` Component (or both):

[!code-cs[combine-query](../DocCodeSamples.Tests/EntityQueryExamples.cs#combine-query)]

## Creating an EntityQuery

Inside a System class, you get a query from the System rather than creating it from scratch. Systems cache any queries that your implementation creates and return the cached instance rather than creating a new one when possible. 

When your System uses [Entities.ForEach], use [WithStoreEntityQueryInField] to get an instance of the query used by an [Entities.ForEach] construction: 

[!code-cs[get-query](../DocCodeSamples.Tests/EntityQueryExamples.cs#get-query)]

In other cases, such as when you need an instance of a query to schedule an IJobChunk job, use the [GetEntityQuery] function: 

[!code-cs[get-query-ijobchunk](../DocCodeSamples.Tests/EntityQueryExamples.cs#get-query-ijobchunk)]

Note that filter settings aren't considered when caching queries. Also, if you set filters on a query, ECS sets the same the next time you access that same query with [GetEntityQuery]. Use [ResetFilter] to clear any existing filters.  

## Defining filters

Filters exclude Entities that ECS would otherwise include among those returned by a query based on the following:
 
* **Shared Component filter**: Filter the set of Entities based on specific values of a shared Component.
* **Change filter**: Filter the set of Entities based on whether the value of a specific Component type has changed.

The filters you set remain in effect until you call [ResetFilter] on the query object.

> [!NOTE]
> Write Groups use a different mechanism. See [Query options].

### Shared Component filters

To use a shared Component filter, include the shared Component in the [EntityQuery] - along with other needed Components - and call the [SetSharedComponentFilter] function. Then pass in a struct of the same `ISharedComponent` type that contains the values to select. All values must match. You can add up to two different shared Components to the filter.

You can change the filter at any time, but if you change the filter, it doesn't change any existing arrays of Entities or Components that you received from the group [ToComponentDataArray]&lt;T&gt; or [ToEntityArray] functions. You must recreate these arrays.

The following example defines a shared Component named `SharedGrouping` and a System that only processes Entities that have the Group field set to `1`.

[!code-cs[shared-component-filter](../DocCodeSamples.Tests/EntityQueryExamples.cs#shared-component-filter)]

### Change filters

If you only need to update Entities when a Component value has changed, you can use the [SetChangedVersionFilter] function to add that Component to the [EntityQuery] filter. For example, the following [EntityQuery] only includes Entities from chunks that another System has already written to the Translation Component: 

[!code-cs[change-filter](../DocCodeSamples.Tests/EntityQueryExamples.cs#change-filter)]

> [!NOTE]
> For efficiency, the change filter applies to whole chunks, not individual Entities. The change filter also only checks whether a System has run that declared write access to the Component, not whether it actually changed any data. In other words, if another job which had the ability to write to that Component type accesses the chunk, then the change filter includes all Entities in that chunk. This is why you should always declare read-only access to Components that you don't need to modify.

## Executing the query

Typically, you execute a query when you schedule a job that uses it. You can also call one of the [EntityQuery] methods that returns arrays of Entities, Components, or chunks:

* [ToEntityArray] returns an array of the selected Entities.
* [ToComponentDataArray] returns an array of the Components of type `T` for the selected Entities.
* [CreateArchetypeChunkArray] returns all the chunks that contain the selected Entities. Because a query operates on Archetypes, shared Component values, and change filters, which are all identical for all the Entities in a chunk, the set of Entities stored in the returned set of chunks is the same as the set of Entities [ToEntityArray] returns.

## Queries in the Editor

In the Editor, the following icon represents a query: ![](images/editor-query-icon.png) . You’ll see this when you use the specific [Entities windows and Inspectors](editor-workflows.md). You can also use the [Query window](editor-query-window.md) to see the Components and Entities that match the selected query.

[Query options]: #query-options
[EntityQuery]: xref:Unity.Entities.EntityQuery
[Entities.ForEach]: xref:ecs-entities-foreach
[typeof]: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/operators/type-testing-and-cast#typeof-operator
[EntityQueryDesc]: xref:Unity.Entities.EntityQueryDesc
[WithStoreEntityQueryInField]: xref:Unity.Entities.SystemBase.Entities
[GetEntityQuery]: xref:Unity.Entities.ComponentSystemBase.GetEntityQuery
[ResetFilter]: xref:Unity.Entities.EntityQuery.ResetFilter
[SetSharedComponentFilter]: xref:Unity.Entities.EntityQuery.SetSharedComponentFilter*
[ToComponentDataArray]: xref:Unity.Entities.EntityQuery.ToComponentDataArray*
[ToEntityArray]: xref:Unity.Entities.EntityQuery.ToEntityArray*
[CreateArchetypeChunkArray]: xref:Unity.Entities.EntityQuery.CreateArchetypeChunkArray*
[SetChangedVersionFilter]: xref:Unity.Entities.EntityQuery.SetChangedVersionFilter*
[ComponentType.ReadOnly&lt;T&gt;]: xref:Unity.Entities.ComponentType.ReadOnly*
[ArchetypeChunk.Has<T>]: xref:Unity.Entities.ArchetypeChunk.Has*
[GetEntityQuery]: xref:Unity.Entities.ComponentSystemBase.GetEntityQuery*
