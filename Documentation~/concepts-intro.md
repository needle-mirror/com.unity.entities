# Entities package concepts
 
The Entities package uses the entity component system (ECS) architecture to organize code and data. An **entity** is a unique identifier, like a lightweight unmanaged alternative to a GameObject. An entity acts as an ID associated with individual **components** that contain data about the entity. Unlike GameObjects, entities contain no code: they're units of data that the systems you create process. 

|**Topic**|**Description**|
|---|---|
|[Entity concepts](concepts-entities.md)|An entity is a lightweight alternative to a GameObject which contains no code.|
|[Component concepts](concepts-components.md)|A component contains data about an individual entity.|
|[World concepts](concepts-worlds.md)|A world organizes entities into isolated groups.|
|[Archetypes concepts](concepts-archetypes.md)|An archetype is a unique combination of components, which one or several entities might have.|
|[Structural changes](concepts-structural-changes.md)|Structural changes are resource-intensive operations that happen that affect the performance of your application.|
|[System concepts](concepts-systems.md)|Add code to systems to process entities and components.|
