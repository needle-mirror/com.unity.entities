---
uid: components-buffer-introducing
---

# Dynamic buffer components introduction

A dynamic buffer component is a component that acts as a resizable array of unmanaged structs. You can use it to store array data for an entity, such as waypoint positions for the entity to navigate between.

Alongside the data, each buffer stores a `Length`, a `Capacity`, and an internal pointer:

* The `Length` is the number of elements in the buffer. It starts at `0` and increments when you append a value to the buffer.
* The `Capacity` is the amount of storage in the buffer. It starts out matching the internal buffer capacity. Setting `Capacity` resizes the buffer.
* The pointer indicates where the dynamic buffer data is. The pointer is initially `null` to signify that the data is in the chunk with the entity, and if Unity moves the data outside the chunk, the pointer is set to point to the new array. For more information on how Unity stores dynamic buffer components, refer to [Set the capacity of a dynamic buffer](components-buffer-set-capacity.md).

## Dynamic buffer capacity

The initial capacity of a dynamic buffer is defined by the type that the buffer stores. By default, the capacity defaults to the number of elements that fit within 128 bytes. For more information on dynamic buffer capacity, refer to [Set the capacity of a dynamic buffer](components-buffer-set-capacity.md). 

You can also use the [`InternalBufferCapacity`](xref:Unity.Entities.InternalBufferCapacityAttribute) attribute to specify a custom capacity. For more information, refer to [Create a dynamic buffer component type](components-buffer-create.md).

## Structural changes

[Structural changes](concepts-structural-changes.md) might destroy or move the array referenced by a dynamic buffer which means that any handle to a dynamic buffer becomes invalid after a structural change. You must reacquire dynamic buffers after any structural changes. For example:

```c#
public partial struct DynamicBufferExampleSystem : ISystem
{
    EntityQuery m_BufferEntityQuery;
    
    public void OnCreate(ref SystemState state)
    {
        m_BufferEntityQuery = SystemAPI.QueryBuilder().WithAll<MyElement>().Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Acquires entities with the desired buffer.
        var entities = m_BufferEntityQuery.ToEntityArray(Allocator.Persistent);
        if(entities.Length == 0) return;

        // Acquires a dynamic buffer of type MyElement from the first entity in the array.
        DynamicBuffer<MyElement> myBuff = state.EntityManager.GetBuffer<MyElement>(entities[0]);

        // This structural change invalidates the previously acquired DynamicBuffer.
        state.EntityManager.CreateEntity();

        // A safety check will throw an exception on any read or write actions on the buffer.
        var x = myBuff[0];

        // Reacquires the dynamic buffer after the above structural changes.
        myBuff = state.EntityManager.GetBuffer<MyElement>(entities[0]);
        var y = myBuff[0];
    }

}
```
## Comparison to native containers in components

Dynamic buffers don't have the job scheduling restrictions that [native containers on components](components-nativecontainers.md) have, so it's usually preferable to use dynamic buffers in your code where possible. Dynamic buffers can also be stored inline inside a [chunk](concepts-archetypes.md#archetype-chunks), which helps reduce memory bandwidth usage.

In general, when there's more than one entity that needs a collection on it, use a dynamic buffer. If there's only one, it might work well as a [singleton component](components-singleton.md) with a native container on it. 

## Additional resources

* [Create a dynamic buffer component](components-buffer-create.md)
* [Set the capacity of a dynamic buffer](components-buffer-set-capacity.md)
