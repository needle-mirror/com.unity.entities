---
uid: ecs-whats-new
---

# What's new in Entities 1.4

This section contains information about new features, improvements, and issues fixed in Entities 1.4.

For a complete list of changes made in Entities 1.4, refer to the [Changelog](xref:changelog).

## Deprecated API

In this release, the following API are marked as obsolete:

* The [`Entities.ForEach`](xref:Unity.Entities.SystemBase.Entities) and [`Job.WithCode`](xref:Unity.Entities.SystemBase.Job) methods are marked as obsolete.
    
    Use the following API instead:
            
    *    Use [`IJobEntity`](xref:Unity.Entities.IJobEntity) and [`SystemAPI.Query`](xref:Unity.Entities.SystemAPI.Query*) instead of [`Entities.ForEach`](xref:Unity.Entities.SystemBase.Entities). 

    *    Use [`IJob`](https://docs.unity3d.com/6000.1/Documentation/ScriptReference/Unity.Jobs.IJob.html) instead of [`Job.WithCode`](xref:Unity.Entities.SystemBase.Job). 

* The [`IAspect`](xref:Unity.Entities.IAspect) interface is marked as obsolete. Use the [`Component`](components-read-and-write.md) and [`EntityQuery`](xref:Unity.Entities.EntityQuery) APIs instead.

* The [`ComponentLookup.GetRefRWOptional`](xref:Unity.Entities.ComponentLookup`1.GetRefRWOptional*) and [`GetRefROOptional`](xref:Unity.Entities.ComponentLookup`1.GetRefROOptional*) methods are marked as obsolete. These methods were designed for Aspect source generation and were intended for internal use. Use [`TryGetRefRO`](xref:Unity.Entities.ComponentLookup`1.TryGetRefRO*) and [`TryGetRefRW`](xref:Unity.Entities.ComponentLookup`1.TryGetRefRW*) methods instead for improved safety and clarity.

All of these APIs are supported in the Entities 1.x package but will be removed in a future major release of Entities.

For more information on upgrading to Entities 1.4, refer to the [upgrade guide](upgrade-guide.md).

## Improvements

This release adds improvements in usability, performance, and workflows, while also expanding API capabilities and documentation coverage.

* System Inspector window improvements:

    * The **Queries** tab now displays the following components when executing a query: Disabled, Present, Absent, and None.

    * Added the **Dependencies** tab, which displays which components a system depends on.

* **Query** window improvement: the window highlights prefabs with appropriate icons to differentiate them from other entities.

* The [`WorldUnmanaged`](xref:Unity.Entities.WorldUnmanaged) struct now has the [`GetSystemTypeIndex(SystemHandle SystemHandle)`](xref:Unity.Entities.WorldUnmanaged.GetSystemTypeIndex(Unity.Entities.SystemHandle)) method, which lets you get the `SystemTypeIndex` from a `SystemHandle`.

* [RemoteContentCatalogBuildUtility.PublishContent](xref:Unity.Entities.Content.RemoteContentCatalogBuildUtility.PublishContent*) method improvements:

    * The method now creates content sets for all objects and scenes, using the `UntypedWeakReferenceId.ToString` method as the name of the set. This enables downloading dependencies of specific objects and scenes. For subscenes, a content set is named with a GUID and contains the header, all entity section files, and any Unity object references in content archives.
    * The method now creates the `DebugCatalog.txt` text file containing all remapping information in the root of the remote content folder. This file shows how each file is remapped to its cache location and what its `RemoteContentId` is. It also lists all content sets defined during the Publish step.
    * The method now accepts an enumerable file list rather than a directory name. This ensures that content updates include only the specified files in the catalog rather than all files in a folder.

* Safe component access: The new [`ComponentLookup.TryGetRefRW`](xref:Unity.Entities.ComponentLookup`1.TryGetRefRW*) and [`ComponentLookup.TryGetRefRO`](xref:Unity.Entities.ComponentLookup`1.TryGetRefRO*) methods let you safely check if a component exists, and retrieve it from an entity in a single method call.

* Custom Editor enhancement: Added an `OnGUI` override in the `WeakReferencePropertyDrawer` class to ensure proper display of the `WeakObjectReference`, `WeakObjectSceneReference`, `EntitySceneReference`, and `EntityPrefabReference` fields.

* World bootstrapping management: Added the [`DisableBootstrapOverridesAttribute`](xref:Unity.Entities.DisableBootstrapOverridesAttribute) attribute for types or assemblies that use [`ICustomBootstrap`](xref:Unity.Entities.ICustomBootstrap) to prevent unintended bootstrap implementations.

* Buffer API improvements: Added methods [`GetBufferAccessorRO`](xref:Unity.Entities.ArchetypeChunk.GetBufferAccessorRO*) and [`GetBufferAccessorRW`](xref:Unity.Entities.ArchetypeChunk.GetBufferAccessorRW*) to the `ArchetypeChunk` struct. These methods enable callers to explicitly request a specific access mode for a buffer component instead of relying on the implicit access mode of the buffer type handle. This enables users to request read-only access from a read-write buffer type handle without introducing unnecessary write dependencies.

* Advanced buffer handling: Added a new [`GetUntypedBufferAccessorReinterpret<T>`](xref:Unity.Entities.ArchetypeChunk.GetUntypedBufferAccessorReinterpret*) method to the `ArchetypeChunk` struct, which is equivalent to the existing [`GetDynamicComponentDataArrayReinterpret<T>`](xref:Unity.Entities.ArchetypeChunk.GetDynamicComponentDataArrayReinterpret*) method but is designed for buffer components. This enables you to create a compile-time-typed `BufferAccessor<T>` from a runtime-typed `DynamicComponentTypeHandle`, including an accessor to a different type than the one stored in the handle, in case the types are safely aliasable in memory.

## Performance improvements

* Optimized performance of the following methods: [`SetSharedComponentManaged`](xref:Unity.Entities.EntityCommandBuffer.SetSharedComponentManaged*), [`IEntitiesPlayerSettings.GetFilterSettings`](xref:Unity.Entities.Build.IEntitiesPlayerSettings.GetFilterSettings*).

* The [TypeManager.Initialize](xref:Unity.Entities.TypeManager.Initialize*) method is approximately twice as fast on startup in player builds in large projects with many assemblies that don't reference `Unity.Entities.dll`.

    The method now does not scan all player assemblies on startup for types inheriting from `UnityEngine.Object` (for example, `MonoBehaviour`, `ScriptableObject`). If Unity is raising errors because a type in a project is not registered with the `TypeManager` (for example, because you need to use it in a query), register it explicitly using 
[`[assembly: RegisterUnityEngineComponentType(typeof(YourParticularType))]`](xref:Unity.Entities.RegisterUnityEngineComponentTypeAttribute).
  
* Improved the performance of the `ChunkEntityEnumerator` constructor in every case except when running with argument `useEnabledMask = false` on Mono, in which case it's about twice as slow compared to the previous version. If your application uses the constructor with such argument, use a for loop instead.

## Documentation improvements

* Added documentation on storing references to `UnityEngine.Object` types using the [`UnityObjectRef`](reference-unity-objects.md) API.

* Expanded documentation on the [LinkedEntityGroup](linked-entity-group.md) buffer and [Transforms in entities](transforms-intro.md).

## Additional resources

* [Upgrade guide](upgrade-guide.md)
* [Changelog](xref:changelog)
