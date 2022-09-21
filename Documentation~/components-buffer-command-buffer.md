# Modify dynamic buffers with an `EntityCommandBuffer`

An [`EntityCommandBuffer`](xref:Unity.Entities.EntityCommandBuffer) records commands to add, remove, or set buffer components for entities. There are dynamic buffer-specific APIs that are different than the regular component APIs.

The following code sample walks through some general dynamic buffer-specific `EntityCommandBuffer` APIs. It assumes a dynamic buffer called `MyElement` exists.

```csharp
private void Example(Entity e, Entity otherEntity)
{
    EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

    // Record a command to remove the MyElement dynamic buffer from an entity.
    ecb.RemoveComponent<MyElement>(e);

    // Record a command to add a MyElement dynamic buffer to an existing entity.
    // The data of the returned DynamicBuffer is stored in the EntityCommandBuffer, 
    // so changes to the returned buffer are also recorded changes. 
    DynamicBuffer<MyElement> myBuff = ecb.AddBuffer<MyElement>(e);

    // After playback, the entity will have a MyElement buffer with 
    // Length 20 and these recorded values.
    myBuff.Length = 20;
    myBuff[0] = new MyElement { Value = 5 };
    myBuff[3] = new MyElement { Value = -9 };

    // SetBuffer is like AddBuffer, but safety checks will throw an exception at playback if 
    // the entity doesn't already have a MyElement buffer. 
    DynamicBuffer<MyElement> otherBuf = ecb.SetBuffer<MyElement>(otherEntity);

    // Records a MyElement value to append to the buffer. Throws an exception at 
    // playback if the entity doesn't already have a MyElement buffer.
    ecb.AppendToBuffer<MyElement>(otherEntity, new MyElement { Value = 12 });
}
```

When you set the `Length`, `Capacity`, and content of the `DynamicBuffer`, ECS records those changes into the `EntityCommandBuffer`. When you play back the `EntityCommandBuffer`, ECS makes the changes to the dynamic buffer.

## Additional resources

* [Access dynamic buffers from jobs](components-buffer-jobs.md)