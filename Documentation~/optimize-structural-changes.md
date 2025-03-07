# Optimize structural changes

[Structural changes](concepts-structural-changes.md) cause sync points that affect performance. There are also other CPU tasks that Unity must perform during structural changes, which can also impact performance.

## Structural change process

For example, if you have an entity with two components called `A` and `B`, and you want to add a third component called `C`, you might write the following code:

```c#
// create archetype and entity in a Burst-friendly way
var abComponents =  new FixedList128Bytes<ComponentType>
{
   ComponentType.ReadWrite<A>(),
   ComponentType.ReadWrite<B>(),
}.ToNativeArray(state.WorldUpdateAllocator);

var abArchetype = state.EntityManager.CreateArchetype(abComponents);
var entity = state.EntityManager.CreateEntity(abArchetype);

// ... Some time later... 
state.EntityManager.AddComponent<C>(entity);
```

Adding component `C` to an entity which has an [archetype](concepts-archetypes.md) of `AB` causes Unity to perform the following process:

1. Check if an `EntityArchetype` for `ABC` already exists, and create one if it doesn't.
1. Check if the `ABC` archetype has a chunk free for a new entity. If there's insufficient chunk space, allocate a new chunk.
1. Use `Memcpy()` to copy components `A` and `B` into the new chunk.
1. Create or copy the component `C` into the new chunk.
1. Update the `EntityManager` so that the entity which previously pointed to an index in an `AB` chunk now points to the new index in the `ABC` chunk.
1. Use `swap_back()` to remove the original entity from the `AB` chunk.
    * Free the chunk memory if the original entity was the only entity in the chunk.
    * Otherwise, update `EntityManager` with the new index of the chunk.
1. Clear the cached list of chunks for every `EntityQuery` that involves that chunk's archetype. The `EntityQuery` recalculates the list of chunks it refers to the next time it executes.

The individual steps to perform structural changes aren't slow, but when thousands of entities change archetypes in a single frame, this can significantly impact performance. The processing overhead scales with the number of EntityArchetypes and EntityQueries that have been declared at runtime.

## Structural changes approach comparision

The following table compares different approaches to structural changes, and the time in milliseconds that it takes to add one component to one million entities with each approach:

| **Method** | **Description** | **Time in ms** |
| --- | --- | --- |
| **EntityManager and query with enableable components** | Don't add any components, and enable a component that implements `IEnableable`, which was previously disabled. For more information, refer to [Enableable components](components-enableable.md) | 0.03 |
| **EntityManager and query** | Pass an `EntityQuery` to the `EntityManager` with `AddComponent` to immediately add components in bulk on the main thread. | 3.5 |
| **EntityManager and NativeArray** | Pass a `NativeArray<Entity>` to the `EntityManager` to immediately add components on the main thread | 35 |
| **Entity command buffer and playback query** | Pass an `EntityQuery` to an `EntityCommandBuffer` on the main thread to queue components to add using the `EntityQueryCaptureMode.AtPlayback` flag. Then execute that entity command buffer (time includes the entity command buffer execution time). For more information, refer to [Entity command buffers](systems-entity-command-buffer-use.md).| 3.5 |
| **Entity command buffer and NativeArray** | Pass a `NativeArray<Entity>` to an `EntityCommandBuffer` on the main thread to queue components to add, then execute that entity command buffer (time includes the entity command buffer execution time).| 35 |
| **Entity command buffer and job system with IJobChunk** | Use an `IJobChunk` across multiple worker threads to pass a `NativeArray<Entity>` per chunk to an `EntityCommandBuffer`, then execute that entity command buffer (time includes the entity command buffer execution time). | 17 |
| **Entity command buffer and job system with IJobEntity** | Use an `IJobEntity` across multiple worker threads to pass instructions to add components to entities one at a time to an `EntityCommandBuffer`, then execute that entity command buffer (time includes the entity command buffer execution time)| 170 |

## Optimize native arrays for chunks

If you need to build a `NativeArray` of entities to apply a structural change to, match the entity order in the array with the order of the entities in memory. The simplest way to do this is with an [`IJobChunk`](iterating-data-ijobchunk.md) which can iterate over the chunks matching your target query. The job can iterate over the entities in the chunk in order and build a `NativeArray` of the entities to apply the change to. 

You can pass this NativeArray to an `EntityCommandBuffer.ParallelWriter` to queue up the required changes. When Unity executes the `EntityCommandBuffer`, entities are accessed one by one via lookups to the `EntityManager`. This process increases the chance of CPU cache hits because it accesses the entities in order. 

## Entity command buffers and entity queries

When an `EntityQuery` is passed to an `EntityManager` method, the method operates at a chunk level rather than on individual entities. When you pass an `EntityQuery` to an `EntityCommandBuffer` method, between the time the command is added to the `EntityCommandBuffer` and the time when the buffer executes its commands, the content of the chunks might change because of other structural changes. 

Use [`EntityQueryCaptureMode.AtPlayback`](xref:Unity.Entities.EntityQueryCaptureMode) to store the `EntityQuery` and evaluate it when the buffer is executed, which avoids executing structural changes one entity at a time.

## Enable systems to avoid structural changes

If you want to stop a specific system from processing every entity that matches its `EntityQuery`, instead of removing a component from all those entities, you can disable the system itself. The best way to do this is to add or remove a component from an entity to signal if the system should be enabled. Then call the `SystemState`'s [`RequireForUpdate()`](xref:Unity.Entities.SystemState.RequireForUpdate``1) method in your system's `OnCreate()` method specifying such a component. If an entity with the component you specify exists, your system updates. If you remove the component, the system stops updating, and you only have to add or remove one component.

You can also use the `Enabled` flag in [`SystemState`](xref:Unity.Entities.SystemState) to disable a system. 

## Structural changes during entity creation

Avoid adding components one at a time to construct entities at runtime. Calling `EntityManager.AddComponent()` creates a new archetype and moves the entity into a whole new chunk. The archetype exists for the rest of the runtime of your application and contributes to the performance overhead of the necessary calculations any time a new `EntityQuery` needs to calculate which `EntityArchetype` instances it references.

You should create the archetype that describes the entity you want to end up with and then create an entity directly from that archetype. For example:

```c#
// Cache this archetype if we intend to use it again later  
var newEntityArchetype = state.EntityManager.CreateArchetype(typeof(Foo), typeof(Bar), typeof(Baz));  
var entity = EntityManager.CreateEntity(newEntityArchetype);


// Better yet, if you want to create lots of identical entities at the same time  
var entities = new NativeArray<Entity>(10000, Allocator.Temp);  
state.EntityManager.CreateEntity(newEntityArchetype, entities); 
```
### Adding or removing multiple components simultaneously

If you need to add or remove more than one component to an entity (or a set of entities) at runtime, you can use the [`ComponentTypeSet`](xref:Unity.Entities.ComponentTypeSet) struct to specify all the components to be added or removed at once, which helps to minimize the number of structural changes and redundant archetypes. The struct can be passed to `EntityManager` methods such as:

* [`AddComponent(Entity, ComponentTypeSet)`](xref:Unity.Entities.EntityManager.AddComponent(Unity.Entities.Entity,Unity.Entities.ComponentTypeSet@))  
* [`AddComponent(EntityQuery, ComponentTypeSet)`](xref:Unity.Entities.EntityManager.AddComponent(Unity.Entities.EntityQuery,Unity.Entities.ComponentType))
* [`AddComponent(SystemHandle, ComponentTypeSet)`](xref:Unity.Entities.EntityManager.AddComponent(Unity.Entities.SystemHandle,Unity.Entities.ComponentTypeSet@))
* Equivalent [`RemoveComponent()`](xref:Unity.Entities.EntityManager.RemoveComponent*) methods.

## Measuring the performance of structural changes

Use the [Structural Changes Profiler module](profiler-module-structural-changes.md) to use this to monitor the impact of structural changes on your project's runtime performance. For more information on how to use the Profiler, refer to [Profiler overview](xref:um-profiler).

## Additional resources

* [Structural changes concepts](concepts-structural-changes.md)
* [Use entity command buffers](systems-entity-command-buffer-use.md)
* [Enableable components](components-enableable.md)