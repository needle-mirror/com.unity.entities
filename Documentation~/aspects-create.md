# Create an aspect

> [!WARNING]  
> Aspects are deprecated and will be removed in a future release. Use component access and query methods directly instead.

To create an aspect, use the [`IAspect`](xref:Unity.Entities.IAspect) interface. You must declare an aspect as a readonly partial struct, and the struct must implement the `IAspect` interface:

```c#
using Unity.Entities;

readonly partial struct MyAspect : IAspect
{
    // Your Aspect code
}
```

## Fields

You can use `RefRW<T>` or `RefRO<T>` to declare a component as part of an aspect. `RefRW<T>` provides read-write access to the component, and `RefRO<T>` provides read-only access.

To declare a buffer, use `DynamicBuffer<T>`. For more information on the fields available, refer to the [`IAspect`](xref:Unity.Entities.IAspect) documentation.

Fields declared inside an aspect define what data must be queried in order for an aspect instance to be valid on a specific entity.

To make a field optional, use the `[Optional]` attribute. To declare `DynamicBuffer` and nested aspects as read-only, use the `[ReadOnly]` attribute. 

## Create aspect instances in a system

To create aspect instances in a system, call [`SystemAPI.GetAspect`](xref:Unity.Entities.SystemAPI.GetAspect*):

```c#
// Throws if the entity is missing any of 
// the required components of MyAspect.
MyAspect asp = SystemAPI.GetAspect<MyAspect>(myEntity);
```

To create aspect instances outside of a system, use [`EntityManager.GetAspect`](xref:Unity.Entities.EntityManager.GetAspect*).

### Iterate over an aspect

If you want to iterate over an aspect, you can use [`SystemAPI.Query`](systems-systemapi-query.md):

[!code-cs[aspects](../DocCodeSamples.Tests/AspectExamples.cs#aspect-iterate)]

## Example

In this example, the `CannonBallAspect` sets the transform, position, and speed of the cannon ball Component in a tank themed game. 

[!code-cs[aspects](../DocCodeSamples.Tests/AspectExamples.cs#aspect-example)]


To use this aspect in other code, you can request `CannonBallAspect` in the same way as a component:

```c#

using Unity.Entities;
using Unity.Burst;

// It's best practice to Burst-compile your code
[BurstCompile]
partial struct CannonBallJob : IJobEntity
{
    void Execute(CannonBallAspect cannonBall)
    {
        // Your game logic
    }
}

```
