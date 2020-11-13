# Change log

## [0.17.0] - 2020-11-13

### Added

* ISystemBase interface for making struct based systems that allow Burst compiling main thread update
* New UnsafeBufferAccessor struct that permit to un-typed and unsafe access the dynamic buffers pointers.
* New ArchetypeChunk.GetUnsafeAccessor public API that allow to retrieve dynamic buffers unsafe accessors using DynamicTypeHandle.
* safety check in DynamicComponentDataArrayReinterpret<T> that throw an ArgumentException if used to access the component data for IBufferDataElement type
* ComponentSystemBase.TryGetSingleton
* ComponentSystemBase.TryGetSingletonEntity
* Tests for Tiny.UI transformations.
* Added documentation for incremental conversion and dependencies
* New scheduling API for `IJobEntityBatch` which limits the resulting batches to an input `NativeArray<Entity>`
* BlobAssetStore. AddUniqueBlobAsset. A much simpler approach to managing blob assets during conversion. BlobAssetComputationContext continues to be the recommended approach for scalable blob asset generation.
* UnsafeUntypedBlobAsset gives a non-generic untyped blob that can be created and later casted to a specific BlobAssetType. This can be used for storing multiple types of blob assets in the same container.
* GameObjectConversionSystem.IsBuildingForEditor returns true when building data that will be loaded by the editor (As opposed to building data that will be loaded by the standalone player). This can be used to strip out editor only debug data.
* GameObjectConversionSystem.BuildConfigGUID returns the GUID of the build config that is used by this conversion context
* Tiny.UI support for 9-Slice (including sprite sheet support)
* `DynamicSharedComponentHandle` and related methods for accessing shared components without compile time type information.
* The public static method `EntitySelectionProxy.CreateInstance` was added. It creates, configures, and returns a valid instance of `EntitySelectionProxy`.
* The public static method `EntitySelectionProxy.SelectEntity` was added. It creates, configures, and selects an instance of `EntitySelectionProxy`, without returning it.
* All the public properties and methods of `EntitySelectionProxy` have been documented.
* Tiny.UI support for text alignment
* Tiny.UI support for multi-line text
* TypeManager will now store whether or not an `ISharedComponentData` is managed or unmanaged.
* EntityScenesInBuild class that allows registering EntityScenes that are generated via a custom asset importer into the build. This is used by the Environment system to generate streamable tile data that is generated procedurally from tile inputs.
* New EntityCommandBuffer methods that affect a set of entities matching a query. Unlike existing methods, these new methods 'capture' the entities from the query at record time rather than playback time: the array of entities is stored in the command, and then playback of the command affects all entities of the array. The methods are `AddComponentForEntityQuery(EntityQuery, ComponentType)`, `AddComponentForEntityQuery(EntityQuery, ComponentTypes)`, `RemoveComponentForEntityQuery(EntityQuery, ComponentType)`, `RemoveComponentForEntityQuery(EntityQuery, ComponentTypes)`, `DestroyEntitiesForEntityQuery(EntityQuery)`.
* EntityManager.Debug.GetEntitiesForAuthoringObject and EntityManager.Debug.GetAuthoringObjectForEntity. They provide a convenient API to map game object authoring & entity runtime representation.
* New `ComponentSystemGroup.EnableSystemSorting` property allows individual system groups to opt out of automatic system sorting. **PLEASE NOTE:** Certain system update order constraints are necessary for correct DOTS functionality. Disabling the automatic system sorting should be only be a last resort, and only on system groups with full control over which systems they contain.
* Entities.WithFilter(NativeArray<Entity> filteredEntities) allows for filtering with a set of specific entities in addition to the EntityQuery requirements
* Added Live Conversion debug logging to more easily see what is reconverted (enable from the menu `DOTS/LiveLink Mode/Incremental Conversion Logging`)

### Changed

* Update burst to 1.4.1.
* Improved the performance of ILPostProcessor type resolution.
* ProcessAfterLoadGroup is now public.  This group runs after a subscene is loaded.
* `Unity.Transforms` systems now use `IJobEntityBatch` instead of `IJobChunk`. Expect modest performance gains due to the new job type's lower scheduling overhead, depending on the workload size.
* When `DOTS/Live Link Mode/Live Conversion in Edit Mode` is active in 2020.2 or later, conversion is now incremental
* Removed deprecated `Entities.ForEach.WithDeallocateOnJobCompletion`.  Please use `Entities.ForEach.WithDisposeOnCompletion` instead.
* Fixed livelink patching for `BlobAssetReference<T>` fields in managed components and shared components.
* Updated package `com.unity.platforms` to version `0.9.0-preview.15`.
* `TypeManager.Equals` and `TypeManager.GetHashCode` performance has been improved when operating on blittable component types.
* BlobAsset and entity patching for managed IComponentData & ISharedComponentData now use an early out if the class is known to not contain any blob assets or entity references
* Managed class IComponentData now supports patching of entity references in EntityCommandBuffer.AddComponent.
* Improved EntityManager.GetCreatedAndDestroyedEntities performance by introducing an internal entity creation / destruction version number that is used to early out when calling GetCreatedAndDestroyedEntities
* TypeManger.TypeInfo.Debug has been removed. TypeName has been moved directly into TypeInfo and renamed to DebugTypeName.
* Nested or variant prefabs used in a scene now correctly trigger reimports on subscenes when the parents parent prefab changes
* Update minimum editor version to 2020.1.2f1
* `EntitySelectionProxy` was streamlined to ensure that its usage does not override inspector locking behaviour and respects the Undo / Redo stack. With the new workflow, there is a 1:1 relationship between an Entity and its EntitySelectionProxy. Static utility methods were added to support this new workflow.
* Rename `TypeManager.IsSharedComponent` to `IsSharedComponentType` and add `IsManagedType`
*Enabled generic systems to be instantiated in non-tiny dots runtime
* Updated platform packages to version `0.10.0-preview.1`.
* Made SystemBase/JobComponentSystem classes partial in preparation of use of Roslyn source generators for code-generation (more to come).

### Deprecated

* Forking of `GameObjectConversionSettings` is no longer supported
* The public delegate `EntitySelectionProxy.EntityControlSelectButtonHandler` has been deprecated.
* The public event `EntitySelectionProxy.EntityControlSelectButton` has been deprecated.
* The public method `EntitySelectionProxy.SetEntity` has been deprecated.
* The public method `EntitySelectionProxy.OnEntityControlSelectButton` has been deprecated.
* The public property `EntitySelectionProxy.EntityManager` has been deprecated. Use `EntitySelectionProxy.World.EntityManager` manager instead. This change was made to remove boilerplate checks in the code.
* Deprecated `Frozen` component as it is no longer in use

### Removed

* Removed deprecated proxy component types. CopyTransformToGameObject, CopyTransformFromGameObject and CopyInitialTransformFromGameObject now use [GenerateAuthoringComponent] instead.
* Removed expired `EntityManager.IsCreated` API
* Removed expired API to compare `EntityManager` to `null` (`EntityManager` is a struct now)
* Removed deprecated types and methods: `NativeArraySharedValue<S>`, implicit `EntityQuery` conversion to `null`,  `ComponentDataFromEntity.Exists` and `BufferFromEntity.Exists`, `ArchetypeChunkArray.GetComponentVersion`, `IJobEntityBatch.ScheduleSingle`, `IJobEntityBatch.ScheduleParallelBatch`, `EntityManager.LockChunk`, `EntityManager.UnlockChunk`, `World.AllWorlds`, `World.CreateSystem`, all `GetArchetypeChunkX` methods, `EntityCommandBuffer.ToConcurrent` and `EntityManager.CreateChunk`
* `ComponentSystemBase.ExecutingSystemType` has been removed. With the introduction of unmanaged systems, this information has been incorrect. Furthermore, there cannot be a `static` global property for this since multiple worlds might execute at the same time. If you need this information, consider passing it manually.

### Fixed

* Wrong query and check in ACS_DynamicComponentDataArrayReinterpret
* Fixed ICE (internal compiler error) thrown when storing into a field in reference type in bursted/scheduled lambda.
* `EntityQuery.ToEntityArray` will work when temp memory is passed in an a parameter for allocator
* `EntityQuery.ToComponentDataArrayAsync` and `EntityQuery.CopyFromComponentDataArrayAsync` will throw errors if user tries to use Temp memory containers.
* Hybrid Component Lights flickering when LiveLink edited.
* Fix crash when using singleton access methods with types with generic arguments.
* Code generation for indexers in structs with [BurstCompatible] attribute.
* Fixed potential `JobHandle` leak if an exception was thrown while scheduling an `IJobForEach`.
* Fixed that `DynamicBuffer.RemoveAtSwapBack` only copied the first byte of its element data
* Updating the shadow world via the EntityDiffer is now using Burst
* The 'New Sub Scene' menu item is no longer missing in the 'Create' drop down of the Hierarchy .
* Overwriting Sub Scene file when creating new Sub Scene no longer logs an error but instead overwrites the user selected file.
* Fixed livelink patching for `BlobAssetReference<T>` fields in managed components and shared components.
* Fixed entities getting lost during LiveLink when moving GameObjects between multiple subscenes
* Deprecated call to UnityWebRequest.isNetworkError in Unity.Scenes.FileUtilityHybrid
* Generic jobs now get reflection data generated in more cases
* Generic jobs will always work to schedule in editor (but may require attributes in Burst scheduling cases)
* Selecting entities in the Entity Debugger window will now respect the locked state of the inspector.
* several bugs where writing EntityBinaryFiles was not resulting in deterministic files. It is now guranteed that if entities are constructed in the same order, it will result in the same binary exact file.
* Fixed a case where LiveLink would sometimes leave dangling entities when a scene is opened and closed repeatedly
* `TypeManager.InitializeAllComponentTypes` no longer uses `DateTime.Now`, which can be very slow in players
* Structural changes right after scheduling a job in a `SystemBase` no longer crash a player
* Sorting a `ComponentSystemGroup` now correctly sorts any child groups, even if the parent group is already sorted.
* The subscene inspector no longer allows you to unload section 0 if another section is still loaded, and it also disallows loading any section before section 0 is loaded
* `EntityManager.CopyAndReplaceEntitiesFrom` no longer fails when the Entity capacity of the destination is larger than the capacity of the source
* Hybrid components on disabled GameObjects are now also converted
* Children of a nested Parent not updating LocalToWorld if Parent's LocalToWorld was changed by a custom system with an archetype containing a WriteGroup for LocalToWorld
* `EntityQuery` APIs which take an input `NativeArray<Entity>` for filtering (such as `ToEntityArray()`) can now be called with ReadOnly `NativeArray<Entity>` without throwing an exception.

### Upgrade guide

* managed class IComponentData now supports patching of entity references in EntityCommandBuffer.AddComponent. This can result in a significant performance regression if there might be an entity on the managed component when playing the command buffer. If a managed component has a reference to another class that is not sealed it is unprovable that there may or may not be entity references on it. Thus we have to walk the whole class data to apply it. If there is in fact no entity reference on a class referenced from a managed component, then it is recommended to mark the referenced class as sealed, so that the type manager can prove that there is no entity references present on the managed component and thus completely skip all relatively slow entity patching code.



## [0.16.0] - 2020-09-24

### Added

* EntityManager method CreateEntity(EntityArchetype, int). Unlike existing overloads of CreateEntity, this new overload takes no Entity array and returns no Entity array, so it avoids waste and bother in cases where callers don't need the actual Entity values.
* Special handling for Cameras and Colliders in preparation for root scene conversion, though they are disabled by default still
* `World.MaximumDeltaTime` now controls the maximum deltaTime that is reported to a World.
* Exception's stacktrace are recorded in conversion logs.
* Add `IFixedRateManager` interface for fixed-timestep implementations. See `FixedRateUtils.cs` for reference implementations.
* Add `ComponentSystemGroup.FixedRateManager` property, to store the current active `IFixedRateManager` implementation.
* Added `SceneSystem.IsSectionLoaded` to enable querying if a specific section of a scene is loaded.
* `EntityQuery.SetOrderVersionFilter()` and `EntityQuery.AddOrderVersionFilter()` which can be used to filter the Order Version independently from the Changed Version of a chunk.
* DOTS naming standards to CONVENTIONS.md
* libcurl Stevedore artifact registration
* Mathematics tests are turned on in CI

### Changed

* Improved performance of `EntityQuery.ToEntityArray()`
* Platform packages updated to `0.9.0-preview.9`
* Properties packages updated to `1.5.0-preview`
* The job safety system has be moved to NativeJobs as C++ code
* The UnsafeUtility memory allocators have been moved to NativeJobs
* improved performance of EntityQuery.CopyFromComponentDataArray
* changed chunk size from 16128 bytes (16 KB - 256 bytes) to exactly 16384 bytes (16 KB).
* `TypeManager.GetFieldInfo` now takes in a `Type` to return an `NativeArray<FieldInfo>`. The passed in type must be registered to have field information generated explicitly via the `[GenerateComponentFieldInfo]` assembly attribute.
* `IJobEntityBatch` and `IJobEntityBatchWithIndex` now quietly skip batches whose size is zero. This can happen legitimately if the requested `batchesPerChunk` value is higher than the entity count for a particular chunk.
*Removed deprecated `ArchetypeChunk.Locked()` method.
*Deprecated `ArchetypeChunk.BatchEntityCount` property. The `.Count` property should be used instead.
* Removed usage of TempAssetCache for some livelink cases. Now these files are under SceneDependencyCache instead, so there is only one magic directory to deal with until we can remove it completely in future Unity versions.
*Fixed Reduced overhead of `IJobEntityBatchWithIndex` prefiltering by up to 20% if `batchesPerChunk` is 1, or if `EntityQuery` filtering is disabled.

### Deprecated

* `FixedStepSimulationSystemGroup.MaximumDeltaTime` has been deprecated. The maximum delta time is now stored in `World.MaximumDeltaTime`. For better compatibility with UnityEngine, the new field applies to both the fixed-rate and variable-rate timesteps.
* `ComponentSystemGroup.UpdateCallback` is deprecated. Instead, the group calls the `ShouldGroupUpdate()` method on its `FixedRateManager` property (if non-null) to accomplish the same effect.
* `FixedRateUtils.EnableFixedRateCatchUp()`, `FixedRateUtils.EnableFixedRateSimple()`, and `FixedRateUtils.DisableFixedRate()`. These functions were used to set the deprecated `ComponentSystemGroup.UpdateCallback` field; instead, just set `ComponentSystemGroup.FixedRateManager` directly.

### Removed

* Old <2020.1 ifdef blocks in LiveLink scene culling code
* Deprecated legacy sort order code in ComponentSystemGroup was removed

### Fixed

* Removed GC-allocations in `SceneSystem` and `SceneSectionStreamingSystem` that were happening every frame
* Issue with invalid GUID in SubScene importer causing Player LiveLink to stall waiting for an asset it will never get
* Hybrid component transform syncing was not working when entity order changed
* Hybrid components being editable when in Preview Scenes (by selecting gizmos)
* Fixed an issue in 2020.2 which caused `NativeContainer` min-max ranges to be incorrectly patched when scheduling and `IJobChunk` or `IJobEntityBatch` with a "Single" or "Run" schedule call.
* Fields marked with `RestrictAuthoringInputTo` can now be set to `None` in the inspector
* The Entities package now uses a faster code path for `CreateArchetypeChunkArray()` more consistently.
* Retroactively added a changelog entry that notes a behavior change in `RemoveComponent(EntityQuery, ComponentTypes)`. See 'Change' entry under 0.14.0.
* Generic job reflection data across assemblies would sometimes not work
* Fixed HLOD component throwing out of bounds exception when setup incorrectly against LODGroup.
* Scene section meta data now works in standalone builds again
* Native memory leak in EditorSubSceneLiveLinkSystem when failing to apply patches
* Generic job registration is more robust when generic parameters
* LiveLink will not generate errors on scenes that have not yet loaded scene sections
* Corrected inverted test in `IJobEntityBatchWithIndex` if EntityQuery filtering is enabled.
* `EntityManger.AddComponent<T>(EntityQuery entityQuery)` and `EntityManger.AddComponentData<T>(EntityQuery entityQuery, NativeArray<T> componentArray)` is 2x faster.
* Reduced overhead of `IJobEntityBatch` execution by 5-10% if `batchesPerChunk` is 1.

### Security




## [0.15.0] - 2020-08-26

### Added

* More detailed profiling of individual EntitiesILPostProcessors to Editor log (look for lines with "EILPP" marker).
* Added `EntityQuery.IsEmpty` function which respects the `EntityQueryFilter`s

### Changed

* DOTS Runtime now supports `Burst.CompileFunctionPointer` allowing for lambda job and `EntityCommandBuffer` playback to be Burst compiled.
* `World.Time.ElapsedTime` is now initialized to zero when the World is created.
* Bumped Burst to 1.3.5.
* Updated package `com.unity.platforms` to version `0.9.0-preview.1`.
* Improved performance of `EntityQuery.CreateArchetypeChunkArray()`
* Updated packages `com.unity.properties` and `com.unity.serialization` to version `1.4.3-preview`.
* improved performance of `EntityManager.AddComponent(NativeArray<Entity>,ComponentType)` and `EntityManager.RemoveComponent(NativeArray<Entity>,ComponentType)`
* `TypeCategory.Class` is deprecated in favour of `TypeCategory.UnityEngineObject`
* A `ComponentTypes` value can no longer consist of duplicate types. (The collections safety checks look for duplicates and throw an exception.)

### Deprecated

* Deprecated `RequiresEntityConversion` attribute since it is not used anymore

### Removed
* Removed previously deprecated `LiveLinkBuildImporter.GetHash`

### Fixed

* Limit of 128 million Entities per World instituted.
* Fixed `[GenerateAuthoringComponent]` on `IBufferElementData` throwing a NullReferenceException at initialization when Live Conversion is active.
* Fixed an issue which caused an exception to occur when `IJobEntityBatchWithIndex` is scheduled with `.Run()`
* Fixed a few methods not correctly preserving shared component values: `EntityManager.RemoveComponent(EntityQuery, ComponentTypes)`, `EntityCommandBuffer.RemoveComponent(EntityQuery, ComponentTypes)`, `EntityCommandBuffer.AddComponent(EntityQuery, ComponentTypes)`.
* Fixed a bug with `BufferFromEntity<T>` which caused it to incorrectly update the version number of the buffer when marked `ReadOnly`
* `TypeManager.GetWriteGroupTypes()` no longer leaks `AtomicSafetyHandle` instances each time it is called.
* Fixed an issue where `GetEntityInfo()` can potentially crash the editor if the user passes in an invalid Entity
* Fixed buffer element authoring component not showing up in DOTS Compiler Inspector.
* `TypeManager.Initialize` now uses the TypeCache in Editor, improving the time it takes to enter playmode when no script compilation occurs. (1800ms -> 200ms)
* Fix `Entities.ForEach` `WithDisposeOnJob` method to work correctly with NativeArrays when scheduled with `.Run`.
* Fix IL2CPP build error with local methods used inside of Entities.ForEach lambdas.



## [0.14.0] - 2020-08-04

### Added

* Added `IsEmpty` property to `DynamicBuffer`.
* Added deduplication for asset bundles generated for subscenes.
* Added new `EntityManager` methods: `AddComponent(EntityQuery, ComponentTypes)`, which adds multiple components to all entities matching a query; and `RemoveComponent(Entity, ComponentTypes)`, which removes multiple components from a single entity. (`AddComponent(Entity, ComponentTypes)` and `RemoveComponent(EntityQuery, ComponentTypes)` already existed. This patch just fills in a few 'missing' methods.)
* Added `EntityManagerDifferOptions.UseReferentialEquality` which instructs the Differ to compare entity fields by GUID and blob asset reference fields by hash instead of bitwise equality

### Changed
* `BlockAllocator` is now backed by memory retrieved from platform virtual memory APIs. Platforms which do not support virtual memory will fall back to malloc/free.
* `IJobEntityBatch.ScheduleSingle` is being renamed to `IJobEntityBatch.Schedule` to match our naming guidelines for job scheduling.
* When `DefaultWorldInitialization.Initialize()` adds the default World's system groups to the Unity player loop, it now bases its modifications on the current player loop instead of the default player loop. This prevents the Entities package from accidentally erasing any previous player loop modifications made outside the package.
* `DefaultWorldInitialization.DomainUnloadOrPlayModeChangeShutdown()` now removes all existing `World`s from the Unity player loop before destroying them. If a `World` that was added to the player loop is destroyed manually prior to domain unload, it must also be removed from the player loop manually using `ScriptBehaviourUpdateOrder.RemoveWorldFromPlayerLoop()`.
* Updated package `com.unity.platforms` to version `0.7.0-preview.8`.
* `EntityManager.CreateEntity()`, `EntityManager.SetArchetype()`, and `EntityCommandBuffer.CreateEntity()` no longer accept the value returned by `new EntityArchetype()` because it's invalid. Same for `EntityCommandBuffer.CreateEntity()` and `EntityCommandBuffer.ParallelWriter.CreateEntity()`. Always use `EntityManager.CreateArchetype()` instead of `new EntityArchetype()` to create `EntityArchetype` values. (Ideally, the `EntityArchetype` constructor wouldn't be public, but C# doesn't allow that for a struct.)
* Subscene Inspector now uses a table format to allow easier management of multiple subscenes
* RemoveComponent(EntityQuery, ComponentTypes) now removes all provided components at once from all entities matching the query. Previously, the components were removed one at a time in a loop. This was less efficient and could affect which entities matched the query in subsequent loop iterations in unexpected ways.

### Deprecated
* `IJobEntityBatch.ScheduleParallelBatched` is being deprecated in favor of adding a batching parameter to `IJobEntityBatch.ScheduleParallel`
* `ScriptBehaviourUpdateOrder.UpdatePlayerLoop()` is being deprecated in favor of `ScriptBehaviourUpdateOrder.AddWorldToPlayerLoop()`. Due to slightly different semantics, a direct automated API update is not possible: the new function always takes a `PlayerLoopSystem` object to modify, does not call `UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop()`, and does not create the top-level system groups if they don't exist.
* `ScriptBehaviourUpdateOrder.IsWorldInPlayerLoop(World)` is being deprecated in favor of `ScriptBehaviourUpdateOrder.IsWorldInCurrentPlayerLoop(World)`.

### Removed

* Removed obsolete `ScriptBehaviourUpdateOrder.CurrentPlayerLoop`. Use `UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop()` instead.
* Removed obsolete `ScriptBehaviourUpdateOrder.SetPlayerLoop()`. Use `UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop()` instead.

### Fixed

* Setting the Scene Asset on a Subscene would sometimes fail to trigger an import/conversion because the default ECS world was missing.
* Fixed crash when using Singleton access methods (GetSingleton, SetSingleton, etc.) with a generic parameter as argument.
* Fixed an issue which caused WebGL not to work, and could produce this error message on IL2CPP-based backends:
```
NotSupportedException: To marshal a managed method, please add an attribute named 'MonoPInvokeCallback' to the method definition. The method we're attempting to marshal is: Unity.Entities.SystemBase::UnmanagedUpdate
```


## [0.13.0] - 2020-07-10

### Added
* Added new `EntityCommandBuffer` methods: `AddComponent(Entity, ComponentTypes)` and `AddComponent(EntityQuery, ComponentTypes)` for adding multiple components in one call. (`EntityManager` has an equivalent of the first already and will get an equivalent of the second later.)
* Added new `EntityCommandBuffer` methods: `RemoveComponent(Entity, ComponentTypes)` and `RemoveComponent(EntityQuery, ComponentTypes)` for removing multiple components in one call. (`EntityManager` will get equivalents in the future.)
* Added new `IJobEntityBatchWithIndex` job interface, a variant of `IJobEntityBatch` that provides an additional `indexOfFirstEntityInQuery` parameter, which provides a per-batch index that is the aggregate of all previous batch counts.
* Added `MaximumDeltaTime` property to `FixedStepSimulationSystemGroup`, used similarly to `UnityEngine.Time.maximumDeltaTime` to control how gradually the application should recover from large transient frame time spikes.
* Added new player loop management functions to the `ScriptBehaviourUpdateOrder` class:
  * `AppendSystemToPlayerLoopList()`: adds a single ECS system to a specific point in the Unity player loop.
  * `AddWorldToPlayerLoop()`: adds the three standard top-level system groups to their standard player loop locations.
  * `IsWorldInPlayerLoop(World, PlayerLoopSystem)`: searches the provided player loop for any systems owned by the provided World.
  * `RemoveWorldFromPlayerLoop()`: removes all systems owned by a World from the provided player loop.
  * `AddWorldToCurrentPlayerLoop()`, `IsWorldInCurrentPlayerLoop()`, and `RemoveWorldFromCurrentPlayerLoop()`: wrappers around the above functions that operate directly on the currently active player loop.

### Changed

* Updated minimum Unity Editor version to 2020.1.0b15 (40d9420e7de8)
* Profiler markers for `EntityCommandBuffer.Playback` from `EntityCommandBufferSystem`s now include name of the system that recorded the `EntityCommandBuffer`.
* Bumped burst to 1.3.2 version.
* EntityQuery commands for `AddComponent`, `RemoveComponent`, and `DestroyEntity` in the EntityCommandBuffer now use Burst during Playback.
* IJobChunk and Entities.ForEach ScheduleParallel has been optimized in case there is no EntityQuery filtering necessary (Shared component or change filtering)
* `TypeManager.GetSystems()` now returns an `IReadOnlyList<Type>` rather than a `List<Type>`
* Updated package `com.unity.platforms` to version `0.6.0-preview.4`.
* `EntityContainer` will now allow to write data back to the entity.
* Updated package `com.unity.properties` and `com.unity.serialization` to version `1.3.1-preview`.

### Fixed

* Fixed warning treated as error in the case that a warning is emitted for Entities.ForEach passing a component type as value.
* Fixed paths displayed in IL post-processing error messages to be more consistent with Unity error messages.
* Fixed exceptions being thrown when inspecting an entity with a GameObject added through `EntityManager.AddComponentObject`.
* Fixed `DCICE002` error thrown during IL post-processing when `Entities.ForEach` contains multiple Entities.ForEach in same scope capturing multiple variables.
* `EntityManager`'s `AddComponent()`, `RemoveComponent()`, and `CopyEntitiesFrom()` methods no longer throw an error if their input is a `NativeArray<Entity>` allocated with `Allocator.Temp` whose length is >10 elements.
* Throw error when Entities.ForEach has an argument that is a generic DynamicBuffer.
* Re-adding a system to a `ComponentSystemGroup` immediately after removing it from the group now works correctly.
* `ComponentSystemGroup.Remove()` is now ignored if the target system is already enqueued for removal, or if it isn't in the group's update list in the first place.
* Fixed IL post-processing warnings being emitted with "error" title.
* Fixed "Invalid IL" error when try/finally block occurs in `Entities.ForEach` lambda body or cloned method (usually occurs with `using` or `foreach` and `WithoutBurst`).
* Fixed `Unexpected error` when `Job.WithCode` is used with `WithStructuralChanges` (now throw an error).
* Fixed a bug where `Unity.Scenes.EntityScenesPaths.GetTempCachePath()` could return invalid strings
* Fixed freezing of editor due to accessing the `EntityManager` property within Rider's debugger
* Fixed a bug where calling `SetArchetype` on an entity containing a component with `ISystemStateComponentData` may sometimes incorrectly throw an `ArgumentException`

### Known Issues

* This version is not compatible with 2020.2.0a17. Please update to the forthcoming alpha.

## [0.12.0] - 2020-05-27

### Added
* Added `BufferFromEntity.DidChange()`, with the same semantics as the existing `ComponentDataFromEntity.DidChange()`.
* Added `BufferFromEntity.HasComponent()`, with the same meaning as the existing `.Exists()` call (which is now deprecated).
* Added `WorldSystemFilterFlags.All` flag to include allow calls to `TypeManager.GetSystems()` to return all systems available to the runtime including systems decorated with [DisableAutoCreation].
* Added `DynamicBuffer.RemoveAtSwapBack()` and `DynamicBuffer.RemoveRangeSwapBack()`
* Added `Entities.WithDisposeOnCompletion` to correctly Dispose of types after running an `Entities.ForEach`.
* Added `SystemBase.GetBuffer/GetBufferFromEntity` that are patched so that they can be used inside of `Entities.ForEach`.
* Added BlobAllocator.SetPointer to allow having a blob pointer to an object which already exists in the blob. This can be used for example to reference a parent node in a tree.
* Added `GameObjectConversionSystem.CreateAdditionalEntity` overload that allows to create multiple new entities at once.
* Added a new `FixedStepSimulationSystemGroup`. Systems in this group update with a fixed timestep (60Hz by default), potentially running zero or several times per frame to "catch up" to the actual elapsed time. See the `FixedTimestepSystemUpdate` sample scene for an example of how to use this system group.

### Changed
* Updated minimum Unity Editor version to 2020.1.0b9 (9c0aec301c8d)
* `World.Dispose()` now destroys all the world's systems before removing the `World` from the "all worlds" list.
* Extended `TypeManager.GetSystems()` to support getting systems filtered by any and/or all `WorldSystemFilterFlags`.
* Updated package `com.unity.platforms` to version `0.4.0-preview.5`.
* Updated package `com.unity.burst` to version `1.3.0-preview.12`.
* `Unity.Entities.DefaultWorldInitialization` has been moved from the `Unity.Entities.Hybrid` assembly into the `Unity.Entities` assembly.
* `Unity.Entities.DefaultWorldInitialization.Initialize()` now returns the initialized `World.DefaultGameObjectInjectionWorld` object.
* `ArchetypeChunkComponentType` has been renamed to `ComponentTypeHandle`
* `ArchetypeChunkComponentTypeDynamic` has been renamed to `DynamicComponentTypeHandle`
* `ArchetypeChunkBufferType` has been renamed to `BufferTypeHandle`
* `ArchetypeChunkSharedComponentType` has been renamed to `SharedComponentTypeHandle`
* `ArchetypeChunkEntityType` has been renamed to `EntityTypeHandle`
* `ArchetypeChunkComponentObjects` has been renamed to `ManagedComponentAccessor`
* `Unity.Entities.EditorRenderData` has been moved from the `Unity.Entities.Hybrid` assembly to the `Unity.Entities` assembly.
* `Unity.Scenes.Hybrid` has been renamed to `Unity.Scenes`. Any asmdefs referring to the old assembly name must be updated. The ScriptUpgrader will take care of updating `using` namespace imports.
* `Unity.Entities.SceneBoundingVolume` has moved from the `Unity.Entities.Hybrid` assembly to the `Unity.Scenes` assembly and `Unity.Scenes` namespace. Any asmdefs referring to the old assembly name must be updated. The ScriptUpgrader will take care of updating `using` namespace imports.
* `EntityCommandBuffer.Concurrent` has been renamed to `EntityCommandBuffer.ParallelWriter`.
* `EntityCommandBuffer.ToConcurrent()` has been renamed to `EntityCommandBuffer.AsParallelWriter()` and now returns `EntityCommandBuffer.ParallelWriter` (renamed from `Concurrent`).
* Duplicate query parameters (from `WithAll` and lambda parameters) are now allowed in `Entities.ForEach` (they are now sanitized for the user).
* If a change filter is used in `Entities.ForEach` with `WithChangeFilter`, the component type will automatically get added to the query.
* Add additional warnings around conflicting use of `WithNone`, `WithAll`, `WithAny` and lambda parameters in `Entities.ForEach`.
* Warn if a user passes a struct component parameter by value to their lambda in `Entities.ForEach` (since changes won't be reflected back to the underlying component).
* An exception is now thrown during serialization if a shared component containing entity references is encountered.
* EntityScene generation (Happening in a background process) is now integrated with the async progress bar to indicate when entity data is being generated. The code that tracks dependencies for entity scenes, determines when to regenerated them in the editor is significantly cheaper now.
* When safety checks are enabled `EntityManager.AddComponent(NativeArray<Entity>, ComponentType)` now throws `ArgumentException` instead of `InvalidOperationException` when any of the entities are invalid
* `FixedRateUtils` timesteps outside the range 0.0001 to 10.0f are now clamped, for consistency with `UnityEngine.Time.fixedDeltaTime`.
* Added `[NoAlias]` attributes to the `DynamicBuffer` native container to explain its aliasing to Burst.
* Updated package `com.unity.properties` to version `1.3.0-preview`.
* Updated package `com.unity.serialization` to version `1.3.0-preview`.

### Deprecated

* Deprecated `WithDeallocateOnJobCompletion` for `Entities.ForEach`; Use `WithDisposeOnCompletion` instead.
* Deprecated `ComponentDataFromEntity.Exists()`; Use `.HasComponent()` instead.
* Deprecated `BufferFromEntity.Exists()`; Use `.HasComponent()` instead.

### Fixed
* Fixed data corruption bug in Entities.WithStructuralChange().ForEach() when components on entities that are about to be processed get removed before we process the entity.
* `EntityManager.SetName` now causes less GC memory allocations
* `Entities.WithDeallocateOnJobCompletion()` now correctly deallocates data at the end instead of after the first chunk when used with `Run()` (Note that Entities.WithDeallocateOnJobCompletion() has been deprecated in favor of Entities.WithDisposeOnCompletion().)
* `Entities.WithDeallocateOnJobCompletion()` now deallocates data when used with `WithStructuralChanges()` (Note that Entities.WithDeallocateOnJobCompletion() has been deprecated in favor of Entities.WithDisposeOnCompletion().)
* Creating section section meta data during conversion will no longer trigger an invalid warning about missing SceneSection components
* Fixed a crash that could happen when calling `EntityDataAccess.PlaybackManagedChanges` from bursted code after a domain reload
* Fixed compilation issue when compiling multiple `Entities.ForEach` in the same method that use captured variables from different scopes.
* Fix to unexpected error when using capturing Entities.ForEach inside a method with a generic argument (error now correctly indicates that it is not currently supported).
* UnloadAllAssets will no longer unload assets referenced by shared or managed components.
* Fixed load order of JobReflection static methods which were causing `InvalidOperationException: Reflection data was not set up by code generation` exceptions in player builds.
* Beginning an exclusive entity transaction while another one is in progress now doesn't fail silently anymore but throws an exception
* Fixed race condition in the Chunks component version
* Shared component version is now always based off a global version. Thus Destroying all usage of a specific shared component and recreating it will now result in a changed version number.
* The `Loading Entity Scene failed` message now contains more information for why the scene failed to load
* `GameObjectEntityEditor` no longer throws exceptions when selecting a prefab
* Components on GameObjects with invalid MonoBehaviours no longer cause exceptions when used as hybrid components
* Deleting a GameObject with `ConvertToEntity` before converting it no longer throws an exception
* Errors happening during scene streaming now contain the callstack
* The `jobIndex` parameter passed to EntityCommandBuffer.ParallelWriter methods has been renamed to `sortKey` to better express its purpose. Its functionality is unchanged.
* Invalid uses of the new `OrderFirst` and `OrderLast` fields in the `[UpdateInGroup]` attribute are now detected and reported. Some previous spurious warnings regarding these fields are now suppressed.
* Greatly reduced the garbage generated by redrawing the chunk utilization histograms in the entity debugger
* Improved performance of `EntityManager.AddComponent(NativeArray<Entity>, ComponentType)` when safety checks are enabled
* `EntityManager.RemoveComponent(NativeArray<Entity>, ComponentType)` now always checks that the component can be removed, not just for the case of few entities
* Fixed issues where `FixedRateUtils.FixedRateCatchUpManager` was occasionally not running its first update at elapsedTime = 0.0.

## [0.11.0] - 2020-05-04

### Added

* Added `ArchetypeChunkComponentObjects<T>.Length`

### Changed

* Updated package `com.unity.burst` to version `1.3.0-preview.11`
* Improves `ComponentType.ToString` names in Dots Runtime to provide the full type name when available, and if not, defaults to the StableTypeHash.
* `EntityManager.Version` and `EntityManager.GlobalSystemVersion` will throw if the manager is not valid instead of returning 0.

### Deprecated

* Deprecate system sorting via virtual functions and direct modification of system list. There are now two new properties on the `UpdateInGroup` attribute: `OrderFirst` and `OrderLast`. Setting either of these properties to true will group the system together with others tagged in the same way, and those systems will sort in a subgroup by themselves. This change was needed to enable Burst compatible systems in the future.
* Deprecate `EntityManager.IsCreated` which cannot be efficiently implemented with EntityManager as a struct. For the (hopefully rare) cases where you need to determine if an entity manager is still valid, use `World.IsCreated` instead as the world and entity manager are always created and destroyed in tandem.

### Removed

* Removed expired API `EntityQuery.CreateArchetypeChunkArray(Allocator, out JobHandle)`.
* Removed expired API `EntityQuery.ToEntityArray(Allocator, out JobHandle)`.
* Removed expired API `EntityQuery.ToComponentDataArray<T>(Allocator, out JobHandle)`.
* Removed expired API `EntityQuery.CopyFromComponentDataArray<T>(NativeArray<T>, out JobHandle)`.

### Fixed

* Improved JobsDebugger errors invvolving the `EntityManager`. requires Unity 2020.1.0b5 or later
* Fixed potential infinite loop in `FixedRateUtils.FixedRateCatchUpManager` if these callbacks were enabled on the first frame of execution.
* When `FixedRateUtils.FixedRateCatchUpManager` or `FixedRateUtils.FixedRateSimpleManager` are enabled, the first update is now guaranteed to take place at elapsedTime = 0.0.
* Asset dependencies registered via `GameObjectConversionSystem.DeclareAssetDependency` are now checked for validity, inbuilt assets are ignored
* Improved performance of the `EntityPatcher` when applying changes to large amounts of entities
* The script template for ECS systems now uses `SystemBase` instead of `JobComponentSystem`
* Fixed instantiation of entities with multiple hybrid components causing corruption of the managed store.
* Removed remapping of entity fields in hybrid components during instantiation (this wasn't supposed to happen).
* Fix crash when trying to remap entity references within recursive types.
* Serialization and LiveLink now supports blob asset references in shared components.
* Serialization and LiveLink now supports blob asset references in managed components.
* Fix bug with `EntityQuery.CopyFromComponentDataArray` causing it to behave like `ToComponentDataArray`

## [0.10.0] - 2020-04-28

### Added

* Added `GetOrderVersion()` to ArchetypeChunk. Order version bumped whenever structural change occurs on chunk.
* Added `GetComponentDataFromEntity` method that streamlines access to components through entities when using the `SystemBase` class.  These methods call through to the `ComponentSystemBase` method when in OnUpdate code and codegen access through a stored `ComponentDataFromEntity` when inside of `Entities.ForEach`.
* Added support for WorldSystemFilterFlags.ProcessAfterLoad which enable systems to run in the streaming world after a entity section is loaded.
* Added `DynamicBuffer.CopyFrom()` variant that copies from a `NativeSlice`
* Added `DynamicBuffer.GetUnsafeReadOnlyPtr()`, for cases where only read-only access is required.
* Added PostLoadCommandBuffer component which can be added to scene or section entities to play back a command buffer in the streaming world after a entity section is loaded. Adding it to the scene entity will play back the command buffer on all sections in the scene.
* Added `WorldSystemFilterFlags.HybridGameObjectConversion` and `WorldSystemFilterFlags.DotsRuntimeGameObjectConversion`to annotate conversion systems to be used specifically for hybrid or dots runtime.
* Added missing profiler markers when running an `Entities.ForEach` directly with `.Run`.
* Added support for storing metadata components in the header of converted subscenes. Components can be added to the section entities requested with `GameObjectConversionSystem.GetSceneSectionEntity`. The added components are serialized into the entities header and will be added to the section entities at runtime when the scene is resolved.
* ResolvedSectionEntity buffer component is now public and can be used to access metadata components on a resolved scene entity.
* Added 'Clear Entities Cache' window under the DOTS->Clear Entities Cache menu. By default, all caches are enabled for clearing. Clearing Live Link Player cache wipes the local player cache of a livelink build next time it connects to the editor. Clearing Entity Scene cache invalidates all Entity Scenes (SubScenes) causing them to reimport on next access. Clearing Live Link Assets cache, causing the next Live Link connection to reimport all on-demand live link assets.

### Changed

* Bumped Burst version to improve compile time and fix multiple bugs.
* ChangeVersions behavior more consistent across various entry points.
* Updated package `com.unity.properties` to version `1.1.1-preview`.
* Updated package `com.unity.serialization` to version `1.1.1-preview`.
* Updated package `com.unity.platforms` to version `0.3.0-preview.4`.
* `ConvertToEntity` no longer logs a warning if there are multiples of a given authoring component on the converted GameObject, so it is now compatible with conversion systems that can support multiples.
* Improved the StableTypeHash calculation used when serializing components to be more resilient. The hash will now properly invalidate serialized data should component data layout change as a result of `[StructLayout(LayoutKind.Explict)]`, as well as if a nested component field's data layout changes.
* Make it possible to use Entities.ForEach with >8 parameters if you supply your own delegate type

### Deprecated

* EntityManager.UnlockChunk deprecated
* Adding components to entities converted from GameObjects using proxy components has been deprecated, please use the new conversion workflows using `GameObjectConversionSystem` and `IConvertGameObjectToEntity`
* `ComponentDataProxyBaseEditor`, `DynamicBufferProxyBaseEditor` from `Unity.Entities.Editor` deprecated
* `ComponentDataProxy<T>`, `ComponentDataProxyBase`, `DynamicBufferProxy<T>`, `SharedComponentDataProxy<T>`, `SceneSectionProxy` from `Unity.Entities.Hybrid`deprecated
* `MockDataProxy`, `MockDynamicBufferDataProxy`, `MockSharedDataProxy`, `MockSharedDisallowMultipleProxy` from `Unity.Entities.Tests` deprecated
* `CopyInitialTransformFromGameObjectProxy`, `CopyTransformFromGameObjectProxy`, `CopyTransformToGameObjectProxy`, `LocalToWorldProxy`, `NonUniformScaleProxy`, `RotationProxy`, `TranslationProxy` from `Unity.Transforms` deprecated
* Deprecated `ScriptBehaviourUpdateOrder.CurrentPlayerLoop`; Use `PlayerLoop.GetCurrentPlayerLoop()` instead
* Deprecated `ScriptBehaviourUpdateOrder.SetPlayerLoop`; Use `PlayerLoop.SetPlayerLoop()` instead

### Removed

* Removed expired API `BlobAssetComputationContext.AssociateBlobAssetWithGameObject(Hash128, GameObject)`
* Removed expired API `BlobAssetReference.Release()`
* Removed expired API `BlobAssetStore.UpdateBlobAssetForGameObject<T>(int, NativeArray<Hash128>)`
* Removed expired API `class TerminatesProgramAttribute`
* Removed expired API `EntityManager.LockChunkOrder(ArchetypeChunk)`
* Removed expired API `EntityManager.LockChunkOrder(EntityQuery)`
* Removed expired API `EntityManager.LockChunkOrder(NativeArray<ArchetypeChunk>)`
* Removed expired API `EntityManager.UnlockChunkOrder(ArchetypeChunk)`
* Removed expired API `EntityManager.UnlockChunkOrder(EntityQuery)`
* Removed expired API `GameObjectConversionSettings.BuildSettings`
* Removed expired API `GameObjectConversionSystem.GetBuildSettingsComponent<T>()`
* Removed expired API `GameObjectConversionSystem.TryGetBuildSettingsComponent<T>(out T)`
* Removed expired API `LambdaJobDescriptionConstructionMethods.WithBurst(...)`
* Removed expired API `LambdaJobDescriptionConstructionMethods.WithNativeDisableUnsafePtrRestrictionAttribute(...)`
* Removed expired API `SceneSystem.BuildSettingsGUID`
* Removed expired overload of `BlobBuilder.Allocate<T>(int, ref BlobArray<T>)`
* Removed expired overload of `EntityQuery.CopyFromComponentDataArray<T>(...)`
* Removed expired overload of `EntityQuery.CreateArchetypeChunkArray(...)`
* Removed expired overload of `EntityQuery.ToComponentDataArray<T>(...)`
* Removed expired overload of `EntityQuery.ToEntityArray(...)`

### Fixed

* Fixed the synchronization of transforms for Hybrid Components to handle scale properly.
* Improved JobsDebugger error messages when accessing `ComponentDataFromEntity`, `ArchetypeChunkComponentType`, `ArchetypeChunkComponentTypeDynamic`, `ArchetypeChunkBufferType`, `ArchetypeChunkSharedComponentType`, `ArchetypeChunkEntityType`, and `BufferFromEntity` after a structural change. (requires Unity 2020.1.0b2 or later)
* Fixed scene camera culling masks not being reset in the case of using ConvertToEntity but not any scene conversion
* Fix to IL2CPP compilation errors occuring in IL2CPP builds with Entities.ForEach with nested captures.
* Fixed the entity inspector showing incorrect data for chunk components.
* Fixed entity scene load error caused by type hash mismatch when serializing hybrid components with conditionally compiled fields.
* `LambdaJobTestFixture` and `AutoCreateComponentSystemTests_*` systems are no longer added to the simulation world by default.
* `GameObjectConversionSystem.DependOnAsset` now correctly handles multiple sub-scenes
* Ensure that patched component access methods (`GetComponent/SetComponent/HasComponent`) don't break `Entities.ForEach` when there are a lot of them (due to short branch IL instructions).
* Fixed deactivation of Hybrid Components when the entity was disabled or turned into a prefab.
* Improved performance of singleton access methods (`SetSingleton`/`GetSingleton`).
* Fixed managed components not being serialized during player livelink.
* Fixed `CompanionLink` being incorrectly synced during player livelink.
* Fixed a false-positive in the EntityDiffer when a shared component in a changed chunk has its default value
* Fixed Entities.ForEach lambdas that call static methods as well as component access methods (`GetComponent/SetComponent/HasComponent`).
* Remapping no longer incorrectly visits UnityEngine.Object types (i.e. assets).
* Improved performance for managed object operations (Equality, Cloning and Remapping).

## [0.9.1] - 2020-04-15

### Fixed

* Fixed NullReferenceException issue with Singleton access methods in SystemBase.


## [0.9.0] - 2020-04-09

### Added

* `public void GetCreatedAndDestroyedEntitiesAsync(NativeList<int> state, NativeList<Entity> createdEntities, NativeList<Entity> destroyedEntities)` detects which entities were created and destroyed since the last call to this method.
* Added the ability to reimport a SubScene via an inspector button, which forces reconversion.
* Added `GameObjectConversionSystem.DeclareAssetDependency` which expresses that the conversion result of a GameObject depends on an Asset
* Added `void EntityManager.Instantiate(NativeArray<Entity> srcEntities, NativeArray<Entity> dstEntities)`. It gives explicit control over the set of entities that are instantiated as a set. Entity references on components that are cloned to entities inside the set are remapped to the instantiated entities.
* Added `void EntityManager.CopyEntitiesFrom(EntityManager srcEntityManager, NativeArray<Entity> srcEntities, NativeArray<Entity> outputEntities = default)`. It lets you copy a specific set of entities from one World to another. Entity references on components that are cloned to entities inside the set are remapped to the instantiated entities.
* Added assembly for Mesh Deformation data structures.

### Changed

* Systems are now constructed in two phases. First, ECS creates a new instance of all systems and invokes the constructor. Then, it invokes all `OnCreate` methods. This way, you can now use `World.GetExistingSystem<OtherSystem>()` from inside `OnCreate()`.
* Systems are now destroyed in three phases. First, ECS stops all running systems (i.e. OnStopRunning() is invoked). Then it invokes all `OnDestroy` methods. Finally, ECS destroys all systems. This means you can perform safe and predictable cleanup of systems with cross-references to other systems.
* EntityCommandBuffer Playback now Bursted through function pointers. When there's a mix of unmanaged and managed commands in a single buffer, unmanaged commands will be Bursted. When there are no managed commands, each chain's Playback is fully Bursted.
* `Entities.ForEach` in a `GameObjectConversionSystem` no longer logs a warning if there are multiples of a queried authoring component on a matching GameObject. It now returns the first component instance of the desired type, so conversion systems can optionally call `GetComponents<T>()` in order to handle multiples if desired.
* Declaring a non-Prefab object as a referenced Prefab during conversion now emits a warning
* Improved performance of access to singletons through `SetSingleton` and `GetSingleton` in SystemBase (peformance is also improved through these methods on EntityQuery).
* Updated package `com.unity.properties` to version `1.1.0-preview`.
* Updated package `com.unity.serialization` to version `1.1.0-preview`.
* Updated package `com.unity.platforms` to version `0.2.2-preview.3`.
* Updated package `com.unity.platforms` to version `0.2.2-preview.7`.

### Deprecated

* Deprecated `public T World.CreateSystem<T>(params object[] constructorArguments)`. Please use `World.AddSystem(new MySystem(myParams));` instead.
* Deprecated `LiveLinkBuildImport.GetHash/GetDependencies/GetBundlePath`.

### Removed

* Removed expired API `TypeManager.CreateTypeIndexForComponent<T>()`
* Removed expired API `TypeManager.CreateTypeIndexForSharedComponent<T>()`
* Removed expired API `TypeManager.CreateTypeIndexForBufferElement<T>()`
* Removed expired API `DynamicBuffer.Reserve(int)`
* Removed expired API `World.Active`

### Fixed

* Fix BlobAssetSafetyVerifier to generate a better error message when `readonly` is used with BlobAsset references.
* Fixed incorrect comparison in `EntityChunk.CompareTo()`.
* `SceneManager.IsSceneLoaded` now works for converted entity Scenes and returns whether all sections of an entity Scene have loaded.
* Fixed Exception in conversion code when trying to delete entities that are part of a Prefab.
* Fixed Hybrid Component conversion failing when multiple components were added for the same GameObject.
* Fixed use of component access methods (GetComponent/SetComponent/HasComponent) inside Entities.ForEach with nested captures.
* Fix compilation issue when `ENABLE_SIMPLE_SYSTEM_DEPENDENCIES` is enabled.

### Known Issues

* System groups do not currently apply to systems running as part of `EntitySceneOptimizations`

### Known Issues

* System groups do not currently apply to systems running as part of `EntitySceneOptimizations`


## [0.8.0] - 2020-03-12

### Added

* Added missing dynamic component version API: `ArchetypeChunk.GetComponentVersion(ArchetypeChunkComponentTypeDynamic)`
* Added missing dynamic component has API: `ArchetypeChunk.Has(ArchetypeChunkComponentTypeDynamic)`
* `EntityArchetype` didn't expose whether it was Prefab or not. Added bool `EntityArchetype.Prefab`. This is needed for meta entity queries, because meta entity queries don't avoid Prefabs.
* Added Build Configurations and Build Pipelines for Linux
* LiveLink now gives an error if a LiveLink player attempts to connect to the wrong Editor, and advises the user on how to correct this.

### Changed
* Renamed `GetComponentVersion()` to `GetChangedVersion()` when referring to version number changes on write access to components.
* Optimized `ArchetypeChunkComponentTypeDynamic` memory layout. 48->40 bytes.
* LiveLink: Editor no longer freezes when sending LiveLink assets to a LiveLinked player.
* LiveLink: No longer includes every Asset from builtin_extra to depend on a single Asset, and sends only what is used. This massively speeds up the first-time LiveLink to a Player.
* Upgraded Burst to fix multiple issues and introduced native debugging feature.

### Deprecated

### Fixed

* Fixed LiveLinking with SubScene Sections indices that were not contiguous (0, 1, 2..). Now works with whatever index you use.
* Fixed warning when live converting disabled GameObjects.
* Allow usage of `Entities.WithReadOnly`, `Entities.WithDeallocateOnJobCompletion`, `Entities.WithNativeDisableContainerSafetyRestriction`, and `Entities.WithNativeDisableParallelForRestriction` on types that contain valid NativeContainers.


## [0.7.0] - 2020-03-03

### Added

* Added `HasComponent`/`GetComponent`/`SetComponent` methods that streamline access to components through entities when using the `SystemBase` class.  These methods call through to `EntityManager` methods when in OnUpdate code and codegen access through `ComponentDataFromEntity` when inside of `Entities.ForEach`.
* `SubScene` support for hybrid components, allowing Editor LiveLink (Player LiveLink is not supported yet).
* Added `GameObjectConversionSettings.Systems` to allow users to explicitly specify what systems should be included in the conversion

### Changed

* Fixed an issue where shared component filtering could be broken until the shared component data is manually set/added when using a deserialized world.
* Users can control the update behaviour of a `ComponentSystemGroup` via an update callback.  See the documentation for `ComponentSystemGroup.UpdateCallback`, as well as examples in `FixedRateUtils`.
* `IDisposable` and `ICloneable` are now supported on managed components.
* `World` now exposes a `Flags` field allowing the editor to improve how it filters world to show in various tooling windows.
* `World.Systems` is now a read only collection that does not allocate managed memory while being iterated over.
* Updated package `com.unity.platforms` to version `0.2.1-preview.4`.

### Deprecated

* Property `World.AllWorlds` is now replaced by `World.All` which now returns a read only collection that does not allocate managed memory while being iterated over.

### Removed

* Removed expired API `implicit operator GameObjectConversionSettings(World)`
* Removed expired API `implicit operator GameObjectConversionSettings(Hash128)`
* Removed expired API `implicit operator GameObjectConversionSettings(UnityEditor.GUID)`
* Removed expired API `TimeData.deltaTime`
* Removed expired API `TimeData.time`
* Removed expired API `TimeData.timeSinceLevelLoad`
* Removed expired API `TimeData.captureFramerate`
* Removed expired API `TimeData.fixedTime`
* Removed expired API `TimeData.frameCount`
* Removed expired API `TimeData.timeScale`
* Removed expired API `TimeData.unscaledTime`
* Removed expired API `TimeData.captureDeltaTime`
* Removed expired API `TimeData.fixedUnscaledTime`
* Removed expired API `TimeData.maximumDeltaTime`
* Removed expired API `TimeData.realtimeSinceStartup`
* Removed expired API `TimeData.renderedFrameCount`
* Removed expired API `TimeData.smoothDeltaTime`
* Removed expired API `TimeData.unscaledDeltaTime`
* Removed expired API `TimeData.fixedUnscaledDeltaTime`
* Removed expired API `TimeData.maximumParticleDeltaTime`
* Removed expired API `TimeData.inFixedTimeStep`
* Removed expired API `ComponentSystemBase.OnCreateManager()`
* Removed expired API `ComponentSystemBase.OnDestroyManager()`
* Removed expired API `ConverterVersionAttribute(int)`

### Fixed

* Non-moving children in transform hierarchies no longer trigger transform system updates.
* Fixed a bug where dynamic buffer components would sometimes leak during live link.
* Fixed crash that would occur if only method in a module was generated from a `[GenerateAuthoringComponent]` type.
* `Entities.ForEach` now throws a correct error message when it is used with a delegate stored in a variable, field or returned from a method.
* Fix IL2CPP compilation error with `Entities.ForEach` that uses a tag component and `WithStructuralChanges`.
* `Entities.ForEach` now marshals lambda parameters for DOTS Runtime when the lambda is burst compiled and has collection checks enabled. Previously using `EntityCommandBuffer` or other types with a `DisposeSentinel` field as part of your lambda function (when using DOTS Runtime) may have resulted in memory access violation.
* `.Run()` on `IJobChunk` may have dereferenced null or invalid chunk on filtered queries.
* BlobAssetSafetyVerifier would throw a `ThrowArgumentOutOfRangeException` if a blob asset was using in a struct with a method that yielded (instead of generating a valid error).


### Security

* Throw correct error message if accessing `ToComponentDataArrayAsync` `CopyFromComponentDataArray` or `CopyFromComponentDataArrayAsync` from an unrelated query.



## [0.6.0] - 2020-02-17

### Added

* The `[GenerateAuthoringComponent]` attribute is now allowed on structs implementing `IBufferElementData`. An authoring component is automatically generated to support adding a `DynamicBuffer` of the type implementing `IBufferElementData` to an entity.
* Added new `SystemBase` base class for component systems.  This new way of defining component systems manages dependencies for the user (manual dependency management is still possible by accessing the `SystemBase.Dependency` field directly).
* New `ScheduleParallel` methods in `IJobChunk` and `Entities.ForEach` (in `SystemBase`) to make parallel scheduling of jobs explicit.  `ScheduleSingle` in `IJobChunk` indicates scheduling work to be done in a non-parallel manner.
* New editor workflow to quickly and easily build LiveLink player using the `BuildConfiguration` API.
* Adds Live Link support for `GameObject` scenes.
* The `SceneSystem` API now also loads `GameObject` scenes via `LoadSceneAsync` API.
* Added new build component for LiveLink settings in `Unity.Scenes.Editor` to control how initial scenes are handled (LiveLink all, embed all, embed first).
* Users can now inspect post-procssed IL code inside Unity Editor: `DOTS` -> `DOTS Compiler` -> `Open Inspector`
* `GetAssignableComponentTypes()` can now be called with or without a List&lt;Type&gt; argument to collect the data. When omitted, the list will be allocated, which is the same behavior as before.

### Changed

 * The package `com.unity.build` has been merged into the package `com.unity.platforms`. As such, removed the dependency on `com.unity.build@0.1.0-preview` and replaced it with `com.unity.platforms@0.2.1-preview.1`. Please read the changelog of `com.unity.platforms` for more details.
* Managed components are now stored in a way that will generate less GC allocations when entities change archetype.
* Moved `Unity.Entities.ICustomBootstrap` from Unity.Entities.Hybrid to Unity.Entities.
* `World.Dispose()` now completes all reader/writer jobs on the `World`'s `EntityManager` before releasing any resources, to avoid use-after-free errors.
* Fix `AssemblyResolveException` when loading a project with dependent packages that are using Burst in static initializers or `InitializeOnLoad`.
* `.sceneWithBuildSettings` files that are stored in Assets/SceneDependencyCache are no longer rebuilt constantly. Because they are required for SubScene behaviour to work in the editor, if these are deleted they are recreated by OnValidate of the SubScene in the edited Scene. They should also be recreated on domain reload (restarting unity, entering/exiting playmode, etc).
* `EntityQuery.cs`: Overloads of `CreateArchetypeChunkArray`, `ToComponentDataArray`, `ToEntityArray`, and `CopyFromComponentDataArray` that return a JobHandle (allowing the work to be done asynchronously) have been renamed to add `Async` to the title (i.e. `ToComponentDataArrayAsync`). The old overloads have been deprecated and an API Updater clause has been added.
 * `Entities.WithName` now only accepts names that use letters, digits, and underscores (not starting with a digit, no two consecutive underscores)
 * Updated package `com.unity.properties` to version `0.10.4-preview`.
 * Updated package `com.unity.serialization` to version `0.6.4-preview`.
 * The entity debugger now remembers whether chunk info panel is visible
 * The entity debugger now displays the full name for nested types in the system list
 * The entity debugger now sorts previously used filter components to the top of the filter GUI
 * Bumped burst version to include the new features and fixes including:
 * Fix an issue with function pointers being corrupted after a domain reload that could lead to hard crashes.
 * Fix potential deadlock between Burst and the AssetDatabase if burst is being used when building the database.

### Deprecated

 * Method `GetBuildSettingsComponent` on class `GameObjectConversionSystem` has been renamed to `GetBuildConfigurationComponent`.
 * Method `TryGetBuildSettingsComponent` on class `GameObjectConversionSystem` has been renamed to `TryGetBuildConfigurationComponent`.
 * Member `BuildSettings` on class `GameObjectConversionSettings` has been renamed to `BuildConfiguration`.
 * Member `BuildSettingsGUID` on class `SceneSystem` has been renamed to `BuildConfigurationGUID`.

### Removed

 * Removed expired API `SceneSectionData.SharedComponentCount`
 * Removed expired API `struct SceneData`
 * Removed expired API `SubScene._SceneEntities`
 * Removed expired API `World.Active`

### Fixed

* Ability to open and close SubScenes from the scene hierarchy window (Without having to move cursor to inspector window).
* Ability to create a new empty Sub Scene without first creating a game object.
* Improve performance of SubScene loading and change tracking in the editor.
* Fixed regression where `GetSingleton` would create a new query on every call.
* Fixed SubScenes trying to load an already loaded AssetBundle when loaded multiple times on the same player, but with different Worlds.
* Make it clear that SubScenes in Prefabs are not supported.
* Lambda job codegen tests now fail if the error message does not contain the expected contents.
* Improved performance of setting up the world required for game object conversion
* The `chunkIndex` parameter passed to `IJobChunk.Execute()` now has the correct value.
* Fixed an error which caused entities with `ISystemStateSharedComponentData` components to not be cleaned up correctly.
* Managed components containing `Entity` fields will now correctly serialize.
* Fixed issue where `BlobAssetVerifier` will throw error if it can't resolve a type.
* Exposed the Managed Component extensions for `EntityQuery`.
* `Entities.ForEach` now identifies when `this` of the enclosing system is captured due to calling an extension method on it when compilation fails since the lambda was emitted as a member function
* `Entities.ForEach` now reports when a field of the outer system is captured and used by reference when compilation fails since the lambda was emitted as a member function
* `Entities.ForEach` does not erronously point to calling static functions as the source of the error when compilation fails since the lambda was emitted as a member function
* Debugging inside of `Entities.ForEach` with Visual Studio 2017/2019 (some debugging features will need an upcoming update of the com.unity.ide.visualstudio package).
* `EntityQuery.ToComponentArray<T>` with `T` deriving from `UnityEngine.Component` now correctly collects all data in a chunk
* Fixed an issue with `ComponentSystemBase.GetEntityQuery` and `EntityManager.CreateEntityQuery` calls made with `EntityQueryDesc` not respecting read-only permissions.


## [0.5.1] - 2020-01-28

### Changed

* Constructor-related exceptions thrown during `World.CreateSystem` will now included the inner exception details.
* `DefaultWorldInitialization.GetAllSystems` now returns `IReadOnlyList<Type>` instead of `List<Type>`
* `DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups` now takes `IEnumerable<Type>` instead of `List<Type>`

### Fixed

* Fixed an issue where `BlobAssetReference` types was not guaranteed to be 8-byte aligned on all platforms which could result in failing to read Blob data in components correctly on 32-bit platforms.
* Fixed issue in `MinMaxAABB.Equals()` comparing `Min` to itself rather than `other`.
* `Entities.ForEach` now properly treats `in` parameters of `DynamicBuffer` type as read-only
* Fixed potential crash caused by a leaked job after an exception is thrown during a call to `IJobChunk.Schedule`.
* Fixed regression in `ComponentSystemBase.GetSingleton()` where a new query would be created every timee the function is called.


## [0.5.0] - 2020-01-16

### Added

* Added AndroidHybrid.buildpipeline with RunStepAndroid

### Changed

* `Entities.WithReadOnly`, `Entities.WithNativeDisableParallelForRestriction`, `Entities.WithDeallocateOnJobCompletion`, `Entities.WithNativeDisableSafetyRestriction` and `Entities.WithNativeDisableUnsafePtrRestriction` now check their argument types for the proper attributes (`[NativeContainer]`, `[NativeContainerSupportsDeallocateOnJobCompletion]`) at compile time and throw an error when used on a field of a user defined type.
* Log entries emitted during subscene conversion without a context object are now displayed in the subscene inspector instead of discarded

### Deprecated

* Adding removal dates to the API that have been deprecated but did not have the date set.
* `BlobAssetReference<T>`: `Release()` was deprecated, use `Dispose()` instead.

### Removed

 * Adding removal dates to the API that have been deprecated but did not have the date set.
 * `BlobAssetReference<T>`: `Release()` was deprecated, use `Dispose()` instead.
 * `EntityQuery.cs`: Removed expired API `CalculateLength()`, `SetFilter()` and `SetFilterChanged()`.

### Fixed
* Fixed an issue where trying to perform EntityRemapping on Managed Components could throw if a component field was null.
* `EntityManager.MoveEntitiesFrom` with query was not bumping shared component versions, order versions or dirty versions correctly. Now it does.
* Fixed that adding a Sub Scene component from the Add Components dropdown was not reflected in the Hierarchy.
* Fixed so that Undo/Redo of changes to SceneAsset objectfield in the Sub Scene Inspector is reflected in the Hierarchy.
* Make it clear when Sub Scene duplicates are present: shown in Hierarchy and by showing a warning box in the Inspector.
* Support Undo for 'Create Sub Scene From Selection' context menu item.
* Better file name error handling for the 'New Sub Scene From Selection' context menu item.
* Keep sibling order for new Sub Scene when created using 'New Sub Scene From Selection' (prevents the new Sub Scene from ending as the last sibling).
* Handle if selection contains part of a Prefab instance when creating Sub Scene from Selection.
* Fix dangling loaded Sub Scenes not visualized in the Hierarchy when removing Scene Asset reference in Sub Scene component.
* Fixed an issue with invalid IL generated by `Entities.ForEach` when structs are captured as locals from two different scopes and their fields are accessed.
* Make it clear in the Hierarchy and Sub Scene Inspector that nesting Sub Scenes is not yet supported.
* Fixed an issue with `BinaryWriter` where serializing a `System.String[]` with a single element would throw an exception.
* Fixed an issue with `ComponentSystem.GetEntityQuery` and `JobComponentSystem.GetEntityQuery` which caused improper caching of queries when using "None" or "Any" fields.


## [0.4.0] - 2019-12-16

**This version requires Unity 2019.3.0f1+**

### New Features

* Two new methods added to the public API:
  * `void EntityCommandBuffer.AddComponent<T>(EntityQuery entityQuery)`
  * `void EntityCommandBuffer.RemoveComponent<T>(EntityQuery entityQuery)`
* BlobArray, BlobString & BlobPtr are not allowed to be copied by value since they carry offset pointers that aree relative to the location of the memory. This could easily result in programming mistakes. The compiler now prevents incorrect usage by enforcing any type attributed with [MayOnlyLiveInBlobStorage] to never be copied by value.

### Changes

* Deprecates `TypeManager.CreateTypeIndexForComponent` and it's other component type variants. Types can be dynamically added (in Editor builds) by instead passing the new unregistered types to `TypeManager.AddNewComponentTypes` instead.
* `RequireForUpdate(EntityQuery)` and `RequireSingletonForUpdate` on a system with `[AlwaysUpdate]` will now throw an exception instead of being ignored.
* ChangeVersionUtility.IncrementGlobalSystemVersion & ChangeVersionUtility.InitialGlobalSystemVersion is now internal. They were accidentally public previously.
* Entity inspector now shows entity names and allows to rename the selected entity
* Improved entity debugger UI
* Create WorldRenderBounds for prefabs and disabled entities with renderers during conversion, this make instantiation of those entities significantly faster.
* Reduced stack depth of System.Update / OnUpdate method (So it looks better in debugger)
* Assert when using EntityQuery from another world
* Using an EntityQuery created in one world on another world was resulting in memory corruption. We now detect it in the EntityManager API and throw an argument exception
* Structural changes now go through a bursted codepath and are significantly faster
* DynamicBuffer.Capacity is now settable

### Fixes

* Remove unnecessary & incorrect warning in DeclareReferencedPrefab when the referenced game object is a scene object
* GameObjects with ConvertAndInject won't get detached from a non-converted parent (fixes regression)
* Fixed a crash that could occur when destroying an entity with an empty LinkedEntityGroup.
* Updated performance package dependency to 1.3.2 which fixes an obsoletion warning
* The `EntityCommandBuffer` can be replayed repeatedly.
* Fixed exception in entity binary scene serialization when referencing a null UnityEngine.Object from a shared component
* Moving scripts between assemblies now triggers asset bundle rebuilds where necessary for live link
* Fixed LiveLink on Android


## [0.3.0] - 2019-12-03

### New Features

* ENABLE_SIMPLE_SYSTEM_DEPENDENCIES define can now be used to replace the automatic dependency chaining with a much simplified strategy. With ENABLE_SIMPLE_SYSTEM_DEPENDENCIES it simply chains jobs in the order of the systems against previous jobs. Without ENABLE_SIMPLE_SYSTEM_DEPENDENCIES, dependencies are automatically chained based on read / write access of component data of each system. In cases when there game code is forced to very few cores or there are many systems, this can improve performance since it reduces overhead in calculating optimal dependencies.
* Added `DebuggerTypeProxy` for `MultiListEnumerator<T>` (e.g. this makes the results of `GameObjectConversionSystem.GetEntities` calls readable in the debugger)
* Two new methods added to the public API:
  * EntityManager.CreateEntity(Archetype type, int count, Allocator allocator);
  * EntityManager.Instantiate(Entity entity, int count, Allocator allocator);
  Both methods return a `NativeArray<Entity>`.

### Changes

Removed the following deprecated API as announced in/before `0.1.1-preview`:

* From GameObjectConversionUtility.cs: `ConvertIncrementalInitialize()` and `ConvertScene()`.
* From Translation.cs: `struct Position`.
* From EditorEntityScenes.cs: `WriteEntityScene()`.
* From GameObjectConversionSystem.cs: `AddReferencedPrefab()`, `AddDependency()`, `AddLinkedEntityGroup()`, `DstWorld`.
* From DefaultWorld.cs: `class EndPresentationEntityCommandBufferSystem`.

### Fixes

* ConvertAndInject won't destroy the root GameObject anymore (fixes regression introduced in 0.2.0)
* Fix Android/iOS build when using new build pipeline
  * Provide correct application extension apk, aab or empty for project export when building to Android


## [0.2.0] - 2019-11-22

**This version requires Unity 2019.3 0b11+**

### New Features

* Automatically generate authoring components for IComponentData with IL post-processing. Any component data marked with a GenerateAuthoringComponent attribute will generate the corresponding authoring MonoBehaviour with a Convert method.
* BuildSettings assets are now used to define a single build recipe asset on disk. This gives full control over the build pipeline in a modular way from C# code.
  * BuildSettings let you attach builtin or your own custom IBuildSettingsComponents for full configurability
  * BuildPipelines let you define the exact IBuildStep that should be run and in which order
  * IBuildStep is either builtin or your own custom build step
  * BuildSettings files can be inherited so you can easily make base build settings with most configuration complete and then do minor adjustments per build setting
  * Right now most player configuration is still in the existing PlayerSettings, our plan is to over time expose all Player Settings via BuildSettings as well to ease configuration of complex projects with many build recipes & artifacts
* SubScenes are now automatically converted to entity binary files & cached by the asset pipeline. The entity cache files previously present in the project folder should be removed. Conversion systems can use the ConverterVersion attribute to convert to trigger a reconversion if the conversion system has changed behaviour. The conversion happens asynchronously in another process. Thus on first open the subscenes might not show up immediately.

* Live link builds can be built with the new BuildSettings pipeline.
  Open sub scene
  * Closed Entity scenes are built by the asset pipeline and loaded via livelink on demand
  * Opened Entity scenes are send via live entity patcher with patches on a per component / entity basis based on what has changed
  * Assets referenced by entity scenes are transferred via livelink when saving the asset
  * Scenes loaded as game objects are currently not live linked (This is in progress)
 by assigning the LiveLink build pipeline

* `Entities.ForEach` syntax for supplying jobified code in a `JobComponentSystem`'s `OnUpdate` method directly by using a lambda (instead of supplying an additional `IJobForEach`).

* `EntityQueryMask` has been added, which allows for quick confirmation of if an Entity would be returned by an `EntityQuery` without filters via `EntityQueryMask.Matches(Entity entity)`.  An EntityQueryMask can be obtained by calling `EntityManager.GetEntityQueryMask(EntityQuery query).`
* Unity Entities now supports the _Fast Enter playmode_ which can be enabled in the project settings. It is recommended to be turned on for all dots projects.
* The UnityEngine component `StopConvertToEntity` can be used to interrupt `ConvertToEntity` recursion, and should be preferred over a `ConvertToEntity` set to "convert and inject" for that purpose.
* _EntityDebugger_ now shows IDs in a separate column, so you can still see them when entities have custom names
* Entity references in the Entity Inspector have a "Show" button which will select the referenced Entity in the Debugger.
* An `ArchetypeChunkIterator` can be created by calling `GetArchetypeChunkIterator` on an `EntityQuery`. You may run an `IJobChunk` while bypassing the Jobs API by passing an `ArchetypeChunkIterator` into `IJobChunk.RunWithoutJobs()`.
* The `[AlwaysSynchronizeSystem]` attribute has been added, which can be applied to a `JobComponentSystem` to force it to synchronize on all of its dependencies before every update.
* `BoneIndexOffset` has been added, which allows the Animation system to communicate a bone index offset to the Hybrid Renderer.
* Initial support for using Hybrid Components during conversion, see the HybridComponent sample in the StressTests folder.
* New `GameObjectConversionSystem.ForkSettings()` that provides a very specialized method for creating a fork of the current conversion settings with a different "EntityGuid namespace", which can be used for nested conversions. This is useful for example in net code where multiple root-level variants of the same authoring object need to be created in the destination world.
* `EntityManager` `LockChunkOrder` and `UnlockChunkOrder` are deprecated.
* Entity Scenes can be loaded synchronously (during the next streaming system update) by using `SceneLoadFlags.BlockOnStreamIn` in `SceneSystem.LoadParameters`.
* `EntityCommandBuffer` can now be played back on an `ExclusiveEntityTransaction` as well as an `EntityManager`. This allows ECB playback to   be invoked from a job (though exclusive access to the EntityManager data is still required for the duration of playback).

### Upgrade guide
* If you are using SubScenes you must use the new BuildSettings assets to make a build & run it. SubScenes are not supported from the File -> BuildSettings... & File -> Build and Run workflows.
* Entities requires AssetDatabase V2 for certain new features, we do not provide support for AssetDatabase V1.

### Fixes

* Setting `ComponentSystemGroup.Enabled` to `false` now calls `OnStopRunning()` recursively on the group's member systems, not just on the group itself.
* Updated Properties pacakge to `0.10.3-preview` to fix an exception when showing Physics ComponentData in the inspector as well as fix IL2CPP Ahead of Time linker errors for generic virtual function calls.
* The `LocalToParentSystem` will no longer write to the `LocalToWorld` component of entities that have a component with the `WriteGroup(typeof(LocalToWorld))`.
* Entity Debugger styling work better with Pro theme
* Entity Inspector no longer has runaway indentation
* Fixed issue where `AddSharedComponentData`, `SetSharedComponentData` did not always update `SharedComponentOrderVersion`.
* Fixes serialization issue when reading in managed `IComponentData` containing array types and `UnityEngine.Object` references.
* No exception is thrown when re-adding a tag component with `EntityQuery`.
* `AddComponent<T>(NativeArray<Entity>)` now reliably throws an `ArgumentException` if any of the target entities are invalid.
* Fixed an issue where the Entity Debugger would not repaint in edit mode
* Marking a system as `[UpdateInGroup(typeof(LateSimulationSystemGroup))]` no longer emits a warning about `[DisableAutoCreation]`.
* Fixed rendering of chunk info to be compatible with HDRP
* Fixed issue where `ToComponentDataArray` ignored the filter settings on the `EntityQuery` for managed component types.

### Changes

* Deprecated `DynamicBuffer.Reserve` and made `DynamicBuffer.Capacity` a settable property. `DynamicBuffer.Reserve(10)` should now be `DynamicBuffer.Capacity = 10`.
* Moved `NativeString` code from Unity.Entities to Unity.Collections.
* Updated dependencies for this package.
* Significantly improved `Entity` instantiation performance when running in-Editor.
* Added support for managed `IComponentData` types such as `class MyComponent : IComponentData {}` which allows managed types such as GameObjects or List<>s to be stored in components. Users should use managed components sparingly in production code when possible as these components cannot be used by the Job System or archetype chunk storage and thus will be significantly slower to work with. Refer to the documentation for [component data](xref:ecs-component-data) for more details on managed component use, implications and prevention.
* 'SubSceneStreamingSystem' has been renamed to `SceneSectionStreamingSystem` and is now internal
* Deprecated `_SceneEntities` in `SubScene.cs`. Please use `SceneSystem.LoadAsync` / `Unload` with the respective SceneGUID instead.
* Updated `com.unity.serialization` to `0.6.3-preview`.
* The deprecated `GetComponentGroup()` APIs are now `protected` and can only be called from inside a System like their `GetEntityQuery()` successors.
* All GameObjects with a ConvertToEntity set to "Convert and Destroy" will all be processed within the same conversion pass, this allows cross-referencing.
* Duplicate component adds are always ignored
* When adding component to single entity via EntityQuery, entity is moved to matching chunk instead of chunk achetype changing.
* "Used by Systems" list skips queries with filters
* Managed `IComponentData` no longer require all fields to be non-null after default construction.
* `ISharedComponentData` is serialized inline with entity and managed `IComponentData`. If a shared component references a `UnityEngine.Object` type, that type is serialized separately in an "objrefs" resource asset.
* `EntityManager` calls `EntityComponentStore` via burst delegates for `Add`/`Remove` components.
* `EntityComponentStore` cannot throw exceptions (since called as burst delegate from main thread.)
* `bool ICustomBootstrap.Initialize(string defaultWorldName)` has changed API with no deprecated fallback. It now simply gives you a chance to completely replace the default world initialization by returning true.
* `ICustomBootstrap` & `DefaultWorldInitialization` is now composable like this:
```
class MyCustomBootStrap : ICustomBootstrap
{
    public bool Initialize(string defaultWorldName)
    {
        Debug.Log("Executing bootstrap");
        var world = new World("Custom world");
        World.DefaultGameObjectInjectionWorld = world;
        var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);

        DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);
        ScriptBehaviourUpdateOrder.UpdatePlayerLoop(world);
        return true;
    }
}
```
* `ICustomBootstrap` can now be inherited and only the most deepest subclass bootstrap will be executed.
* `DefaultWorldInitialization.GetAllSystems` is not affected by bootstrap, it simply returns a list of systems based on the present dlls & attributes.
* `Time` is now available per-World, and is a property in a `ComponentSystem`.  It is updated from the `UnityEngine.Time` during the `InitializationSystemGroup` of each world.  If you need access to time in a sytem that runs in the `InitializationSystemGroup`, make sure you schedule your system after `UpdateWorldTimeSystem`.  `Time` is also a limited `TimeData` struct; if you need access to any of the extended fields available in `UnityEngine.Time`, access `UnityEngine.Time` explicitly`
* Systems are no longer removed from a `ComponentSystemGroup` if they throw an exception from their `OnUpdate`. This behavior was more confusing than helpful.
* Managed IComponentData no longer require implementing the `IEquatable<>` interface and overriding `GetHashCode()`. If either function is provided it will be preferred, otherwise the component will be inspected generically for equality.
* `EntityGuid` is now constructed from an originating ID, a namespace ID, and a serial, which can be safely extracted from their packed form using new getters. Use `a` and `b` fields when wanting to treat this as an opaque struct (the packing may change again in the future, as there are still unused bits remaining). The a/b constructor has been removed, to avoid any ambiguity.
* Updated `com.unity.platforms` to `0.1.6-preview`.
* The default Api Compatibility Level should now be `.NET Standard 2.0` and a warning is generated when the project uses `.NET 4.x`.
* Added `[UnityEngine.ExecuteAlways]` to `LateSimulationSystemGroup`, so its systems run in Edit Mode.


## [0.1.1] - 2019-08-06

### New Features
* EntityManager.SetSharedComponentData(EntityQuery query, T componentData) has been added which lets you efficiently swap a shared component data for a whole query. (Without moving any component data)

### Upgrade guide

* The deprecated `OnCreateManager` and `OnDestroyManager` are now compilation errors in the `NET_DOTS` profile as overrides can not be detected reliably (without reflection).
To avoid the confusion of "why is that not being called", especially when there is no warning issued, this will now be a compilation error. Use `OnCreate` and `OnDestroy` instead.

### Changes

* Updated default version of burst to `1.1.2`

### Fixes

* Fixed potential memory corruption when calling RemoveComponent on a batch of entities that didn't have the component.
* Fixed an issue where an assert about chunk layout compatibility could be triggered when adding a shared component via EntityManager.AddSharedComponentData<T>(EntityQuery entityQuery, T componentData).
* Fixed an issue where Entities without any Components would cause UI errors in the Chunk Info view
* Fixed EntityManager.AddComponent(NativeArray<Entity> entities, ComponentType componentType) so that it handles duplicate entities in the input NativeArray. Duplicate entities are discarded and the component is added only once. Prior to this fix, an assert would be triggered when checking for chunk layout compatibility.
* Fixed invalid update path for `ComponentType.Create`. Auto-update is available in Unity `2019.3` and was removed for previous versions where it would fail (the fallback implementation will work as before).


## [0.1.0] - 2019-07-30

### New Features

* Added the `#UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_RUNTIME_WORLD` and `#UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_EDITOR_WORLD` defines which respectively can be used to disable runtime and editor default world generation.  Defining `#UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP` will still disable all default world generation.
* Allow structural changes to entities (add/remove components, add/destroy entities, etc.) while inside of `ForEach` lambda functions.  This negates the need for using `PostUpdateCommands` inside of ForEach.
* `EntityCommandBuffer` has some additional methods for adding components based on `ComponentType`, or for adding empty components of a certain type (`<T>`)
* EntityManagerDiffer & EntityManagerPatcher provides highly optimized diffing & patching functionality. It is used in the editor for providing scene conversion live link.
* Added support for `EntityManager.MoveEntitiesFrom` with managed arrays (Object Components).
* EntityManager.SetArchetype lets you change an entity to a specific archetype. Removing & adding the necessary components with default values. System state components are not allowed to be removed with this method, it throws an exception to avoid accidental system state removal. (Used in incremental live link conversion it made conversion from 100ms -> 40ms for 1000 changed game objects)
* Entity Debugger's system list now has a string filter field. This makes it easier to find a system by name when you have a lot of systems.
* Added IComponentData type `Asset` that will be used by Tiny to convert Editor assets to runtime assets
* Filled in some `<T>` holes in the overloads we provide in `EntityManager`
* New `Entities.WithIncludeAll()` that will include in matching all components that are normally ignored by default (currently `Prefab` and `Disabled`)
* EntityManager.CopyAndReplaceEntitiesFrom has been added it can be used to store & restore a backup of the world for the purposes of general purpose simulation rollback.

### Upgrade guide

* WorldDiff has been removed. It has been replaced by EntityManagerDiff & EntityManagerPatch.
* Renamed `EntityGroupManager` to `EntityQueryManager`.

### Changes

* EntityArchetype.GetComponentTypes no longer includes Entity in the list of components (it is implied). Behaviour now matches the EntityMangager.GetComponentTypes method. This matches the behavior of the corresponding `EntityManager` function.
* `EntityCommandBuffer.AddComponent(Entity, ComponentType)` no longer fails if the target entity already has the specified component.
*  DestroyEntity(EntityQuery entityQuery) now uses burst internally.

### Fixes

* Entity Inspector now shows DynamicBuffer elements in pages of five at a time
* Resources folder renamed to Styles so as not to add editor assets to built player
* `EntityQueryBuilder.ShallowEquals` (used from `Entities.ForEach`) no longer boxes and allocs GC
* Improved error message for unnecessary/invalid `UpdateBefore` and `UpdateAfter`
* Fixed leak in BlobBuilder.CreateBlobAssetReference
* ComponentSystems are now properly preserved when running the UnityLinker. Note this requires 19.3a10 to work correctly. If your project is not yet using 19.3 you can workaround the issue using the link.xml file. https://docs.unity3d.com/Manual//IL2CPP-BytecodeStripping.html
* Types that trigger an exception in the TypeManager won't prevent other types from initializing properly.

## [0.0.12-preview.33] - 2019-05-24

### New Features

* `[DisableAutoCreation]` can now apply to entire assemblies, which will cause all systems contained within to be excluded from automatic system creation. Useful for test assemblies.
* Added `ComponentSystemGroup.RemoveSystemFromUpdateList()`
* `EntityCommandBuffer` has commands for adding/removing components, deleting entities and adding shared components based on an EntityQuery and its filter. Not available in the `Concurrent` version

### Changes

* Generic component data types must now be registered in advance. Use [RegisterGenericComponentType] attribute to register each concrete use. e.g. `[assembly: RegisterGenericComponentType(typeof(TypeManagerTests.GenericComponent<int>))]`
* Attempting to call `Playback()` more than once on the same EntityCommandBuffer will now throw an error.
* Improved error checking for `[UpdateInGroup]`, `[UpdateBefore]`, and `[UpdateAfter]` attributes
* TypeManager no longer imposes alignment requirements on components containing pointers. Instead, it now throws an exception if you try to serialize a blittable component containing an unmanaged pointer, which suggests different alternatives.

### Fixes

* Fixed regression where accessing and destroying a blob asset in a burst job caused an exception
* Fixed bug where entities with manually specified `CompositeScale` were not updated by `TRSLocalToWorldSystem`.
* Error message when passing in invalid parameters to CreateSystem() is improved.
* Fixed bug where an exception due to aggressive pointer restrictions could leave the `TypeManager` in an invalid state
* SceneBoundingVolume is now generated seperately for each subsection
* SceneBoundingVolume no longer throws exceptions in conversion flow
* Fixed regression where calling AddComponent(NativeArray<Entity> entities, ComponentType componentType) could cause a crash.
* Fixed bug causing error message to appear in Inspector header when `ConvertToEntity` component was added to a disabled GameObject.

## [0.0.12-preview.32] - 2019-05-16

### New Features

* Added BlobBuilder which is a new API to build Blob Assets that does not require preallocating one contiguous block of memory. The BlobAllocator is now marked obsolete.
* Added versions of `IJobForEach` that support `DynamicBuffer`s
  * Due to C# language constraints, these overloads needed different names. The format for these overloads follows the following structure:
    * All job names begin with either `IJobForEach` or `IJobForEachEntity`
    * All jobs names are then followed by an underscore `_` and a combination of letter corresponding to the parameter types of the job
      * `B` - `IBufferElementData`
      * `C` - `IComponentData`
      * `E` - `Entity` (`IJobForEachWithEntity` only)
    * All suffixes for `WithEntity` jobs begin with `E`
    * All data types in a suffix are in alphabetical order
  * Here is the complete list of overloads:
    * `IJobForEach_C`, `IJobForEach_CC`, `IJobForEach_CCC`, `IJobForEach_CCCC`, `IJobForEach_CCCCC`, `IJobForEach_CCCCCC`
    * `IJobForEach_B`, `IJobForEach_BB`, `IJobForEach_BBB`, `IJobForEach_BBBB`, `IJobForEach_BBBBB`, `IJobForEach_BBBBBB`
    * `IJobForEach_BC`, `IJobForEach_BCC`, `IJobForEach_BCCC`, `IJobForEach_BCCCC`, `IJobForEach_BCCCCC`, `IJobForEach_BBC`, `IJobForEach_BBCC`, `IJobForEach_BBCCC`, `IJobForEach_BBCCCC`, `IJobForEach_BBBC`, `IJobForEach_BBBCC`, `IJobForEach_BBBCCC`, `IJobForEach_BBBCCC`, `IJobForEach_BBBBC`, `IJobForEach_BBBBCC`, `IJobForEach_BBBBBC`
    * `IJobForEachWithEntity_EB`, `IJobForEachWithEntity_EBB`, `IJobForEachWithEntity_EBBB`, `IJobForEachWithEntity_EBBBB`, `IJobForEachWithEntity_EBBBBB`, `IJobForEachWithEntity_EBBBBBB`
    * `IJobForEachWithEntity_EC`, `IJobForEachWithEntity_ECC`, `IJobForEachWithEntity_ECCC`, `IJobForEachWithEntity_ECCCC`, `IJobForEachWithEntity_ECCCCC`, `IJobForEachWithEntity_ECCCCCC`
    * `IJobForEachWithEntity_BC`, `IJobForEachWithEntity_BCC`, `IJobForEachWithEntity_BCCC`, `IJobForEachWithEntity_BCCCC`, `IJobForEachWithEntity_BCCCCC`, `IJobForEachWithEntity_BBC`, `IJobForEachWithEntity_BBCC`, `IJobForEachWithEntity_BBCCC`, `IJobForEachWithEntity_BBCCCC`, `IJobForEachWithEntity_BBBC`, `IJobForEachWithEntity_BBBCC`, `IJobForEachWithEntity_BBBCCC`, `IJobForEachWithEntity_BBBCCC`, `IJobForEachWithEntity_BBBBC`, `IJobForEachWithEntity_BBBBCC`, `IJobForEachWithEntity_BBBBBC`
    * Note that you can still use `IJobForEach` and `IJobForEachWithEntity` as before if you're using only `IComponentData`.
* EntityManager.SetEnabled API automatically enables & disables an entity or set of entities. If LinkedEntityGroup is present the whole group is enabled / disabled. Inactive game objects automatically get a LinkedEntityGroup added so that EntityManager.SetEnabled works as expected out of the box.
* Add `WithAnyReadOnly` and `WithAllReadyOnly` methods to EntityQueryBuilder to specify queries that filter on components with access type ReadOnly.
* No longer throw when the same type is in a WithAll and ForEach delegate param for ForEach queries.
* `DynamicBuffer` CopyFrom method now supports another DynamicBuffer as a parameter.
* Fixed cases that would not be handled correctly by the api updater.

### Upgrade guide

* Usages of BlobAllocator will need to be changed to use BlobBuilder instead. The API is similar but Allocate now returns the data that can be populated:

  ```csharp
  ref var root = ref builder.ConstructRoot<MyData>();
  var floatArray = builder.Allocate(3, ref root.floatArray);
  floatArray[0] = 0; // root.floatArray[0] can not be used and will throw on access
  ```

* ISharedComponentData with managed fields must implement IEquatable and GetHashCode
* IComponentData and ISharedComponentData implementing IEquatable must also override GetHashCode

### Fixes

* Comparisons of managed objects (e.g. in shared components) now work as expected
* Prefabs referencing other prefabs are now supported in game object entity conversion process
* Fixed a regression where ComponentDataProxy was not working correctly on Prefabs due to a ordering issue.
* Exposed GameObjectConversionDeclarePrefabsGroup for declaring prefab references. (Must happen before any conversion systems run)
* Inactive game objects are automatically converted to be Disabled entities
* Disabled components are ignored during conversion process. Behaviour.Enabled has no direct mapping in ECS. It is recommended to Disable whole entities instead
* Warnings are now issues when asking for a GetPrimaryEntity that is not a game object that is part of the converted group. HasPrimaryEntity can be used to check if the game object is part of the converted group in case that is necessary.
* Fixed a race condition in `EntityCommandBuffer.AddBuffer()` and `EntityCommandBuffer.SetBuffer()`

## [0.0.12-preview.31] - 2019-05-01

### New Features

### Upgrade guide

* Serialized entities file format version has changed, Sub Scenes entity caches will require rebuilding.

### Changes

* Adding components to entities that already have them is now properly ignored in the cases where no data would be overwritten. That means the inspectable state does not change and thus determinism can still be guaranteed.
* Restored backwards compatibility for `ForEach` API directly on `ComponentSystem` to ease people upgrading to the latest Unity.Entities package on top of Megacity.
* Rebuilding the entity cache files for sub scenes will now properly request checkout from source control if required.

### Fixes

* `IJobForEach` will only create new entity queries when scheduled, and won't rely on injection anymore. This avoids the creation of useless queries when explicit ones are used to schedule those jobs. Those useless queries could cause systems to keep updating even though the actual queries were empty.
* APIs changed in the previous version now have better obsolete stubs and upgrade paths.  All obsolete APIs requiring manual code changes will now soft warn and continue to work, instead of erroring at compile time.  These respective APIs will be removed in a future release after that date.
* LODGroup conversion now handles renderers being present in a LOD Group in multipe LOD levels correctly
* Fixed potential memory leak when disposing an EntityCommandBuffer after certain types of playback errors
* Fixed an issue where chunk utilization histograms weren't properly clipped in EntityDebugger
* Fixed an issue where tag components were incorrectly shown as subtractive in EntityDebugger
* ComponentSystem.ShouldRunSystem() exception message now more accurately reports the most likely reason for the error when the system does not exist.

### Known Issues

* It might happen that shared component data with managed references is not compared for equality correctly with certain profiles.


## [0.0.12-preview.30] - 2019-04-05

### New Features
Script templates have been added to help you create new component types and systems, similar to Unity's built-in template for new MonoBehaviours. Use them via the Assets/Create/ECS menu.

### Upgrade guide

Some APIs have been deprecated in this release:

[API Deprecation FAQ](https://forum.unity.com/threads/api-deprecation-faq-0-0-23.636994/)

** Removed obsolete ComponentSystem.ForEach
** Removed obsolete [Inject]
** Removed obsolete ComponentDataArray
** Removed obsolete SharedComponentDataArray
** Removed obsolete BufferArray
** Removed obsolete EntityArray
** Removed obsolete ComponentGroupArray

####ScriptBehaviourManager removal
* The ScriptBehaviourManager class has been removed.
* ComponentSystem and JobComponentSystem remain as system base classes (with a common ComponentSystemBase class)
  * ComponentSystems have overridable methods OnCreateManager and OnDestroyManager. These have been renamed to OnCreate and OnDestroy.
    * This is NOT handled by the obsolete API updater and will need to be done manually.
    * The old OnCreateManager/OnDestroyManager will continue to work temporarily, but will print a warning if a system contains them.
* World APIs have been updated as follows:
  * CreateManager, GetOrCreateManager, GetExistingManager, DestroyManager, BehaviourManagers have been renamed to CreateSystem, GetOrCreateSystem, GetExistingSystem, DestroySystem, Systems.
    * These should be handled by the obsolete API updater.
  * EntityManager is no longer accessed via GetExistingManager. There is now a property directly on World: World.EntityManager.
    * This is NOT handled by the obsolete API updater and will need to be done manually.
    * Searching and replacing Manager<EntityManager> should locate the right spots. For example, world.GetExistingManager<EntityManager>() should become just world.EntityManager.

#### IJobProcessComponentData renamed to IJobForeach
This rename unfortunately cannot be handled by the obsolete API updater.
A global search and replace of IJobProcessComponentData to IJobForEach should be sufficient.

#### ComponentGroup renamed to EntityQuery
ComponentGroup has been renamed to EntityQuery to better represent what it does.
All APIs that refer to ComponentGroup have been changed to refer to EntityQuery in their name, e.g. CreateEntityQuery, GetEntityQuery, etc.

#### EntityArchetypeQuery renamed to EntityQueryDesc
EntityArchetypeQuery has been renamed to EntityQueryDesc

### Changes
* Minimum required Unity version is now 2019.1.0b9
* Adding components to entities that already have them is now properly ignored in the cases where no data would be overwritten.
* UNITY_CSHARP_TINY is now NET_DOTS to match our other NET_* defines

### Fixes
* Fixed exception in inspector when Script is missing
* The presence of chunk components could lead to corruption of the entity remapping during deserialization of SubScene sections.
* Fix for an issue causing filtering with IJobForEachWithEntity to try to access entities outside of the range of the group it was scheduled with.
