# Entities overview

The Entities package lets you use Unity's Entity Component System (ECS), which organizes your project in a data-oriented way.

![](images/entities-splash-image.png)

## Package installation

To use the Entities package, you must have Unity version 2022.2.0b8 and later installed.

To install the package, open the Package Manager window (**Window &gt; Package Manager**) and perform one of the following options:

* [Add the package by its name](xref:upm-ui-quick)
* [Add the package from its Git URL](xref:upm-ui-giturl)

## Known issues

* If an exception happens in the entity iteration blocks [`Entities.ForEach`](iterating-data-entities-foreach.md) or the idiomatic foreach loop in [`SystemAPI.Query`](xref:Unity.Entities.SystemAPI.Query*), then ECS doesn't clean up the exception if the calling code isn't from a derived system method such as `OnCreate`, `OnUpdate`, `OnDestroy`, `OnStartRunning`, or `OnStopRunning`. This causes further exceptions to be erroneously reported.
* You can't schedule an [`IJobEntity`](iterating-data-ijobentity.md) that takes any argument directly constructed from a method invocation. For example, the following code doesn't work:

    ```c#
    new SomeJob().Schedule(JobHandle.CombineDependecies(handle1, handle2))
    ``` 

    To fix this, store the argument in a local variable and use that as the argument. For example: 

    ```c#
    (new SomeJob().Schedule(someCombinedHandle))
    ```
* If you schedule an [`IJobEntity`](iterating-data-ijobentity.md) constructed with an initialization expression that invokes a [`SystemAPI`](xref:Unity.Entities.SystemAPI) method, such as:

    ```c# 
    SomeJob{targetLookup = SystemAPI.GetComponentLookup<Target>()}.Schedule()
    ``` 

    This causes the following runtime error:  

    ```
    No suitable code replacement generated, this is either due to generators failing, or lack of support in your current context.
    ``` 

    To fix this, put in a local variable first. For example, the following works: 

    ```c#
    var targetLookup = SystemAPI.GetComponentLookup<Target>(); new SomeJob{targetLookup = targetLookup}.Schedule()
    ```
* Calling `SystemAPI` methods from static methods in a system causes the following runtime error:

    ```
    No suitable code replacement generated, this is either due to generators failing, or lack of support in your current context.
    ```

## Additional resources

* [Getting started](getting-started.md)
* [Upgrade guide](upgrade-guide.md)
* [What's new](whats-new.md)
* [ECS packages](ecs-packages.md)
