# Job dependencies

Unity analyzes the data dependencies of each system based on the ECS components that the system reads and writes. Jobs scheduled by systems usually depend on previously scheduled system jobs based on the components they read and write. If one system schedules a job that reads a component, and a later system schedules a job that writes that component, the latter job depends on the former. To prevent [race conditions](https://en.wikipedia.org/wiki/Race_condition), the job scheduler makes sure that all the jobs a system depends on have finished before it runs that system's jobs. 

## Dependency property overview

The update order and read/write access influences a job's dependencies. At the beginning of a system's execution, Unity calculates the initial value of a system's [`Dependency`](xref:Unity.Entities.SystemBase.Dependency) property by combining the handles to the dependent systems' jobs. This way, you can schedule jobs that wait for the correct dependencies. As you schedule those jobs, Unity combines their handles and stores them into the same system's `Dependency` property, extending the dependency chain to include these jobs. This way, the next systems to be executed know which jobs were scheduled by this one. 

Unity calculates the initial `Dependency` property by combining the `Dependency` handles of previously executed systems that wrote to the components that the system needs to read or write to. It also combines the system handles that read the components that the system needs to write to. For more information, refer to the [`SystemState` API documentation](xref:Unity.Entities.SystemState).

>[!NOTE]
>Because this system dependency approach works at a system level, it can result in jobs waiting for other jobs to access components that the original jobs don't need. This is a known issue which Unity is exploring ways to mitigate.
   
The following diagram illustrates an example of a job waiting for an unneeded dependency. Green arrows represent the jobs and dependencies explicitly scheduled in each system. Red arrows represent the job dependencies generated when scheduling using the `Dependency` property (the default `Schedule()` method dependency). Finally, dashed-borders represent read-only jobs.

![Job dependency diagram depicting one system writing to two jobs, and another system reading one of the jobs.](images/job-dependencies.png)

`System1` schedules two jobs: one that writes to `ComponentA`, and another that writes to `ComponentB`. They were scheduled using the default chaining approach, so by the end of `System1` execution its `Dependency` property will contain the `Write B` job handle, which depends on the `Write A` job. Later, `System2` schedules a job that reads `ComponentA`. The jobs in `System2` have to wait for both jobs scheduled by `System1` to complete, even if `System2` doesn't need to access `ComponentB`.

The `Read A` job is waiting for the `Write B` job needlessly. To get around this unneeded dependency, you could make `System1` only schedule the `Write B` job, and then `System2` schedule both `Write A` and `Read A` jobs.

## `Dependency` property

A system's [`Dependency`](xref:Unity.Entities.SystemBase.Dependency) property is a [`JobHandle`](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.html) that represents the ECS-related dependencies of the system. Before [`OnUpdate()`](xref:Unity.Entities.SystemBase.OnUpdate*), the `Dependency` property reflects the incoming dependencies that the system has on prior jobs. By default, the system updates the `Dependency` property based on the components that each job reads and writes as you schedule jobs in a system.

## Override the default dependency structure

To override the default dependency structure, use the `Schedule` method in jobs that inherit from [`IJobEntity`](xref:Unity.Entities.IJobEntity). You can use `Schedule` implicitly, or explicitly, but when you use it explicitly, ECS doesn't automatically combine the job handles with the system's `Dependency` property. You must combine them manually when required. 

The `Dependency` property doesn't track the dependencies that a job might have on data passed through a [`NativeArray`](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeArray_1.html) or other similar containers. If you write a `NativeArray` in one job, and read that array in another, you must manually add the `JobHandle` of the first job as a dependency of the second. You can use [`JobHandle.CombineDependencies`](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.CombineDependencies.html) to do this.

## Additional resources

* [JobHandle and dependencies](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.CombineDependencies.html)
* [Unity's job system](xref:um-job-system)
