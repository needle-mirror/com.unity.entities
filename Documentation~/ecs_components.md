---
uid: ecs-components
---
# Components

ECS Components are different from core [Unity Components](https://docs.unity3d.com/ScriptReference/Component.html). The main differences are outlined below:

|**ECS Component**|**Core Unity Component**|
|---|---|
|Usually an instance of a struct (an unmanaged component). Can also be an instance of a class (a managed component)|An instance of a class|
|Associated with an Entity (or with multiple Entities, in the case of shared Components and chunk Components)|Contained by a GameObject|
|Typically does not include behavior (methods) |Typically includes behavior (methods)|
|Implements one of these interfaces:<br/><br/>`IComponentData`<br/> `ISharedComponentData`<br/>`ISystemStateComponentData`<br/> `ISystemStateSharedComponentData`<br/>`IBufferElementData`<br/><br/>These interfaces have no methods or properties, but they effectively mark the struct or class as being a type of ECS component.|Inherits from `UnityEngine.Component`|

>[!NOTE]
>Only the `IComponentData` interface is discussed on this page. For the other interfaces, see the other pages of this manual section.

## Unmanaged IComponentData components

`IComponentData` marks a struct as an unmanaged component type. These are the most common kind of components.

 The fields of an `IComponentData` struct can only be these types:
    
 * [Blittable types](https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types)
 * `bool`
 * `char`
 * `BlobAssetReference<T>` (a reference to a Blob data structure)
 * `Collections.FixedString` (a fixed-sized character buffer)
 * `Collections.FixedList`
 * [Fixed array](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/fixed-statement) (only allowed in an [unsafe](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/unsafe) context)
 * Other structs conforming to these same restrictions

## Managed IComponentData components

`IComponentData` marks a class as a managed component type. These are used much less commonly than unmanaged components.

The fields of an `IComponentData` class can be of any type, including NativeContainers or any managed types (though they may require special handling for cloning, comparison, and serialization). Unlike unmanaged components, a managed component:

* Can't be accessed in jobs.
* Can't be used in Burst compiled code.
* Requires garbage collection.
* Must be default constructible (for serialization purposes).

Because of their performance drawbacks, you should avoid using managed components where unmanaged components suffice.

## Archetypes and chunks

An Entity's set of component types is known as its **archetype**.

Entities and their components are stored in blocks of memory called **chunks**. A chunk can only store entities of the same archetype.

- Each archetype == zero or more chunks.
- Each chunk == one or more entities (all of the same archetype).

Each chunk is 16KiB in size, divided into parallel arrays: one for each component type, plus an additional array for storing the IDs of the entities. The ID and components of the chunk's first Entity are stored at index 0 of these arrays; the chunk's second Entity at index 1; the chunk's third Entity at index 2; and so forth.

You don't have direct control over the creation and destruction of chunks: the ECS system manages the chunk storage and puts Entities in chunks that match their archetypes. You can only tell the system when you want to create or destroy Entities and add or remove components of the Entities.

The number of Entities that can be stored in a chunk depends upon the number and sizes of the component types. For example, if components A, B, and C add up to 92 bytes in size, then a single entity of this archetype requires 100 bytes of storage (including the 8 bytes for the entity id). So a chunk of this archetype can store ~163 entities (16384 bytes divided by 100). (The precise count depends upon padding and the size of the chunk header.)

A chunk header includes the current count of entities in the chunk (which may be less than the chunk's capacity). The occupied slots of a chunk's arrays always precede the empty slots, with no gaps interspersed:

- When an Entity is added to a chunk, it's placed in the first free slot.
- When an Entity is removed from a chunk, the last entity in the chunk is moved to fill the gap.

Adding or removing components of an entity changes it archetype, and so the entity is moved to a different chunk, one that matches its new archetype.

![](images/ArchetypeChunkDiagram.png)

## Tag components

An `IComponentData` struct with no fields is known as a *tag* component. Because tag components have no data, chunks store no component arrays for their tag components. Otherwise, tag components behave like regular unmanaged component types.    

## Unmanaged component storage

Unlike unmanaged components, managed components aren't stored directly in the chunks. Instead, the managed component class instances are all referenced in one big array for the whole `World`, and a managed type component array in a chunk just stores indexes into this array. Accessing a managed component of an entity requires an extra indexing lookup, which is what makes managed components less optimal then unmanaged components.

## Adding and removing components from entities

On the main thread, you can add and remove the components of a `World`'s entities using the `World`'s `EntityManager`. As mentioned above, adding and removing components changes an entity's archetype, which means that the entity must be moved to a different chunk.

Structural changes include:

- Creating a chunk.
- Destroying a chunk.
- Adding entities to a chunk.
- Removing entities from a chunk.
- Setting an entity's `ISharedComponentData` value (because it [requires moving the entity to another chunk](shared_component_data.md)).

Setting an entity's `IComponentData` value, however, isn't considered to be a structural change.

Structural changes can only be performed on the main thread, and not from jobs. The workaround for this is to use an [EntityCommandBuffer](entity_command_buffer.md) in a job to record your intention to make changes later. The `EntityCommandBuffer` can then be 'played back' later on the main thread to enact the recorded changes.

While you can't immediately add or remove components in a job, you can use an `EntityCommandBuffer` to record your intention to add or remove components later.

## Reading and writing component values of entities


### Reading and writing a single component

Sometimes you might want to read or write a single component of one entity at a time. To do this, on the main thread, you can ask the `EntityManager` to read or write a component value of an individual entity. (The `EntityManager` keeps a lookup table to quickly find each entity's chunk and index within the chunk.)

### Reading and writing multiple components

For most work, you'll want to read or write the components of all entities in a chunk (or set of chunks):

- An `ArchetypeChunk` allows you to directly read and write the component arrays of a chunk.
- An `EntityQuery` efficiently retrieves the set of chunks which match the query.
- An [Entities.ForEach](cs_entities_foreach.md) conveniently handles the creation and use of an `EntityQuery` for you, while also making it more convenient to iterate through the entities of the chunk(s), either on the main thread or in a job.

### Deferring component value changes

In some cases, you might want to defer component value changes for later. You can use an [EntityCommandBuffer](entity_command_buffer.md) which records your intention to write (but not read) component values. These changes only happen when you later play back the `EntityCommandBuffer` on the main thread.

## Favor small components
 
When memory is accessed, it is copied between memory and cache in 64-byte units called [cache lines](https://en.wikipedia.org/wiki/CPU_cache#Cache_entries). Cache line size varies between platforms. 64 bytes is the most common size, but some hardware has 128-byte cache lines.

When looping through bytes of memory, ignored bytes still get copied if the system accesses other bytes in the same cache line. Effectively, skipping over bytes wastes [memory bandwidth](https://en.wikipedia.org/wiki/Memory_bandwidth).

So, when looping through an array of component structs, bytes of ignored fields might take up memory bandwidth. By splitting your components up into more components with fewer fields, it's less likely that the loops will wastefully ignore one or more component fields, and that your code will waste memory bandwidth.  

Splitting data up into a lot of small component types is the most optimum strategy. In fact, components with one field are often the norm. However, as your code stabilizes, you might notice that some components are always used together, in which case you might consider consolidating them together.

## Avoid putting methods on components

Unlike GameObject components, ECS components are intended to have just data, not code, so you should usually put code in methods of systems rather than methods of ECS components. However, it's fine to give an ECS component small accessor methods.

## Components in the Editor

In the Editor, the following icons represent the different types of Components. You’ll see this when you use the specific [Entities windows and Inspectors](editor-workflows.md).

|**Icon**|**Component type**|
|---|---|
|![](images/editor-buffer-component.png)| A buffer Component|
|![](images/editor-chunk-component.png)| A chunk component|
|![](images/editor-managed-component.png)| A managed Component|
|![](images/editor-shared-component.png)| A shared Component|
|![](images/editor-tag-component.png)| A tagged Component|
