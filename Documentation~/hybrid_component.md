---
uid: hybrid-components
---
# DOTS Hybrid Components

## Prelude

Please note that Hybrid Components are not compatible with [Project Tiny].

## Purpose

Many existing features of Unity don’t have a DOTS equivalent (yet). So in many cases, DOTS projects will be hybrid: a mix of classic GameObjects and ECS.

Hybrid Components provide a way to conveniently access UnityEngine components from ECS code. They were initially designed to deal with rendering components like lights, reflection probes, post-process volumes, etc.

The major limitations associated with Hybrid Components include:
- Only for data-like components, most [event functions] won’t be called.
- No performance benefits over GameObjects, including no jobs, no Burst, no improvement in memory usage.
- Not a general purpose feature, the use of hybrid components is explicit (opt-in).
- As of entities 0.16, hybrid components aren’t fully supported by LiveLink.
- Hybrid Components can only be created at conversion time.

## Component Objects

Hybrid Components are implemented on top of [Component] objects, so let’s first discuss what Component objects are.
Component objects are regular UnityEngine components added to an entity by using [AddComponentObject] and accessed via [GetComponentObject], allowing those types of components to be used in queries and a few other places.

Internally, references to such components are not stored in chunks but in managed arrays. The chunks only contain indices to those arrays instead of storing the components themselves.

This means that every access to a Component object requires an extra indirection and also that the entities don’t "own" those components. You must use caution when dealing with Component objects because of the chance that many problematic situations can arise because of this lack of clearly defined ownership. For example, it is possible to share the same component between multiple entities or to destroy components without removing them.

## Companion GameObjects

Hybrid components were designed to solve the ownership problem of [Component] objects.

Since regular UnityEngine components cannot exist on their own, hybrid components use hidden GameObjects (via `HideFlags`) that we call "Companion GameObjects" in this context.

ECS makes the management of these companion GameObjects transparent in a way that your code should never have to worry about them.

> [!NOTE]
> The design is based on the constraint that the entity is in charge of the companion GameObject. The GameObject shouldn’t modify the entity or its other components.

## Conversion

Conversion systems can declare some UnityEngine component instances from the authoring GameObjects as hybrid components by using [AddHybridComponent].

[!code-cs[conversion](../DocCodeSamples.Tests/ConversionExamples.cs#HybridComponent_ConversionSystem)]

At the end of conversion, each authoring GameObject that has at least one hybrid component will be cloned, all the other components will be removed from the clone, and that clone will be stored alongside the entity and become its companion GameObject.

## CompanionLink

The link between an entity and its companion GameObject is aptly named `CompanionLink`. A `CompanionLink` is a managed component (`IComponentData` class) that also implements `IDisposable` and `ICloneable`. This component manages the lifecycle of the companion GameObject and contains a reference to the GameObject.

This allows entity prefab instantiation and entity destruction to just work transparently.

> [!NOTE]
> This is a one way link from an entity to a companion GameObject. There is no link back from the companion GameObject to the entity.

## Copy to GameObject.Transform

If it exists, the transform matrix in the `LocalToWorld` component of the entity is copied to its companion GameObject’s `Transform`. Those GameObjects are always in world space, the transform hierarchy only exists on the ECS side.

> [!NOTE]
> This copy only happens in one direction. Direct modification of the companion’s transform is an error, it will be overwritten by the synchronization system eventually. Since this synchronization system is reactive, there is no guarantee when this will happen. It is only guaranteed to happen when the `LocalToWorld` component of the entity is modified.

[Project Tiny]: https://docs.unity3d.com/Packages/com.unity.tiny.all@latest
[event functions]: https://docs.unity3d.com/Manual/EventFunctions.html
[AddComponentObject]: xref:Unity.Entities.EntityManager.AddComponentObject*
[GetComponentObject]: xref:Unity.Entities.EntityManager.GetComponentObject*
[Component]: xref:UnityEngine.Component
[AddHybridComponent]: xref:Global%20Namespace.GameObjectConversionSystem.AddHybridComponent*

