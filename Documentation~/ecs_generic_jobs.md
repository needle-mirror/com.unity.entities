---
uid: ecs-generic_jobs
---

# Generic jobs

In normal C#, we can use inheritance and interfaces to make a piece of code work with a range of types. For example:

```csharp
    // The method is not limited to just one kind of 
    // input but rather any type which implements IBlendable.
    void foo(IBlendable a) {...}
```

In HPC#, we can't use managed types or virtual method calls, so generics are our only option for making a piece of code operate on a range of types:

```csharp
    // This method can operate on any IBlendable struct (and can call the 
    // IBlendable methods) but requires no managed objects or virtual method calls.
    void foo<T>(T a) where T : struct, IBlendable {...}
```

Jobs must be written in HPC#, so for a job to operate upon a range of types, it must be generic:

```csharp
    [BurstCompile()]
    public struct BlendJob<T> : IJob
        where T : struct, IBlendable
    {
        public NativeReference<T> blendable;

        public void Execute() 
        {
            var val = blendable.Value;
            val.Blend();
            blendable.Value = val;
        }
    }
```

# Scheduling generic jobs from Bursted code

Scheduling a generic job from Burst-compiled code requires reflection data for the concrete specialization of the job. Unfortunately, this reflection is not automatically genererated for all concrete specializations, so in some cases you must register them manually:

```csharp
// This assembly attribute allows Burst-compiled 
// code in the same assembly to schedule the
// concrete specialization <int, float> for MyJob. */
[assembly: RegisterGenericJobType(typeof(MyJob<int, float>))]
```

Attempting to schedule a job for which the concrete specialization is not registered in an assembly throws an exception.

Which assembly a type is registered in doesn't matter, *e.g.* if a job type is registered only in assembly *Foo*, it can be scheduled fine in assembly *Bar*.

Redundantly registering the same concrete specialization more than once is not an error.

## Automatic registration of concrete job types

When you instantiate a concrete specialization of a generic job directly, the specialization is automatically registered in the assembly:

```csharp
// Registers specialization <int, float> for MyJob in the assembly.
var job = new MyJob<int, float>();
```

However, when instantiating a concrete specialization *indirectly*, it is *not* automatically registered...

```csharp
void makeJob<T>()
{
    new MyJob<T, float>().Schedule();   
}

void foo()
{
    makeJob<int>();    // does NOT register MyJob<int, float>
}
```

...*unless* the generic job is included in the signature as either the return type or an `out` param:

```csharp
MyJob<T, float> makeJob<T>()
{
    var j = new MyJob<T, float>()
    j.Schedule();   
    return j;
}

void foo()
{
    makeJob<int>();    // registers MyJob<int, float>
}
```

This indirect registration works through multiple levels of generic method calls:

```csharp
MyJob<T, float> makeJob<T>()
{
    var j = new MyJob<T, float>()
    j.Schedule();   
    return j;
}

void foo<T>()
{
    makeJob<T>();    
}

void bar()
{
    foo<int>();       // registers MyJob<int, float>
}
```

Another sometimes useful trick is to nest the generic job in another class or struct:

```csharp
struct BlendJobWrapper<T> where T : struct, IBlendable
{
    public T blendable;

    [BurstCompile()]
    public struct BlendJob : IJob
    {
        public T blendable;

        public void Execute() {...}
    }

    public JobHandle Schedule(JobHandle dep = new JobHandle())
    {
        return new BlendJob { blendable = blendable }.Schedule(dep);
    }
}
```

Above, if `BlendJobWrapper<foo>` is registered automatically or manually, then `BlendJob<foo>` is also effectively registered. A wrapper type around just one generic job doesn't really solve anything, but these wrapper types can allow for more elegant job creation and scheduling when multiple generic jobs are used in conjunction (as shown in the next section).

## Example: Jobified sorting

The `NativeSortExtension` class has methods for sorting, including this one that uses jobs to do the sorting:

```csharp
public unsafe static JobHandle Sort<T, U>(T* array, int length, U comp, JobHandle deps)
    where T : unmanaged
    where U : IComparer<T>
{
    if (length == 0)
        return inputDeps;
    
    var segmentSortJob = new SegmentSort<T, U> { Data = array, Comp = comp, Length = length, SegmentWidth = 1024 };
    var segmentSortMergeJob = new SegmentSortMerge<T, U> { Data = array, Comp = comp, Length = length, SegmentWidth = 1024 };

    var segmentCount = (length + 1023) / 1024;
    var workerSegmentCount = segmentCount / math.max(1, JobsUtility.MaxJobThreadCount);
    var handle = segmentSortJob.Schedule(segmentCount, workerSegmentCount, deps);
    return segmentSortMergeJob.Schedule(segmentSortJobHandle);
}
```

Note that the sorting is split across two jobs: the first splits the array into subsections and sorts them individually (in parallel); the second waits for the first and then merges these sorted subsections into a final sorted result.

As currently defined, however, the method will *not* automatically register concrete specializations of the two generic jobs (`SegmentSort` and `SegmentSortMerge`) because neither type is used as the return type or as an `out` parameter of the method.

An ugly solution would be to make both jobs into `out` parameters::

```csharp
public unsafe static JobHandle Sort<T, U>(T* array, int length, U comp, JobHandle deps
        out SegmentSort<T, U> segmentSortJob, out SegmentSortMerge<T, U> segmentSortMergeJob)
    where T : unmanaged
    where U : IComparer<T>
{
    if (length == 0)
        return inputDeps;
    
    segmentSortJob = new SegmentSort<T, U> { Data = array, Comp = comp, Length = length, SegmentWidth = 1024 };
    segmentSortMergeJob = new SegmentSortMerge<T, U> { Data = array, Comp = comp, Length = length, SegmentWidth = 1024 };

    var segmentCount = (length + 1023) / 1024;
    var workerSegmentCount = segmentCount / math.max(1, JobsUtility.MaxJobThreadCount);
    var handle = segmentSortJob.Schedule(segmentCount, workerSegmentCount, deps);
    return segmentSortMergeJob.Schedule(segmentSortJobHandle);
}
```

This solves the registration problem, but users would have to pass `out` arguments to get two job structs they probably don't want.

The arguably more elegant solution is to wrap both job types together in a wrapper type:

```csharp
unsafe struct SortJob<T, U> :
    where T : unamanged
    where U : IComparer<T>
{
    public T* data;
    public U comparer;
    public int length;

    unsafe struct SegmentSort : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction]
        public T* data;
        public U comp;
        public int length;
        public int segmentWidth;

        public void Execute(int index) {...}
    }

    unsafe struct SegmentSortMerge : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        public T* data;
        public U comp;
        public int length;
        public int segmentWidth;

        public void Execute() {...}
    }

    public JobHandle Schedule(JobHandle dep = new JobHandle())
    {
        if (length == 0)
            return inputDeps;
    
        var segmentSortJob = new SegmentSort<T, U> { Data = array, Comp = comp, Length = length, SegmentWidth = 1024 };
        var segmentSortMergeJob = new SegmentSortMerge<T, U> { Data = array, Comp = comp, Length = length, SegmentWidth = 1024 };

        var segmentCount = (length + 1023) / 1024;
        var workerSegmentCount = segmentCount / math.max(1, JobsUtility.MaxJobThreadCount);
        var handle = segmentSortJob.Schedule(segmentCount, workerSegmentCount, deps);
        return segmentSortMergeJob.Schedule(segmentSortJobHandle);
    }
}
```

In this arrangement, rather than call a `Sort()` method, users create an instance of `SortJob` and call its `Schedule()` method. Just by making a concrete instantiation of `SortJob`, users are also automatically registering the needed concrete specializations of `SegmentSort` and `SegmentSortMerge`.

So this pattern of nesting generic jobs enables a convenient API that schedules related sets of generic jobs together.

## Why doesn't the compiler always register the needed reflection data automatically?

The compiler could figure it out, but it would slow down compilation considerably. More details in [this forum post](https://forum.unity.com/threads/will-registergenericjobtype-be-required-for-all-generic-jobs-going-forward.974187/#post-6384873).


