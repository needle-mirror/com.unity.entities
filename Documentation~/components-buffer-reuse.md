# Reuse a dynamic buffer for multiple entities

If all entities of an `Entities.ForEach` need the same buffer, you can get that buffer as a local variable on the main thread above the `Entities.ForEach`.

The following code sample shows how to use the same dynamic buffer for multiple entities. It assumes a dynamic buffer called `MyElement` exists and another Component exists called `OtherComponent`.

```c#
public void DynamicBufferExample(Entity e)
{
    var myBuff = EntityManager.GetBuffer<MyElement>(e);

    Entities.ForEach((in OtherComponent component) => {
        // ... use myBuff
    }).Schedule();
}
```

> [!NOTE]
> If you use `ScheduleParallel`, be aware that you can't write to the dynamic buffer in parallel. You can however use an `EntityCommandBuffer.ParallelWriter` to record changes in parallel.

## Additional resources

* [Access dynamic buffers from jobs](components-buffer-jobs.md)