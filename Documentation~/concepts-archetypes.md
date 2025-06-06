---
uid: concepts-archetypes
---

# Archetypes concepts

An archetype is a unique identifier for all the [entities](concepts-entities.md) in a [world](concepts-worlds.md) that have the same unique combination of [component](concepts-components.md) types. For example, in the following diagram, all the entities in a world that have the components `Speed`, `Direction`, `Position`, and `Renderer` and no others share the archetype labelled `X`. All the entities that have component types `Speed`, `Direction`, and `Position` and no others share a different archetype labelled `Y`. 

![A conceptual diagram, with Entity A and B sharing the same components of Speed, Direction, Position, and Renderer, plus Entity C having just Speed, Direction, and Position. Entity A and B share and archetype. A system in the middle of the diagram manipulates the Position, Speed, and Direction components.](images/entities-concepts.png)

When you add or remove component types from an entity, the world's [`EntityManager`](xref:Unity.Entities.EntityManager) moves the entity to the appropriate archetype. For example, if an entity has the components types `Speed`, `Direction`, and `Position` and you remove the `Speed` component, the `EntityManager` moves the entity to the archetype that has components `Direction` and `Position`. If no such archetype exists, the `EntityManager` creates it. 


>[!IMPORTANT]
>Moving entities frequently is resource-intensive and reduces the performance of your application. For more information, refer to the documentation on  [Structural change concepts](concepts-structural-changes.md).

The archetype-based organization of entities means that it's efficient to query entities by their component types. For example, if you want to find all entities that have component types A and B, you can find all the archetypes with those component types, which is more efficient than scanning through all individual entities. The set of existing archetypes in a world tends to stabilize early in the lifetime of a program, so you can cache queries to get faster performance.

An archetype is only destroyed when its world is destroyed.

## Archetype chunks

All entities and components with the same archetype are stored in uniform blocks of memory called chunks. Each chunk consists of 16 KiB and the number of entities that they can store depends on the number and size of the components in the chunk's archetype. The [`EntityManager`](xref:Unity.Entities.EntityManager) creates and destroys chunks as needed.

A chunk contains an array for each component type, plus an additional array to store the entity IDs. For example, in an archetype with component types A and B, the chunks each have three arrays: one array for the A component values, one array for the B component values, and one array for the entity IDs.

The arrays of a chunk are tightly packed: the first entity of the chunk is stored at index 0 of these arrays, the second entity of the chunk is stored at index 1, and subsequent entities are stored in the consecutive indices. When a new entity is added to the chunk, it's stored in the first available index. When an entity is removed from the chunk (either because it's being destroyed or being moved to another archetype), the last entity of the chunk is moved to fill the gap.

When an entity is added to an archetype, the `EntityManager` creates a new chunk if the archetype's existing chunks are all full. When the last entity is removed from a chunk, the `EntityManager` destroys the chunk.

For information on how to manage chunk memory, refer to [Managing chunk allocations](performance-chunk-allocations.md)

## Archetypes in the Editor

The [Archetypes window](editor-archetypes-window.md) lists the archetypes of all the worlds in your project and it shows the amount of allocated and unused memory of each archetype.

In the Editor, the following icon represents an Archetype: ![Archetype icon - a hexagon with lines intersecting it.](images/editor-archetype-icon.png) . 

## Additional resources

* [Structural changes concepts](concepts-structural-changes.md)
* [Managing chunk allocations](performance-chunk-allocations.md)
* [Archetypes window](editor-archetypes-window.md)