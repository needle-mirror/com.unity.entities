---
uid: systems-entity-command-buffers
---

# Scheduling data changes with an EntityCommandBuffer

To queue entity data changes instead of performing the changes instantly, you can use the [`EntityCommandBuffer`](xref:Unity.Entities.EntityCommandBuffer) struct, which creates a thread-safe command buffer. This is useful if you want to defer any [structural changes](concepts-structural-changes.md) while jobs complete. 

## EntityCommandBuffer methods 

You can use the methods in `EntityCommandBuffer` to record commands, which mirror some of the [`EntityManager`](xref:Unity.Entities.EntityManager) methods, for example:

- `CreateEntity(EntityArchetype)`: Creates a new entity with the specified archetype.
- `DestroyEntity(Entity)`: Destroys the entity.
- `SetComponent<T>(Entity, T)`: Sets the value for a component of type `T` on the entity.
- `AddComponent<T>(Entity)`: Adds a component of type `T` to the entity.
- `RemoveComponent<T>(EntityQuery)`: Removes a component of type `T` from all entities that match the query.

Unity only makes the changes recorded in an `EntityCommandBuffer` when it calls the `Playback` method on the main thread. If you attempt to record any further changes to the command buffer after playback, then Unity throws an exception.

`EntityCommandBuffer` has a job safety handle, similar to a [native container](https://docs.unity3d.com/Manual/JobSystemNativeContainer.html). The safety checks throw an exception if you try to do any of the following on an incomplete scheduled job that uses a command buffer: 

* Access the `EntityCommandBuffer` through its `AddComponent`, `Playback`, `Dispose`, or other methods.
* Schedule another job that accesses the same `EntityCommandBuffer`, unless the new job depends on the already scheduled job.

## Use EntityCommandBuffer in a single-threaded job

Unity can't perform [structural changes](concepts-structural-changes.md) in a job, so you can use a command buffer for entities to defer structural changes until Unity completes the job. For example:

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_single_threaded)]

## Use EntityCommandBuffer in a parallel job

If you want to use an entity command buffer in a parallel job, use `EntityCommandBuffer.ParallelWriter`, which concurrently records in a thread-safe way to a command buffer:

```c#
EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

// Methods of this writer record commands to 
// the EntityCommandBuffer in a thread-safe way.
EntityCommandBuffer.ParallelWriter parallelEcb = ecb.AsParallelWriter();
```

> [!NOTE]
> Only recording needs to be thread-safe for concurrency. Playback is always single-threaded on the main thread.

### Deterministic playback

Because recording of the commands is split across threads, the order of recorded commands depends on job scheduling, so is non-deterministic. 

Determinism isn't always essential, but code which produces deterministic results is easier to debug. There are also networking scenarios which require consistent results across different machines. However, determinism can has an impact on performance, so you might need to accept indeterminism in some projects.

You can't avoid the indeterminate order of recording, but you can make the playback order of the commands deterministic in the following way:

1. Each command records a 'sort key' `int` passed as the first argument to each command method. You must call the lambda parameter `entityInQueryIndex`, or `Entities.ForEach` won't be able to recognize the int.
1. On playback, sort the commands by their sort keys before the commands are enacted.

As long as the recorded sort keys are independent from the scheduling, the sorting makes the playback order deterministic.

In a parallel job, the sort key you need for each entity is a number that has a fixed and unique association with that entity in the job's query. 

The `entityInQueryIndex` value provided in a parallel job meets those criteria. In the list of archetype chunks that match the job's query, entities have the following indexes:

- The first entity of the first chunk has `entityInQueryIndex` 0
- The second entity of the first chunk has `entityInQueryIndex`  1
- The first entity of the second chunk has an `entityInQueryIndex` which is the count of the first chunk
- The first entity of the third chunk has an `entityInQueryIndex` which is the sum of the counts of the first two chunks

The `entityInQueryIndex` follows this pattern throughout.

The following example code shows an entity command buffer used in a parallel job:

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_multi_threaded)]


## Reusing an EntityCommandBuffer instance

It's best practice to give each job its own command buffer. This is because recording a set of commands to several command buffers has little overhead compared to recording the same commands in a single command buffer.

However, you can reuse the same `EntityCommandBuffer` in non-parallel jobs, as long as those jobs don't overlap in scheduling. If you reuse an `EntityCommandBuffer` instance in a parallel job, this might lead to unexpected sort orders of the commands in playback, unless the sort keys for each job are in different ranges.

## Multi-playback

If you call the `Playback` method more than once, it throws an exception. To avoid this, create an `EntityCommandBuffer` instance with the `PlaybackPolicy.MultiPlayback` option:

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_multi_playback)]

Multi-playback is useful if you want to repeatedly spawn a set of entities. To do this, create and configure a set of new entities with an `EntityCommandBuffer`, and then repeat playback to respawn another matching set of entities.

## Use an EntityCommandBuffer on the main thread

You can record command buffer changes on the main thread. This can be useful in the following situations:

- To delay your changes.
- To play back a set of changes multiple times.
- To play back a lot of changes in one consolidated place. This is more efficient than interspersing the changes across different parts of the frame.

Every structural change operation triggers a [sync point](concepts-structural-changes.md#sync-points), which means that the operation must wait for some or all scheduled jobs to complete. If you combine the structural changes into a command buffer, the frame has fewer sync points.

## Automatically playback and dispose of command buffers with EntityCommandBufferSystem

You can use [`EntityCommandBufferSystem`](xref:Unity.Entities.EntityCommandBufferSystem) to play back and dispose of a command buffer rather than manually doing it yourself. To do this:

1. Get the instance of the `EntityCommandBufferSystem` which you want to do the playback.
1. Create an `EntityCommandBuffer` instance via the system.
1. Schedule a job that writes commands to the `EntityCommandBuffer`.
1. Register the scheduled job for the system to complete.

For example:

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_from_ecbsystem)]

> [!IMPORTANT]
> Don't manually play back and dispose an `EntityCommandBuffer` that an  `EntityCommandBufferSystem` has created. `EntityCommandBufferSystem` does both for you.

In each update, an `EntityCommandBufferSystem`:

1. Completes all registered jobs, which ensures that they have finished their recording).
1. Playbacks all entity command buffers created via the system in the same order they were created.
1. Disposes of the `EntityCommandBuffer`.

### Default `EntityCommandBufferSystem` systems

The default [world](concepts-worlds.md) has the following default  `EntityCommandBufferSystem` systems:

- `BeginInitializationEntityCommandBufferSystem`
- `EndInitializationEntityCommandBufferSystem`
- `BeginSimulationEntityCommandBufferSystem`
- `EndSimulationEntityCommandBufferSystem`
- `BeginPresentationEntityCommandBufferSystem`

Because structural changes can't happen in the frame after Unity hands off the rendering data to the renderer, there's no `EndPresentationEntityCommandBufferSystem` system. You can use `BeginInitializationEntityCommandBufferSystem` instead: the end of one frame is the beginning of the next frame.

These update at the beginning and end of the standard [system groups](concepts-systems.md#system-groups). For more information, see the documentation on [System update order](systems-update-order.md).

The default systems should be enough for most use cases, but you can create your own `EntityCommandBufferSystem` if necessary:

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_define_ecbsystem)]

## Deferred entities

The `EntityCommandBuffer` methods `CreateEntity` and `Instantiate` record commands that create entities. These methods only record commands and don't create entities. As such, they return `Entity` values with negative indexes that represent placeholder entities that don't exist yet. These placeholder `Entity` values are only meaningful in recorded commands of the same ECB.

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_deferred_entities)]

Values recorded in an `AddComponent`, `SetComponent`, or `SetBuffer` command might have `Entity` fields. In playback, Unity remaps any placeholder `Entity` values in these components or buffers to the corresponding actual entities.

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_deferred_remapping)]

## Use command buffers in the `Entities.ForEach` method
To use a command buffer in the [`Entities.ForEach`](iterating-data-entities-foreach.md) method, pass an `EntityCommandBuffer` parameter to the lambda expression itself. Only a small subset of `EntityCommandBuffer` methods are supported, and they have the `[SupportedInEntitiesForEach]` attribute:

- `Entity Instantiate(Entity entity)`
- `void DestroyEntity(Entity entity)`
- `void AddComponent<T>(Entity e, T component) where T : unmanaged, IComponentData`
- `void SetComponent<T>(Entity e, T component) where T : unmanaged, IComponentData`
- `void RemoveComponent<T>(Entity e)`

For example, the following code does this:
1. It checks each entity to see whether its `HealthLevel` is 0. 
1. If true, it records a command to destroy the entity. 
1. It also specifies that the `EndSimulationEntityCommandBufferSystem` should play back the command.

```c#
public struct HealthLevel : IComponentData
{
    public int Value;
}

Entities
    .WithDeferredPlaybackSystem<EndSimulationEntityCommandBufferSystem>
    .ForEach(
        (Entity entity, EntityCommandBuffer buffer, HealthLevel healthLevel) => 
        {
            if (healthLevel == 0)
            {
                buffer.DestroyEntity(entity);
            }
        }
    ).ScheduleParallel();
```

When you use any of these methods within a `ForEach()` function, at runtime the compiler generates the code necessary to create, populate, play back, and dispose of an `EntityCommandBuffer` instance, or an `EntityCommandBuffer.ParallelWriter` instance, if `ScheduleParallel()` is called.

Invoking these methods outside of `ForEach()` results in an exception.

### Play back an `EntityCommandBuffer` in `Entities.forEach`

To pass an `EntityCommandBuffer` parameter to the `ForEach()` function, you must also call one of the following methods to specify when you want to play back the commands:

- **Deferred playback:** Call `WithDeferredPlaybackSystem<T>()`, where `T` identifies the entity command buffer system that plays back the commands. It must be a type that derives from `EntityCommandBufferSystem`.
- **Immediate playback:** call `WithImmediatePlayback()` to execute the entity commands immediately after the `ForEach()` function has finished all iterations. You can only use `WithImmediatePlayback()` with `Run()`.

The compiler automatically generates code to create and dispose of any `EntityCommandBuffer` instances.
