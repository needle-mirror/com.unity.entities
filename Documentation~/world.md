---
uid: ecs-world
---
# World

A [World] organizes entities into isolated groups. A world owns both an [EntityManager](xref:Unity.Entities.EntityManager) and a set of [Systems](ecs_systems.md). Entities created in one world only have meaning in that world, but can be transfered to other worlds (with [EntityManager.MoveEntitiesFrom]). Systems can only access entities in the same world. You can create as many worlds as you like.

By default Unity creates a default [World] when your application starts up (or you enter __Play Mode__). Unity instantiates all systems (classes that extend [ComponentSystemBase]) and adds them to this default world. Unity also creates specialized worlds in the Editor. For example, it creates an Editor world for entities and systems that run only in the Editor, not in playmode and also creates conversion worlds for managing the conversion of GameObjects to entities. See [WorldFlags] for examples of different types of worlds that can be created.

Use [World.DefaultGameObjectInjectionWorld] to access the default world.

## Managing systems

The [World] object provides methods for creating, accessing and removing systems from the world.

In most cases, you can use [GetOrCreateSystem] to get an instance of a system (creating an instance if one doesn't already exist).

## Time

The value of the [Time] property of systems is controlled by the [World] a system is in. By default, Unity creates a [TimeData] entity for each world, which is updated by a [UpdateWorldTimeSystem] instance to reflect the elapsed time since the previous frame. A system's [Time] property is an alias for the current world time. 

The [FixedStepSimulationSystemGroup] treats time differently than other system groups. Instead of updating once at the current delta time, systems in the fixed step simulation group update at a fixed interval and might update more than once per frame if the fixed interval is a small enough fraction of the frame time.

If you need finer control of time in a [World], you can specify a time value directly with [World.SetTime]. You can also [PushTime] to temporarily change the world time and then [PopTime] to return to the previous time (in a time stack).

## Custom initialization

To initialize you game manually at startup, you can implement the [ICustomBootstrap] interface. Unity runs your `ICustomBootstrap` implementation with the default world so that you can modify or entirely replace the system creation and initialization sequence.
 
You can also disable the default World creation entirely by defining the following global symbols:

* `#UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_RUNTIME_WORLD` disables generation of the default runtime World.
* `#UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_EDITOR_WORLD` disables generation of the default Editor World.
* `#UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP` disables generation of both default Worlds.

Your code is then responsible for creating any needed worlds, as well as instantiating and updating systems. You can use the Unity scriptable [PlayerLoop] to modify the normal Unity player loop so that your systems are updated when required.

 [World]: xref:Unity.Entities.World
 [EntityManager]: xref:Unity.Entities.EntityManager
 [Systems]: ecs_systems.md
 [ComponentSystemBase]: xref:Unity.Entities.ComponentSystemBase
 [ICustomBootstrap]: xref:Unity.Entities.ICustomBootstrap
[PlayerLoop]: xref:UnityEngine.LowLevel.PlayerLoop
[EntityManager.MoveEntitiesFrom]: xref:Unity.Entities.EntityManager.MoveEntitiesFrom*
[WorldFlags]: xref:Unity.Entities.WorldFlags
[World.DefaultGameObjectInjectionWorld]: xref:Unity.Entities.World.DefaultGameObjectInjectionWorld
[GetOrCreateSystem]: xref:Unity.Entities.World.GetOrCreateSystem*
[FixedStepSimulationSystemGroup]: xref:Unity.Entities.FixedStepSimulationSystemGroup
[World.SetTime]: xref:Unity.Entities.World.SetTime(Unity.Core.TimeData)
[TimeData]: xref:Unity.Core.TimeData
[UpdateWorldTimeSystem]: xref:Unity.Entities.UpdateWorldTimeSystem
[PushTime]: xref:Unity.Entities.World.PushTime(Unity.Core.TimeData)
[PopTime]:  xref:Unity.Entities.World.PopTime
[Time]: xref:Unity.Entities.ComponentSystemBase.Time
