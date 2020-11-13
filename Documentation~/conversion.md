---
uid: conversion
---
# Conversion Workflow

To use Unity’s DOTS technology, you need to create entities, components and systems.

The generation process that consumes GameObjects (authoring data) and generates entities and components (runtime data) is called _conversion_.

- This process is the preferred way of authoring ECS data
- It is a fundamental part of DOTS, and not something temporary
- Conversion is only about data, there is no conversion process for code

The overall workflow looks like this:
1. The Unity Editor is a user interface to work with authoring data
1. The conversion from authoring data to runtime data happens in the Unity Editor
1. The runtime (e.g. the game) should only ever have to deal with runtime data

## Fundamental principles

Authoring data and runtime data are optimized for wildly different goals.

- Authoring data is optimized for flexibility
    - Human understandability and editability
    - Version control (mergeability, no duplication)
    - Teamwork organization
- Runtime data is optimized for performance
    - Cache efficiency
    - Loading time and streaming
    - Distribution size

A key observation is that nothing requires a 1:1 mapping between GameObjects and entities.

- A single GameObject can turn into a set of entities, e.g. procedural generation
- Multiple GameObjects can be aggregated into a single entity, e.g. LOD baking
- Some GameObjects might have no purpose at runtime, e.g. level editing markers

The same can be said about components. A conversion system can read from any amount of Unity components and add any amount of ECS components to any amount of entities.

## Key concepts

All those concepts get explained in further detail in the rest of this document, but it's useful to introduce some vocabulary beforehand.

- __Authoring scene__ <br>
A regular Unity scene, containing GameObjects, destined to be converted to runtime data.
- __Subscene__ <br>
A simple GameObject component that references an authoring scene and will either load the authoring scene (when the Subscene is in edit mode), or stream in the converted entity scene (when the Subscene is closed).
- __Entity scene__ <br>
The result of converting an authoring scene. Because entity scenes are the output of an asset import, they are stored in the Library folder. Entity scenes can be made of multiple sections, and each of those can be independently loaded.
- __LiveConversion__ <br>
When an authoring scene is loaded as GameObjects for editing, every change will trigger an update to the entity scene, making it look as if the entity scene was directly edited, we call this process _LiveConversion_.
- __LiveConnection__ <br>
The Unity Editor can be connected to a _LiveLink Player_ eventually running on a different machine. This is out of scope for this document but the main thing to know about it is that it shouldn't require any particular attention. It's part of the framework, not something that has to be implemented for each feature.
- __LiveLink__ <br>
We call _LiveLink_ the set of features that make live editing possible. It's not something optional, it's a concept which is deeply embedded in everything related to conversion. The main consequence you should pay attention to is that this can cause ECS data to change at any time. Taking this into consideration while designing ECS systems is where the real challenge is.

## Scene conversion

Conversion systems run on whole scenes at once.

We still support more granular approaches like the `ConvertToEntity` MonoBehaviour and some function calls in `GameObjectConversionUtility`, but those should be avoided and are only kept around for dependencies we will remove in the near future. These approaches do not scale, are bound to be deprecated, and will not be addressed by this document.

When converting an authoring scene to an entity scene, the following steps are executed, in order:

1. Set up a _conversion world_ and create an entity in it for every GameObject in the scene
1. Collect external references (e.g. prefabs)
1. Create primary entities in the destination world corresponding to those in the conversion world
1. Update the main conversion system groups
1. Tag entity prefabs
1. Create companion GameObjects for hybrid components
1. Create linked entity groups
1. Update the export system group (only for Unity Tiny)

All these points are covered in this document, keeping the above sequence in mind will help you structure the information.

Note that all the referenced prefabs are converted at the same time as the regular authoring GameObjects, and not in a dedicated pass.

## Large scale performance

The conversion workflow allows dealing efficiently with large scenes thanks to the following principles:

- In the Unity Editor, a scene can be broken down into subscenes. Those can be toggled back and forth between runtime data and authoring data, depending on which parts of the scene have to be worked on.
- Conversion can run as an asset database v2 importer, this allows proper dependency tracking, on-demand importing and running in a background process in order to not stall the Editor.
- Changes to the authoring data are monitored in order to only convert what needs to be updated, this is called incremental conversion.

## The Subscene Monobehaviour

The conversion of entity scenes is usually done through the use of a Subscene. This Monobehaviour does very little. It references an authoring scene and triggers the conversion and loading of the resulting entity scene.

There is a toggle to control the automatic loading of the entity scene, and button to a load and unload the sections manually. You also have a button to force the reimport (redoing the conversion) of the scene.

> [!WARNING]
> Be aware that forcing the reimport can hide problems, this option is intended for testing and debugging purposes. If the reimport doesn't happen automatically, it might be because some dependencies or versioning info are missing. Please read the further sections about the asset pipeline for more details.

More importantly, you can toggle between edit mode (authoring) and closed (runtime). The contents of a scene can only be accessed by the Unity editor when the Subscene that references it is in edit mode.

Since authoring scenes are normal Unity scenes they can also be directly opened like any other scene.

## DOTS > Live Link Mode

This menu offers two options (one on/off, one toggle), it's important to understand their implications when using conversion.

### 1. Live Conversion in Edit Mode

When in play mode, the Subscenes will always stream in the runtime scene sections. When not in play mode, the existence of the converted entities depends on both the "Live Conversion in Edit Mode" option and on the availability of the authoring representation (Subscene in edit mode).

When a Subscene is in edit mode, the authoring GameObjects show up in the Hierarchy window of the Unity editor and can be interacted with. The runtime representation for the authoring scene referenced by the Subscene will only be available if "Live Conversion in Edit Mode" is enabled, and since every change in the authoring representation can potentially make the runtime representation obsolete, this conversion will happen every time something is edited.

### 2. SceneView: Editing State / Live Game State

When editing an authoring scene, either in play mode or in edit mode with the "Live Conversion in Edit Mode" enabled, both the authoring and runtime components are available. The "SceneView" option is a toggle between the two.

If "Editing State" is selected, the SceneView will display the authoring components. Since those are regular GameObjects, they can be interacted with in a familiar fashion (selection, gizmos, etc.) but if "Live Game State" is selected the scene view will render runtime components that cannot be interacted with from the editor.

In many cases it won't be possible to visually tell those two modes apart, since most of the authoring components will be converted into similar looking runtime components. Also keep in mind that when you have a mix of subscenes in edit mode and closed subscenes, even if "Editing State" is selected, the closed subscenes will still render runtime components since their authoring components are not available.

## Conversion systems 101

The conversion process is a succession of component systems that update only once each. A big difference between those and regular DOTS systems is that conversion systems straddle two worlds, they read from one and write to the other.

Conversion systems inherit from `GameObjectConversionSystem` and run from a temporary _conversion world_ (authoring world), which should be treated as read-only input. During update, they write to the _destination world_ (converted world), which is accessed through the `DstEntityManager` property of each system.

In the example below, note the use of `GetPrimaryEntity` to access the entity in the destination world that corresponds to the provided authoring component. Adding an `Entity` parameter to the `ForEach` lambda would provide the entity from the authoring world instead, which would be pointless since conversion systems should not modify the conversion world and only write to the destination world.

Here's the "hello world" of conversion systems, that does a 1:1 conversion of all authoring components of a certain type to their ECS equivalent.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#conversion101)]

In a `GameObjectConversionSystem`, `ForEach` will not create jobs. It runs on the main thread, without Burst, and this allows accessing classic Unity without restraint. This is also why it doesn't require a call to `.Run()` or `.Schedule()`.

Also note that the entity query looks for classic Unity components, in this case `FooAuthoring` that derives from `MonoBehaviour`. Since those are reference types, they do not require `ref` or `in`.

## Conversion World (the input)

When a conversion starts, an entity is created in the conversion world for each GameObject that should be processed. In the case of a whole authoring scene, that's typically all the GameObjects it contains and all the GameObjects from all the referenced prefabs (recursively). Prefabs are discussed in detail further on.

Each component on those GameObjects is then added to the corresponding entities. This is a mechanism rarely used in DOTS, because using classic Unity components is something that doesn't scale. Those components are reference types and each access from ECS accesses memory in an inefficient way.

The only reason this is done this way is to allow conversion systems to access authoring components using entity queries.

Note that disabled authoring components are not added to the conversion world, so the queries from the conversion systems will not pick them up. And inactive GameObjects turn into disabled entities, but the conversion happens normally.

## Destination World (the output)

For each authoring GameObject in the conversion world, a _primary entity_ will automatically be created in the destination world before any conversion system runs. The entity associated with a GameObject can be subsequently accessed via `GameObjectConversionSystem.GetPrimaryEntity`.

Every entity in the destination world is associated with a GameObject in the conversion world. This is important for keeping track of dependencies: when the authoring GameObject changes, all the entities that were created as a result of its existence have to be updated.

At creation time, based on conversion settings, the entities in the destination world will contain a combination of the following components:
- `Static`, to bake transforms.
- `EntityGuid`, for LiveLink.
- `Disabled`, to mark entities coming from disabled GameObjects as disabled.
- `SceneSection`, for streaming sections.

Altering that set of components will break the logic of conversion, so care should be taken (e.g. `SetArchetype` shouldn't be used during conversion).

On top of that, the name of the GameObject will also be copied as the entity name (which is a debug only feature, stripped from builds), and the mapping between GameObjects and entities is recorded for error reporting.

> Directly creating new entities in the destination world (via `CreateEntity`, `Instantiate`, etc.) will bypass that setup and will cause issues, so when a new entity has to be created, it has to be done though `GameObjectConversionSystem.CreateAdditionalEntity` instead. This function will also update the dependencies by associating the new entity with the GameObject.

## Conversion Systems Ordering

Like any other system, conversion systems can be ordered by using the following attributes:

* `[UpdateBefore]`
* `[UpdateAfter]`
* `[UpdateInGroup]`

The default system groups provided for conversion are, in this order:

1. `[GameObjectDeclareReferencedObjectsGroup]` - before the creation of entities in the destination world.
1. `[GameObjectBeforeConversionGroup]` - early conversion group
1. `[GameObjectConversionGroup]` - main conversion group (this is the default when no groups is explicitly specified)
1. `[GameObjectAfterConversionGroup]` - late conversion group
1. `[GameObjectExportGroup]` - only for Unity Tiny

> [!IMPORTANT]
> Calling `GetPrimaryEntity` during conversion will return a __partially__ constructed entity, the set of components on this entity will depend on system ordering.

## Prefabs

An entity prefab is nothing more than an entity with a `Prefab` tag and a `LinkedEntityGroup`. The former identifies the prefab and makes it invisible to all entity queries but the ones who explicitly include prefabs, and the latter links together a set of entities, since entity prefabs can be complex assemblies (equivalent to GameObject hierarchies).

So the following two components are equivalent, one in classic Unity and the other in DOTS.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#PrefabReference)]

By default, the conversion workflow only processes the actual contents of an authoring scene, so a specific mechanism is required to also include prefabs from the asset folder. This is the purpose of the system group `GameObjectDeclareReferencedObjectsGroup`, it runs before the primary entities are created in the destination world, and provides a way of registering prefabs for conversion.

Example: the following system will register all the prefabs referenced by the `PrefabReference` component above, this will cause primary entities to be created for all the GameObjects contained in those prefabs.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#PrefabConverterDeclare)]

 Please note that this system will be updated for as long as the set of declared GameObjects keeps growing. This means that if you have a GameObject `A` (in an authoring scene) that references a prefab `B` (in the asset folder) that itself references another prefab `C` (in the asset folder) that doesn't reference anything, the system above will update three times.

- The first time `PrefabConverterDeclare` runs, the `ForEach` will iterate over the set `{ A }` and it will declare `A.Prefab` (this grows the set by one, it becomes `{ A, B }`).
- The second time `PrefabConverterDeclare` runs, the `ForEach` will iterate over the set `{ A, B }` and it will declare `A.Prefab` and `B.Prefab` (this grows the set by one, it becomes `{ A, B, C }`).
- The third time `PrefabConverterDeclare` runs, the `ForEach` will iterate over the set `{ A, B, C }` and it will declare `A.Prefab` and `B.Prefab` (there is no C.prefab, so this doesn't grow the set, it remains `{ A, B, C }`).
- Because the set didn't grow since the last iteration, the process stops.

Calling `DeclareReferencedPrefab` multiple times on the same prefab will only register it once.

Calling `DeclareReferencedPrefab` from a system which isn't part of `GameObjectDeclareReferencedObjectsGroup` is an error and will throw an exception.

Declared prefabs can be retrieved as entities by calling `GetPrimaryEntity` in a system that runs after the creation of those entities, in other words in a system which isn't part of `GameObjectDeclareReferencedObjectsGroup`.

Example: The following system will convert the components declared in the previous example.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#PrefabConverter)]

> Important remark: prefabs cannot be instantiated during conversion, for the following reasons.
> * prefabs are converted alongside all the other GameObjects, this means that `GetPrimaryEntity` will return a partially converted prefab. 
> * prefabs require a `LinkedEntityGroup` which is only initialized at the end of conversion.
> * prefab instantiation is equivalent to manually creating entities in the destination world, which breaks conversion for reasons stated earlier in this document.

## The `IConvertGameObjectToEntity` interface

Writing custom conversion systems offers maximum flexibility, but in cases where simplicity is preferred, the `IConvertGameObjectToEntity` interface can be implemented on a `MonoBehaviour` instead. During the update of the main conversion group `GameObjectConversionGroup`, all the authoring components in the conversion world that implement the `IConvertGameObjectToEntity` interface will have their `Convert` method called.

Example: The following code is equivalent to the earlier conversion system example.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#IConvertGameObjectToEntity)]

The `IConvertGameObjectToEntity` interface only requires a `Convert` function that takes the following parameters:
* `Entity entity` - the primary entity that corresponds to the current authoring component.
* `EntityManager dstManager` - the entity manager of the destination world.
* `GameObjectConversionSystem conversionSystem` - the currently running conversion system, which is calling all the `Convert` methods.

Please note that:
* `entity` lives in the destination world, so it only makes sense to use it with `dstManager`.
* `dstManager` is equivalent to `conversionSystem.DstEntityManager` and is only provided for convenience.
* There is no way to control the order in which the `Convert` functions from various `MonoBehaviour` will be called. If you need that control, you'll have to use custom conversion systems instead.

## The `IDeclareReferencedPrefabs` interface

Likewise, instead of declaring prefabs using a custom system, you can implement the `IDeclareReferencedPrefabs` interface.

Example: The following code is equivalent to the earlier `PrefabConverterDeclare` system example.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#IDeclareReferencedPrefabs)]

The `IDeclareReferencedPrefabs` interface only requires a `DeclareReferencedPrefabs` function that takes the following parameter:
* `List<GameObject> referencedPrefabs` - adding a prefab to this list will declare it. This list might already contain prefabs added by other authoring components that implement `IDeclareReferencedPrefabs`, do not clear it.

Please note that:
* Just like when declaring prefabs in a system, this process handles prefabs recursively referencing other prefabs: it will keep running as long as the set of GameObjects to be converted is growing, so the `DeclareReferencedPrefabs` function might be called several times during the conversion process.
* Adding multiple times the same prefab to the list will only register it once.

[!NOTE]
Combining `IDeclareReferencedPrefabs` and `IConvertGameObjectToEntity` on the same MonoBehaviour is fully supported and frequently used.

## Generated authoring components

For simple runtime components, the `GenerateAuthoringComponent` attribute can be used to request the automatic creation of an authoring component for a runtime component. You can then add the script containing the runtime component directly to a GameObject within the Editor.

Example: The following runtime component will generate the authoring component below, note that `DisallowMultipleComponent` is a standard Unity attribute and isn't specific to DOTS.

```c#
// Runtime component
[GenerateAuthoringComponent]
public struct Foo : IComponentData
{
    public int ValueA;
    public float ValueB;
    public Entity PrefabC;
    public Entity PrefabD;
}

// Authoring component (generated code retrieved using the DOTS Compiler Inspector)
[DisallowMultipleComponent]
internal class FooAuthoring : MonoBehaviour, IConvertGameObjectToEntity,
    IDeclareReferencedPrefabs
{
    public int ValueA;
    public float ValueB;
    public GameObject PrefabC;
    public GameObject PrefabD;

    public void Convert(Entity entity, EntityManager dstManager,
        GameObjectConversionSystem conversionSystem)
    {
        Foo componentData = default(Foo);
        componentData.ValueA = ValueA;
        componentData.ValueB = ValueB;
        componentData.PrefabC = conversionSystem.GetPrimaryEntity(PrefabC);
        componentData.PrefabD = conversionSystem.GetPrimaryEntity(PrefabD);
        dstManager.AddComponentData(entity, componentData);
    }

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        GeneratedAuthoringComponentImplementation
            .AddReferencedPrefab(referencedPrefabs, PrefabC);
        GeneratedAuthoringComponentImplementation
            .AddReferencedPrefab(referencedPrefabs, PrefabD);
    }
}
```

Note the following restrictions:

* Generated authoring types _will_ overwrite existing types with the same names. E.g., if you have an `IComponentData` type named `MyAwesomeComponent` with the `[GenerateAuthoringComponent]` attribute, your own implementation of `MyAwesomeComponentAuthoring` will be overwritten by the generated `MyAwesomeComponentAuthoring`.
* Only one component in a single C# file can have a generated authoring component, and the C# file must not have another MonoBehaviour in it.
* The file doesn't have to follow any naming convention, i.e. it doesn't have to be named after the generated authoring component.
* ECS only reflects public fields and they have the same name as that specified in the component.
* ECS reflects fields of an Entity type in the IComponentData as fields of GameObject types in the MonoBehaviour it generates. ECS converts the GameObjects or Prefabs you assign to these fields as referenced Prefabs.
* There is no way to specify default values for the fields.
* There is no way to implement authoring callbacks (e.g. `OnValidate`)

You can also generate authoring components for types that implement `IBufferElementData`.

Example: The following runtime component will generate the authoring component below, the source for `BufferElementAuthoring` is in the entities package, it does exactly what you'd expect.

```c#
// Runtime component
[GenerateAuthoringComponent]
public struct FooBuffer : IBufferElementData
{
    public int Value;
}

// Authoring component (generated code retrieved using ILSpy)
internal class FooBufferAuthoring :
    Unity.Entities.Hybrid.BufferElementAuthoring<FooBuffer, int>
{
}
```

Note the following additional restrictions:

* `IBufferElementData` authoring components cannot be automatically generated for types that contain 2 or more fields.
* `IBufferElementData` authoring components cannot be automatically generated for types that have an explicit layout.

## Asset pipeline V2 and background importing

A scene conversion can happen in two different situations:

* When a subscene is open for edit, the conversion runs in the Unity editor process every time something changes.
* When a subscene is closed, the result of the conversion (entity scene) is loaded as an asset.

In that second case, entity scenes are produced on demand by the asset pipeline through the use of a scripted importer (see the asset pipeline V2 [documentation](https://docs.unity3d.com/Manual/ScriptedImporters.html) for further information on scripted importers). This conversion happens in a separate Unity process running in the background, we call this process an "asset worker".

This has a series of consequences you should be aware of:

* Importing an entity scene is asynchronous, and the first time a scene gets imported (converted) it might eventually take a long time, because the background process has to start (and it's a full blown but headless Unity editor instance). Once started, it stays resident and subsequent imports will be much faster.
* Exceptions, errors, warnings, logs, etc. won't show up in the Unity Editor. The conversion log will be visible in the inspector for each subscene, and can be monitored on the disk in the "Logs" folder inside the project folder. You'll find a file named `AssetImportWorker#.log` there, where `#` is a number that will be incremented every time the process crashes and has to be restarted. So if all goes well you should only ever see `AssetImportWorker0.log`.
* When attaching the debugger to a Unity process you should pay attention that each process will have a child process (if at least one entity scene import happened since startup), depending if you want to debug the main process or the asset import process you'll have to pick the right one. You can rely on the process name for that purpose, or on the parenting relation between the two processes: the asset worker is the child.
* The asset pipeline v2 will import assets on demand, and checks dependencies to figure out if an asset is up to date or not. It also keeps a cache of previous imports, making switching targets very efficient. But this also means that if there is a missing dependency, you can end up with stale assets. This requires extra care, as detailed hereafter.
* Because the asset pipeline keeps a cache of imported assets and their dependencies, moving back to a previous configuration will likely hit the cache and won't cause a reimport. So don't expect that doing and undoing the same change will cause a reimport.

## Type dependencies

An entity scene contains a stable hash for every runtime component type it references. This hash is used to detect any structural change in the type, in which case it would trigger a reimport of the entity scene. This means that changes to a component type will trigger the conversion process.

## The `ConverterVersion` attribute

Changes to authoring types and conversion systems will not be automatically detected. The `ConverterVersion` attribute can be used for that purpose, it has to be used on either a conversion system or an authoring type that implements `IConvertGameObjectToEntity`.

A "converter version" is a combination of the two parameters of the attribute:

* A string identifier
* A version number

A change to any of those two will affect the dependencies. The reason for the string identifier is to prevent merge issues, if two people were to bump the version number in two different development branches, it would be easy to miss that when merging and forget to bump the version number again. The string identifier can be used to force a merge conflict, as long as people changing the version don't forget to set the identifier to something that uniquely identifies them.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#ConverterVersion1)]

Please note that in the example above, the attribute has to be put on the system, not on the authoring component. Because any relationship between a conversion system and any component only exists in the `OnUpdate` of the system, so it's not something that the dependency system can reason about automatically.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#ConverterVersion2)]

In the case of an authoring component that implements `IConvertGameObjectToEntity`, the conversion code and the definition of the authoring type are in the same class, so there's no ambiguity about the location of the `ConverterVersion` attribute.
