---
uid: systems-entityquery
---

# Query data with EntityQuery

An [`EntityQuery`](xref:Unity.Entities.EntityQuery) finds [archetypes](concepts-archetypes.md) that have a specified set of component types. It then gathers the archetype's chunks into an array which a system can process. 

For example, if a query matches component types A and B, then the query gathers the chunks of all the archetypes that have those two component types, regardless of whatever other component types those archetypes might have. Therefore, an archetype with component types A, B, and C would match the query.

You can use `EntityQuery` to do the following: 

* Run a job to process the selected entities and components
* Get a `NativeArray` that contains all the selected entities
* Get a `NativeArray` of the selected components by component type

The entity and component arrays that `EntityQuery` returns are parallel. This means that the same index value always applies to the same entity in any array. 

## Create an entity query

To create an entity query, you can pass component types to the [`EntityQueryBuilder`](xref:Unity.Entities.EntityQueryBuilder) helper type. The following example defines an `EntityQuery` that finds all entities with both `ObjectRotation` and `ObjectRotationSpeed` components:

[!code-cs[define-query](../DocCodeSamples.Tests/EntityQueryExamples.cs#define-query)]

The query uses [`EntityQueryBuilder.WithAllRW<T>`](xref:Unity.Entities.EntityQueryBuilder.WithAllRW*) to show that the system writes to `ObjectRotation`. You should always specify read-only access when possible, because there are fewer constraints on read access to data. This helps the job scheduler execute the jobs more efficiently. 


### Specify which archetypes the system selects

Queries will only match archetypes that contain the components you specify. Components can be specified with three different [`EntityQueryBuilder`](xref:Unity.Entities.EntityQueryBuilder) methods: 

* `WithAll<T>()`: These components are **required**. In order to match the query, an archetype must contain all of the query's required components.
* `WithAny<T>()`: These components are **optional**. In order to match the query, an archetype must contain at least one of the query's optional components.
* `WithNone<T>()`: These components are **excluded**. In order to match the query, an archetype must not contain any of the query's excluded components.

For example, the following query includes archetypes that contain the `ObjectRotation` and `ObjectRotationSpeed`components, but excludes any archetypes that contain the `Static` component:

[!code-cs[query-desc](../DocCodeSamples.Tests/EntityQueryExamples.cs#query-desc)]


> [!IMPORTANT]
> To handle optional components, use the [`ArchetypeChunk.Has<T>`](xref:Unity.Entities.ArchetypeChunk.Has*) method to determine whether a chunk contains the optional component or not. This is because all entities in the same chunk have the same components, so you only need to check whether an optional component exists once per chunk: not once per entity.

You can use [`EntityQueryBuilder.WithOptions()`](xref:Unity.Entities.EntityQueryBuilder.WithOptions*) to find specialized archetypes. For example:

* `IncludePrefab`: Includes archetypes that contain the Prefab [tag component](components-tag.md).
* `IncludeDisabledEntities`: Includes archetypes that contain the [`Disabled`](xref:Unity.Entities.Disabled) tag component.
* `FilterWriteGroup`: Includes only entities with components in a [`WriteGroup`](xref:Unity.Entities.WriteGroupAttribute) that are explicitly included in the query. Excludes entities that have any additional components from the same `WriteGroup`.

See [`EntityQueryOptions`](xref:Unity.Entities.EntityQueryOptions) for the full list of options.

#### Filter by write group

In the following example `LuigiComponent` and `MarioComponent` are components in the same `WriteGroup` based on the `CharacterComponent` component. This query uses the `FilterWriteGroup` option that requires `CharacterComponent` and `MarioComponent`:

[!code-cs[query-writegroup](../DocCodeSamples.Tests/EntityQueryExamples.cs#query-writegroup)]

This query excludes any entities with both `LuigiComponent` and `MarioComponent` because `LuigiComponent` isn't explicitly included in the query. 

This is more efficient than the `None` field because you don't need to change the queries that the other systems use, as long as they also use write groups.

You can use write groups to extend existing systems. For example, if you've defined `CharacterComponent` and `LuigiComponent` in another system as part of a library you don't control, you can put `MarioComponent` in the same write group as `LuigiComponent` to change how `CharacterComponent` is updated. Then, for any entities you add to the `MarioComponent`, the system updates `CharacterComponent`, but the original system doesn't update it. For entities that don't have `MarioComponent`, the original system updates `CharacterComponent` as before. For more information, see the documentation on [Write groups](systems-write-groups.md).

## Define a filter

To further sort entities, you can use a filter to exclude entities based on the following:
 
* [Shared component filter](xref:Unity.Entities.EntityQuery.SetSharedComponentFilter*): Filter the set of entities based on specific values of a shared component.
* [Change filter](xref:Unity.Entities.EntityQuery.SetChangedVersionFilter*): Filter the set of Entities based on whether the value of a specific component type has changed.

The filters you set remain in effect until you call [`ResetFilter`](xref:Unity.Entities.EntityQuery.ResetFilter) on the query object.

To ignore the query's active chunk filters, use the `EntityQuery` methods that have names ending in `IgnoreFilter`.  These methods are generally more efficient than the filtering equivalents. For example, see [`IsEmpty`](xref:Unity.Entities.EntityQuery.IsEmpty*) vs. [`IsEmptyIgnoreFilter`](xref:Unity.Entities.EntityQuery.IsEmptyIgnoreFilter). 

### Use a shared component filter

To use a shared component filter, include the [shared component](components-shared.md) in the `EntityQuery` along with any other needed components and call the [`SetSharedComponentFilter`](xref:Unity.Entities.EntityQuery.SetSharedComponentFilter*) method. Then pass in a struct of the same `ISharedComponent` type that contains the values to select. All values must match. You can add up to two different shared components to the filter.

You can change the filter at any time, but if you change the filter, it doesn't change any existing arrays of entities or components that you received from the group [`ToComponentDataArray<T>`](xref:Unity.Entities.EntityQuery.ToComponentDataArray*) or [`ToEntityArray`](xref:Unity.Entities.EntityQuery.ToEntityArray*) methods. You must recreate these arrays.

The following example defines a shared component named `SharedGrouping` and a system that only processes entities that have the group field set to `1`.

[!code-cs[shared-component-filter](../DocCodeSamples.Tests/EntityQueryExamples.cs#shared-component-filter)]

### Use a change filter

If you only need to update entities when a component value has changed, use the [`SetChangedVersionFilter`](xref:Unity.Entities.EntityQuery.SetSharedComponentFilter*) method to add that component to the `EntityQuery` filter. For example, the following `EntityQuery` only includes entities from chunks that another system has already written to the `Translation` component: 

[!code-cs[change-filter](../DocCodeSamples.Tests/EntityQueryExamples.cs#change-filter)]


For efficiency, the change filter applies to whole archetype chunks, and not individual entities. The change filter also only checks whether a system that declared write access to the component has run, and not whether it changed any data. For example, if another job that can write to that component type accesses the chunk, then the change filter includes all entities in that chunk. This is why you should always declare read-only access to components that you don't need to modify.

## Filtering by enableable components

[Enableable components](components-enableable.md) allow components on individual entities to be enabled and disabled at runtime. Disabling components on an entity doesn't move that entity into a new archetype, but for the purposes of `EntityQuery` matching, the entity is treated as if it doesn't have the component. Specifically:

* If an entity has component `T` disabled, it will not match queries that require component `T` (using `WithAll<T>()`).
* If an entity has component `T` disabled, it will match queries that exclude component `T` (using `WithNone<T>()`).

Most `EntityQuery` operations, such as [`ToEntityArray`](xref:Unity.Entities.EntityQuery.ToEntityArray*) and [`CalculateEntityCount`](xref:Unity.Entities.EntityQuery.CalculateEntityCount*), automatically filter out entities whose enableable components would cause them not to match the query. To disable this filtering, use the `IgnoreFilter` variants of these operations, or pass the [`EntityQueryOptions.IgnoreComponentEnabledState`](xref:Unity.Entities.EntityQueryOptions.IgnoreComponentEnabledState) at query creation time.

See the [enableable components](components-enableable.md) documentation for more details.

## Combine queries

To effectively combine multiple queries into one, you can create a query that contains multiple query descriptions. The resulting query matches archetypes that match any of the provided query descriptions. Essentially, the combined query matches the union of the query descriptions. The following example selects any archetypes that contain a `ObjectRotation` component or a `ObjectRotationSpeed` component (or both):

[!code-cs[combine-query](../DocCodeSamples.Tests/EntityQueryExamples.cs#combine-query)]


## Execute the query

Typically, you execute an entity query when you schedule a job that uses it. You can also call one of the `EntityQuery` methods that returns arrays of entities, components, or archetype chunks:

* [`ToEntityArray`](xref:Unity.Entities.EntityQuery.ToEntityArray*): Returns an array of the selected entities.
* [`ToComponentDataArray`](xref:Unity.Entities.EntityQuery.ToComponentDataArray*): Returns an array of the components of type `T` for the selected entities.
* [`CreateArchetypeChunkArray`](xref:Unity.Entities.EntityQuery.CreateArchetypeChunkArray*): Returns all the chunks that contain the selected entities. Because a query operates on archetypes, shared component values, and change filters, which are all identical for all the entities in a chunk, the set of entities stored in the returned set of chunks is the same as the set of entities `ToEntityArray` returns.

Asynchronous versions of the above methods are also available, which schedule a job to gather the requested data. Some of these variants must return a `NativeList` instead of a `NativeArray` in order to support [enableable components](components-enableable.md). See [`ToEntityListAsync`](xref:Unity.Entities.EntityQuery.ToEntityListAsync*), [`ToComponentDataListAsync`](xref:Unity.Entities.EntityQuery.ToComponentDataListAsync*), and [`CreateArchetypeChunkArrayAsync`](xref:Unity.Entities.EntityQuery.CreateArchetypeChunkArrayAsync*).

## Queries in the Editor

In the Editor, the following icon represents a query: ![](images/editor-query-icon.png) . You’ll see this when you use the specific [Entities windows and Inspectors](editor-workflows.md). You can also use the [Query window](editor-query-window.md) to see the Components and Entities that match the selected query.
