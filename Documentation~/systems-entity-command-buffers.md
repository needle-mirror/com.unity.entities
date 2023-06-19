---
uid: systems-entity-command-buffers
---

# Entity command buffer overview

An entity command buffer (ECB) stores a queue of thread-safe commands which you can add to and later play back. You can use an ECB to schedule [structural changes](concepts-structural-changes.md) from jobs and perform changes on the main thread after the jobs complete. You can also use ECBs on the main thread to delay changes, or play back a set of changes multiple times.

The [methods in `EntityCommandBuffer`](xref:Unity.Entities.EntityCommandBuffer) record commands, which mirror methods available in [`EntityManager`](xref:Unity.Entities.EntityManager). For example:

* `CreateEntity(EntityArchetype)`: Registers a command that creates a new entity with the specified archetype.
* `DestroyEntity(Entity)`: Registers a command that destroys the entity.
* `SetComponent<T>(Entity, T)`: Registers a command that sets the value for a component of type `T` on the entity.
* `AddComponent<T>(Entity)`: Registers a command that adds a component of type `T` to the entity.
* `RemoveComponent<T>(EntityQuery)`: Registers a command that removes a component of type `T` from all entities that match the query.

## Entity command buffer safety

`EntityCommandBuffer` has a job safety handle, similar to a [native container](xref:JobSystemNativeContainer). This safety is only available in the Unity Editor, and not in player builds. The safety checks throw an exception if you try to do any of the following on an incomplete scheduled job that uses an ECB:

* Access the `EntityCommandBuffer` through its `AddComponent`, `Playback`, `Dispose`, or other methods.
* Schedule another job that accesses the same `EntityCommandBuffer`, unless the new job [depends on](xref:JobSystemJobDependencies) the already scheduled job.

>[!NOTE] 
>It’s best practice to use a separate ECB for each distinct job. This is because if you reuse an ECB in consecutive jobs, the jobs might use an overlapping set of sort keys (such as if both use `ChunkIndexInQuery`), and the commands that the jobs record might be interleaved.

## Additional resources

* [Use an entity command buffer](systems-entity-command-buffer-use.md)
* [Entity command buffer playback](systems-entity-command-buffer-playback.md)