# Convert data with Baking 

Baking provides a system for transforming GameObject data in the Editor (authoring data) to Entities written to Entity Scenes (runtime data). 

Baking is broken down into multiple phases, but at its core are two key steps: Bakers and Baking Systems.

Incremental Baking also happens when you have a Sub Scene open and you're editing the authoring objects in it. ECS detects the changes you're making and identifies the minimal amount of Bakers that need to re-run as a result of this change. The result of this is patched into the Entity World in the Editor during Edit mode and Play mode.

## Baker class

Use the [Baker](xref:Unity.Entities.Baker`1) class to directly interface with Unity objects, such as authoring components. Bakers are also where dependencies are implicitly or explicitly captured, and all components added are capable of automatically being reverted if a Baker re-runs. A Baker can only add components to the primary entity that it's baking and the additional entities created by itself. For example:

```c#
public class MyMonoBehaviour : MonoBehaviour
{
   public float value;
}

public class MyBaker : Baker<MyMonoBehaviour>
{
   public override void Bake(MyMonoBehaviour authoring)
   {
       AddComponent(new MyComponent {Value = authoring.value} );
   }
}
```

### Accessing other data sources in a Baker

To keep incremental baking working, you need to track what data is used to convert the GameObject in a Baker. Any field in the authoring component is automatically tracked and the Baker re-runs if any of that data changes.

Information from other authoring components isn't tracked automatically and you need to add a dependency to it for it to be tracked. To do this, use the functions that the Baker provides to access other components, instead of the ones provided by GameObject:

```c#
public struct MyComponent : IComponentData
{
   public float Value;
}

public class MyMonoBehaviour : MonoBehaviour
{
   public GameObject otherGO;
}

public class MyBaker : Baker<MyMonoBehaviour>
{
   public override void Bake(MyMonoBehaviour authoring)
   {
       var transform = GetComponent<Transform>(authoring.otherGO);
       AddComponent(new MyComponent {Value = transform.position.x} );
   }
}
```

In the same way, if you access data from an asset, you need to create a dependency for it, so the Baker reruns if the asset changes. 

```c#
public struct MyComponent : IComponentData
{
   public int Value;
}

public class MyMonoBehaviour : MonoBehaviour
{
   public Mesh mesh;
}

public class MyBaker : Baker<MyMonoBehaviour>
{
   public override void Bake(MyMonoBehaviour authoring)
   {
       // We want to rebake if anything changes in the mesh itself
       DependsOn(authoring.mesh);
       AddComponent(new MyComponent { Value = authoring.mesh.vertexCount } );
   }
}
```

## Prefabs in Bakers
To declare and convert Prefabs, call `GetEntity` in the baker:

```c#
public struct MyComponent : IComponentData
{
   public Entity Prefab;
}

public class MyMonoBehaviour : MonoBehaviour
{
   public GameObject prefab;
}

public class MyBaker : Baker<MyMonoBehaviour>
{
   public override void Bake(MyMonoBehaviour authoring)
   {
       AddComponent(new MyComponent { Prefab = GetEntity(authoring.prefab) } );
   }
}
```

`GetEntity` returns the entity that's used to create the entity Prefab, but it hasn’t been converted at this point. This happens later on a separate pass.

## Baking systems

Baking systems are regular [systems](concepts-systems.md) that process the output that a Baker produces, for example by combining results. This means that Baking systems should only work with entity data and not the managed authoring types, such as GameObjects and Components. This also means that Baking systems can use Burst and Jobs to process data.

To create a Baking system, mark it with the `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]` attribute. This allows baking to discover them and add them to the baking world. Baking systems are updated on every single Baking pass.

```c#
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
partial class BakingOnlyEntityAuthoringBakingSystem : SystemBase
{
   protected override void OnUpdate()
   {
      // … Your code here …
   }
}
```

While Bakers are usually necessary, baking systems are optional and only required for advanced use cases.
