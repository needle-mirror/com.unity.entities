# Archetypes window reference

The Archetypes window displays how much allocated and unused memory ECS has allocated to each [Archetype](ecs_core.md#archetypes) in your project. It contains a list of Archetypes across all the Worlds in your project, which indicates the current memory layout of the ECS framework.

To open the Archetypes window go to **Window &gt; DOTS &gt; Archetypes**.


![](images/editor-archetypes-window.png)<br/>_Archetypes window with an Archetype selected_

To see more information about an Archetype, click to select it. The information appears in the panel on the right. 

To include the empty Archetypes in your project in this list, open the More menu (⋮) and enable the **Show Empty Archetypes** setting. An empty Archetype is one that has zero Entities associated with it. Often, this happens when you use [AddComponent](xref:Unity.Entities.EntityManager.AddComponent) to add Components to an Entity one at a time.

For each Archetype, the panel shows: 

| **Property** | **Description** |
|---|---|
| Archetype name | The Archetype name is its hash, which you can use to find the Archetype again across future Unity sessions. |
| Entities | Number of Entities within the selected Archetype. |
| Unused Entities | The total number of Entities that can fit into all available chunks for the selected Archetype, minus the number of active Entities (represented by the Entities stat). |
| Chunks | Number of [chunks](ecs_core.md#memory-chunks) this Archetype uses. |
| Chunk Capacity | The number of Entities with this Archetype that can fit into a chunk. This number is equal to the total number of **Entities** and **Unused Entities**. |
| Components | Displays the total number of Components in the Archetype and the total amount of memory assigned to them in KB. <br/><br/>To see the list of Components and their individual memory allocation, expand this section. |
| External Components | Lists the [Chunk Components](ecs_chunk_component.md) and [Shared Components](shared_component_data.md) that affect this Archetype. |

## Additional resources

* [Archetype user manual](ecs_core.md#archetypes)
* [Chunk user manual](ecs_core.md#memory-chunks)
* [Shared Components user manual](shared_component_data.md)
* [Chunk Components user manual](ecs_chunk_component.md)
