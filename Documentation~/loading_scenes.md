---
uid: loading-scenes
---
# Loading scenes at runtime

## Streaming

Loading large scenes takes time, so in order to avoid stalls, all scene loading in DOTS is asynchronous by default. This is called _streaming_.

Because retrofitting a project to use streaming can be onerous, you're best off deciding whether to use streaming in a project as early as possible.

The main advantages of streaming are:

- The application can remain responsive while scenes are streamed in the background.
- Seamless worlds larger than would fit memory are made possible by dynamically loading and unloading scenes without interrupting gameplay.
- In Editor play mode, if an entity scene file is missing or outdated, the scene gets converted on demand. Because the conversion and loading of the entity scene happens asynchronously and in a separate process, the editor remains responsive.

The main disadvantages of streaming are:

- The game cannot assume loaded data to be immediately present, particularly at startup. This makes game code a bit more complicated.
- Scenes are loaded by systems from the "Scene System Group", which is itself part of the "Initialization Group". Systems which update later in the frame will see the loaded data in the same frame, but systems which update *earlier* than the group won't see the loaded data until the next frame. Your code then must account for this inconsistent view of data within a single frame.

## The Subscene Monobehaviour

A Subscene Monobehavior is a simple Unity component that abstracts conversion and streaming concerns.

- When a Subscene is opened, the authoring GameObject scene shows up within the hierarchy of the parent scene (see [Conversion]).
- When a Subscene is closed, the contents of the converted scene are streamed in.

The rest of this page describes how to directly control streaming without using a Subscene MonoBehavior.

## Scene loading 101

The high level API for dealing with scenes is provided by [SceneSystem].

Here is the most basic example of loading a scene at runtime. This should be done in the `OnUpdate` of a system.

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloading_101)]

>[!WARNING]
>This example schedules a load. During the call to `LoadSceneAsync`, the only thing that is created is a scene entity, which is then used to control the rest of the loading process. Notably, the scene header, the section entities, and their content are not yet loaded at this point and will only appear in the world a few frames later.

- In the context of DOTS, a scene [GUID] is a [Hash128].
- The path points to a Unity authoring scene. If the corresponding entity scene file is missing or outdated, conversion is triggered (see [Conversion]).

## Using scene GUIDs

Identifying a scene by its GUID is way more efficient than using its string path. Consequently, the usual approach is to store the scene GUIDs during conversion to be used for loading at runtime.

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloader_component)]

The sample system processing the `SceneLoader` components at runtime would look like this:

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloadersystem)]

>[!NOTE]
> In the Editor, the `SceneSystem.GetGUID` function internally uses the `UnityEditor.AssetDatabase` class to map a scene path to a GUID.
>
>In a standalone player, `UnityEditor.AssetDatabase` cannot be used, so `SceneSystem.GetGUID` instead uses the "StreamingAssets/catalog.bin" file. This "catalog.bin" file is nothing more than a "path to GUID" mapping table, and that catalog file is produced by making a standalone build using a build configuration (see "Standalone Builds" in [DOTS Project Setup]).

## Scene and section meta entities

Conversion of a authoring scene produces an entity scene file. The header of each entity scene file contains:
- A list of sections (containing data like file names, file sizes, bounding volumes, etc.).
- A list of asset bundle dependencies (GUIDs).
- Optional user-defined metadata.

The list of sections and bundles determines the list of files that should be loaded, and the custom metadata can be used for game-specific purposes. For example, the custom metadata could contain [PVS] information to inform the decision of when to stream the scene in, or gameplay conditions like "this scene is only relevant if quest XYZ is active". It is up to each game to decide how to use the custom metadata. But using custom metadata is both optional and an advanced topic, so it will be documented and illustrated later.

Loading an entity scene is done in two steps. First, the "resolve" stage loads the header, and creates one meta entity per scene and per section. And only after, the contents of the sections will be loaded.

These scene and section meta entities are used to control the actual streaming. By default, a call to `SceneSystem.LoadSceneAsync` will resolve and load everything.

>[!NOTE]
>- Whole scenes should be loaded and unloaded by calling methods on `SceneSystem`.
>- Scene sections are loaded and unloaded by adding and removing the `RequestLoaded` component on the entity representing the scene. These requests are processed by the `SceneSectionStreamingSystem`, which is part of the `SceneSystemGroup`.

## Streaming status

Streaming is asynchronous, so there is no guarantee of how long it will take for the data to be loaded after it has been requested. Although the `SceneSystem` allows querying the loading status of scenes and sections, doing so should not be necessary in most cases.

Ideally, systems should react to the presence or absence of data it needs, not to certain scenes being loaded or not. If the data a system requires to run is part of a particular scene, then the determination of whether to update the system should be done by checking whether that specific data has been loaded, not by checking whether the scene itself has been loaded. This approach avoids tying systems to particular scenes: if the data needed by a system is moved to a different scene, downloaded from the network, or procedurally generated, the system will still work the same without changes to its code.

Still, you can check for whether a scene or section has been loaded. For example, this could be useful for implementing a loading screen that should remain visible until all the scheduled streaming is completed.

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloading_isstuffloaded)]

## Scene sections

The individual sections of a scene can be loaded and unloaded independently.

Each entity in a scene has a [SceneSection] shared component, which contains the GUID of the scene ([Hash128]) and the section number (integer). Section 0 is the default section.

During conversion, the section of an entity can be set by changing the value of the [SceneSection] shared component, like so:

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#setsection)]

Adding the above component to a GameObject in an authoring scene referenced by a Subscene will cause the inspector for that Subscene to look like this:

![](images/scene_section.png)

Notice that the default section 0 is always there (first line) even if it is empty. The "Section: 0" part of the name is omitted, but all other sections containing at least one entity will show up with their full name.

>[!NOTE]
>All sections can reference both their own entities and the entities from section 0. Describing the way this reference system works is out of scope here, but an important consequence is that loading any section from a scene requires section 0 from that same scene to also be loaded. The same constraint applies for unloading: section 0 of a scene can only be unloaded when no other sections from the same scene are currently loaded.

Some existing DOTS features, like HLOD from the Hybrid Renderer, already leverage section loading. You can explicitly control section loading in your own code by writing custom conversion systems, by using `IConvertGameObjectToEntity` (see example above), or by using the authoring component `SceneSectionComponent` (which will affect all authoring GameObjects within a hierarchy).

## Independently loading scene sections

Calling `LoadSceneAsync` with the `DisableAutoLoad` parameter will resolve a scene by creating the scene and section meta entities, but will not load the section contents:

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloading_resolveonly)]

Once the load request has been processed, the sections are resolved and their meta entities are created. The `ResolvedSectionEntity` buffer can then be queried on the scene meta entity. As an illustration, the following code will load every other section of a given scene.

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloading_requestsections)]

## Unloading scenes and sections

Unloading a whole scene and all its sections is done through the `SceneSystem`.

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloading_unloadscene)]

It is also possible to call `UnloadScene` with a scene's GUID instead of its meta entity, but this has two disadvantages:
- The function will have to do a (potentially costly) search for the meta entity representing the scene that matches the GUID.
- If multiple instances of the same scene are loaded, unloading by GUID will only unload one instance.

[SceneSystem]: xref:Unity.Scenes.SceneSystem
[Hash128]: xref:Unity.Entities.Hash128
[Conversion]: xref:conversion
[DOTS Project Setup]: xref:install-setup
[SceneSection]: xref:Unity.Entities.SceneSection
[GUID]: https://en.wikipedia.org/wiki/Universally_unique_identifier
[PVS]: https://en.wikipedia.org/wiki/Potentially_visible_set