# Manage structural changes introduction

The entity component system (ECS) has the following ways to manage [structural changes](concepts-structural-changes.md) within your project:

* Use [entity command buffers (ECB)](systems-entity-command-buffers.md) to defer data changes.
* Use [the methods in `EntityManager`](systems-entitymanager.md) to manage data changes on the main thread.
* Use [enableable components](#enabling-and-disabling-components) to enable and disable components if entity changes aren't frequent in your project.

## Entity command buffer and EntityManager comparison

The difference between using an ECB and the methods in `EntityManager` are as follows:

* If you want to queue up [structural changes](concepts-structural-changes.md) from a job, you must [use an ECB](systems-entity-command-buffers.md).
* If you want to perform structural changes on the main thread:
    * To have them happen instantly, use the [methods in `EntityManager`](systems-entitymanager.md).
    * To have them happen at a later point (such as after a job completes), use an ECB.

>[!IMPORTANT]
>The changes recorded in an ECB only are applied when [`Playback`](xref:Unity.Entities.EntityCommandBuffer.Playback*) is called on the main thread. If you try to record any further changes to the ECB after playback, then Unity throws an exception.

Passing an `EntityQuery` to an `EntityManager` method is the most efficient way to make structural changes. This is because the method can operate on whole chunks rather than individual entities.

However, the job system doesn't allow you to schedule or parallelize jobs that use `EntityManager`, or to execute them anywhere other than the main thread. instead, you can add commands to an [entity command buffer](systems-entity-command-buffers.md) from scheduled and parallelized jobs, because no structural changes occur until its corresponding `EntityCommandBufferSystem` plays back the `EntityCommandBuffer` on the main thread.

If you use an ECB, you can schedule playback to occur during pre-existing sync points in the frame, whereas `EntityManager` creates a new sync point every time it makes a structural change. The added overhead of using an `EntityCommandBuffer` might be worth it to avoid introducing a new sync point. You can avoid this overhead if the system using `EntityManager` is executed before or after an `EntityCommandBufferSystem` with queued commands. This merges these systems' sync points, provided no jobs are scheduled between these systems.

`EntityManager` is useful in some circumstances when you require a structural change to influence a system or job that runs later in the same frame, although you can often avoid the need for structural changes in the middle of a `SystemGroup` by controlling system update ordering. You can use `EntityManager` to ensure that the structural change and the system or job that relies on the change are separated by a pre-existing [sync point](performance-sync-points.md).

The best way to decide whether `EntityManager` or `EntityCommandBuffer` is right for any given structural change is to examine the [Systems window](editor-systems-window.md) and the [CPU Usage Timeline view](xref:um-profiler-cpu) in the Profiler window to understand what's happening on the main and worker threads at the time the change is required.

## Enabling and disabling components

[Enabling and disabling components](components-enableable.md) is faster than adding or removing components from entities, especially if changes are frequent. However, enableable components might affect the performance of jobs and systems that access the archetypes that contain them. If entity changes are infrequent in your project, or you want to optimize chunk fragmentation and CPU cache usage, then adding and removing components might be the preferable option.

For information, refer to [Manage structural changes with enableable components](structural-changes-enableable-components.md).

## Additional resources

* [Entity command buffers overview](systems-entity-command-buffers.md)
* [EntityManager overview](systems-entitymanager.md)
* [Manage structural changes with enableable components](structural-changes-enableable-components.md)
