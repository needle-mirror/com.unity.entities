---
uid: iterating-data-entities-foreach
---

# Iterate over data with Entities.ForEach

If you use the [`SystemBase`](xref:Unity.Entities.SystemBase) class to create your systems, you can use the [`Entities.ForEach`](xref:Unity.Entities.SystemBase.Entities) construction to define and execute algorithms over entities and their components. At compile time, the Unity translates each `ForEach()` call into a generated job.

You pass `Entities.ForEach` a lambda expression, and Unity generates an [entity query](systems-entityquery.md) based on the lambda parameter types. When the generated job runs, Unity calls the lambda once for each entity that matches the query. `ForEachLambdaJobDescription` represents this generated job.

## Define a lambda expression

When you define the [`Entities.ForEach`](xref:Unity.Entities.SystemBase.Entities) lambda expression, you can declare parameters that the [`SystemBase`](xref:Unity.Entities.SystemBase) class uses to pass in information about the current entity when it executes the method.

A typical lambda expression looks like this:

[!code-cs[lambda-params](../DocCodeSamples.Tests/LambdaJobExamples.cs#lambda-params)]

You can pass up to eight parameters to an `Entities.ForEach` lambda expression. If you need to pass more parameters, you can define a custom delegate. For more information, see the section on [Custom delegates](#custom-delegates) in this document. 

When you use the standard delegates, you must group the parameters in the following order:

1. Parameters passed-by-value (no parameter modifiers)
1. Writable parameters (`ref` parameter modifier)
1. Read-only parameters (`in` parameter modifier)

You must use the `ref` or `in` parameter modify keyword on all components. If you don't, the component struct that Unity passes to your method is a copy instead of a reference. This means that it takes up extra memory for the read-ony parameters, and any changes you make to the components are silently thrown when the copied struct goes out of scope after the function returns.

If the lambda expression doesn't follow this order, and you haven't created a suitable delegate, the compiler provides an error similar to:

`error CS1593: Delegate 'Invalid_ForEach_Signature_See_ForEach_Documentation_For_Rules_And_Restrictions' does not take N arguments`

This error message cites the number of arguments as the issue even when though the problem is the parameter order.

### Custom delegates

If you want to use more than eight arguments in a `ForEach` lambda expression, you must declare your own delegate type and `ForEach` overload. This allows you to use an unlimited amount of arguments and to put the `ref`, `in`, and `value` parameters in any order you want.

You can also declare the three [named parameters](#named-parameters) `entity`, `entityInQueryIndex`, and `nativeThreadIndex` anywhere in your parameter list. Don't use `ref` or `in` modifiers for these parameters. 

The following example shows 12 arguments, and uses the `entity` parameter within the lambda expression:

[!code-cs[lambda-params](../DocCodeSamples.Tests/LambdaJobExamples.cs#lambda-params-many)]

### Component parameters

To access a component associated with an entity, you must pass a parameter of that component type to the lambda expression. The compiler automatically adds all components passed to the lambda expression to the entity query as required components. 

To update a component value, you must use the `ref` keyword in the parameter list to pass it to the lambda expression. Without the `ref` keyword, Unity makes any modifications to a temporary copy of the component.

To declare a read-only component passed to the lambda expression use the `in` keyword in the parameter list.

When you use `ref`, Unity marks the components in the current chunk as changed, even if the lambda expression doesn't actually modify them. For efficiency, you should always use the `in` keyword to declare components that your lambda expression doesn't modify as read only.

The following example passes a `Source` component parameter to a job as read-only, and a `Destination` component parameter as writable: 

[!code-cs[read-write-modifiers](../DocCodeSamples.Tests/LambdaJobExamples.cs#read-write-modifiers)]

> [!IMPORTANT]
> You can't pass [chunk components](components-chunk.md) to the `Entities.ForEach` lambda expression.

For dynamic buffers, use `DynamicBuffer<T>` rather than the component type stored in the buffer:

[!code-cs[dynamicbuffer](../DocCodeSamples.Tests/LambdaJobExamples.cs#dynamicbuffer)]

### Named parameters

You can also pass the following named parameters to the `Entities.ForEach` lambda expression, which Unity assigns values based on the entity the job is processing.

|**Parameter**|**Function**|
|---|---|
|`Entity entity`| The entity instance of the current entity. You can name the parameter anything as long as the type is `Entity`.|
|`int entityInQueryIndex`| The index of the entity in the list of all entities the query selected. Use the entity index value when you have a native array that you need to fill with a unique value for each entity. You can use the `entityInQueryIndex` as the index in that array. You should use `entityInQueryIndex` as the `sortKey` to add commands to a concurrent [entity command buffer](systems-entity-command-buffers.md).|
|`int nativeThreadIndex`| A unique index of the thread executing the current iteration of the lambda expression. When you use `Run()` to execute the lambda expression, `nativeThreadIndex` is always zero. Don't use `nativeThreadIndex` as the `sortKey` of a concurrent [entity command buffer](systems-entity-command-buffers.md); use `entityInQueryIndex`instead.|
|`EntityCommands commands`| You can name this parameter anything as long as the type is `EntityCommands`. Use this parameter only in conjunction with either `WithDeferredPlaybackSystem<T>()` or `WithImmediatePlayback()`. The `EntityCommands` type contains several methods that mirror their counterparts in the `EntityCommandBuffer` type. If you use an `EntityCommands` instance inside `Entities.ForEach()`, the compiler creates extra code where appropriate to handle the creation, scheduling, playback, and disposal of entity command buffers, on which counterparts to `EntityCommands` methods are invoked.|

## Execute an Entities.ForEach lambda expression
You can execute a job lambda expression in the following ways:

* Use `Schedule()` and `ScheduleParallel()` to schedule the job
* Use `Run()` to execute the job immediately on the main thread. 

The following example illustrates a `SystemBase` implementation that uses `Entities.ForEach` to read the `Velocity` component and write to the `ObjectPosition` component:

[!code-cs[entities-foreach-example](../DocCodeSamples.Tests/LambdaJobExamples.cs#entities-foreach-example)]


## Select entities

`Entities.ForEach` has its own mechanism to define the entity query that it uses to select the entities to process. The query automatically includes any components that you use as parameters of the lambda expression. 

You can also use the `WithAll`, `WithAny`, and `WithNone` clauses to further refine which entities `Entities.ForEach` selects. See [`SystemBase.Entities`](xref:Unity.Entities.SystemBase.Entities) for the complete list of query options. 

The following example uses these clauses to select entities based on these parameters:

* The entity has the components, `Destination`, `Source`, and `LocalToWorld`
* The entity has at least one of the components, `ObjectRotation`, `ObjectPosition`, or `ObjectUniformScale`
* The entity doesn't have a `ObjectNonUniformScale` component.

[!code-cs[entity-query](../DocCodeSamples.Tests/LambdaJobExamples.cs#entity-query)]

In this example, only the `Destination` and `Source` components are accessed inside the lambda expression because they're the only components in the parameter list.

## Access the EntityQuery object 

`Entities.ForEach` creates an [`EntityQuery`](xref:Unity.Entities.EntityQuery) in [`OnCreate`](xref:Unity.Entities.ComponentSystemBase.OnCreate), which you can use a copy of at any time, even before `Entities.ForEach` is invoked.

To access this entity query, use [`WithStoreEntityQueryInField(ref query)`](xref:Unity.Entities.SystemBase.Entities) with the `ref` parameter modifier. This method assigns a reference to the query to the field you provide. However, this `EntityQuery` doesn't have any of the filters that the `Entities.ForEach` invocation sets up.

The following example illustrates how to access the `EntityQuery` object implicitly created for an `Entities.ForEach` construction. It uses the `EntityQuery` object to invoke the [`CalculateEntityCount()`](xref:Unity.Entities.EntityQuery.CalculateEntityCount*) method and uses this count to create a native array with enough space to store one value per entity that the query selects:

[!code-cs[store-query](../DocCodeSamples.Tests/LambdaJobExamples.cs#store-query)]

## Access optional components

The `Entities.ForEach` lambda expression doesn't support querying and accessing optional components with `WithAny<T,U>`. 

If you want to read or write to an optional component, split the `Entities.ForEach` construction into multiple jobs for each combination of the optional components. For example, if you have two optional components, you would need three `ForEach` constructions: one including the first optional component, one including the second, and one including both components. Another alternative is to use `IJobChunk `iterate by chunk. For more information, see [Iterating over data by batch](iterating-data-ijobentitybatch.md).

## Change filtering

You can use `WithChangeFilter<T>` to enable change filtering, which processes components only if another component in the entity has changed since the last time the current `SystemBase` instance has run. The component type in the change filter must either be in the lambda expression parameter list, or part of a `WithAll<T>` statement. For example:

[!code-cs[with-change-filter](../DocCodeSamples.Tests/LambdaJobExamples.cs#with-change-filter)]

An entity query supports change filtering on up to two component types.

Unity applies change filtering at the archetype chunk level. If any code accesses a component in a chunk that has write access, then Unity marks that component type in that archetype chunk as changed, even if the code didn’t change any data. 

## Shared component filtering

Unity groups entities with [shared components](components-shared.md) into chunks with other entities that have the same value for their shared components. To select groups of entities that have specific shared component values, use the [`WithSharedComponentFilter`](xref:Unity.Entities.LambdaJobQueryConstructionMethods.WithSharedComponentFilter*) method.

The following example selects entities grouped by a `Cohort ISharedComponentData`. The lambda expression in this example sets a `DisplayColor IComponentData` component based on the entity’s cohort:

[!code-cs[with-shared-component](../DocCodeSamples.Tests/LambdaJobExamples.cs#with-shared-component)]

The example uses the `EntityManager` to get all the unique cohort values. It then schedules a lambda job for each cohort, and passes the new color to the lambda expression as a captured variable. 

## Capture variables

You can capture local variables for the `Entities.ForEach` lambda expression. When you call one of the `Schedule` methods instead of `Run` to use a job to execute the lambda expression, there are some restrictions on the captured variables and how you use them:

* You can only capture [native containers](https://docs.unity3d.com/Manual/JobSystemNativeContainer.html) and blittable types.
* A job can only write to captured variables that are native containers. To return a single value, create a native array with one element.

If you read a native container, but don't write to it, always use `WithReadOnly(variable)` to specify read-only access. For more information about setting attributes for captured variables, see [`SystemBase.Entities`](xref:Unity.Entities.SystemBase.Entities). `Entities.ForEach` provides these as methods because the C# language doesn't allow attributes on local variables.

To dispose of captured native containers or types that contain native containers after `Entities.ForEach` runs, use `WithDisposeOnCompletion(variable)`. If you call this in `Run()`, this disposes of the types immediately after the lambda expression runs. If you call this in `Schedule()` and`ScheduleParallel()`, it schedules them to be disposed of later with a job, and returns the JobHandle.

> [!NOTE]
> When you execute the method with `Run()` you can write to captured variables that aren't native containers. However, you should still use blittable types where possible so that the method can be compiled with [Burst](https://docs.unity3d.com/Packages/com.unity.burst@latest/index.html).

## Supported features

Use `Run()` to execute the lambda expression on the main thread. You can also use `Schedule()` to execute it as a single job, or `ScheduleParallel()` to execute it as a parallel job. These different execution methods have different constraints on how you access data. Also, the [Burst compiler](https://docs.unity3d.com/Packages/com.unity.burst@latest/index.html) uses a restricted subset of the C# language, so you need to specify `WithoutBurst()` if you want to use C# features outside this subset. This includes accessing managed types. 

The following table shows which features are supported in `Entities.ForEach` for the different methods of scheduling available in `SystemBase`:

| Supported feature             | `Run`   | `Schedule` | `ScheduleParallel`|
|-------------------------------|---------|------------|-------------------|
| Capture local value type      | &#10003;| &#10003;   |&#10003;           |
| Capture local reference type  | Only `WithoutBurst` and not in `ISystem`|||
| Writing to captured variables |&#10003; |            |                   |
| Use field on the system class | Only `WithoutBurst`| |                    |
| Methods on reference types    | Only `WithoutBurst` and not in `ISystem`|||
| Shared Components             | Only `WithoutBurst` and not in `ISystem`|||
| Managed Components            | Only `WithoutBurst` and not in `ISystem`|||
| Structural changes            | Only `WithStructuralChanges` and not in `ISystem`|||
| `SystemBase.GetComponent`     | &#10003;| &#10003;   |&#10003;           |
| `SystemBase.SetComponent`     | &#10003;| &#10003;   |                   |
| `GetComponentDataFromEntity`  | &#10003;| &#10003;   | Only as `ReadOnly`|
| `HasComponent`                | &#10003;| &#10003;   |&#10003;           |
| `WithDisposeOnCompletion`     | &#10003;| &#10003;   |&#10003;           |
| `WithScheduleGranularity `    |         |            |&#10003;           |
| `WithDeferredPlaybackSystem ` | &#10003;| &#10003;   |&#10003;           |
| `WithImmediatePlayback`       | &#10003;|            |                   |
| `HasBuffer `                  | &#10003;| &#10003;   |&#10003;           |
| `SystemBase.GetStorageInfoFromEntity`| &#10003;| &#10003;|&#10003;       |
| `SystemBase.Exists  `                | &#10003;| &#10003;|&#10003;       |

>[!IMPORTANT]
> `WithStructuralChanges()` disables Burst. Don't use this option if you want to achieve high levels of performance `Entities.ForEach`. If you want to use this option, use an [`EntityCommandBuffer`](xref:Unity.Entities.EntityCommandBuffer).

An `Entities.ForEach` construction uses Roslyn source generators to translate the code you write for the construction into correct ECS code. This translation means you can express the intent of your algorithm without having to include complex, boilerplate code. However, it means that some common ways of writing code aren't allowed.

The following features aren't supported:

* Dynamic code in `.With` invocations
* `SharedComponent` parameters `by ref` 
* Nested `Entities.ForEach` lambda expressions
* Calling with a delegate stored in a variable, field, or by method
* `SetComponent` with lambda parameter type
* `GetComponent` with writable lambda parameter
* Generic parameters in lambdas
* In systems with generic parameters

## Dependencies

By default, a system uses its [`Dependency`](xref:Unity.Entities.SystemBase.Dependency) property to manage its ECS-related dependencies. By default, the system adds each job created with `Entities.ForEach` and `Job.WithCode` to the `Dependency` job handle in the order that they appear in the [`OnUpdate()`](xref:Unity.Entities.SystemBase.OnUpdate*) function. You can also pass a `JobHandle` to your `Schedule` methods to manage job dependencies manually, which then return the resulting dependency. For more information, see the [`Dependency`](xref:Unity.Entities.SystemBase.Dependency) documentation.
 
See [Job dependencies](scheduling-jobs-dependencies.md) for more general information about job dependencies.
