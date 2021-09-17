---
uid: ecs-shared-component-data
---
# Shared components

Shared component values (structs implementing `ISharedComponent`) differ from regular components in the following ways:

### Values stored in an array
Shared component values are stored outside the chunks in an array. A chunk stores only one index for each shared component in its archetype, so all entities in a chunk share the same shared component value(s). Because multiple chunks may store the same index(es), a shared component value may be shared by multiple chunks.

### Structrual changes
Setting a shared component value on an entity is a structural change: because all entities in a chunk have the same shared component values, changing any of an individual entity's shared component values requires moving the entity to a chunk which shares the entity's new value(s). (If no such chunk yet exists, a new one is created. If the new shared component value happens to match the entity's prior shared component value, the entity is not moved.)

### Indexes store reference counts
Each index in the array stores a reference count that tracks the number of chunks that stores the index. When an index's reference count is decremented to 0, the index is cleared so that a shared component value added later can occupy the index.

### Shared component values stored as managed objects
Even though they are structs, shared component values are always stored as managed objects. On the plus side, this means that fields of shared components can also be managed objects. On the down side, this means that shared components cannot be accessed in Burst code and jobs. (Unmanaged shared components are a forthcoming feature.)

### Shared values are de-duplicated
Shared component values are de-duplicated: the same value will not be stored more than once. When a shared component value is set on an entity, if an equal value is already present in the array, the entity stores the index of the existing value; otherwise if no equal value is present, the new value is added to the array, and the entity stores the index of this new value. (Finding a specific value in the array is cheap because the values are also indexed in an `UnsafeMultiHashMap` using their hashcodes from `GetHashCode()`. Note that the default `GetHashCode()` and equality test may suffice for some shared component types, but others should override `GetHashCode()` and implement `IEquatable<T>`.)

## Filtering an `EntityQuery` for a specific shared component value

A key feature of shared components is that you can filter an `EntityQuery` to only match chunks with a specific shared component value.

```csharp
// A query that finds all chunks with a particular shared component value.
EntityQuery query = GetEntityQuery(typeof(MySharedComponent));
MySharedComponent val = new MySharedComponent{Value = 5};
query.SetSharedComponentFilter(val);

// Get only the chunks with the matching MySharedComponent value.
NativeArray<ArchetypeChunk> chunks = query.CreateArchetypeChunkArray(Allocator.TempJob);
```

You can also retrieve all unique shared component values currently stored for a given type. 

```csharp
List<MySharedComponent> uniques = new List<MySharedComponent>();

// Populates the list with all unique MySharedComponent values.
EntityManager.GetAllUniqueSharedComponentData(uniques);
```

Right now, `GetAllUniqueSharedComponentData` is implemented by scanning the whole array, but it should be made more efficient in a future version.

## Avoid updating shared component values frequently
 
Because updating a shared component value of an entity moves the entity to another chunk, doing so frequently should be generally avoided.

## Avoid mutating objects referenced by shared component values

Because shared components may contain reference types and pointers, it's possible to mutate elements of a shared component value without the knowledge of the Entities API:

```csharp
// Set a shared component on an entity.
var counter = new MyCounter(0);  // a class instance
var shared = new MySharedComponent{ MyCounter = counter };
EntityManager.SetSharedComponentData(myEntity, shared);

// Mutate the counter stored by the shared component.
// The API does not update the hashcode of the stored component
// because it is unaware of the change.
counter.Increment();    

// Even though the 'new' value equals what is already stored, 
// this call erroneously adds another value to the array because
// the 'new' hashcode doesn't match the one stored.
EntityManager.SetSharedComponentData(myEntity, shared);
```

To work correctly, shared components rely upon unchanging hashcodes, so be careful to not mutate objects referenced by a shared component value.

## Avoid high proportions of unique shared component values

Because all entities in a chunk always share the same shared component values, giving unique shared component values to a high proportion of entities wastefully fragments those entities across many nearly-empty chunks.

For example, say you have 500 entities of an archetype with a shared component. If each entity has its own unique shared component value, then the 500 entities each must reside in their own individual chunks. Not only does this likely waste most of the space in the chunks, it means that looping through all entities of the archetype requires looping through 500 different chunks and thereby skipping around memory to visit each entity. This entirely defeats the cache coherence benefits of the ECS chunk layout!

You avoid this problem if the 500 entities share between them a smaller proportion of unique values. If, say, the 500 entiites share just 10 unique shared values, then the entities can be stored in as few as 10 chunks. (The exact number depends upon the size of the components and the distribution of the unique shared values amongst the entities.)

Be especially careful with archetypes having multiple shared component types: all entities in a chunk must have the same *combination* of shared component values, so archetypes with multiple shared component types are very susceptible to fragmentation.

There is no hard rule here. Just be aware of the performance implications!

> [!NOTE]
> To check for chunk fragmentation, you can view the chunk utilization in the [Entity Debugger](ecs_debugging.md).
