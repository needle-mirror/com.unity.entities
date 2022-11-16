---
uid: loading-scenes
---

# Streaming scenes

Loading large scenes takes time, so to avoid stalls, all scene loading in Entities is asynchronous. This is called **streaming**.

The main advantages of streaming are:

- Your application can remain responsive while Unity streams scenes in the background.
- Unity can dynamically load and unload scenes in seamless worlds that are larger than can fit memory without interrupting gameplay.
- In Editor Play mode, if an entity scene file is missing or outdated, Unity converts the scene on demand. Because the baking and loading of the entity scene happens asynchronously and in a separate process, the Editor remains responsive.

The main disadvantages of streaming are:

- Your application can't assume scene data is present, particularly at startup. This makes game code a bit more complicated.
- Systems load scenes from the scene system group, which is part of the [initialization group](systems-update-order.md#default-system-groups). Systems that update later in the frame receive the loaded data in the same frame, but systems that update earlier than the group don't receive the loaded data until the next frame. Your code must account for this inconsistent data view within a single frame.

## Subscene Monobehaviour

A [subscene](conversion-subscenes.md) MonoBehaviour is a Unity component that triggers baking and streaming. When a subscene is opened, the authoring GameObject scene displays within the hierarchy of the parent scene.

When a subscene is closed, Unity streams in the contents of the baked scene.

The rest of this page describes how to directly control streaming without using a subscene MonoBehavior.

## Scene loading

The high level API for dealing with scenes is [`SceneSystem`](xref:Unity.Scenes.SceneSystem).

The following is an example of loading a scene at runtime. This should happen in the `OnUpdate` of a system:

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloading_101)]

>[!WARNING]
>This example schedules a load. During the call to `LoadSceneAsync`, the only thing that it creates is a scene entity, which is then used to control the rest of the loading process. The scene header, the section entities, and their content aren't loaded at this point and only appear in the world a few frames later.

The path points to a Unity authoring scene. If the corresponding entity scene file is missing or outdated, [baking](baking.md) is triggered.

## Using scene GUIDs

A scene's [GUID] is a [Hash128](xref:Unity.Entities.Hash128). Identifying a scene by its GUID is more efficient than using its string path. It's best practice to store the scene GUIDs during baking to be used for loading at runtime.

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloader_component)]

The following is an example of a sample system that processes the `SceneLoader` components at runtime:

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloadersystem)]


In the Editor, the `SceneSystem.GetGUID` method internally uses the `UnityEditor.AssetDatabase` class to map a scene path to a GUID.

In a standalone player, `UnityEditor.AssetDatabase` can't be used, so `SceneSystem.GetGUID` instead uses the `StreamingAssets/catalog.bin` file. This `catalog.bin` file is a path to a GUID mapping table, which a standalone build that uses a build configuration automatically makes.

## Scene and section meta entities

Baking an authoring scene produces an entity scene file. The header of each entity scene file contains:

- A list of sections, containing data such as file names, file sizes, and bounding volumes.
- A list of AssetBundle dependencies (GUIDs).
- Optional user-defined metadata.

The list of sections and bundles determines the list of files that Unity should load. You can optionally use the custom metadata for game-specific purposes. For example, the custom metadata might contain PVS information that can inform when to stream the scene in, or to display the scene when certain gameplay conditions have triggered, such as encountering a checkpoint. 

Loading an entity scene happens in two steps. First, the resolve stage loads the header, and creates one meta entity per scene and per section. And only after, Unity loads the contents of the sections.

Unity uses these scene and section meta entities to control streaming. By default, a call to `SceneSystem.LoadSceneAsync` resolves and load everything.

You should be aware of the following:

* To load and unload whole scenes, call methods on `SceneSystem`.
* To load and unload scene sections, add and remove the `RequestLoaded` component on the entity that represents the scene. The `SceneSectionStreamingSystem`, which is part of the `SceneSystemGroup` processes these requests.

## Streaming status

Streaming is asynchronous, so there is no guarantee of how long it takes for the data to load after it has been requested. Although you can use `SceneSystem` to query the loading status of scenes and sections, you shouldn't usually have to.

It's best practice to make your systems react to the presence or absence of data it needs, and not to whether certain scenes have loaded or not. If the data that a system requires to run is part of a particular scene, then the system should check whether that specific data has loaded, and not whether the scene has loaded.

This approach avoids tying systems to particular scenes: if the data that a system needs is moved to a different scene, downloaded from the network, or procedurally generated, the system still works the same without changes to its code.

However, there are cases where you might need to check whether a scene or section has loaded. For example, if you want to  implement a loading screen that remains visible until all the scheduled streaming is complete. The following example shows how to do this:

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloading_isstuffloaded)]

## Scene sections

You can load and unload the individual sections of a scene independently.

Each entity in a scene has a [SceneSection](xref:Unity.Entities.SceneSection) shared component, which contains the GUID of the scene as a [Hash128](xref:Unity.Entities.Hash128), and the section number as an integer. Section 0 is the default section.

To set the section of an entity, during baking, change the value of the `SceneSection` shared component:

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#setsection)]

Adding the above component to a GameObject in an authoring scene referenced by a subscene causes the Inspector for that subscene to look like this:

![](images/scene_section.png)

The default section 0 (called `ConvertedScene` in the above example) is always displayed even if it's empty. The `Section: 0` part of the name is omitted, but all other sections that contain at least one entity display with their full name.

All sections can reference both their own entities and the entities from section 0. Because of this, loading any section from a scene requires section 0 from that same scene to also be loaded. The same constraint applies for unloading: section 0 of a scene can only be unloaded when no other sections from the same scene are currently loaded.

Some existing Entities features already leverage section loading. To explicitly control section loading in your own code, you can write [custom baking systems](baking.md), or use the authoring component `SceneSectionComponent` which affects all authoring GameObjects within a hierarchy.

## Independently loading scene sections

Calling `LoadSceneAsync` with the `DisableAutoLoad` parameter creates a scene and section meta entities to resolve a scene, but doesn't load the section contents:

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloading_resolveonly)]

Once Unity processes the load request, the sections are resolved and their meta entities are created. You can then query the `ResolvedSectionEntity` buffer on the scene meta entity. The following code example loads every other section of a given scene:

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloading_requestsections)]

## Unloading scenes and sections

To unload a whole scene and its sections, use `SceneSystem`:

[!code-cs[sample](../DocCodeSamples.Tests/StreamingExamples.cs#sceneloading_unloadscene)]

You can call `UnloadScene` with a scene's GUID instead of its meta entity, but this has two disadvantages:

- This method has to search for the meta entity that represents the scene that matches the GUID, which might affect performance.
- If multiple instances of the same scene are loaded, then unloading by GUID only unloads one instance.
