---
uid: ecs-system-state-component-data
---
# System State Components

System state components are like regular components, but an entity with one or more system state components are not destroyed in the normal way:

1. `DestroyEntity` will not actually destroy an entity with system state components but instead will specially mark the entity and remove all of its non-system state components.
2. Removing all of a specially marked entity's components will destroy the entity for real.

```csharp
// Defines a system state component type.
public struct FooSystemState : ISystemStateComponentData
{
    public int Value;
}

public struct BarSystemState : ISystemStateComponentData
{
    public float Value;
}
```

> [!NOTE]
> An `ISystemStateComponentData` struct is unmanaged and has all of the same restrictions as an `IComponentData` struct.

```csharp
Entity e = EntityManger.CreateEntity(
    typeof(Translation), typeof(Rotation), typeof(FooSystemState), typeof(BarSystemState));

// Because the entity has system state components, it is not actually destroyed.
// Instead, the Translation and Rotation components are removed. 
EntityManager.DestroyEntity(e);

// We can use the entity like normal, and even add more components.
EntityManager.AddComponent<Translation>(e);

// However, removing all of the components destroys the entity.
EntityManager.DestroyEntity(e, new ComponentTypes(typeof(FooSystemState), typeof(BarSystemState), typeof(Translation)));

// Now the entity has actually been destroyed, so this returns false.
bool b = EntityManager.Exists(e);
```

## Using system state components to perform cleanup

The primary use case for a system state component is to tag entities for cleanup upon destruction.

Let's say we have a system state component called *Cleanup* that contains all information needed to perform cleanup after the destruction of certain entity archetypes. The general pattern is:

1. The entities that require cleanup get created in one or more parts of code.
2. Every frame, we look for any of these entities which have been newly created with a query that excludes *Cleanup*. For all entities matching the query, we add the *Cleanup* component.
3. Every frame, we also look for any of these entities which have been provisionally destroyed with a query that includes the *Cleanup* component but *excludes* the non-system state components. For all entities matching the query, we perform the appropriate cleanup work and remove the *Cleanup* component, thereby actually destroying the entity.

> [!NOTE]
> Usual practice is to give each entity with a system state component a uniquely paired tag component. We then find the entities which have been provisionally destroyed with a query that includes the system state component but excludes the tag.

In some cases, empty system state components are sufficient. In other cases, cleanup may require information to be stored in the system state components.

It's typical but not required that a system state component's data is not modified after creation.

## System state shared components

An `ISystemStateSharedComponentData` is a managed component that is stored like a `SharedComponentData`, but it has the destruction semantics of a system state component.
```