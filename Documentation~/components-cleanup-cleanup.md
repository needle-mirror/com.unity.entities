# Use cleanup components to perform cleanup

The primary use case for [cleanup components](components-cleanup.md) is to help you manage entities that require cleanup when destroyed. To make this possible, they prevent you from destroying entities that contains a cleanup component. Instead, when you try to destroy an entity with an attached cleanup component, Unity removes all non-cleanup components instead. The entity still exists until you remove all cleanup Components from it.

To perform cleanup for entities of a specific archetype:

1. [Create a new tag component](components-tag-create.md) and add the tag component to the archetype.
2. [Create a new cleanup component](components-cleanup-create.md) that contains information required to clean up a certain entity archetype.
3. Create a system that:
   1. Gets newly created entities of the target archetype. These are entities that contain the tag Component but not the cleanup component.
   2. Adds the cleanup component to these entities.
4. Create a system that:
   1. Gets the entities that have been provisionally destroyed and require cleanup. These are entities that contain the cleanup component, but not the tag component.
   2. Performs the appropriate cleanup work for the entities.
   3. Remove the cleanup component(s) from the entities.
