# Job extensions

The Unity C# job system lets you run code on multiple threads. The system provides scheduling, parallel processing, and multi-threaded safety. The job system is a core Unity module that provides the general purpose interfaces and classes to create and run jobs, whether or not you're using the Entities package. 

These interfaces include:

* [`IJob`](https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJob.html): Create a job that runs on any thread or core, which the job system scheduler determines.
* [`IJobParallelFor`](https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJobParallelFor.html): Create a job that can run on multiple threads in parallel, to process the elements of a [`NativeContainer`](https://docs.unity3d.com/Manual/JobSystemNativeContainer.html).
* [`IJobExtensions`](https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJobExtensions.html): Provides extension methods to run `IJob` jobs.
* [`IJobParallelForExtensions`](https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJobParallelForExtensions.html): Provides extension methods to run `IJobParallelFor` jobs.
* [`JobHandle`](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.html): A handle to access a scheduled job. You can also use `JobHandle` instances to specify dependencies between jobs.

For an overview of the jobs system see the [Job system](xref:JobSystem) documentation in the Unity User Manual.

The [Collections package](https://docs.unity3d.com/Packages/com.unity.collections@latest) extends the job system to support ECS. It contains:

* [`IJobParallelForDeferExtensions`](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/api/Unity.Jobs.IJobParallelForDeferExtensions.html)
* [`IJobFilter`](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/api/Unity.Jobs.IJobFilter.html)
* [`JobParallelIndexListExtensions`](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/api/Unity.Jobs.JobParallelIndexListExtensions.html)
* [`Job​Struct​Produce<T>`](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/api/Unity.Jobs.JobParallelIndexListExtensions.JobStructProduce-1.html)

