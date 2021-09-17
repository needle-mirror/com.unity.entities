---
uid: ecs-chunk-component-data
---

# Chunk component data

A chunk component is a kind of component for which one value is stored per chunk, not per entity.

Unlike shared components:

- A chunk component type is defined as an `IComponentData` struct.
- A chunk component value logically belongs to the chunk itself, not the individual entities of the chunk.
- Setting a chunk component value never moves entities to different chunks.
- Unlike with shared components, unique chunk component values are not deduplicated: chunks with matching chunk component values store their own separate copies.
- Chunk components are always just unmanaged struct values, not managed objects.
- An entity gets moved to a new chunk when its archetype changes or when its shared component values change, but these moves never modify the chunk component values of either the source or destination chunk.

```csharp
// A chunk component definition is the same as a regular component.
public struct MyChunkComp : IComponentData
{
    public int Value;
}
```

## Add, remove, set, and query chunk components

```csharp
// Add MyChunkComp to the entity's chunk.
EntityManager.AddChunkComponent<MyChunkComp>(e);

// Find all chunks with a MyComp (regular component) and with a MyChunkComp (chunk component).
// (The chunk component must be specified with ComponentType.ChunkComponent to
// distinguish it from a regular IComponentData.)
EntityQuery query = GetEntityQuery(typeof(MyComp), ComponentType.ChunkComponent<MyChunkComp>());
NativeArray<ArchetypeChunk> chunks = query.CreateArchetypeChunkArray(Allocator.Temp);

// Set MyChunkComp value of the first chunk.
EntityManager.SetChunkComponentData<MyChunkComp>(chunks[0], new MyChunkComp { Value = 6 });

// Get MyChunkComp value of the first chunk.
MyChunkComp myChunkComp = EntityManager.GetChunkComponentData<MyChunkComp>(chunks[0]);
Debug.Log(myChunkComp.Value)    // 6
```

> [!NOTE]
> If you only want to read a chunk component and not write to it, you should use `ComponentType.ChunkComponentReadOnly` when you define the query. Marking components included in a query as readonly helps avoid creating unnecessary job scheduling constraints.

Although chunk components 'belong' to the chunks themselves, adding or removing chunk components on an entity changes its archetype and so necessitates moving the entity to a different chunk. Again, these move operations never modify the chunk component values of the source or destination chunks.

> [!NOTE]
> For a newly created chunk, its chunk component values (if any) are initialized to the default values of those types.

As a convenience, we can also get and set the chunk components of a chunk *via* any of its entities:

```csharp
// Set MyChunkComp value of the entity's chunk.
EntityManager.SetChunkComponentData<MyChunkComp>(e, new MyChunkComp { Value = 6 });

// Get MyChunkComp value of the entity's chunk.
MyChunkComp myChunkComp = EntityManager.GetChunkComponentData<MyChunkComp>(e);
Debug.Log(myChunkComp.Value)    // 6
```

As always in jobs, we can't use an `EntityManager`, so to access a chunk component, we instead need its `ComponentTypeHandle`:

```csharp
struct MyJob : IJobChunk
{
    public ComponentTypeHandle<MyChunkComp> MyChunkCompHandle;
    
    public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
    {
        // Get the chunk's MyChunkComp.
        MyChunkComp myChunkComp = chunk.GetChunkComponentData(MyChunkCompHandle);
        //...
        // Set the chunk's MyChunkComp. 
        chunk.SetChunkComponentData(MyChunkCompHandle, new MyChunkComp { Value = 7 });
    }
}
```