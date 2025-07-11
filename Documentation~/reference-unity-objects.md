---
uid: reference-unity-objects
---

# Reference Unity objects in your code

To store references to `UnityEngine.Object` types in your code, you can use the [`UnityObjectRef`](xref:Unity.Entities.UnityObjectRef-1) struct inside an [unmanaged `IComponentData` component](components-unmanaged.md). You can then use this reference to access the original object and use it in [systems](concepts-systems.md).

For example, you have a `GameObject` with an `Animator` object which you want to instantiate and access in a system. You can achieve this by creating an `IComponentData` struct with a `UnityObjectRef` field that holds a reference to the `GameObject`. You can then instantiate this prefab from a system, and use the target `Animator` component. 

## Using UnityObjectRef

The following example shows how to define an `IComponentData` with a `UnityObjectRef` field and how to use a [baker](baking-baker-overview.md) to store a reference to the `GameObject` prefab during conversion:

[!code-cs[UnityObjectRef example](../DocCodeSamples.Tests/UnityObjectRefExamples.cs#unityobjectref-example)]

You can use [`SystemAPI`](systems-systemapi.md) to access and instantiate the prefab:

[!code-cs[UnityObjectRef Spawn System example](../DocCodeSamples.Tests/UnityObjectRefExamples.cs#unityobjectref-spawn-system-example)]

You can also access and modify the `Animator` from a separate system. The following example shows how to adjust the animation speed dynamically:
[!code-cs[UnityObjectRef Animator example](../DocCodeSamples.Tests/UnityObjectRefExamples.cs#unityobjectref-anim-system-example)]


## Referencing the same asset in MonoBehaviour and IComponentData code

During a Player build, Unity collects all `UntypedWeakReferenceId` values from each included subscene. This also includes any `WeakObjectReference<T>` and `WeakObjectSceneReference` properties. Any Unity objects directly referenced from entity data (including `UnityObjectRef<T>`) are also collected and added to a special `ScriptableObject` that has an `UntypedWeakReferenceId` created for it. 

Unity builds these references into a set of `ContentArchive` instances that it optimizes for usage and duplication as follows: 

* Any objects that are used together are put into the same archive to maximize loading efficiency. 
* Any shared objects are put into separate archives to prevent duplication.
* Any objects that have direct references from normal scenes are built directly into Player data, which is separate from the archive data. 

If an object is referenced by both normal and entity scenes it is duplicated in both sets of data and each has its own InstanceID at runtime. A normal scene can contain a `WeakObjectReference<T>` and you can use this reference to load from the archive data at runtime as long as the reference is also included in an entity scene. This setup only includes one copy of the asset in the build.

## Additional resources

* [`UnityObjectRef` API reference](xref:Unity.Entities.UnityObjectRef-1)
* [Unmanaged components](components-unmanaged.md)
* [Convert data with baking](baking.md)
