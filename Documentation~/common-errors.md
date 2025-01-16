# Common error messages
This section lists some causes and solutions for common errors coming from [the safety system](concepts-safety.md).

## Errors related to the Dependency property
The [`Dependency`](xref:Unity.Entities.SystemState.Dependency) property is used to record the handles of all jobs a system is scheduling. This is important so that later systems can pass the right dependencies into their jobs to avoid data races. The safety system makes a best effort attempt to detect cases where a job isn't assigned to the `Dependency` property. In these cases, the following error is produced:

```
The system <SYSTEM NAME> reads <COMPONENT NAME> via <JOB NAME> but that type was not assigned to the Dependency property. To ensure correct behavior of other systems, the job or a dependency must be assigned to the Dependency property before returning from the OnUpdate method.
```

Common causes for this error are:
 - Your code doesn't assign the `JobHandle`s of the jobs it's scheduling to the `Dependency` property.
 - The safety system could not determine which component types your system used. This can happen when you use [`EntityQueries`](xref:Unity.Entities.EntityQuery) that weren't created with [`GetEntityQuery`](xref:Unity.Entities.SystemState.GetEntityQuery*). Your system must not use any query that it didn't create itself using `GetEntityQuery`. In particular, do not create entity queries using the `EntityManager`.
 - An exception elsewhere in the system update is causing `OnUpdate` to terminate before the `Dependency` property is assigned. In this case, the real exception is logged to the console, meaning that the previous error is misleading and can be ignored.

## Errors related to parallel writing

You might sometimes encounter errors such as:

```
InvalidOperationException: <JOB FIELD> is not declared [ReadOnly] in a IJobParallelFor job. The container does not support parallel writing. Please use a more suitable container type.
```

This error means that a parallel job is accessing something in parallel in a way that might be unsafe. Parallel jobs in the Entities package include [`IJobChunk`](xref:Unity.Entities.IJobChunk) and [`IJobEntity`](xref:Unity.Entities.IJobEntity), and [`ParallelFor` and `ParallelForTransform`](xref:um-job-system-jobs) in the core engine.

It's generally unsafe to write the same data concurrently from multiple threads. However, sometimes you might know through domain specific knowledge that your concurrent writes are safe. This is for example the case if you know that no two threads will ever access the same value in parallel.

Common causes for this error are:
 - You are actually only reading from this data. In this case, adding the [`ReadOnly`](https://docs.unity3d.com/ScriptReference/Unity.Collections.ReadOnly.html) attribute is the correct fix. 
 - You are using a native container in a parallel job without the [`ReadOnly`](https://docs.unity3d.com/ScriptReference/Unity.Collections.ReadOnly.html) attribute.
 - You are using a [`ComponentLookup`](xref:Unity.Entities.ComponentLookup`1) in a parallel job without the [`ReadOnly`](https://docs.unity3d.com/ScriptReference/Unity.Collections.ReadOnly.html) attribute.

If you can guarantee that your access is safe, you can use the [`NativeDisableParallelForRestriction`](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeDisableParallelForRestrictionAttribute.html) attribute to silence the error.

## Errors related to missing dependencies on previously scheduled jobs

When the safety system detects that a job is missing a dependency on another job, it produces an error such as this:

```
InvalidOperationException: The previously scheduled job <JOB NAME> writes to the <OBJECT TYPE> <FIELD IN JOB>. You are trying to schedule a new job <OTHER JOB NAME>, which writes to the same <OBJECT TYPE> (via <FIELD IN OTHER JOB>). To guarantee safety, you must include <JOB NAME> as a dependency of the newly scheduled job.
```

The safety system will flag all cases where the same component type is used in multiple jobs that could run concurrently. Sometimes, it's still safe for these jobs to be executing in parallel as long as you can guarantee that the entities the concurrent jobs operate on don't overlap.

A common cause for this error is that you are scheduling multiple copies of the same job, each on a query with different shared component values. This is safe, as queries with different values for the same shared component type never overlap. You can use the [`NativeDisableContainerSafetyRestriction`](https://docs.unity3d.com/ScriptReference/Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestrictionAttribute.html) attribute on affected fields to disable this error.

## Errors related to safety handles

When the safety system detects that a job is reading a resource that it doesn't have access to, an error like the following might be produced:

```
InvalidOperationException: The <JOB FIELD> has been declared as [WriteOnly] in the job, but you are reading from it.
```
In practice, `[WriteOnly]` resources are rare. A more likely cause for this error is that the read in question is occurring from a job that was launched from within another job. Launching jobs from jobs is not currently supported, and the resulting safety handles won't be set up correctly. This includes obscure cases like using `.job.Run()` from inside of an `Entities.ForEach().Run()` lambda function, which is itself implemented as a main-thread job. 

## Errors related to system type definitions

All system types and `IJobEntity` implementations must have the `partial` keyword, so that the source generators can extend the types with additional generated methods. If you forget to add the `partial` keyword, it might lead to the following compiler error:

```
error CS0101: The namespace <NAMESPACE> already contains a definition for <SYSTEM/JOB TYPE>
```

Also note that a system deriving from the managed `SystemBase` interface must be a `class`,
while an unmanaged system implementing the `ISystem` interface must be a `struct`.
