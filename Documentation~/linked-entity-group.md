# Linked entity groups

A [`LinkedEntityGroup`](xref:Unity.Entities.LinkedEntityGroup) instance is a [`Dynamic Buffer`](components-buffer.md) that has special semantics:

* **Instantiating**: [`EntityManager.Instantiate`](xref:Unity.Entities.EntityManager.Instantiate*) instantiates all the entities which are part of the group.
* **Destroying**: [`EntityManager.DestroyEntity`](xref:Unity.Entities.EntityManager.DestroyEntity*) destroys all the entities which are part of the group.
* **Enabling or disabling**: [`EntityManager.SetEnabled`](xref:Unity.Entities.EntityManager.SetEnabled*) adds or removes the `Disabled` tag component on all the entities which are part of the group.

The first element of an entity's `LinkedEntityGroup` buffer must always be the entity itself.

[Prefabs that the baking process creates](baking-prefabs.md) always have a `LinkedEntityGroup` at the root. Instances created from those prefabs also have one.

## Working with linked entity groups

`LinkedEntityGroup` and [transform hierarchy](transforms-concepts.md#transform-hierarchy) are separate concepts. For example, adding children under a parent with a `LinkedEntityGroup` doesn't automatically add them to the `LinkedEntityGroup`. Similarly, removing entities from a `LinkedEntityGroup` doesn't remove them from the parent.

Unity doesn't process `LinkedEntityGroup` instances recursively. If an entity which is part of a `LinkedEntityGroup` A has a `LinkedEntityGroup` B of its own, processing `LinkedEntityGroup` A doesn't include the contents of `LinkedEntityGroup` B. To prevent confusion, avoid nesting groups.

### Destroying entities

`LinkedEntityGroup` must only contain valid entities. When entities which are part of a `LinkedEntityGroup` are individually destroyed, they also have to be explicitly removed from the group. 

When destroying entities [with a query](systems-entityquery-intro.md), either all the entities within a `LinkedEntityGroup` need to match the query, or that none of them match. Therefore, the contents of a `LinkedEntityGroup` can't partially match the query. 

This is relevant if you use entity scenes, because when Unity unloads an entity scene, it uses the `SceneTag` shared component value of the entity scene to identify the entities that it needs to destroy. When you add entities to `LinkedEntityGroups` which are part of a scene, make sure that those entities have the proper `SceneTag`.

## Add an entity to a linked entity group

The following is an example of how to add an entity to a linked entity group in a system:

[!code-cs[LinkedEntityGroup example](../DocCodeSamples.Tests/LinkedEntityGroupExample.cs#linked-entity-group)]        

## Additional resources
* [Dynamic buffer components](components-buffer.md)
* [Entity concepts](concepts-entities.md)
* [System concepts](concepts-systems.md)