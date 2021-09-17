---
uid: ecs-dynamic-buffers
---

# Dynamic buffer components

A dynamic buffer is a kind of component which is a resizeable array. A dynamic buffer type is defined by a struct implementing `IBufferElementData`:

```csharp
// An entity with this dynamic buffer component has a 
// MyElement array with space for 16 elements stored directly in the chunk.
//
// When the internal buffer capacity is exceeded, the buffer is 
// copied to a new separate array outside the chunk.
[InternalBufferCapacity(16)]
public struct MyElement : IBufferElementData
{
    public int Value;
}
```

If omitted, `InternalBufferCapacity` defaults to the number of elements that fits within 128 bytes (see [DefaultBufferCapacityNumerator](xref:Unity.Entities.TypeManager.DefaultBufferCapacityNumerator)).

An `IBufferElementData` struct is unmanaged and so subject to the same constraints as an `IComponentData` struct.

Be clear that an `IBufferElementData` struct defines the elements of a dynamic buffer type and also represents the dynamic buffer component type itself. Meanwhile, a `DynamicBuffer` struct represents just an individual dynamic buffer, not a component type.

Each buffer also stores a `Length`, a `Capacity`, and a pointer:

* The `Length` is the logical length. It starts at 0 and increments when a value is appended into the buffer.
* The `Capacity` is the actual amount of storage in the buffer. It starts out matching the internal buffer capacity. Setting `Capacity` resizes the buffer.
* Until a buffer grows beyond its internal buffer capacity, the buffer's data is stored directly in the chunk, and its pointer remains null. Once the internal capacity is exceeded, all the buffer's data is copied to a new larger array separately allocated outside the chunk, and the pointer is set to point to this new array.

The internal buffer capacity is part of the chunk and so is only deallocated when the chunk itself is deallocated. Any external arrays that get created are automatically deallocated when no longer needed, such as when the entity is destroyed.

> [!NOTE]
> If the data in a buffer need not ever change after creation, you can instead use a [blob asset](xref:Unity.Entities.BlobBuilder). Blob assets can store structured data, including arrays, and multiple entities can share a single blob asset.

## Basic dynamic buffer usage


A dynamic buffer component can be added, removed, and queried just like other components:

```csharp
// ...The IBufferElementData struct MyElement is defined above.

// Create an entity with a dynamic buffer with elements of type MyElement.
EntityManager.CreateEntity(typeof(MyElement));    

// Add a MyElement dynamic buffer to an existing entity.
EntityManager.AddComponent<MyElement>(e);    

// Remove the MyElement  dynamic buffer from an entity.
EntityManager.RemoveComponent<MyElement>(e);

// A query that matches entities with a MyElement dynamic buffer.
EntityQuery query = GetEntityQuery(typeof(MyElement));
```

A `DynamicBuffer` struct represents an individual buffer and lets you read, write, and append values in the buffer, and set the buffer's `Length` and `Capacity`:

```csharp
// Get a DynamicBuffer representing the entity's MyElement buffer.
DynamicBuffer<MyElement> myBuff = EntityManager.GetBuffer<MyElement>(e);

// Read and set index 5 of the buffer. 
// Throws a safety check exception if 5 is >= Length.
int x = myBuff[5].Value;
myBuff[5] = new MyElement { Value = x + 1 };

// Append a new value at index Length and increments Length.
// If new Length exceeds Capacity, the buffer is resized to double Capacity.
myBuff.Add(new MyElement { Value = 100 });

// Effectively, set the range of usable indexes to 0 through 9.
// If necessary, will increase Capacity to accommodate the new Length.  
myBuff.Length = 10; 

// Resize the array to this precise size.
// Throws a safety check exception if less than Length.
myBuff.Capacity = 20;
```

In `Entities.ForEach`, you can access a dynamic buffer component with a lambda parameter:

```csharp
Entities.ForEach((in DynamicBuffer<MyElement> myBuff) => {
    for (int i = 0; i < myBuff.Length; i++)
    {
        // ... read myBuff[i]
    }
}).Schedule();
```

For write access, make the parameter `ref` instead of `in`.

### Structural changes invalidate a DynamicBuffer
 
Because a [structural change](sync_points.md#structural-changes) might destroy or move the underlying array referenced by a `DynamicBuffer`, a `DynamicBuffer` struct cannot be used after any structural change. The `DynamicBuffer` must be reacquired to access the buffer again.

```csharp
DynamicBuffer<MyElement> myBuff = EntityManager.GetBuffer<MyElement>(e);

// This structural change invalidates the previously acquired DynamicBuffer.
EntityManager.CreateEntity();

// Safety check will throw an exception on any read or write of the buffer.
var x = myBuff[0];   // Exception!

// Reacquire the DynamicBuffer.
myBuff = EntityManager.GetBuffer<MyElement>(e);

var y = myBuff[0];   // OK
```
    
### Random lookup of buffers through BufferFromEntity        

If all entities of an `Entities.ForEach` need the same buffer, you can just capture that buffer as a local variable on the main thread:

```csharp
var myBuff = EntityManager.GetBuffer<MyElement>(someEntity);  

Entities.ForEach((in SomeComp someComp) => {    
    // ... use myBuff
}).Schedule();
```

> [!NOTE]
> If using `ScheduleParallel`, be aware that a buffer cannot be written in parallel. You can however use an `EntityCommandBuffer.ParallelWriter` to record changes in parallel.

If though an `Entities.ForEach` needs to lookup one or more buffers in its code, you need a `BufferFromEntity` struct, which provides random lookup of buffers by entity. For the sake of job safety, a system needs to keep track of which components it accesses, so a `BufferFromEntity` is created via `GetBufferFromEntity` of `SystemBase`. (There's also [GetBuffer of SystemBase](xref:Unity.Entities.SystemBase.GetBuffer), which implicitly creates a `BufferFromEntity` when used in an `Entities.ForEach` job.)

```csharp
// ...in the OnUpdate of a SystemBase class
BufferFromEntity<MyElement> lookup = GetBufferFromEntity<MyElement>();

Entities.ForEach((in SomeComp someComp) => {
    // EntityManager cannot be used in the job, so instead we use
    // the captured BufferFromEntity to lookup MyElement buffers by entity.
    DynamicBuffer<MyElement> myBuff = lookup[someComp.OtherEntity];
    
    // ... use myBuff
}).Schedule();
```

## Generating an authoring component

```csharp
[GenerateAuthoringComponent]
public struct MyElement: IBufferElementData
{
    public int Value;
}
```

This will generate a `MonoBehaviour` class named `MyElementAuthoring` with a public field of type `List<int>`. When a `GameObject` with this authoring component is converted into an entity, the list of ints is added to the entity as a `DynamicBuffer<Bar>` component. Note that: 

- `[GenerateAuthoringComponent]` cannot be applied to `IBufferElementData` structs which have more than one field or which use `[StructLayout (LayoutKind.Explicit)]`
- As always, `[GenerateAuthoringComponent]` can only be applied to one type in a source file, and the file must not define any other `MonoBehaviour` classes.

## Modifying dynamic buffers with an EntityCommandBuffer

An `EntityCommandBuffer` can record commands to add buffer components to entities, remove them, or set them.

```csharp
// ...The IBufferElementData struct MyElement is defined above.

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
```
 
When you set the `Length`, `Capacity`, and content of the `DynamicBuffer` returned by `AddBuffer`, those changes are recorded into the `EntityCommandBuffer`. Upon playback, the buffer added to the entity will have those changes.

### Get all buffers of a chunk

The `ArchetypeChunk` method `GetBufferAccessor` takes a `BufferTypeHandle<T>` and returns a `BufferAccessor`. Indexing the `BufferAccessor<T>` returns the chunk's buffers of type `T`:

```csharp
// ... assume a chunk with MyElement dynamic buffers

// Get a BufferTypeHandle representing dynamic buffer type MyElement from SystemBase.
BufferTypeHandle<MyElement> myElementHandle = GetBufferTypeHandle<MyElement>();

// Get a BufferAccessor from the chunk.
BufferAccessor<MyElement> buffers = chunk.GetBufferAccessor(myElementHandle);

// Iterate through all MyElement buffers of each entity in the chunk. 
for (int i = 0; i < chunk.Count; i++)
{
    DynamicBuffer<MyElement> buffer = buffers[i];
    
    // Iterate through all elements of the buffer.
    for (int i = 0; i < buffer.Length; i++)
    {
        // ...
    }
}
```

## Reinterpreting buffers

A `DynamicBuffer<T>` can be 'reinterpreted' such that you get another `DynamicBuffer<U>`, where `T` and `U` have the same size. This reinterpretation aliases the same memory, so changing the value at index `i` of one changes the value at index `i` of the other:

```csharp
DynamicBuffer<MyElement> myBuff = EntityManager.GetBuffer<MyElement>(e);

// Valid as long as each MyElement struct is four bytes. 
DynamicBuffer<int> intBuffer = myBuff.Reinterpret<int>();

intBuffer[2] = 6;  // same effect as: myBuff[2] = new MyElement { Value = 6 };

// The MyElement value has the same four bytes as int value 6. 
MyElement myElement = myBuff[2];
Debug.Log(myElement.Value);    // 6
```

The reinterpreted buffer shares the safety handle of the original and so is subject to all the same safety restrictions. 

> [!NOTE]
> The `Reinterpret` method only enforces that the original type and new type have the same size. For example, you can reinterpret a `uint` to a `float` because both types are 32-bit. It is your responsibility to decide whether the reinterpretation makes sense for your purposes.
