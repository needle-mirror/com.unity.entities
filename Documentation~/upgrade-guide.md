# Upgrading from Entities 0.17 to Entities 0.51

To upgrade to Entities 0.51 from 0.17 you'll need to do the following:

**Entities**

* [Rename FixedList128 to FixedList128Bytes](#fixedlist)
* [Add partial keyword to all SystemBase, ISystem, and ISystemBase types](#partial)
* [Upgrade JobComponentSystem to SystemBase](#systembase)
* [Rename ISystemBase to ISystem](#isystem)
* [Replace IJobForEach with IJobEntity](#ijobentity)
* [Remove DOTS Editor package](#editor)
* [Remove Audio package](#audio)
* [Remove Animations package](#animations)
* [Use string literals with Entities.WithName](#efewithname)
* [Remove nested Entities.ForEach](#nestedefe)
* [Add a reference to the Jobs package for Unity.Entities.RegisterGenericJobTypeAttribute](#genericjob)
* [Ensure Entities.WithReadOnly has valid parameters](#withreadonly)
* [Remove platform-specific Platforms packages and add com.unity.platforms](#platforms)

**Netcode for Entities**

* [Remove GhostCollectionAuthoringComponent](#ghostcollection)
* [Move UpdateInWorld.TargetWorld to a top-level enum](#updateinworld)
* [Source-generators replace code-generation](#sourcecode)

**Hybrid Renderer**

* [Remove Hybrid Renderer V1](#hybrid)

**Physics**

* [Update TriggerEventSystem from JobComponentSystem to SystemBase](#physics)

## Entities 
<a name="fixedlist"></a>

### Rename FixedList128 to FixedList128Bytes

`FixedList128` has been renamed to `FixedList128Bytes`. All other sizes and  `FixedStrings` have been similarly renamed. This change more accurately reflects that the struct takes up 128 bytes, rather than having 128 elements. 

When the Script Updater runs, it automatically renames any usage of renamed APIs for you. If you've hard-coded any of these names in your code, you'll need to update them manually.

<a name="partial"></a>

### Add partial keyword to all SystemBase, ISystem, and ISystemBase types

Entities' internal code generation mechanism has changed from IL post-processing to Source Generators. This means you can see the underlying code that the ECS framework generates, and debug it accordingly. You can see this code in your project's `/Temp/GeneratedCode` folder. Because of this, the generated code must be valid C# and the `partial` keyword is needed to augment system types.

Unity generates an error for all types of this kind that don't have the appropriate keyword. You can manually add the keyword, or you can use the Editor feature below to automatically add them to all your system types (make sure to save your work before enabling this).

To auto update your project’s systems with this keyword:
1. Save your project.
1. Go to **DOTS &gt; DOTS Compiler &gt; Add missing partial keyword to systems**
1. Let Unity recompile source files.
1. Disable the **Add missing partial keyword to systems** setting

>[!IMPORTANT]
>Remember to disable this setting from the menu, or else Unity  continues to overwrite your files with added partial keywords on compilation .

<a name="systembase"></a>
## Upgrade JobComponentSystem to SystemBase 

The `JobComponentSystem `type has been fully removed and is replaced by `SystemBase`. `SystemBase` is tested internally with both unit and performance tests, gives more options for scheduling, and does implicit job ordering by default. More information can be found below and in the [SystemBase](xref:Unity.Entities.SystemBase) section of the manual. SystemBase has been previously released and doesn't have new semantics. 

#### Implicit job scheduling: convert JobComponentSystem types to use the Dependency field

You don't have to explicitly use dependencies when scheduling jobs inside of a system. ECS holds the current dependency that the system tracks in a Dependency field and uses it implicitly when scheduling `Entities.ForEach` or `Job.WithCode`. This means that `OnUpdate` now neither takes a `JobHandle` nor returns a `JobHandle`. Instead, ECS sets the Dependency to the input dependency before `OnUpdate` begins and uses it for the next System when the `OnUpdate` ends.  

All `Entities.ForEach` and `Job.WithCode` scheduling methods have versions that both take and return, or don't take or return a `JobHandle`. This means you must convert existing `JobComponentSystem` types to use the Dependency field, rather than using the input dependency parameter and returning one.

#### Sequential and parallel scheduling: Schedule Entities.ForEach as intended

`Entities.ForEach` now has both a `Schedule` and `ScheduleParallel` method that differentiates between sequential and parallel scheduling. All new job types also follow this pattern. This means you must make sure that you've scheduled all `Entities.ForEach` jobs with the intended method.

The previous `JobComponentSystem` type was unable to schedule work to happen sequentially and not in parallel. It can be useful to be able to schedule work to happen off the main thread, but still in sequential order. This makes it easier to understand how the work happens, and gives the safety system more freedom when scheduling.

To upgrade to `SystemBase` from `JobComponentSystem`: 

1. Change the inherited type from `JobComponentSystem` to `SystemBase`. Make sure you've marked the type with the `partial` keyword. For more information on how to do this, see the upgrade entry about [adding partial keywords to SystemBase types](#partial).
1. Change the method signature of `OnUpdate` to no longer take or return a `JobHandle`. It should now use the `void` return type and no longer return a `JobHandle`.
1. Change any place that the input dependency was used or returned, to use the Dependency field on `SystemBase`. This might mean you need to change some logic of your system’s `OnUpdate` method.
1. Change any uses of `Entities.ForEach(...).Schedule` to `Entities.ForEach(...).ScheduleParallel` if you would like to keep parallel execution.

Before:

```c#
public partial class RotationSpeedSystem_ForEach : JobComponentSystem
{
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        return Entities
            .ForEach((ref Rotation rotation, in RotationSpeed rotationSpeed) => 
            { 
            })
            .Schedule(inputDeps);
    }
}
```

After:

```c#
public partial class RotationSpeedSystem_ForEach : SystemBase
{
    protected override void OnUpdate()
    {
        Entities
            .ForEach((ref Rotation rotation, in RotationSpeed rotationSpeed) => 
            { 
            })
            .ScheduleParallel();
    }
}
```

<a name="isystem"></a>

### Rename ISystemBase to ISystem

`ISystemBase` is obsolete, and replaced by the `ISystem` interface. You should change all usages of `ISystemBase` to `ISystem`. You can optionally use the script updater to change the name for you once you reload Unity, and accept the prompt that appears stating for you to backup.

You can continue to use ISystemBase in old code, but it will be removed and unsupported in a future version of Entities.

To upgrade ISystemBase to ISystem either:

* Refresh Unity, and let the script updater run, which updates the naming for you. A back-up prompt appears beforehand, so you can decide whether you want to do it manually.
* Or, change the code from:
    ```c#
    partial struct ExampleSystem : ISystemBase
    {
      public void OnCreate(ref SystemState state){}
      public void OnDestroy(ref SystemState state){}
      public void OnUpdate(ref SystemState state){}
    }
    ```
    to:
    ```c#
    partial struct ExampleSystem : ISystem
    {
      public void OnCreate(ref SystemState state){}
      public void OnDestroy(ref SystemState state){}
      public void OnUpdate(ref SystemState state){}
    }
    ```

<a name="ijobentity"></a>

### Replace IJobForEach with IJobEntity

`IJobForEach` has been removed. You should use `IJobEntity` instead, which works more like a job, and more like `Entities.ForEach`. It's easier to support because it generates the underlying `IJobEntityBatch`. To summarize the changes:

* You can no longer use `IJobForEach`.
* Use `IJobEntity`, with job attributes, implicit scheduling, and query generation instead.
* If you’re still using `ComponentSystem`, the removal of `IJobForEach` means you have to use `IJobEntityBatch` manually.

`IJobEntity` has code reusability: for example, if you want to copy an array of Entity positions, you can make a function to copy and schedule it with different `NativeArray`structs. 

`IJobEntity` has an `Execute` function, similar to a job. Also, each parameter describes a Component, similar to `Entities.ForEach`, and you can use the `in` and `ref` keywords to describe its access pattern. This means that you can use the `in` keyword to directly get a read-only reference, rather than writing `[ReadOnly] ref`.

You can also use all job attributes. Like `Entities.ForEach` in `SystemBase`, this feature does implicit job scheduling. The limit is that `IJobEntity` currently only works in `SystemBase`. It has an overload for both queries, and a job handle. In cases where you don’t specify a query, it makes one implicitly that matches the signature of the execute function. 
 
To upgrade your IJobForEach implementation do the following:

1. Remove `IJobForEach` and replace it with `IJobEntity`.
1. Add the `partial` keyword to the struct.
1. Change `[ReadOnly] ref` to `in`.
1. Remove any scheduling code in systems.
1. Make sure that any scheduling happens from a variable and not from the instantiation directly. For example, don't do this:
    ```c#
    new EntityRotationJob 
    {
      dt = Time.deltaTime 
    }.Schedule();
    ```
    Do this instead:
    ```c#
    var job = new EntityRotationJob 
    { 
      dt = Time.deltaTime 
    };
    job.Schedule();
    ```
1. To upgrade an `IJobForEachWithEntity`, use a parameter of type `Entity`, similar to what you would do with `Entities.ForEach`. If you also want the Index, use the `[EntityInQueryIndex]` attribute on an `int` parameter. For example, you would change the following code:
    ```c#
    struct CopyPositionsJob : 
    IJobForEachWithEntity<Translation>
    {
      public NativeArray<float3> Positions;
     
      public void Execute(
        Entity entity, int index, 
        [ReadOnly] ref LocalToWorld localToWorld)
      {
        Positions[index] = localToWorld.Position;
      }
    }
    ```
    To this:
    ```c#
    partial struct CopyPositionsJob : 
    IJobEntity
    {
      public NativeArray<float3> Positions;
      
      public void Execute(
        [EntityInQueryIndex] int index, 
        in LocalToWorld localToWorld)
      {
        Positions[index] = localToWorld.Position;
      }
    }
    ```
A full example of the `IJobForEach` conversion is below:

Old code:
```c#
[BurstCompile]
struct EntityRotationJob : 
IJobForEach<Rotation, RotationSpeed>
{
  public float dt;
 
  public void Execute(
      ref Rotation rotation, 
      [ReadOnly] ref RotationSpeed speed)
  {
      rotation.value = math.mul(
          math.normalize(rotation.value), 
          quaternion.axisAngle(
              math.up(), 
              speed.speed * dt));
  }
}
 
struct RotationSystem : JobComponentSystem 
{
  protected override JobHandle OnUpdate(
    JobHandle inputDeps)
  {
      var job = new EntityRotationJob 
      {
          dt = Time.deltaTime 
      };
      return job.Schedule(this, inputDeps);
  } 
}

      
```

New code:
```c#
[BurstCompile]
partial struct EntityRotationJob : 
IJobEntity
{
  public float dt;
 
  public void Execute(
      ref Rotation rotation, 
      in RotationSpeed speed)
  {
      rotation.value = math.mul(
          math.normalize(rotation.value), 
          quaternion.axisAngle(
              math.up(), 
              speed.speed * dt));
  }
}
 
struct RotationSystem : SystemBase 
{
  protected override void OnUpdate()
  
  {
      var job = new EntityRotationJob 
      { 
          dt = Time.deltaTime 
      };
      job.Schedule(); 
      // Note: Schedule has to be called 
      // from variable for sourcegen to work
  }
}        
```

<a name="editor"></a>

### Remove DOTS Editor package

Remove any references to the DOTS Editor package (com.unity.dots.editor). The package has been merged into the Entities package, and any previous version will conflict and cause compilation errors.

<a name="audio"></a>

### Remove Audio package

The Audio package (com.unity.audio) isn't supported. You should remove the package and any related scripts from your project.

<a name="animations"></a>

### Remove Animations package

The Animations package (com.unity.animations) isn't supported. You should remove the package and any related scripts from your project.

<a name="#efewithname"></a>

### Use string literals with Entities.WithName

In previous versions, you could use string constants with `Entities.WithName`, but the requirement is now more strict and only string literals will work.

What used to work before:
```c#
const string name = "SomeName";
Entities
    .WithName(name)
    .ForEach((ref LocalToWorld ltw) =>
{
    // ...
}).Schedule();
```

The above code now triggers the following error:
>error DC0008: The argument to WithName needs to be a literal value.

To upgrade, do the following instead:
```c#
Entities
    .WithName("SomeName")
    .ForEach((ref LocalToWorld ltw) =>
{
    // ...
}).Schedule();
```

<a name="nestedefe"></a>

## Remove nested Entities.ForEach

Having an `Entities.ForEach` within another `Entities.ForEach` was never intended nor supported. In previous versions, the following syntax would compile and eventually work:

```c#
Entities.WithoutBurst().ForEach((MyComponent a) =>
{
    Entities.ForEach((MyComponent b) =>
    {

    }).Run();
}).Run();
```

This is now properly rejected by the compiler with the following error:

>error DC0029: Entities.ForEach Lambda expression has a nested Entities.ForEach Lambda expression. Only a single Entities.ForEach Lambda expression is currently supported.

<a name="genericjob"></a>

## Add a reference to the Jobs package if using Unity.Entities.RegisterGenericJobTypeAttribute

Assemblies that require `Unity.Entities.RegisterGenericJobTypeAttribute` need a reference to the Unity.Jobs assembly.

<a name="withreadonly"></a>

## Ensure Entities.WithReadOnly has valid parameters

In previous versions of the Entities package, some invalid uses of `Entities.WithReadOnly` would not cause an error.

```c#
NativeArray<float> someArray = new NativeArray<float>(123, Allocator.Temp);
float someValue = 1;
Entities
    .WithReadOnly(someArray) // error : someArray is not used in the lambda
    .WithReadOnly(someValue) // error : someValue is not a native container
    .ForEach((MyComponent a) =>
{
}).Run();
```

In the current version, the example above doesn't compile and throws the following error instead:

> error DC0012: Entities.WithReadOnly is called with an invalid argument XXX. You can only use Entities.WithReadOnly on local variables that are captured inside the lambda. Please assign the field to a local variable and use that instead.

<a name="platforms"></a>

### Remove platform-specific Platforms packages and add com.unity.platforms

The platform-specific com.unity.platforms packages have been removed. You should remove the following platform-specific packages from your project: 

* com.unity.platforms.android
* com.unity.platforms.desktop
* com.unity.platforms.ios
* com.unity.platforms.linux
* com.unity.platforms.macos
* com.unity.platforms.web
* com.unity.platforms.windows

To build your project, you should use the com.unity.platforms package, which uses Build Configuration assets to build your project. For further information, see the documentation on [Building an Entities project](ecs_building_projects.md).

## Netcode for Entities

<a name="ghostcollection"></a>

### Remove GhostCollectionAuthoringComponent

Ghost collections no longer require any special setup. They only require that the Entity Prefab is loaded on both the client and server.

If you currently have a `GhostCollectionAuthoringComponent` with two Prefabs, a player and an item the custom component for that would look like this:

```c#
[GenerateAuthoringComponent]
public struct CustomSpawner : IComponentData
{
   public Entity Player;
   public Entity Item;
}
```

To upgrade this code, replace the `GhostCollectionAuthoringComponent` with `CustomSpawner` in the scene data and make sure the player and item references point to the correct Prefabs.

The spawner component doesn't need to reference pre-spawned ghosts which are never instantiated at runtime.

The code to spawn a ghost changes to:
```c#
EntityManager.Instantiate(GetSingleton<CustomSpawner>().Player);
```

<a name="updateinworld"></a>

### Move UpdateInWorld.TargetWorld to a top-level enum

The target world enum for the `UpdateInWorld` attribute has been moved to a top-level enum. Replace all references to `UpdateInWorld.TargetWorld` to `TargetWorld`. The enum value stays the same, so for example you would replace `UpdateInWorld.TargetWorld.Client` with `TargetWorld.Client`.

<a name="updateinworld"></a>

### Change LagCompensation from opt-in to opt-out

The component `DisableLagCompensation` no longer exists: if you were previously using it you can remove it.

If you are using lag compensation you must add a new singleton entity with a `LagCompensationConfig` component to opt-in to lag compensation through `PhysicsWorldHistory`.

<a name="sourcecode"></a>

### Source-generators replace code-generation 

The code generation for Netcode for Entities has been rewritten to use roslyn based source generators. You must delete the `Assets/NetCodeGenerated` folder when upgrading to this version. If you don't remove it, you might encounter compilation errors. 

If your project uses Modifiers or templates to customize the code-generation you must take some extra steps:

#### Custom templates
Create a new folder inside your project and add an assembly reference to NetCode. For example:
```
+ CodeGenCustomization/
    + NetCodeRef/
   	    NetCode.asmref
    + Templates/
   	    Templates.asmdef (has NETCODE_CODEGEN_TEMPLATES define constraints)
```

These folders contain your project's templates and subtypes definition. The steps are outlined below, and you can find further information in the [Netcode documentation](https://docs.unity3d.com/Packages/com.unity.netcode@latest).

#### Re-implementing template registration
Create a new file and add a partial class for the `UserDefinedTemplates` inside the folder with the `netcode.asmref` (`NetCodeRef `in the example).

Then, implement the `static partial void RegisterTemplates(...)` method. You will register your templates in this method.

```c#
using System.Collections.Generic;
namespace Unity.NetCode.Generators
{
   public static partial class UserDefinedTemplates
   {
       static partial void RegisterTemplates(List<TypeRegistryEntry> templates, string defaultRootPath)
       {
           templates.AddRange(new[]{
 
               new TypeRegistryEntry
               {
                   Type = "Unity.Mathematics.float3",
                   SubType = Unity.NetCode.GhostFieldSubType.Translation2d,
                   Quantized = true,
                   Smoothing = SmoothingAction.InterpolateAndExtrapolate,
                   SupportCommand = false,
                   Composite = false,
                   Template = "Assets/Samples/NetCodeGen/Templates/Translation2d.cs",
                   TemplateOverride = "",
               },
           }
       }
   }
}
```

#### Subtype definition
If your template uses sub-types (as in the example above), you need to add a partial class for `Unity.NetCode.GhostFieldSubType` inside the Netcode assembly reference folder. For example:

```c#
namespace Unity.NetCode
{
   static public partial class GhostFieldSubType
   {
       public const int MySubType = 1;
   }
}
```

The new subtypes will be available in your project everywhere you  reference the `Unity.NetCode` assembly.

#### GhostComponentModifiers
`ComponentModifiers` have been removed. You should use the `GhostComponentVariation` attribute to create a ghost component variant instead.

To upgrade, create a new file that contains your variants in an assembly that has visibility or access to the types you want to add a variation for. Then, for each modifier, create its respective variant implementation as in the following example:

```c#
 // Old modifier
 new GhostComponentModifier
 {
     typeFullName = "Unity.Transforms.Translation",
     attribute = new GhostComponentAttribute{PrefabType = GhostPrefabType.All, OwnerPredictedSendType = GhostSendType.All, SendDataForChildEntity = false},
     fields = new[]
     {
         new GhostFieldModifier
         {
             name = "Value",
             attribute = new GhostFieldAttribute{Quantization = 100, Smoothing=SmoothingAction.InterpolateAndExtrapolate}
         }
     },
     entityIndex = 0
 };
 
// The new variant
[GhostComponentVariation(typeof(Translation))]
[GhostComponent(SendDataForChildEntity = false)]
public struct MyTranslationVariant
{
 [GhostField(Quantization=100, Smoothing=SmoothingAction.InterpolateAndExtrapolate)] public float3 Value;
}
```

You must also declare these variants as the default to use for that Component. You must implement the `RegisterDefaultVariants` method to create a concrete system implementation for the `DefaultVariantSystemBase`. There are no particular restrictions on where to put this System. 

```c#
class MyDefaultVariantSystem : DefaultVariantSystemBase
{
   protected override void RegisterDefaultVariants(Dictionary<ComponentType, System.Type> defaultVariants)
   {
       defaultVariants.Add(new ComponentType(typeof(Translation)), typeof(MyTranslationVariant));
       ...
   }
}
```

<a name="hybrid"></a>

## Hybrid Renderer

### Remove Hybrid Renderer V1

Hybrid Renderer V1 has been removed from the code base. Hybrid Renderer V2 is enabled by default, so you no longer need the `ENABLE_HYBRID_RENDERER_V2` scripting define.

If your project used the Hybrid Renderer V1, you need to convert it to use the [Universal Render Pipeline](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest), or the [High Definition Render Pipeline](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@latest). This is because the built in render pipeline isn't supported by Hybrid Renderer V2.

<a name="physics"></a>

## Physics

### Update TriggerEventSystem from JobComponentSystem to SystemBase

When declaring an Entity inside `ITriggerEventsJob > Execute`, use:

```c#
Entity entityA = triggerEvent.EntityA;
```

Instead of:

```c#
Entity entityA = triggerEvent.Entities.EntityA;
```

You also no longer need to use `BuildPhysicsWorld`. Previously, `BuildPhysicsWorld` was used like this:

```c#
   protected override void OnCreate()
   {
       base.OnCreate();
       buildPhysicsWorld = World.GetOrCreateSystem<BuildPhysicsWorld>();
       stepPhysicsWorld = World.GetOrCreateSystem<StepPhysicsWorld>();
       commandBufferSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
   }
```

To upgrade it, use the following code:

```c#
   protected override void OnCreate()
   {
       stepPhysicsWorld = World.GetOrCreateSystem<StepPhysicsWorld>();
       commandBufferSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
   }
```

The following code shows the difference between using `ITriggerEventJob` with `JobComponentSystem` or `SystemBase`. 

**JobComponentSystem example:**

```c#
public class PickupOnTriggerSystem : JobComponentSystem
{
   private BuildPhysicsWorld buildPhysicsWorld;
   private StepPhysicsWorld stepPhysicsWorld;
   private EndSimulationEntityCommandBufferSystem commandBufferSystem;
 
   protected override void OnCreate()
   {
       base.OnCreate();
       buildPhysicsWorld = World.GetOrCreateSystem<BuildPhysicsWorld>();
       stepPhysicsWorld = World.GetOrCreateSystem<StepPhysicsWorld>();
       commandBufferSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
   }
 
   protected override JobHandle OnUpdate(JobHandle inputDependencies)
   {
       var job = new PickupOnTriggerSystemJob();
       job.allPickups = GetComponentDataFromEntity<PickupTag>(true);
       job.allPlayers = GetComponentDataFromEntity<PlayerTag>(true);
       job.entityCommandBuffer = commandBufferSystem.CreateCommandBuffer();
 
       JobHandle jobHandle = job.Schedule(stepPhysicsWorld.Simulation,
           inputDependencies);
       commandBufferSystem.AddJobHandleForProducer(jobHandle);
       return jobHandle;
   }
 
   [BurstCompile]
   struct PickupOnTriggerSystemJob : ITriggerEventsJob
   {
       [ReadOnly] public ComponentDataFromEntity<PickupTag> allPickups;
       [ReadOnly] public ComponentDataFromEntity<PlayerTag> allPlayers;
       public EntityCommandBuffer entityCommandBuffer;
 
       public void Execute(TriggerEvent triggerEvent)
       {
           Entity entityA = triggerEvent.EntityA;
           Entity entityB = triggerEvent.EntityB;
           if (allPickups.HasComponent(entityA) && allPickups.HasComponent(entityB))
               return;
 
           if (allPickups.HasComponent(entityA) && allPlayers.HasComponent(entityB))
               entityCommandBuffer.DestroyEntity(entityA);
           else if (allPlayers.HasComponent(entityA) && allPickups.HasComponent(entityB))
               entityCommandBuffer.DestroyEntity(entityB);
       }
   }
}
```

**SystemBase example:**

```c#
public partial class PickupOnTriggerSystem : SystemBase
{
   private StepPhysicsWorld stepPhysicsWorld;
   private EndSimulationEntityCommandBufferSystem commandBufferSystem;
 
   protected override void OnCreate()
   {
       stepPhysicsWorld = World.GetOrCreateSystem<StepPhysicsWorld>();
       commandBufferSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
   }
 
   protected override void OnUpdate()
   {
       var job = new PickupOnTriggerSystemJob
       {
           allPickups = GetComponentDataFromEntity<PickupTag>(true),
           allPlayers = GetComponentDataFromEntity<PlayerTag>(true),
           entityCommandBuffer = commandBufferSystem.CreateCommandBuffer()
       };
       Dependency = job.Schedule(stepPhysicsWorld.Simulation, Dependency);
       commandBufferSystem.AddJobHandleForProducer(Dependency);
   }
  
   [BurstCompile]
   struct PickupOnTriggerSystemJob : ITriggerEventsJob
   {
       [ReadOnly] public ComponentDataFromEntity<PickupTag> allPickups;
       [ReadOnly] public ComponentDataFromEntity<PlayerTag> allPlayers;
       public EntityCommandBuffer entityCommandBuffer;
 
       public void Execute(TriggerEvent triggerEvent)
       {
           Entity entityA = triggerEvent.EntityA;
           Entity entityB = triggerEvent.EntityB;
           if (allPickups.HasComponent(entityA) && allPickups.HasComponent(entityB))
               return;
           
           if (allPickups.HasComponent(entityA) && allPlayers.HasComponent(entityB))
               entityCommandBuffer.DestroyEntity(entityA);
           else if (allPlayers.HasComponent(entityA) && allPickups.HasComponent(entityB))
               entityCommandBuffer.DestroyEntity(entityB);
       }
   }
}
```
