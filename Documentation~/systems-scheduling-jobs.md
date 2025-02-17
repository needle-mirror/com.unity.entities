# Job system in Entities introduction

The Entities package uses the [job system](xref:um-job-system) to create multithreaded code. If you're concerned about performance, use jobs whenever possible.

The following techniques are available, depending on the type of access you require:

* **Job scheduling**: For convenient job scheduling, use [`IJobEntity`](xref:Unity.Entities.IJobEntity). For more information, refer to [Iterate over component data in multiple systems](iterating-data-ijobentity.md).
* **Manual scheduling outside the main thread**: Use [`IJobChunk`](iterating-data-ijobchunk.md)'s `Schedule()` and `ScheduleParallel()` methods to transform data outside the main thread. 

When you schedule jobs, ECS keeps track of which systems read and write which components. Later systems' `Dependency` property include the job handles of earlier systems' scheduled jobs, in cases where the set of components' read and write operations overlap. For more information, refer to [Job dependencies](scheduling-jobs-dependencies.md).

For main thread access outside of the job system, use a `foreach` statement over the `Query` objects in [`SystemAPI`](xref:Unity.Entities.SystemAPI). For more information, refer to [SystemAPI overview](systems-systemapi.md).

## Additional resources

* [Job system introduction](xref:um-job-system)
* [Iterate over chunks of component data](iterating-data-ijobchunk.md)
* [Iterate over component data in multiple systems](iterating-data-ijobentity.md)
