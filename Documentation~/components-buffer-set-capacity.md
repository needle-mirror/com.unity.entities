# Set the capacity of a dynamic buffer

The initial capacity of a dynamic buffer is defined by [the type that the buffer stores](components-buffer-create.md). By default, the capacity defaults to the number of elements that fit within 128 bytes, which [`DefaultBufferCapacityNumerator`](xref:Unity.Entities.TypeManager.DefaultBufferCapacityNumerator) defines. 

To specify a custom capacity, use the [`InternalBufferCapacity`](xref:Unity.Entities.InternalBufferCapacityAttribute) attribute.

## Capacity overview

Initially, Unity stores the dynamic buffer data directly in the [archetype chunk](concepts-archetypes.md#archetype-chunks) of the entity that the component belongs to. If a dynamic buffer is within its initial capacity, it’s stored inline in the chunk, as if the entity contained a component with an array inside it. 

When the length of a dynamic buffer is resized to be greater than the internal buffer capacity, Unity allocates memory outside the chunk and copies the dynamic buffer data to an array outside of the chunk. If the length of the dynamic buffer later shrinks to less than the capacity, Unity still stores the data outside of the chunk. If Unity moves dynamic buffer data outside of a chunk, it never moves the data back into the chunk.

For example, if a dynamic buffer's capacity is 12 elements, and then a thirteenth element is added, the following problems are introduced:

* When you add the new element, ECS allocates the new buffer storage out of chunks, and copies the existing buffer elements to the new memory location. This can take up a lot of time. 
* Every future attempt to access the dynamic buffer results in a cache miss, because the buffer data is no longer inline in the entity chunk.  
* This situation contributes to chunk fragmentation. Once the dynamic buffer has exceeded initial capacity and moved, there's always a 12-element empty space in the chunk that your code is no longer accessing but which persists for as long as the dynamic buffer exists.

The original internal buffer capacity is part of the chunk and Unity only deallocates it when Unity deallocates the chunk itself. This means if the dynamic buffer length exceeds the internal capacity and Unity copies the data outside of the chunk, there's wasted space within the chunk. 

It's best practice to use the data in the chunk when possible. To do this, make sure most of your entities don't exceed the buffer capacities, but also don't set the capacity too high if the entities don't use it. If the size of a dynamic buffer changes too much, it's best practice to store its data outside of the chunk. To do this, set the `InternalBufferCapacity` to `0`. 


## Set the internal buffer capacity

The default capacity of all DynamicBuffers is calculated using [`TypeManager.DefaultBufferCapacityNumerator`](xref:Unity.Entities.TypeManager.DefaultBufferCapacityNumerator). This defaults to 128 bytes, or for example, 32 integers. 

If you know in advance how many elements a given dynamic buffer is likely to contain, you can use the [`[InternalBufferCapacity]`](xref:Unity.Entities.InternalBufferCapacityAttribute) attribute to declare it when you declare the buffer. As long as the buffer never grows past this initial capacity, it never needs to reallocate. You should also consider how chunk fragmentation might impact your implementation. For more information, refer to [Manage chunk allocations](performance-chunk-allocations.md).

The following is an example of using `InternalBufferCapacity` to set capacity:

```c#
// My buffer can contain up to 42 elements inline in the chunk
// If I add any more then ECS will reallocate the buffer onto a heap  
[InternalBufferCapacity(42)]  
public struct MyBufferElement : IBufferElementData  
{  
    public int Value;  
}  
```

## Dynamically set the buffer capacity

If you don't know how much capacity a dynamic buffer needs at compile time, you can dynamically control the capacity. It's useful to dynamically control capacity when you add items to a dynamic buffer one at a time,  because by default the buffer grows by one element every time, which means that every `Add()` that increases the `Capacity` causes an allocation.

Use [`DynamicBuffer.EnsureCapacity`](xref:Unity.Entities.DynamicBuffer`1.EnsureCapacity*) to forcibly reallocate the buffer into an area of memory that’s big enough to accommodate the specified capacity without needing to reallocate every time a new element is added. If dynamic buffers end up taking up too much memory because of capacity padding that you no longer need, you can call [`DynamicBuffer.TrimExcess`](xref:Unity.Entities.DynamicBuffer`1.TrimExcess*) to reduce their size.


## Alternative array data storage

If the dynamic buffer capacity limitations are a problem for your project, the following options are available to store array data:

* [Blob assets](blob-assets-intro.md): Stores tightly packed read-only structured data, including arrays, and multiple entities can share a single blob asset. Because they're read-only, you can access them from multiple threads simultaneously.
* Use [native containers](xref:um-job-system-native-container) with unmanaged [`IComponentData`](xref:Unity.Entities.IComponentData) components.

## Additional resources

* [`[InternalBufferCapacity]` API reference](xref:Unity.Entities.InternalBufferCapacityAttribute)
* [Dynamic buffer components introduction](components-buffer-introducing.md)
* [Create a dynamic buffer component](components-buffer-create.md)
* [Access dynamic buffers in a chunk](components-buffer-get-all-in-chunk.md)
