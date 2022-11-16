# Iterate over component data with SystemAPI.Query

To iterate through a collection of data on the main thread, you can use the [`SystemAPI.Query<T>`](xref:Unity.Entities.SystemAPI.Query*) method in both [`ISystem`](systems-isystem.md) and [`SystemBase`](systems-systembase.md) system types. It uses C#’s idiomatic `foreach` syntax.

You can overload the method with up to seven type parameters. The supported type parameters are:

* `IAspect`
* `IComponentData`
* `ISharedComponentData`
* `DynamicBuffer<T>`
* `RefRO<T>`
* `RefRW<T>`
* `EnabledRefRO<T>` where T : `IEnableableComponent`, `IComponentData`
* `EnabledRefRW<T>` where T : `IEnableableComponent`, `IComponentData`

## SystemAPI.Query implementation

Whenever you invoke `SystemAPI.Query<T>`, the source-generator solution creates an `EntityQuery` field on the system itself, and caches an `EntityQuery` that consists of the queried types, with their respective read-write/read-only access modes, in this field. During compilation, the source-generation solution replaces the `SystemAPI.Query<T>` invocation in a `foreach` statement with an enumerator that iterates through the cached query’s data.

Additionally, the source-generation solution caches all the required type handles, and automatically injects `TypeHandle.Update(SystemBase system)` or `TypeHandle.Update(ref SystemState state)` as necessary before every `foreach`. This ensures that the type handles are safe to use.

The source generators also generate code to automatically complete all necessary read and read-write dependencies before each `foreach` statement.

## Use SystemAPI.Query to query data

The following is an example that uses `SystemAPI.Query` to iterate through every entity that has both `LocalToWorldTransform` and `RotationSpeed` components: 

```cs
public partial struct RotationSpeedSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
   	 float deltaTime = SystemAPI.Time.DeltaTime;

   	 foreach (var (transform, speed) in SystemAPI.Query<RefRW<LocalToWorldTransform>, RefRO<RotationSpeed>>())   	 
   		 transform.ValueRW.Value = transform.ValueRO.Value.RotateY(speed.ValueRO.RadiansPerSecond * deltaTime);   	 
    }
}
```

Because the example modifies the `LocalToWorldTransform` data, it's wrapped inside `RefRW<T>`, as a read-write reference. However, because it only reads the `RotationSpeed` data, it uses `RefRO<T>`. `RefRO<T>` usage is entirely optional: you could use the following instead as valid code: 

```c#
`foreach (var (transform, speed) in SystemAPI.Query<RefRW<LocalToWorldTransform>, RotationSpeed)`
```

[`RefRW<T>.ValueRW`](xref:Unity.Entities.RefRW`1.ValueRW*), [`RefRW<T>.ValueRO`](xref:Unity.Entities.RefRW`1.ValueRO*), and [`RefRO<T>.ValueRO`](xref:Unity.Entities.RefRO`1.ValueRO*) all return a reference to the component. When called, `ValueRW` conducts a safety check for read-write access, and `ValueRO` does the same for read-only access.

## Accessing entities in the `foreach` statement

`Unity.Entities.Entity` isn't a supported type parameter. Every query is already an implicit filter of all existing entities. To get access to the entity, use [`WithEntityAccess`](xref:Unity.Entities.QueryEnumerable`1.WithEntityAccess*). For example:

```c#
foreach (var (transform, speed, entity) in SystemAPI.Query<RefRW<LocalToWorldTransform>, RefRO<RotationSpeed>>().WithEntityAccess())
{
    // Do stuff;
}
```

Note that the `Entity` argument comes last in the returned tuple.

## Known limitations

`SystemAPI.Query` has the following limitations, which are outlined below.

### Dynamic buffer read-only limitation

`DynamicBuffer<T>` type parameters in `SystemAPI.Query<T>` are read-write access by default. However, if you want read-only access, you have to create your own implementation, similar to the following:

```c#
var bufferHandle = systemState.GetBufferTypeHandle<MyBufferElement>(isReadOnly: true);
var myBufferElementQuery = SystemAPI.QueryBuilder().WithAll<MyBufferElement>().Build();
var chunks = myBufferElementQuery.ToArchetypeChunkArray(Allocator.Temp);

foreach (var chunk in chunks)
{
    var numEntities = chunk.Count;
    var bufferAccessor = chunk.GetBufferAccessor(bufferHandle);

    for (int j = 0; j < numEntities; j++)
    {
   	 var dynamicBuffer = bufferAccessor[j];
   	 // Read from `dynamicBuffer` and perform various operations
    }
}
```

### Reusing SystemAPI.Query

You can't store `SystemAPI.Query<T>()` in a variable and then use it in one or more multiple `foreach` statements. This is because the implementation of the API relies on knowing what the query types are at compile time. For example:

```c#
var myQuery = SystemAPI.Query<ComponentA>();

if (firstCondition)
    myQuery = myQuery.WithNone<ComponentB>();
if (secondCondition)
    myQuery = myQuery.WithAll<ComponentC>();
if (thirdCondition)
    myQuery = myQuery.WithAny<ComponentD, ComponentE>();

foreach (var element in myQuery) { // Do stuff; }
```

The source-generation solution doesn't know at compile-time what `EntityQuery` to generate and cache, which type handles it should call `Update` on, nor which dependencies to complete. It's also performance intensive to check whether `myQuery` is always used, without additional chaining with `.WithXXX()` methods.
