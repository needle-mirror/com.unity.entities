# Use the job system with Entities

The Entities package and Unity's DOTS architecture uses the [C# Job System](https://docs.unity3d.com/Manual/JobSystem.html) extensively. Whenever possible, you should use jobs in your system code. 

The [`SystemBase`](xref:Unity.Entities.SystemBase) class provides [`Entities.ForEach`](iterating-data-entities-foreach.md) and [`Job.WithCode`](scheduling-jobs-background-jobs.md) to implement your application's logic as multithreaded code. In more complex situations, you can use [`IJobChunk`](iterating-data-ijobchunk.md)'s `Schedule()` and `ScheduleParallel()` methods, to transform data outside the main thread. `Entities.ForEach` is the simplest to use and typically requires fewer lines of code to implement. 

ECS schedules jobs on the main thread in the order that your systems are in. When you schedule jobs, ECS keeps track of which jobs read and write which components. A job that reads a component is dependent on any prior scheduled job that writes to the same component and vice versa. The job scheduler uses job dependencies to determine which jobs it can run in parallel and which must run in sequence.  

For example, the following system updates positions:

```c#
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Transforms;
    
    public class MovementSpeedSystem : SystemBase
    {
        // OnUpdate runs on the main thread.
        protected override void OnUpdate()
        {
            Entities
                .ForEach((ref Translation position, in MovementSpeed speed) =>
                    {
                        float3 displacement = speed.Value * dt;
                        position = new Translation(){
                                Value = position.Value + displacement
                            };
                    })
                .ScheduleParallel();
        }
    }
```

For more information about systems, see [System concepts](concepts-systems.md).
