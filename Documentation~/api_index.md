---
uid: api-index
---

# Entity Component System API reference


| Entity types | |
| :--- | :--- |
| [Entity] | The fundamental identifier in ECS |
| [EntityArchetype] | A unique combination of component types |
| [EntityQuery] | Use to select entities with specific characteristics |
| [EntityQueryDesc] | Use to create [EntityQuery] objects |
| [EntityManager] | Manages entities and provides utility methods |
| [World] | An isolated collection of entities |

| Component types | |
| :--- | :--- |
| [IComponentData] | A marker interface for general purpose components |
| [ISharedComponentData] | A marker interface for components shared by many entities |
| [ISystemStateComponentData] | A marker interface for specialized system components |
| [IBufferElementData] | A marker interface for buffer elements |
| [DynamicBuffer] | The API to access buffer elements |
| [BlobAssetReference] | A reference to a blob asset in a component |

| System types | |
| :--- | :--- |
| [ComponentSystemBase] | Defines a set of basic functionality for systems |
| [SystemBase] | The base class to extend when writing an ECS system |
| [GameObjectConversionSystem] | The base class to extend when writing GameObject conversion systems |
| [ComponentSystemGroup] | A group of systems that update as a unit |

| ECS job types | |
| :--- | :--- |
| [Entities.ForEach] | An implicitly created job that iterates over a set of entities |
| [Job.WithCode] | An implicitly created single job |
| [IJobEntityBatch] | An interface to implement to explicitly create a job that iterates over the entities returned by an entity query in batches |

| Other important types | |
| :--- | :--- |
| [ArchetypeChunk] | The storage unit for entity components |
| [EntityCommandBuffer] | A buffer for recording entity modifications used to reduce structural changes |
| [ComponentType] | Use to define types when creating entity queries |
| [BlobBuilder] | A utility class for creating blob assets, which are immutable data structures that can be safely shared between jobs using [BlobAssetReference] instances |
| [ICustomBootstrap] | An interface to implement to create your own system loop |

| Attributes | |
| :--- | :--- |
| [UpdateInGroup] | Defines the [ComponentSystemGroup] to which a system should be added |
| [UpdateBefore] | Specifies that one system must update before another |
| [UpdateAfter] | Specifies that one system must update after another|
| [DisableAutoCreation] | Prevents a system from being automatically discovered and run when your application starts up |
| [ExecuteAlways] | Specifies that a system's update function must be invoked every frame, even when no entities are returned by the system's entity query |
| [GenerateAuthoringComponent] | Generates a MonoBehaviour-based Component for an ECS IComponentData struct, allowing you to add it directly to a GameObject in the Unity Editor |
| [ConverterVersion] | Use to ensure that serialized data is up to date with conversion code |

<!-- cross references -->
[Entity]: xref:Unity.Entities.Entity
[EntityArchetype]: xref:Unity.Entities.EntityArchetype
[EntityQuery]: xref:Unity.Entities.EntityQuery
[EntityQueryDesc]: xref:Unity.Entities.EntityQueryDesc
[EntityManager]: xref:Unity.Entities.EntityManager
[World]: xref:Unity.Entities.World
[IComponentData]: xref:Unity.Entities.IComponentData
[ISharedComponentData]: xref:Unity.Entities.ISharedComponentData
[ISystemStateComponentData]: xref:Unity.Entities.ISystemStateComponentData
[IBufferElementData]: xref:Unity.Entities.IBufferElementData
[DynamicBuffer]: xref:Unity.Entities.DynamicBuffer`1
[ComponentSystemBase]: xref:Unity.Entities.ComponentSystemBase
[SystemBase]: xref:Unity.Entities.SystemBase
[GameObjectConversionSystem]: xref:Global%20Namespace.GameObjectConversionSystem
[ComponentSystemGroup]: xref:Unity.Entities.ComponentSystemGroup
[Entities.ForEach]: xref:Unity.Entities.SystemBase.Entities
[Job.WithCode]: xref:Unity.Entities.SystemBase.Job
[IJobEntityBatch]: xref:Unity.Entities.IJobEntityBatch
[ArchetypeChunk]: xref:Unity.Entities.ArchetypeChunk
[EntityCommandBuffer]: xref:Unity.Entities.EntityCommandBuffer
[ICustomBootstrap]: xref:Unity.Entities.ICustomBootstrap
[ComponentType]: xref:Unity.Entities.ComponentType
[BlobBuilder]: xref:Unity.Entities.BlobBuilder
[UpdateInGroup]: xref:Unity.Entities.UpdateInGroupAttribute
[UpdateBefore]: xref:Unity.Entities.UpdateBeforeAttribute
[UpdateAfter]: xref:Unity.Entities.UpdateAfterAttribute
[DisableAutoCreation]: xref:Unity.Entities.DisableAutoCreationAttribute
[ExecuteAlways]: xref:UnityEngine.ExecuteAlways
[GenerateAuthoringComponent]: xref:Unity.Entities.GenerateAuthoringComponentAttribute
[ConverterVersion]: xref:Unity.Entities.ConverterVersionAttribute
