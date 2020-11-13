---
uid: conversion-livelink
---
# Conversion for LiveLink

Unity 2020.2 and later support converting GameObject data to Entities in real-time. To enable this feature, toggle on Live Conversion via the menu item `DOTS/Live Link Mode/Live Conversion in EditMode`. When enabled, all objects in open subscenes are automatically converted to entities in the editor. Any undoable changes to objects in these open subscenes lead to a reconversion of the affected GameObjects in the subscene. The result of this reconversion is then compared to the last known conversion result (the `shadow world` of the subscene) to generate a patch. This patch is applied to the editor world and sent to any connected LiveLink players.

## Incremental Conversion
The key feature that enables editing entity data at scale is _incremental conversion_: Whenever there is a change to a GameObject, the LiveLink code automatically detects this change and marks the GameObject for reconversion. This ensures that only the data that has actually changed is reconverted. All undoable operations are detected as changes. If an operation is not undoable, it is not detected. Since conversion might run every frame in the editor, it is crucial to ensure that the set of objects to convert is as small as possible. This introduces the difficulty that the result of incrementally converting a subset of the objects in a scene must match a full reconversion of the scene.

### Dependency Management
Generally speaking, changes to a GameObject only trigger a reconversion of that specific GameObject. GameObjects are always converted as a whole, so any change will reconvert the entire GameObject. In some cases, your conversion code might depend on other data. If your conversion code depends on more than just the input object, you need to express these additional dependencies explicitly. These are the dependencies that are currently available:
 * depending on an asset (meaning that the conversion result depends on the contents of an asset),
 * depending on another GameObject (meaning that the conversion result depends on the state of another GameObject, e.g. the presence of components),
 * depending on a component on another GameObject (meaning that the conversion result depends on the state of the component data of another GameObject).

In the following, we will give an example and an explanation for all types of dependencies.

#### Depending on the content of an Asset
When your conversion code reads the contents of an asset, you need to declare a dependency on the asset itself. This dependency means that the GameObject needs to be converted whenever the asset changes its content. As a concrete example, assume that you have conversion code that makes use of the bounding box around a mesh. This bounding box depends on the contents of a mesh asset. The GameObject thus needs to be reconverted whenever this mesh changes.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#DependencyOnAsset)]

Note the lack of a check for `null`: All methods for declaring dependencies correctly handle the case of `null`, and it is imperative that you do not perform this check yourself. Unity overrides the comparison operator for the `UnityEngine.Object` type to also equal `null` when the object has been destroyed. Even though the object might be destroyed, we can still extract identifying data from it. This is crucial for correctly handling dependencies on objects that might be deleted and later restored (e.g. the deletion of an object is undone).

You _do not_ need to declare a dependency if you merely _reference_ an asset. References are stable and can be tracked automatically. For example, if your code is merely storing a reference to a mesh there is no need to declare a dependency:

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#NoDependencyOnAssetReference)]

#### Depending on another GameObject
Dependencies on another GameObject need to be declared when you depend on general properties of the GameObject, e.g. its name, whether it is enabled, or the presence of components on that GameObject.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#DependencyOnName)]

Note here that when you depend on the contents of a component on a GameObject you must declare a dependency on that component instead, see below.

#### Depending on Component data
Conversion code might also depend on the component data on this or another GameObject. This is expected to be the most common kind of dependency. For example, your conversion code might depend on a `MeshFilter` that may or may not be stored on another GameObject.

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#DependencyOnComponent)]

Dependencies on Transform components specifically are mandatory: While GameObjects themselves are the smallest unit of conversion, there is code that relies on this dependency information on a component level. `Transform` components are hierarchical and a change to one transform component actually changes an entire hierarchy. There is a special code path for handling this case specifically, since moving around large hierarchies cannot rely on reconverting the entire hierarchy every frame. Instead, transform data on the converted entities is patched directly and only GameObjects whose conversion result actually depends on the transform data are reconverted (e.g. the conversion result depends on the rotation of the object or the specific position of the object in a scene).

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#DependencyOnTransformComponent)]

Dependencies on transform data should be used sparingly since they run the danger of making editing large scenes slow. This is the only case in which you need to declare a reference on a component on the same GameObject.

When you store a reference to a GameObject instead of a component and use that to acquire a reference to a component, you also need to declare a dependency against the GameObject itself:

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#DependencyOnOtherMeshFilterComponent)]

### Debugging Incremental Conversion Failures
The result of incrementally reconverting a subset of objects in a scene must match the result of a full conversion bit-by-bit. This is a hard requirement. Verifying this requirement is a challenge. To facilitate this, you can use `DOTS/Live Link Mode/Debug Incremental Conversion`. This will run a full conversion after every incremental conversion and compare the results. If there are any differences between the two conversion results, it will print out a summary of the differences.

The most common source for a mismatch between the two conversions are missing dependencies. When you are missing a dependency, a change to a GameObject or asset will not correctly reconvert all GameObjects whose conversion result depend on that GameObject or asset.


## Known Issues
There are known issues around `GetPrimaryEntity`. As of version 0.17 of the entities package, there is no way to express a dependency on the existence of a GameObject and `GetPrimaryEntity` does not register such a dependency. Therefore, the following demonstrates how to properly get a reference to another entity:

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#GetPrimaryEntityFailure)]

If the dependency registered on the last line is not present, you may run into an invalid conversion state: Specifically, deleting the GameObject referred to by `Other` and undoing said deletion will not reconvert your GameObject and lead to an invalid Entity reference.
