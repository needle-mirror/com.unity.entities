---
uid: systems-update-order
---

# System groups

A system group can have systems and other system groups as its children. A system group has an update method that you can override, and the base method updates the group's children in a sorted order. 

To create a system group, create a class that inherits from [`ComponentSystemGroup`](xref:Unity.Entities.ComponentSystemGroup). Because systems belong to a world, you must use [`World.GetOrCreateSystem`](xref:Unity.Entities.World.GetOrCreateSystem*) to create a system. To add a system to a group, use `group.AddSystemToUpdateList`. You can add other system groups to existing system groups.

## Update order of systems

Every time you add a group to a system group, it re-sorts the system update order. To control the update order of a system group, add the [`UpdateBefore`](xref:Unity.Entities.UpdateBeforeAttribute) or [`UpdateAfter`](xref:Unity.Entities.UpdateAfterAttribute) attribute to a system to specify which systems it should update before or after. These attributes only apply relative to children of the same system group. For example:

```c#
// If PeachSystem and DaisySystem are children of the same group, then the
// entity component system puts PeachSystem somewhere before DaisySystem in 
// the sorted order.
    [UpdateBefore(typeof(DaisySystem))]
    public partial class PeachSystem : SystemBase { }
```

There are a set of [default system groups](#default-system-groups) that you can use to update systems in the correct phase of a frame. You can nest one group inside another so that all systems in your group update in the correct phase and update according to the order within their group.

## Component system groups

The [`ComponentSystemGroup`](xref:Unity.Entities.ComponentSystemGroup) class represents a list of related component systems that Unity must update together in a specific order. `ComponentSystemGroup` inherits from [`ComponentSystemBase`](xref:Unity.Entities.ComponentSystemBase), so you can order it relative to other systems, and it has an `OnUpdate()` method. This also means that you can nest a `ComponentSystemGroup` in another `ComponentSystemGroup`, and form a hierarchy.

By default, when you call the `Update()` method in `ComponentSystemGroup`, it calls `Update()` on each system in its sorted list of member systems. If any member systems are system groups, they recursively update their own members. The resulting system ordering follows a depth-first traversal of a tree.

## System ordering attributes

You can use the following attributes on a system to determine its update order:

|**Attribute**|**Description**|
|---|---|
|`UpdateInGroup`| Specify a `ComponentSystemGroup` that this system should be a member of. If you don't set this attribute, Unity automatically adds it to the default world's `SimulationSystemGroup`. For more information, see the section on [Default system groups](#default-system-groups).|
|`UpdateBefore`<br/>`UpdateAfter`| Order systems relative to other systems. The system type specified for these attributes must be a member of the same group. Unity handles ordering across group boundaries at the appropriate deepest group that contains both systems.<br/><br/> For example, if `CarSystem` is in `CarGroup`, and `TruckSystem` is in `TruckGroup`, and `CarGroup` and `TruckGroup` are both members of `VehicleGroup`, then the ordering of `CarGroup` and `TruckGroup` implicitly determines the relative ordering of `CarSystem` and `TruckSystem`. You don't need to explicitly order the systems.|
|`CreateBefore`<br/>`CreateAfter`| Order system creation relative to other systems. The same ordering rules for `UpdateBefore` and `UpdateAfter` apply here. By default, systems are created in the same order they are updated. These attributes override the default behavior. System destruction order is defined as the reverse of creation order.|
|`DisableAutoCreation`|Prevents Unity from creating the system during the default world initialization. You must explicitly create and update the system. However, you can add a system with this tag to a `ComponentSystemGroup`’s update list, and it automatically updates just like the other systems in that list.|

If you add the `DisableAutoCreation` attribute to a component system or system group, Unity doesn't create it or add it to the default system groups. To manually create the system, use [`World.GetOrCreateSystem<MySystem>()`](xref:Unity.Entities.World.GetOrCreateSystem*) and call `MySystem.Update()` from the main thread to update it. You can use this to insert systems elsewhere in the Unity player loop, for example, if you have a system that should run later or earlier in the frame.

## Default system groups

The default World contains a hierarchy of `ComponentSystemGroup` instances. There are three root-level system groups in the Unity player loop:

* InitializationSystemGroup: Updates at the end of the `Initialization` phase of the player loop.
* SimulationSystemGroup: Updates at the end of the `Update` phase of the player loop.
* PresentationSystemGroup: Updates at the end of the `PreLateUpdate` phase of the player loop.

The default system groups also have a number of pre-defined member systems:

**InitializationSystemGroup:**

* BeginInitializationEntityCommandBufferSystem
* CopyInitialTransformFromGameObjectSystem
* SubSceneLiveConversionSystem
* SubSceneStreamingSystem
* EndInitializationEntityCommandBufferSystem

**SimulationSystemGroup:**

* BeginSimulationEntityCommandBufferSystem
* TransformSystemGroup
    * ParentSystem
    * CopyTransformFromGameObjectSystem
    * TRSToLocalToWorldSystem
    * TRSToLocalToParentSystem
    * LocalToParentSystem
    * CopyTransformToGameObjectSystem
* LateSimulationSystemGroup
* EndSimulationEntityCommandBufferSystem

**PresentationSystemGroup:**

* BeginPresentationEntityCommandBufferSystem
* CreateMissingRenderBoundsFromMeshRenderer
* RenderingSystemBootstrap
* RenderBoundsUpdateSystem
* RenderMeshSystem
* LODGroupSystemV1
* LodRequirementsUpdateSystem
* EndPresentationEntityCommandBufferSystem

Note that the specific contents of this list is subject to change.

## Multiple worlds

You can create multiple [worlds](concepts-worlds.md) and you can instantiate the same component system class in more than one world. You can also update each instance at different rates from different points in the update order.

You can't manually update every system in a given world, but you can control which systems are created in which world, and which of the existing system groups to add them to. 

For example, you can create a custom world that instantiates `SystemX` and `SystemY`, and add `SystemX` to the default world’s SimulationSystemGroup, and add `SystemY` to the default world’s PresentationSystemGroup. These systems can order themselves relative to their group siblings as usual, and Unity updates them along with the corresponding group.

You can also use the [`ICustomBootstrap`](xref:Unity.Entities.ICustomBootstrap) interface to manage systems in multiple worlds:

``` c#
public interface ICustomBootstrap
{
    // Returns the systems which should be handled by the default bootstrap process.
    // If null is returned the default world will not be created at all.
    // Empty list creates default world and entrypoints
    List<Type> Initialize(List<Type> systems);
}
```

When you implement this interface, it passes the full list of component system types to the  `Initialize()` method, before the default world initialization. A custom bootstrapper can iterate through this list and create systems in the worlds you define. You can return a list of systems from the `Initialize()` method and Unity creates them as part of the default world initialization.

For example, here’s the typical procedure of a custom `MyCustomBootstrap.Initialize()` implementation:

1. Create any additional worlds and their top-level `ComponentSystemGroups`.
1. For each Type in the system Type list:
    1. Search upward through the `ComponentSystemGroup` hierarchy to find this system `Type`’s top-level group.
    1. If it’s one of the groups created in step 1, create the system in that world and add it to the hierarchy with `group.AddSystemToUpdateList()`.
    1. If not, append this `Type` to the `List` to return to `DefaultWorldInitialization`.
1. Call `group.SortSystemUpdateList()` on new top-level groups.
    1. Optionally add them to one of the default world groups
1. Return a list of unhandled systems to `DefaultWorldInitialization`.

> [!NOTE]
> The ECS framework finds your `ICustomBootstrap` implementation by reflection.
