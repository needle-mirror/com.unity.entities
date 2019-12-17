---
uid: sync-points
---

# Sync points
A synchronization point (sync point) is a point in the program execution that will wait for the completion of all jobs that have been scheduled so far. Sync points hence limit your ability to use all worker threads available in the job system for a period of time. You should generally aim to avoid sync points.

## Structural changes
Sync points are caused by operations that you cannot safely perform when there are any other jobs that operate on components. *Structural changes* to the data in the ECS are the primary cause of sync points. All of the following are structural changes:
 
 * Creating entities
 * Deleting entities
 * Adding components to an entity
 * Removing components from an entity
 * Changing the value of shared components

Broadly speaking, any operation that changes the archetype of an entity or causes the order of entities within a chunk to change is a structural change. These structural changes can only be performed on the main thread.

Structural changes not only require a sync point, but they also invalidate all direct references to any component data. This includes instances of [`DynamicBuffer<T>`](xref:Unity.Entities.DynamicBuffer``1) and the result of methods that provide direct access to the components such as [`ComponentSystemBase.GetComponentDataFromEntity`](xref:Unity.Entities.ComponentSystemBase.GetComponentDataFromEntity*).

## Avoiding sync points
You can use [entity command buffers](entity_command_buffer.md) (ECBs) to queue up structural changes instead of immediately performing them. Commands stored in an ECB can be played back at a later point during the frame. This reduces multiple sync points spread across the frame to a single sync point when the ECB is played back.