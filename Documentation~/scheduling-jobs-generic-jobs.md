# Generic jobs

In C# you can use inheritance and interfaces to make a piece of code work with a range of types. For example:

```csharp
    // The method is not limited to just one kind of 
    // input but rather any type which implements IBlendable.
    void foo(IBlendable a) {...}
```

In High Performance C# (HPC#), which the [Burst compiler](https://docs.unity3d.com/Packages/com.unity.burst@latest) uses, you can't use managed types or virtual method calls, so generics are the option to make a piece of code operate on a range of types:

```c#
    // This method can operate on any IBlendable struct (and can call the 
    // IBlendable methods) but requires no managed objects or virtual method calls.
    void foo<T>(T a) where T : struct, IBlendable {...}
```

You must write jobs in HPC#, so for a job to operate upon a range of types, it must be generic:

```c#
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

## Scheduling generic jobs from Burst compiled code

To schedule a generic job from Burst compiled code, you need the reflection data for the concrete specialization of the job. Unfortunately, Unity doesn't automatically generate this reflection for all concrete specializations, so in some cases you must register them manually:

```csharp
// This assembly attribute allows Burst-compiled 
// code in the same assembly to schedule the
// concrete specialization <int, float> for MyJob. */
[assembly: RegisterGenericJobType(typeof(MyJob<int, float>))]
```

If you attempt to schedule a job for which the concrete specialization isn't registered in an assembly, then Unity throws an exception.

The assembly that a type is registered in doesn't matter. For example, if a job type is registered only in assembly `Foo`, you can also scheduled it in assembly `Bar`.

If you redundantly register the same concrete specialization more than once, it's not considered an error.

## Automatic registration of concrete job types

When you instantiate a concrete specialization of a generic job directly, Unity automatically registers the specialization in the assembly:

```csharp
// Registers specialization <int, float> for MyJob in the assembly.
var job = new MyJob<int, float>();
```

However, when instantiating a concrete specialization indirectly, Unity doesn't automatically register it:

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

However, if you include  the generic job in the signature as either the return type or an `out` param, then Unity automatically registers it:

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

You can use this indirect registration works through multiple levels of generic method calls:

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

You can also nest the generic job in another class or struct:

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

In the previous example, if `BlendJobWrapper<foo>` is registered automatically or manually, then `BlendJob<foo>` is also effectively registered. A wrapper type around just one generic job doesn't solve anything, but these wrapper types allow for more elegant job creation and scheduling when you use multiple generic jobs together.

## Jobified sorting

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

In this example, the sorting is split across two jobs: the first splits the array into subsections and sorts them individually in parallel. The second job waits for the first and then merges these sorted subsections into a final sorted result.

However, this method doesn't automatically register concrete specializations of the two generic jobs `SegmentSort` and `SegmentSortMerge`, because neither type is used as the return type or as an `out` parameter of the method.

One solution is to make both jobs into `out` parameters::

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

However, this solves the registration problem, but you then have to pass `out` arguments to get two job structs you probably don't want.

A better solution is to wrap both job types together in a wrapper type:

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

In this arrangement, rather than call a `Sort()` method, you can create an instance of `SortJob` and call its `Schedule()` method. By making a concrete instantiation of `SortJob`, you can also automatically register the needed concrete specializations of `SegmentSort` and `SegmentSortMerge`.

This pattern of nesting generic jobs enables a convenient API that schedules related sets of generic jobs together.
