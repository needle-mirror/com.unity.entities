# Use chunk components

Chunk components use a different set of APIs to add, remove, get, and set them compared to other component types. For example, to add chunk components to an entity, you use [`EntityManager.AddChunkComponentData`](xref:Unity.Entities.EntityManager.AddChunkComponentData*) instead of the regular [`EntityManager.AddComponent`](xref:Unity.Entities.EntityManager.AddComponent*).

The following code sample shows how to add, set, and get a chunk component. It assumes a chunk component called `ExampleChunkComponent` and a non-chunk component called `ExampleComponent` exists:

```c#
private void ChunkComponentExample(Entity e)
{
    // Adds ExampleChunkComponent to the passed in entity's chunk.
    EntityManager.AddChunkComponentData<ExampleChunkComponent>(e);

    // Finds all chunks with an ExampleComponent and an ExampleChunkComponent.
    // To distinguish chunk components from a regular IComponentData, You must
    // specify the chunk component with ComponentType.ChunkComponent.
    EntityQuery query = GetEntityQuery(typeof(ExampleComponent), ComponentType.ChunkComponent<ExampleChunkComponent>());
    NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);

    // Sets the ExampleChunkComponent value of the first chunk.
    EntityManager.SetChunkComponentData<ExampleChunkComponent>(chunks[0], new ExampleChunkComponent { Value = 6 });

    // Gets the ExampleChunkComponent value of the first chunk.
    ExampleChunkComponent exampleChunkComponent = EntityManager.GetChunkComponentData<ExampleChunkComponent>(chunks[0]);
    Debug.Log(exampleChunkComponent.Value)    // 6
}
```

> [!NOTE]
> If you only want to read from a chunk component and not write to it, use `ComponentType.ChunkComponentReadOnly` when you define the query. Marking components included in a query as read-only helps to avoid unnecessary job scheduling constraints.

Although chunk components belong to the chunks themselves, adding or removing chunk components on an entity changes its archetype and causes a structural change.

> [!NOTE]
> Unity initializes newly-created chunk component values to the default values of those types.

You can also get and set the chunk components of a chunk via any of the chunk's entities:

```c#
private void ChunkComponentExample(Entity e)
{
    var entityChunk = EntityManager.GetChunk(e);
    // Sets the ExampleChunkComponent value of the entity's chunk.
    EntityManager.SetChunkComponentData<ExampleChunkComponent>(entityChunk,
        new ExampleChunkComponent { Value = 6 });

    // Gets the ExampleChunkComponent value of the entity's chunk.
    ExampleChunkComponent exampleChunkComponent = EntityManager.GetChunkComponentData<ExampleChunkComponent>(e);
    Debug.Log(exampleChunkComponent.Value)    // 6
}
```

## Use chunk components in jobs

Jobs can't use an `EntityManager`, so to access a chunk Component, you need to use its `ComponentTypeHandle`.

```c#
struct MyJob : IJobChunk
{
    public ComponentTypeHandle<ExampleChunkComponent> ExampleChunkComponentHandle;

    public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
    {
        // Get the chunk's ExampleChunkComponent.
        ExampleChunkComponent exampleChunkComponent = chunk.GetChunkComponentData(ExampleChunkComponentHandle);

        // Set the chunk's ExampleChunkComponent. 
        chunk.SetChunkComponentData(ExampleChunkComponentHandle, new ExampleChunkComponent { Value = 7 });
    }
}
```

