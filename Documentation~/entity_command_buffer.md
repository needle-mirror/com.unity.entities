---
uid: ecs-entity-command-buffer
---
# Entity Command Buffers

An [EntityCommandBuffer](xref:Unity.Entities.EntityCommandBuffer) (ECB) records entity data changes to be enacted later. Commands are recorded with methods of `EntityCommandBuffer` which mirror many (but not all) of the `EntityManager` methods, for example:

- `CreateEntity(EntityArchetype)`: Create a new entity with the specified archetype
- `DestroyEntity(Entity)`: Destroy the entity
- `SetComponent<T>(Entity, T)`: Set value for component of type `T` on the entity
- `AddComponent<T>(Entity)`: Add component of type `T` to the entity
- `RemoveComponent<T>(EntityQuery)`: Remove component of type `T` from all entities matching the query.

None of the changes recorded in an `EntityCommandBuffer` are enacted until its `Playback` method is called on the main thread.

After playback of an ECB, attempting to record more changes throws an exception.

Like a native container, an ECB has a job safety handle. While a job that uses an ECB is scheduled but not yet completed, the safety checks will throw an exception if if the main thread tries to:

- Access the ECB (calls its `AddComponent`, `Playback`, `Dispose`, or other methods)
- Schedule another job that accesses the same ECB (unless this new job depends upon the already scheduled job).

## ECB use in a single-threaded job

[Structural changes](sync_points.md#structural-changes) cannot be performed in a job, so an ECB is very useful for deferring structural changes until the job is completed. For example:

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_single_threaded)]

## ECB use in a parallel job

When recording in a parallel job, you need an `EntityCommandBuffer.ParallelWriter`, which allows for thread-safe concurrent recording to an ECB:

```csharp
EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

// Methods of this writer record commands to 
// the ECB in a thread-safe way.
EntityCommandBuffer.ParallelWriter parallelEcb = ecb.AsParallelWriter();
```

> [!NOTE]
> Be clear that only *recording* needs to be safe for concurrency: *playback* is always just single-threaded on the main thread.

Because recording of the commands is split across threads, the order of recorded commands depends upon the happenstance of scheduling and thus is non-deterministic. Indeterminate order of recording cannot be avoided, but we can make the *playback* order of the commands deterministic:

1. Each command records a 'sort key' int (passed as the first argument to each command method).
1. Upon playback, the commands are sorted by their sort keys before the commands are enacted.

As long as the recorded sort keys are independent from the happenstance of scheduling, the sorting makes the playback order deterministic.

In a parallel job, the sort key you need for each entity is a number that has a fixed and unique association with that entity in the job's query. The `entityInQueryIndex` value provided in a parallel job meets those criteria: in the list of chunks matching the job's query...

- the first entity of the first chunk has `entityInQueryIndex` 0
- the second entity of the first chunk has `entityInQueryIndex`  1
- the first entity of the *second* chunk has an `entityInQueryIndex` which is the *count* of the *first* chunk
- the first entity of the *third* chunk has an `entityInQueryIndex` which is the sum of the counts of the first two chunks
- ...and so forth.

> [!NOTE]
> Determinism isn't always essential, but code which produces deterministic results can be much easier to debug. There are also networking scenarios which require consistent results across different machines. Understand however that determinism can have a cost, so accepting indeterminism may be appropriate in some projects.

Example use:

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_multi_threaded)]

> [!NOTE]
> Note that the lambda parameter must be called `entityInQueryIndex`. Otherwise, `Entities.ForEach` doesn't know what int you're talking about.

> [!WARNING]
> For a regular (non-parallel) ECB, it's OK to use the same ECB in multiple jobs as long as those jobs don't overlap in scheduling. For a parallel ECB, however, this can lead to unexpected (and potentially undesirable) sort orders of the commands in playback...*unless* the sort keys used in each job fall in different ranges. Recording a set of commands across several ECB's has very little overhead compared to recording the same set of commands to a single ECB, so it's generally best to simply give each job its own ECB, especially for parallel jobs.

## Multi-playback

The `Playback` method throws an exception if called more than once unless the ECB is created with the `PlaybackPolicy.MultiPlayback` option:

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_multi_playback)]

The main use case for multi-playback is repeatedly spawning a set of entities: after using an ECB to create and configure a set of new entities, you can repeat playback to respawn another matching set of entities.

After an ECB's first playback, attempting to record additional commands throws an exception.

## ECB use on the main thread

You sometimes might want to record ECB commands on the main thread because:

- You want to delay your changes.
- You want to play back a set of changes multiple times.
- It can be more efficient to play back many changes in one consolidated place rather than interspersing the changes across different parts of the frame.

Regarding this last point, keep in mind that every structural change operation triggers a [sync point](sync_points.md), meaning the operation must wait for some or all currently scheduled jobs to complete. If you consolidate your structural changes with ECB's, your frame can have fewer sync points.

## EntityCommandBufferSystem

Rather than manually play back and dispose a command buffer yourself, you can have an [EntityCommandBufferSystem](xref:Unity.Entities.EntityCommandBufferSystem) do those things for you by following these steps:

1. Get the instance of the ECB system which you want to do the playback.
2. Create an ECB *via* the system.
3. Schedule a job that will write commands to the ECB.
4. Register the scheduled job to be completed by the system.

For example:

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_from_ecbsystem)]

> [!NOTE]
> You should *not* manually play back and dispose an ECB created by an ECB system. The ECB system will do both for you.

In each update, an `EntityCommandBufferSystem` will:

1. Complete all registered jobs (thereby ensuring that they have finished their recording).
1. Playback all ECB's created *via* the system (in the same order they were created).
1. Dispose of the ECB's.

### The standard ECB systems

The default World is automatically given five ECB systems:

- BeginInitializationEntityCommandBufferSystem
- EndInitializationEntityCommandBufferSystem
- BeginSimulationEntityCommandBufferSystem
- EndSimulationEntityCommandBufferSystem
- BeginPresentationEntityCommandBufferSystem

These update at the begin and end of the three standard system groups. See [Default System Groups](system_update_order.md).

> [!NOTE]
> Because structural changes cannot happen in the frame after the rendering data has been handed off to the renderer, there is no `EndPresentationEntityCommandBufferSystem` at the end of the frame. `BeginInitializationEntityCommandBufferSystem` at the start of the frame should suit most of the same purposes: after all, the end of one frame is the beginning of the next frame.

These standard five ECB systems should suffice for most purposes, but you can create your own ECB systems if necessary:

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_define_ecbsystem)]

## Deferred entities

The ECB methods `CreateEntity` and `Instantiate` record commands to create entities. Because these methods just record commands instead of immediately creating entities, they return `Entity` values with negative indexes representing placeholder entities that do not yet exist. These placeholder `Entity` values are only meaningful in recorded commands of the same ECB.

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_deferred_entities)]

Values recorded in an `AddComponent`, `SetComponent`, or `SetBuffer` command might have `Entity` fields. In playback, any placeholder `Entity` values in these components or buffers will be remapped to the corresponding actual entities.

[!code-cs[conversion](../DocCodeSamples.Tests/EntityCommandBuffers.cs#ecb_deferred_remapping)]
