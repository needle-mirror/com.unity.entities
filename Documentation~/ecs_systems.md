---
uid: ecs-systems
---
# Systems

A **System**, the *S* in ECS,  provides the logic that transforms the component data from its current state to its next state — for example, a system might update the positions of all moving entities by their velocity multiplied by the time interval since the previous update.

![](images/BasicSystem.png)

## Instantiating systems

Unity ECS automatically discovers system classes in your project and instantiates them at runtime. It adds each discovered system to one of the default system groups. You can use [system attributes] to specify the parent group of a system and the order of that system within the group . If you do not specify a parent, Unity adds the system to the Simulation system group of the default world in a deterministic, but unspecified, order. You can also use an attribute to disable automatic creation.

A system's update loop is driven by its parent [ComponentSystemGroup]. A ComponentSystemGroup is, itself, a specialized kind of system that is responsible for updating its child systems. Groups can be nested. Systems derive their [time] data from the [World] they are running in; time is updated by the [UpdateWorldTimeSystem].

You can view the system configuration using the Entity Debugger window (menu: **Window** > **Analysis** > **Entity Debugger**). 

<a name="types"></a>
## System types

Unity ECS provides several types of systems. In general, the systems you write to implement your game behavior and data transformations will extend [SystemBase]. The other system classes have specialized purposes. You typically use existing instances of the [EntityCommandBufferSystem] and [ComponentSystemGroup] classes. 

* [SystemBase] -- the base class to implement when creating systems.
* [EntityCommandBufferSystem] -- provides [EntityCommandBuffer] instances for other systems. Each of the default system groups maintains an Entity Command Buffer System at the beginning and end of its list of child systems. This allows you to group structural changes so that they incur fewer [synchronization points] in a frame.
* [ComponentSystemGroup] -- provides nested organization and update order for other systems. Unity ECS creates several Component System Groups by default.
* [GameObjectConversionSystem] -- converts GameObject-based, in-Editor representations of your game to efficient, entity-based, runtime representations. Game conversion systems run in the Unity Editor.

[ComponentSystemGroup]: xref:ecs-system-update-order
[EntityCommandBufferSystem]: xref:ecs-entity-command-buffer
[EntityCommandBuffer]: xref:Unity.Entities.EntityCommandBuffer
[OnCreate()]: xref:Unity.Entities.ComponentSystemBase.OnCreate*
[OnDestroy()]: xref:Unity.Entities.ComponentSystemBase.OnDestroy*
[OnStartRunning()]: xref:Unity.Entities.ComponentSystemBase.OnStartRunning*
[OnStopRunning()]: xref:Unity.Entities.ComponentSystemBase.OnStopRunning*
[OnUpdate()]: xref:Unity.Entities.SystemBase.OnUpdate*
[synchronization points]: xref:sync-points
[system attributes]: system_update_order.md#attributes
[SystemBase]: xref:Unity.Entities.SystemBase
[World]: xref:Unity.Entities.World
[GameObject conversion systems]: gp_overview.md
[GameObjectConversionSystem]: gp_overview.md
[time]: xref:Unity.Core.TimeData
[World]: xref:Unity.Entities.World
[UpdateWorldTimeSystem]: xref:Unity.Entities.UpdateWorldTimeSystem
[system events]: #system-events
[C# Job System]: https://docs.unity3d.com/Manual/JobSystem.html
[system groups]: system_update_order.md#groups
[system attributes]: system_update_order.md#attributes
[ComponentSystem]: https://docs.unity3d.com/Packages/com.unity.entities@0.5/manual/entity_iteration_foreach.html

## Systems in the Editor

In the Editor, the following icons represent the different types of Systems. You’ll see this when you use the specific [Entities windows and Inspectors](editor-workflows.md).

|**Icon**|**Represents**|
|---|---|
|![](images/editor-system-group.png)| A System group|
|![](images/editor-system.png)| A System|
|![](images/editor-system-start-step.png)| An Entity Command Buffer System that is set to execute at the beginning of a System Group using the [OrderFirst](xref:Unity.Entities.UpdateInGroupAttribute.OrderFirst) argument.|
|![](images/editor-system-end-step.png)| An Entity Command Buffer System that is set to execute at the end of a System Group using the [OrderLast]((xref:Unity.Entities.UpdateInGroupAttribute.OrderLast)) argument.|
