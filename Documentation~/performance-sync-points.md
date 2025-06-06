# Manage sync points

You can't make [structural changes](concepts-structural-changes.md) directly in a job because it might invalidate other jobs that are already scheduled, and creates a synchronization point (sync point).

A sync point is a point in program execution that waits on the main thread for the completion of all jobs that have been scheduled so far. Sync points limit your ability to use all worker threads available in the job system for a period of time. As such, you should aim to avoid sync points. 

Structural changes to the data in ECS are the primary cause of sync points. Sync points can also happen when you use `Run` to run a job, or when you use idiomatic `foreach` to iterate over component data. In both cases, Unity blocks the main thread and waits for all job dependencies to complete before the job scheduler executes the job synchronously on the main thread. 

Structural changes not only require a sync point, but they also invalidate all direct references to any component data. This includes instances of [`DynamicBuffer`](xref:Unity.Entities.DynamicBuffer`1) and the result of methods that provide direct access to the components such as [`ComponentSystemBase.GetComponentDataFromEntity`](xref:Unity.Entities.ComponentSystemBase.GetComponentDataFromEntity*).

## Use entity command buffers to queue structural changes

You can use [entity command buffers](systems-entity-command-buffers.md) to queue up structural changes instead of performing them immediately. You can play back commands stored in an entity command buffer at a later point during the frame. This combines all the structural changes into one, improving performance.

Each of the standard [`ComponentSystemGroup`](xref:Unity.Entities.ComponentSystemGroup) instances provide an [`EntityCommandBufferSystem`](xref:Unity.Entities.EntityCommandBuffer) as the first and last systems updated in the group. If you get an entity command buffer object from one of these standard systems, all structural changes happen at the same point in the frame, which results in one sync point. You can also use entity command buffers to record structural changes within a job, rather than only making structural changes on the main thread.

For a list of all the entity command buffer systems, refer to [Default EntityCommandBufferSystem systems](systems-entity-command-buffer-automatic-playback.md#default-entitycommandbuffersystem-systems).

If you can't use an entity command buffer for a task, group any systems that make structural changes together in the system execution order. Two systems that both make structural changes only create one sync point if they update sequentially, unless the first one also schedules jobs, in which case the second one will immediately sync on them.

For information on how to manage structural changes, refer to [Manage structural changes](optimize-structural-changes.md).

## Additional resources

* [Structural changes concepts](concepts-structural-changes.md)
* [Optimize structural changes](optimize-structural-changes.md)
* [Entity command buffers introduction](systems-entity-command-buffers.md)
