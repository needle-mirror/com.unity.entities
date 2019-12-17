---
uid: ecs-writegroups
---

# Write groups

A common ECS pattern is for a system to read one set of *input* components and write to another component as its *output*. However, in some cases, you may want to override the output of a system and update the output component using a different system based on a different set of inputs. Write groups provide a mechanism for one system to override another, even when you cannot change the other system.

The write group of a target component type consists of all other component types to which the [`WriteGroup` attribute](xref:Unity.Entities.WriteGroupAttributes) has been applied with that target component type as the argument. As a system creator, you can use write groups so that your system's users can exclude entities that would otherwise be selected and processed by your system. This filtering mechanism lets system users update components for the excluded entities based on their own logic, while letting your system operate normally on the rest.

To make use of write groups, you must use the [write group filter option](xref:Unity.Entities.EntityQueryOptions) on the queries in your system. This will exclude all entities from the query that have a component from a write group of any of the components that are marked as writable in the query.

To override a system that uses write groups, mark your own component types as part of the write group of the output component type of that system. The original system ignores any entities that have your components and you can update the data of those entities with your own systems. 

## A motivating example
As a concrete example, consider the following setup: You are using an external package to color all characters in your game depending on their state of health. For this, there are two components in the package: `HealthComponent` and `ColorComponent`.

```csharp
public struct HealthComponent : IComponentData
{
   public int Value;
}

public struct ColorComponent : IComponentData
{
   public float4 Value;
}
```
Additionally, there are two systems in the package:
 1. the `ComputeColorFromHealthSystem` (it reads from `HealthComponent` and writes to `ColorComponent`), and
 2. the `RenderWithColorComponent` (it reads from `ColorComponent`).

This setup works great, except when a player uses a power-up and their character becomes invincible, in which case they should have a golden color. You represent invincibility by attaching a `InvincibleTagComponent` to the player's entity. 

You can create your own system to override the `ColorComponent` value, but ideally `ComputeColorFromHealthSystem` would not compute the color for your entity to begin with. It should ignore any entity that has `InvincibleTagComponent`. This becomes especially relevant when there are thousands of players on the screen. Unfortunately, the system is from another package and does not know about your `InvincibleTagComponent`. You still want to use this package since it _is_ solving your problem so far.

This is the problem that _write groups_ are solving: It allows a system to ignore entities in a query when we know that the values it computes would be overridden anyway. There are two things needed to support this:
 1. The `InvincibleTagComponent` must marked as part of the write group of `ColorComponent`:
    ```csharp
    [WriteGroup(typeof(ColorComponent))]
    struct InvincibleTagComponent : IComponentData {}
    ```
    The write group of `ColorComponent` consists of all component types that have the `WriteGroup` attribute with `typeof(ColorComponent)` as the argument.
 2. The `ComputeColorFromHealthSystem` must explicitly support write groups. To achieve this, the system needs to specify the `EntityQueryOptions.FilterWriteGroup` option for all its queries.

The `ComputeColorFromHealthSystem` could be implemented like this:
```csharp
...
protected override JobHandle OnUpdate(JobHandle inputDependencies) {
   return Entities
      .WithName("ComputeColor")
      .WithEntityQueryOptions(EntityQueryOptions.FilterWriteGroup) // support write groups
      .ForEach((ref ColorComponent color, in HealthComponent health) => {
         // compute color here
      }).Schedule(inputDependencies);
}
...
```
When this is executing, the following happens:
 1. the system detects that you write to `ColorComponent` (since it is a by-reference parameter),
 2. it looks up the write group of `ColorComponent` and finds the `InvincibleTagComponent` in it,
 3. it will exclude all entities that have an `InvincibleTagComponent`.

The benefit is that this allows the system to exclude entities based on a type that is unknown to the system and might live in a different package.

**Note:** for more examples you can look at the `Unity.Transforms` code, which uses write groups for every component it updates, including `LocalToWorld`.

## Creating write groups
You can create write groups by adding the `WriteGroup` attribute to the declarations of each component type in the write group. The `WriteGroup` attribute takes one parameter, which is the type of component that the components in the group are used to update. A single component can be a member of more than one write group.

For example, if you have a system that writes to component `W` whenever there are components `A` or `B` on an entity, then you would define a write group for `W` as follows:

```csharp
public struct W : IComponentData
{
   public int Value;
}

[WriteGroup(typeof(W))]
public struct A : IComponentData
{
   public int Value;
}

[WriteGroup(typeof(W))]
public struct B : IComponentData
{
   public int Value;
}
```

Note that you do not add the target of the write group (component `W` in the example above) to its own write group.

## Enabling write group filtering

To enable write group filtering, set the `FilterWriteGroups` flag on your job:

```csharp
public class AddingSystem : JobComponentSystem
{
   protected override JobHandle OnUpdate(JobHandle inputDeps) {
      return Entities
         .WithEntityQueryOptions(EntityQueryOptions.FilterWriteGroup) // support write groups
         .ForEach((ref W w, in B b) => {
            // perform computation here
         }).Schedule(inputDeps);}
}
```

For query description objects, set the flag when you create the query:

```csharp
public class AddingSystem : JobComponentSystem
{
   private EntityQuery m_Query;

   protected override void OnCreate()
   {
       var queryDescription = new EntityQueryDesc
       {
           All = new ComponentType[] {
              ComponentType.ReadWrite<W>(),
              ComponentType.ReadOnly<B>()
           },
           Options = EntityQueryOptions.FilterWriteGroup
       };
       m_Query = GetEntityQuery(queryDescription);
   }
   // Define Job and schedule...
}
```

When you turn on write group filtering in a query, the query adds all components in a write group of a writable component to the *None* list of the query unless you explicitly add them to the *All* or *Any* lists. As a result, the query only selects an entity if every component on that entity from a particular write group is explicitly required by the query. If an entity has one or more additional components from that write group, the query rejects it.

In the example code above, the query will:
 * Exclude any entity that has component `A`, since `W` is writable and `A` is part of the write group of `W`
 * Not exclude any entity that has component `B`. Even though `B` is part of the write group of `W`, it is also explicitly specified in the *All* list.

## Overriding another system that uses write groups
If a system uses write group filtering in its queries, you can override that system and write to those components using your own system. To override the system, add your own components to the write groups of the components to which the other system writes. Since write group filtering excludes any components in the write group that aren’t explicitly required by a query, any entities that have your components will then be ignored by the other system.

For example, if you wanted to set the orientation of your entities by specifying the angle and axis of rotation, you could create a component and a system to convert the angle and axis values into a quaternion and write that to the `Unity.Transforms.Rotation` component. To prevent the `Unity.Transforms` systems from updating `Rotation`, no matter what other components besides yours are present, you can put your component in the write group of `Rotation`:

```csharp
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

[Serializable]
[WriteGroup(typeof(Rotation))]
public struct RotationAngleAxis : IComponentData
{
   public float Angle;
   public float3 Axis;
}
```

You can then update any entities with the `RotationAngleAxis` component without contention:
```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;

public class RotationAngleAxisSystem : JobComponentSystem
{
   protected override JobHandle OnUpdate(JobHandle inputDependencies)
   {
      return Entities.ForEach((ref Rotation destination, in RotationAngleAxis source) =>
      {
         destination.Value = quaternion.AxisAngle(math.normalize(source.Axis), source.Angle);
      }).Schedule(inputDependencies);
   }
}
```

## Extending another system that uses write groups

If you want to extend the other system rather than just override it, and further, you want to allow future systems to override or extend your system, then you can enable write group filtering on your own system. When you do this, however, no combinations of components will be handled by either system by default. You must explicitly query for and process each combination.

As an example, let’s return to the example described earlier, which defined a write group containing components `A` and `B` that targeted component `W`. If you simply add a new component, call it `C`, to the write group, then the new system that knows about `C` can query for entities containing `C` and it does not matter if those entities also have components `A` or `B`. However, if the new system also enables write group filtering, that is no longer true. If you only require component `C`, then write group filtering excludes any entities with either `A` or `B`. Instead, you must explicitly query for each combination of components that make sense. (You can use the “Any” clause of the query when appropriate.)

```csharp
var query = new EntityQueryDesc
{
   All = new ComponentType[] {ComponentType.ReadOnly<C>(), ComponentType.ReadWrite<W>()},
   Any = new ComponentType[] {ComponentType.ReadOnly<A>(), ComponentType.ReadOnly<B>()},
   Options = EntityQueryOptions.FilterWriteGroup
};
```

Any entities containing combinations of components in the write group that are not explicitly mentioned will not be handled by any system that writes to the target of the write group (and filters on write groups). But then, it is most likely a logical error in the program to create such entities in the first place.
