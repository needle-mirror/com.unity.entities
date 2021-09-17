---
uid: ecs-ijobentitiesforeach
---

# IJobEntitiesForEach
[IJobEntityBatch](../ecs_ijobentitybatch.md) is a useful interface that, when implemented, allows you to iterate through and transform your data in batches of entities within a system.

If you would like to **perform the exact same data transformations in multiple systems**, you should create a type that implements the `IJobEntitiesForEach` interface. Doing so provides two advantages:
1. An `IJobEntitiesForEach` type can easily be (re-)used in multiple systems.
2. Each `IJobEntitiesForEach` type can be flexibly tailored to suit different needs in different systems.

The `IJobEntitiesForEach` interface is currently an experimental feature. In order to use it, you must add this scripting define in Player Settings: `SYSTEM_SOURCEGEN_ENABLED`.

## Implementing IJobEntitiesForEach
Create a type that implements `IJobEntitiesForEach`, and add a single `public void Execute()` method (with no overloads) that accepts an arbitrary number of parameters of [allowed types]. Inside the `Execute()` method, perform read/write actions on your data. 

Let's look at an example:

```cs
[GenerateAuthoringComponent]
public struct RotationSpeed : IComponentData
{
    public float RadiansPerSecond;
}

public struct RotateEntityJob : IJobEntitiesForEach
{
    public float Multiplier;

    public void Execute(ref Rotation rotation, in RotationSpeed speed)
    {
        rotation.Value =
            math.mul(
                math.normalize(rotation.Value),
                quaternion.AxisAngle(math.up(), speed.RadiansPerSecond * Multiplier));
    }
}
```

<a name="execute" id="execute"></a>
### The Execute() method in an IJobEntitiesForEach type
In the code sample above, what `RotateEntityJob` does in the `Execute()` method is straightforward: For every entity that has both the `Rotation` and the `RotationSpeed` components, the method recalculates (and updates) its `Rotation` value. When you schedule or run `Entities.ForEach(new RotateEntityJob { Multiplier = myFloat })` inside a system, this recalculation will then run on all entities that have both the `Rotation` and the `RotationSpeed` components.

Specifically, the following parameter types are allowed in the `Execute()` method in an `IJobEntitiesForEach` type:
- `IComponentData` (both managed and unmanaged)
- `ISharedComponentData`
- `DynamicBuffer<T>`
- `Entity`
- `int` (though only two parameter names are recognized: `entityInQueryIndex` and `nativeThreadIndex`)

Data that will be modified inside the `Execute()` method must be passed with the `ref` keyword. Use the `in` keyword to specify read-only access. 

**Parameters do not have to be written in any specific order, insofar as the aforementioned rules are observed.**

### Burst support
Notice that, in the code sample above, no `BurstCompile` attribute is placed on `RotateEntityJob`. It is important to note that you **should never place that attribute on `IJobEntitiesForEach` types**. 

When you schedule or run `Entities.ForEach(IJobEntitiesForEach job)` inside a system, a corresponding `IJobEntityBatch` type is [generated and injected into the system itself]. This generated `IJobEntityBatch` type is Burst-enabled by default, with default float mode, standard float precision, and non-synchronous compilation. 

If you would like to disable Burst on the resulting `IJobEntityBatch` type, simply schedule or run `Entities.WithoutBurst().ForEach(IJobEntitiesForEach job)`. Similarly, it is easy to enable Burst with different settings: Just schedule or run `Entities.WithBurst(myFloatMode, myFloatPrecision, myCompilationPreference).ForEach(IJobEntitiesForEach job)`. 

As mentioned earlier, one of the main selling points of using the `IJobEntitiesForEach` interface is the ability to perform the same data transformations under different conditions. Being able to configure Burst compilation in multiple systems that schedule or run the same `IJobEntitiesForEach` type is a showcase of such flexibility.

<a name="chaining-methods" id="chaining-methods"></a>
## Chaining methods in Entities.ForEach(IJobEntitiesForEach job)
The following methods can be chained together in an `Entities.ForEach(IJobEntitiesForEach job)` invocation:

- `WithBurst(FloatMode floatMode, FloatPrecision floatPrecision, bool synchronousCompilation)`
- `WithName(string name)`
- `WithAll<T>()`
- `WithAny<T>()`
- `WithNone<T>()`
- `WithStoreEntityQueryInField(ref EntityQuery query)`
- `WithSharedComponentFilter(T sharedComponent)`
- `WithEntityQueryOptions(EntityQueryOptions options)`
- `WithChangeFilter<T>`
- `WithSharedComponentFilter<T>`

E.g., scheduling or running `Entities.WithName("myName").ForEach(new RotateEntityJob())` will compile just fine.

The following methods **cannot** be chained together in an `Entities.ForEach(IJobEntitiesForEach job)` invocation:

- `WithStructuralChanges<T>()`
- `WithReadOnly(T capturedVariable)`
- `WithDisposeOnCompletion(T capturedVariable)`
- `WithNativeDisableContainerSafetyRestriction(T capturedVariable)`
- `WithNativeDisableParallelForRestriction(T capturedVariable)`
- `WithNativeDisableUnsafePtrRestriction(T capturedVariable)`

E.g., scheduling or running `Entities.WithReadOnly(myNativeArray).ForEach(new RotateEntityJob())` will trigger a compilation error.

## Passing an anonymous function vs an IJobEntitiesForEach instance to Entities.ForEach()
You should pass an anonymous function as an argument to `Entities.ForEach()` if you need to use any of the methods that cannot be [chained together] with `Entities.ForEach(IJobEntitiesForEach job)`.

Additionally, for data transformations that are performed in only one system, there is no advantage to preferring an `IJobEntitiesForEach` instance over an anonymous function.

<a name="implementation-details" id="implementation-details"></a>
### Implementation details
This section is for the curious who want to learn how the feature is implemented behind the scenes. Let's look at a system that invokes `Entities.ForEach(new RotateEntityJob())`:

```cs
public partial class RotationSpeedSystem_IJobEntitiesForEach : SystemBase
{
    protected override void OnUpdate()
    {
        const float rotationMultiplier = 10f;
        Entities.ForEach(new RotateEntityJob { Multiplier = rotationMultiplier }).ScheduleParallel();
    }
}
```

In every `SystemBase`-derived class where `Entities.ForEach(IJobEntitiesForEach job)` is scheduled or run, the following are generated and injected into the class:
- An `EntityQuery` field,
- A `OnCreateForCompiler()` method, which populates the `EntityQuery` field with a query that covers all component types passed as arguments to the `Execute()` method in the invoked `IJobEntitiesForEach` type,
- An `IJobEntityBatch` or `IJobEntityBatchWithIndex` type,
- A modified `OnUpdate()` method, which calls into another generated method that in turn creates an instance of the generated `IJobEntityBatch` type, sets its fields appropriately, and finally schedules or runs it.

To illustrate, the system above would be modified into the following during compilation:

```cs
[System.Runtime.CompilerServices.CompilerGenerated]
unsafe public partial class RotationSpeedSystem_IJobEntitiesForEach : SystemBase
{
    [Unity.Entities.DOTSCompilerPatchedMethod("OnUpdate")]
    protected void __OnUpdate_43FD287D()
    {        
        const float rotationMultiplier = 10f;
        RotationSpeedSystem_JobEntitiesForEach_0_Execute(rotationMultiplier);
    }

    [System.Runtime.CompilerServices.CompilerGenerated]
    [Unity.Burst.NoAlias]
    [Unity.Burst.BurstCompile(FloatMode = Unity.Burst.FloatMode.Default, FloatPrecision = Unity.Burst.FloatPrecision.Standard, CompileSynchronously = false)]
    struct RotateEntityJob_Execute_0 : Unity.Entities.IJobEntityBatch
    {
        public RotateEntityJob __JobData;
        public Unity.Entities.ComponentTypeHandle<Unity.Transforms.Rotation> __RotationTypeHandle;
        [Unity.Collections.ReadOnly] public Unity.Entities.ComponentTypeHandle<RotationSpeed> __RotationSpeedTypeHandle;
        public unsafe void Execute(Unity.Entities.ArchetypeChunk batch, int batchIndex)
        {
            Unity.Transforms.Rotation* rotations = (Unity.Transforms.Rotation*)batch.GetNativeArray(__RotationTypeHandle).GetUnsafePtr();
            RotationSpeed* rotationSpeeds = (RotationSpeed*)batch.GetNativeArray(__RotationSpeedTypeHandle).GetUnsafeReadOnlyPtr();
            for (int i = 0; i < batch.Count; i++)
            {
                __JobData.Execute(ref rotations[i], in rotationSpeeds[i]);
            }
        }
    }

    Unity.Entities.EntityQuery RotationSpeedSystem_JobEntitiesForEach_0_Query;
    void RotationSpeedSystem_JobEntitiesForEach_0_Execute(float rotationMultiplier)
    {
        var __jobData = new RotateEntityJob{Multiplier = rotationMultiplier};
        Dependency =
			new RotateEntityJob_Execute_0
			{
				__JobData = __jobData, 
				__RotationTypeHandle = GetComponentTypeHandle<Unity.Transforms.Rotation>(), 
				__RotationSpeed_ForEach_IJobForEachEntitiesTypeHandle = 
					GetComponentTypeHandle<RotationSpeed_ForEach_IJobForEachEntities>(isReadOnly: true)
			}
			.ScheduleParallel(RotationSpeedSystem_JobEntitiesForEach_0_Query, dependsOn: Dependency);
    }

    protected override void OnCreateForCompiler()
    {
        base.OnCreateForCompiler();
		
        RotationSpeedSystem_JobEntitiesForEach_0_Query = 
			GetEntityQuery(
				new Unity.Entities.EntityQueryDesc
				{
					All = 
						new Unity.Entities.ComponentType[]
						{
							ComponentType.ReadOnly<RotationSpeed_ForEach_IJobForEachEntities>(), 
							ComponentType.ReadWrite<Unity.Transforms.Rotation>()
						}, 
					Any = new Unity.Entities.ComponentType[]{},
					None = new Unity.Entities.ComponentType[]{}, 
					Options = Unity.Entities.EntityQueryOptions.Default
				}
			);
    }
}
```

[allowed types]: #execute
[chained together]: #chaining-methods
[generated and injected into the system itself]: #implementation-details
