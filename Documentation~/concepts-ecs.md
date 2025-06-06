# Entity component system introduction

The Entities package uses the entity component system (ECS) architecture to organize code and data for some of its processes. An [entity](concepts-entities.md) is a unique identifier, like a lightweight unmanaged alternative to a GameObject. An entity acts as an ID associated with individual components that contain data about the entity. Unlike types that inherit from MonoBehaviour, entities contain no code, or data: they're a reference to units of data that the systems you create process.

The following diagram illustrates how entities, components, and systems work together:

![A conceptual diagram, with Entity A and B sharing the same components of Speed, Direction, Position, and Renderer, plus Entity C having just Speed, Direction, and Position. Entity A and B share an archetype. A system in the middle of the diagram manipulates the Position, Speed, and Direction components.](images/entities-concepts.png)

In this diagram, a [system](concepts-systems.md) reads Speed and Direction [components](concepts-components.md), multiplies them and then updates the corresponding Position components.

The fact that entities A and B have a `Renderer` component but entity C doesn't makes no difference to the system, because the system has no knowledge of `Renderer` components.

You can set up a system so that it requires a `Renderer` component, in which case, the system ignores the components of entity C. Alternatively, you can set up a system to exclude entities with `Renderer` components, which then ignores the components of entities A and B.

The diagram also illustrates the concept of an [archetype](concepts-archetypes.md), which is a unique combination of component types.

## Additional resources
* [Entity concepts](concepts-entities.md)
* [Component concepts](concepts-components.md)
* [System concepts](concepts-systems.md)