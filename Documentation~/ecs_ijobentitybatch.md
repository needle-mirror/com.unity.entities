---
uid: ecs-ijobentitybatch
---

# Using Entity Batch jobs

Implement [IJobEntityBatch] or [IJobEntityBatchWithIndex] inside a system to iterate through your data in batches of entities. 

When you schedule an [IJobEntityBatch] job in the [OnUpdate] function of a system, the system identifies the chunks that should be passed to the job using the entity query you pass to the schedule function. The job invokes your `Execute` function once for each batch of entities in those chunks. By default, the batch size is a full chunk, but you can set the batch size to be some fraction of a chunk when scheduling the job. No matter the batch size, the entities in a given batch are always stored in the same chunk. In your job’s `Execute` function, you can iterate over the data inside each batch, entity by entity. 

Use  [IJobEntityBatchWithIndex] when you need an index value for all entities across the set of batches. Otherwise, [IJobEntityBatch] is more efficient since it doesn’t need to calculate these indices.

To implement a batch job:

1. [Query for data with an EntityQuery] to identify the entities that you want to process.

2. [Define the job struct] using either [IJobEntityBatch]  or [IJobEntityBatchWithIndex].

3. [Declare the data your job accesses]. On the job structure, include fields for ComponentTypeHandle objects that identify the types of components the job must directly access. Also, specify whether the job reads or writes to those components. You can also include fields that identify data you want to look up for entities that aren’t part of the query, as well as fields for non-entity data.

4. [Write the Execute function] of the job struct to transform your data. Get the NativeArray instances for the components the job reads or writes and then iterate over the current batch to perform the desired work.

5. [Schedule the job] in the system OnUpdate function, passing the entity query identifying the entities to process to the schedule function.

> [!NOTE]
> Iterating with [IJobEntityBatch]  or [IJobEntityBatchWithIndex] is more complicated and requires more code setup than using Entities.ForEach, and should only be used when necessary or more efficient.

For more information, the [ECS samples repository] contains a simple HelloCube example that demonstrates how to use IJobEntityBatch.

> [!NOTE]
> [IJobEntityBatch] supersedes [IJobChunk]. The primary differences are that you can schedule an [IJobEntityBatch] to iterate over smaller batches of entities than a full chunk and that you use the variant [IJobEntityBatchWithIndex] if you need an job-wide index for the entities in each batch. 

<a name="write-the-query" id="write-the-query"></a>
## Query for data with an EntityQuery

An [EntityQuery] defines the set of component types that an [EntityArchetype] must contain for the system to process its associated chunks and entities. An archetype can have additional components, but it must have at least those that the query defines. You can also exclude archetypes that contain specific types of components. 

Pass the query that selects the entities your job should process to the schedule method that you use to schedule the job.

See [Using an EntityQuery to query data] for information about defining queries.

> [!NOTE]
> Do not include completely optional components in the [EntityQuery]. To handle optional components, use the [ArchetypeChunk.Has] method inside [IJobEntityBatch.Execute] to determine whether the current [ArchetypeChunk] has the optional component or not. Because all entities in the same batch have the same components, you only need to check whether an optional component exists once per batch, not once per entity.

<a name="define-the-job-struct" id="define-the-job-struct"></a>
## Define the job struct

A job struct consists of an [Execute] function that does the work to be performed and fields that declare the data used by the `Execute` function.

A typical [IJobEntityBatch] job struct looks like:

[!code-cs[typical-struct](../DocCodeSamples.Tests/IJobEntityBatchExamples.cs#typical-struct)]

This example accesses the data for two components of an entity, VelocityVector and [Translation], and calculates a new translation based on the time elapsed since the last update.

### IJobEntityBatch versus IJobEntityBatchWithIndex

The only difference between [IJobEntityBatch] and [IJobEntityBatchWithIndex] is that [IJobEntityBatchWithIndex] passes an `indexOfFirstEntityInQuery` parameter when it invokes the Execute function on a batch. This parameter is the index of the first entity in the current batch in the list of all entities selected by the entity query. 

Use  [IJobEntityBatchWithIndex] when you need an individual index for each entity. For example, if you calculate a unique result for each entity, you could use this index to write each result to a different element of a [native array]. If you don’t use the `indexOfFirstEntityInQuery` value, use [IJobEntityBatch] instead, to avoid the overhead of calculating the index values.

> [!NOTE]
> When you are adding commands to an [EntityCommandBuffer.ParallelWriter] , you can use the `batchIndex` parameter as the `sortKey` argument of the command buffer functions. You do not need to use [IJobEntityBatchWithIndex] just to get a unique sort key for each entity. The `batchIndex` parameter available from both job types works for this purpose.

<a name="declare-data" id="declare-data"></a>
### Declare the data your job accesses

The fields in your job struct declare the data available to your Execute function. These fields fall into four general categories:

* [ComponentTypeHandle] fields -- component handle fields allow your Execute function to access the entity components and buffers stored in the current chunk. See [Accessing entity component and buffer data].

* [ComponentDataFromEntity], [BufferFromEntity] fields -- these "data from entity" fields allow your  Execute function to look up data for any entity no matter where it is stored. (This type of random access is the least efficient way to access data and should only be used when necessary.) See [Looking up data for other entities].

* Other fields -- you can declare other fields for your struct as needed. You can set the value of such fields each time you schedule the job. See [Accessing other data].

* Output fields -- in addition to updating writable entity components or buffers in a job, you can also write to [native container] fields declared for the job struct. Such fields must be a native container, such as a [NativeArray]; you cannot use other data types.

<a name="access-components" id="access-components"></a>
#### Accessing entity component and buffer data

Accessing data stored in a component of one of the entities in the query is three-step process:

__First__, you must define a [ComponentTypeHandle] field on the job struct, setting T to the data type of the component. For example:

[!code-cs[component-handle](../DocCodeSamples.Tests/IJobEntityBatchExamples.cs#component-handle)]

__Next__, you use this handle field inside the job’s `Execute` method to access the array containing the data for that type component (as a [NativeArray]). This array contains an element for every entity in a batch:

[!code-cs[component-array](../DocCodeSamples.Tests/IJobEntityBatchExamples.cs#component-array)]

__Finally__, when you schedule the job (in the system’s [OnUpdate] method, you assign a value to the type handle field using the [ComponentSystemBase.GetComponentTypeHandle] function:

[!code-cs[component-set-handle](../DocCodeSamples.Tests/IJobEntityBatchExamples.cs#component-set-handle)]

Always set the component handle fields of a job every time you schedule the job. Do not cache a type handle and use it later.

Each array of component data in a batch is aligned such that a given index corresponds to the same entity in all arrays. In other words, if your job uses two components of an entity, use the same array index in both data arrays to access data for the same entity.  

You can use [ComponentTypeHandle] variables to access component types that you do not include in the [EntityQuery]. However, you must check to make sure that the current batch contains the component before you try to access it. Use the [Has] function to check whether the current batch contains a specific component type:

The [ComponentTypeHandle] fields are part of the ECS job safety system that prevents race conditions when reading and writing of data in jobs. Always set the `isReadOnly` argument of the [GetComponentTypeHandle] function to accurately reflect how the component is accessed in a job. 

<a name="look-up-data" id="look-up-data"></a>
#### Looking up data for other entities

Accessing component data through an [EntityQuery] and an [IJobEntityBatch] job (or [Entities.ForEach]) is almost always the most efficient way to access your data. However, there are often cases where you need to look up data in a random-access fashion, for example, when one entity depends on data in another. To perform this type of data lookup, you must pass a different type of handle to your job through the job struct:

[ComponentDataFromEntity] -- access the component of any entity with that component type

[BufferFromEntity] -- access a buffer of any entity with that buffer type

These types provide an array-like interface to components and buffers, indexed by [Entity] object. In addition to being relatively inefficient because of the random data access, looking up data this way can also increase the chances that you run into the safeguards erected by the job safety system. For example, if you try to set the transform of one entity based on the transform of another entity, the job safety system cannot tell if this is safe, since you have access to all transforms through the [ComponentDataFromEntity] object. You could be writing to the same data you are reading and so creating a race condition. 

To use [ComponentDataFromEntity] and [BufferFromEntity], declare a field of type [ComponentDataFromEntity] or [BufferFromEntity] on the job struct and set the value of the field before scheduling the job.

For more information, see [Looking up data].

<a name="access-other-data" id="access-other-data"></a>
#### Accessing other data

If you need other information when executing a job, you can define a field on the job struct and then access the field inside the `Execute` method. You can only set the value when scheduling the job and that value remains the same for all batches.

For example, if you are updating moving objects, you most likely need to pass in the time elapsed since the previous update. To do this, you could define a field named `DeltaTime`, set its value in `OnUpdate` and use that value in the job `Execute` function. At each frame, you would calculate and assign a new value to your `DeltaTime` field before scheduling the job for the new frame.

<a name="write-execute-function" id="write-execute-function"></a>
### Write the Execute function

Write the `Execute` function of your job struct to transform your data from its input state to the desired output state. 

The signature of the [IJobEntityBatch.Execute] method is:

```cs
void Execute(ArchetypeChunk batchInChunk, int batchIndex)
```
And for [IJobEntityBatchWithIndex.Execute], the signature is:

```cs
void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
```

#### The batchInChunk parameter

The `batchInChunk` parameter provides the [ArchetypeChunk] instance that contains the entities and components for this iteration of the job. Because a chunk can only contain a single archetype, all of the entities in a chunk have the same set of components. By default, this object includes all the entities in a single chunk; however, if you schedule the job with [ScheduleParallel], you can specify that a batch contains only a fraction of the number of entities in the chunk. 

Use the `batchInChunk` parameter to get the [NativeArray] instances you need to access the component data. (You must also declare a field with the corresponding component type handle — and set that field when scheduling the job.)

#### The batchIndex parameter

The `batchIndex` parameter is the index of the current batch in the list of all batches created for the current job. The batches in a job are not necessarily processed in the indexed order.

You can use the `batchIndex` value in situations where you have a native container with one element per batch to which you want to write a value computed in your `Execute` function. Use the `batchIndex` as the array index into this container.

If you use a parallel writing [entity command buffer], pass the `batchIndex` argument as the `sortKey` parameter to the command buffer functions. 

#### The indexOfFirstEntityInQuery parameter

An [IJobEntityBatchWithIndex] `Execute` function has an additional parameter named `indexofFirstEntityInQuery`. If you picture the entities selected by your query as a single list, `indexOfFirstEntityInQuery` would be the index into that list of the first entity in the current batch. The batches in a job are not necessarily processed in the indexed order. 

#### Optional components

If you have the [Any] filter in your entity query or have completely optional components that don’t appear in the query at all, you can use the [ArchetypeChunk.Has] function to test whether the current chunk contains one of those components before you use it:

[!code-cs[batch-has-component](../DocCodeSamples.Tests/IJobEntityBatchExamples.cs#batch-has-component)]

<a name="schedule-the-job" id="schedule-the-job"></a>
## Schedule the job

To run an [IJobEntityBatch] job, you must create an instance of your job struct, set the struct fields, and then schedule the job. When you do this in the [OnUpdate] function of a [SystemBase] implementation, the system schedules the job to run every frame.

[!code-cs[schedule-job](../DocCodeSamples.Tests/IJobEntityBatchExamples.cs#schedule-job)]

When you call the [GetComponentTypeHandle] function to set your component type variables, make sure that you set the `isReadOnly` parameter to true for components that the job reads, but doesn’t write. Setting these parameters correctly can have a significant impact on how efficiently the ECS framework can schedule your jobs. These access mode settings must match their equivalents in both the struct definition, and the [EntityQuery].

Do not cache the return value of [GetComponentTypeHandle] in a system class variable. You must call the function every time the system runs, and pass the updated value to the job.

### Scheduling options

You can control how a job executes by choosing the appropriate function when you schedule the job:

* [Run] -- executes the job immediately on the current (main) thread. Run also completes any scheduled jobs that the current job depends upon. Batch size is always 1 (an entire chunk).

* [Schedule] -- schedules the job to run on a worker thread after any scheduled jobs that the current job depends upon. The jobs execute function is called once for each chunk selected by the entity query. Chunks are processed in sequence. Batch size is always 1.

* [ScheduleParallel] -- Like Schedule, except that you can specify a batch size and the batches are processed in parallel (assuming worker threads are available) rather than sequentially.

### Setting the batch size

To set a batch size, use the [ScheduleParallel] method to schedule the job and set the `batchesPerChunk` parameter to a positive integer. Use a value of 1 to set the batch size to a full chunk. 

Each chunk selected by the query used to schedule the job is divided into the number of batches specified by `batchesPerChunk`. Each batch from the same chunk contains approximately the same number of entities; however, batches from different chunks may contain very different numbers of entities. The largest batch size is 1, which means that all the entities in each chunk are processed together in one call to your `Execute` function. Entities from different chunks can never be included in the same batch.

> [!NOTE]
> Typically, it is most efficient to use a `batchesPerChunk` setting of 1 to process all the entities in a chunk in a single call to `Execute`. However, that is not always the case. For example, if you have a small number of entities and an expensive algorithm performed by your `Execute` function, you could gain additional benefit from parallel processing by using smaller batches of entities.  

## Skipping chunks with unchanged entities

If you only need to update entities when a component value has changed, you can add that component type to the change filter of the [EntityQuery] that selects the entities and chunks for the job. For example, if you have a system that reads two components and only needs to update a third when one of the first two has changed, you can use an [EntityQuery] as follows:

[!code-cs[filter-query](../DocCodeSamples.Tests/IJobEntityBatchExamples.cs#filter-query)]

The [EntityQuery] change filter supports up to two components. If you want to check more or you aren't using an [EntityQuery], you can make the check manually. To make this check, use the [ArchetypeChunk.DidChange] function to compare the chunk’s change version for the component to the system's [LastSystemVersion]. If this function returns false, you can skip the current chunk altogether because none of the components of that type have changed since the last time the system ran.

You must use a struct field to pass the [LastSystemVersion] from the system into the job, as follows:

[!code-cs[skip-unchanged-batches-job](../DocCodeSamples.Tests/IJobEntityBatchExamples.cs#skip-unchanged-batches-job)]

As with all the job struct fields, you must assign its value before you schedule the job:

[!code-cs[skip-unchanged-batches-system](../DocCodeSamples.Tests/IJobEntityBatchExamples.cs#skip-unchanged-batches-system)]

> [!NOTE]
> For efficiency, the change version applies to whole chunks not individual entities. If another job which has the ability to write to that type of component accesses a chunk, then ECS increments the change version for that component and the DidChange function returns true. ECS increments the change version even if the job that declares write access to a component does not actually change the component value. (This is one of the reasons you should always read-only when you are reading component data and not updating it.)


[Accessing entity component and buffer data]: #access-components
[Accessing other data]: #access-other-data
[Any]: xref:Unity.Entities.EntityQueryDesc.Any
[ArchetypeChunk.DidChange]: xref:Unity.Entities.ArchetypeChunk.DidChange*
[ArchetypeChunk.Has]: xref:Unity.Entities.ArchetypeChunk.Has*
[ArchetypeChunk]: xref:Unity.Entities.ArchetypeChunk
[BufferFromEntity]: xref:Unity.Entities.BufferFromEntity`1
[ComponentDataFromEntity]: xref:Unity.Entities.ComponentDataFromEntity`1
[ComponentTypeHandle]: xref:Unity.Entities.ComponentTypeHandle`1
[ComponentSystemBase.GetComponentTypeHandle]: xref:Unity.Entities.ComponentSystemBase.GetComponentTypeHandle*
[ComponentTypeHandle]: xref:Unity.Entities.ComponentTypeHandle`1
[Declare the data your job accesses]: #declare-data
[Define the job struct]: #define-the-job-struct
[ECS samples repository]: https://github.com/Unity-Technologies/EntityComponentSystemSamples
[Entities.ForEach]: xref:ecs-entities-foreach
[entity command buffer]: xref:ecs-entity-command-buffer
[EntityQuery]: xref:Unity.Entities.EntityQuery
[GetComponentTypeHandle]: xref:Unity.Entities.ComponentSystemBase.GetComponentTypeHandle*
[Has]: xref:Unity.Entities.ArchetypeChunk.Has*
[IJobEntityBatch.Execute]: xref:Unity.Entities.IJobEntityBatch.Execute*
[Execute]: xref:Unity.Entities.IJobEntityBatch.Execute*
[IJobEntityBatchWithIndex]: xref:Unity.Entities.IJobEntityBatchWithIndex
[IJobEntityBatchWithIndex.Execute]: xref:Unity.Entities.IJobEntityBatchWithIndex.Execute*
[IJobEntityBatch]: xref:Unity.Entities.IJobEntityBatch
[LastSystemVersion]: xref:Unity.Entities.ComponentSystemBase.LastSystemVersion
[Looking up data for other entities]: #look-up-data
[Looking up data]: xref:ecs-data-lookup
[native array]: xref:Unity.Collections.NativeArray`1
[native container]: xref:JobSystemNativeContainer
[NativeArray]: xref:Unity.Collections.NativeArray`1
[OnUpdate]: xref:Unity.Entities.SystemBase.OnUpdate*
[Query for data with an EntityQuery]: #write-the-query
[Run]: xref:Unity.Entities.JobEntityBatchExtensions.Run*
[Schedule the job]: #schedule-the-job
[ScheduleParallel]: xref:Unity.Entities.JobEntityBatchExtensions.ScheduleParallel*
[Schedule]: xref:Unity.Entities.JobEntityBatchExtensions.Schedule*
[SystemBase]: xref:Unity.Entities.SystemBase
[Translation]: xref:Unity.Transforms.Translation
[Using an EntityQuery to query data]: xref:ecs-entity-query
[Write the Execute function]: #write-execute-function
[IJobChunk]: xref:ecs-ijobchunk
[EntityArchetype]: xref:Unity.Entities.EntityArchetype
[Entity]: xref:Unity.Entities.Entity
