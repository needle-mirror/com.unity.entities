# Access data on the main thread

How you access data in a system depends on whether you’ve used a managed [`SystemBase`](systems-systembase.md) system, or an unmanaged [`ISystem`](systems-isystem.md) system. The following are ways of accessing the data in a system:

* [**SystemAPI**](#systemapi): Calls the data you can get from `SystemState`, but caches and updates the data for you, meaning that you can use [`SystemAPI`](xref:Unity.Entities.SystemAPI) methods directly in an `Update` loop. Because you can directly use these methods in `Update`, there's no runtime overhead of using `SystemAPI`. However, `SystemAPI` works via codegen, so if you want to avoid some of the iteration time penalty associated with codegen, `SystemState` might be preferable.
* [**SystemState**](#systemstate): Use the properties and methods in [`SystemState`](xref:Unity.Entities.SystemState) to access raw entity state data in `ISystem` systems. `SystemBase` and `SystemAPI` natively use the data in `SystemState`.
* [**SystemBase**](#systembase): Contains the same methods as `SystemState`, but you can call them from a [`SystemBase`](xref:Unity.Entities.SystemBase) system. 

## SystemAPI

SystemAPI is a class that provides caching and utility methods for accessing data in an entity's world. It works in non-static methods in `SystemBase` and non-static methods in `ISystem` that take `ref SystemState` as a parameter. Because you can directly use these methods in `Update`, there's no runtime cost to using `SystemAPI`, so use `SystemAPI` to access data, wherever possible, unless you are concerned with codegen. Because `SystemAPI` works via codegen it has some associated iteration time penalties, so you can use `SystemState` instead to avoid this.

For more information, refer to the [SystemAPI overview documentation](systems-systemapi.md).

## SystemState

You can use `SystemState` to get data in the following ways:

* Find out information about worlds
* Query a system to get information about it
* Get data which to add as a dependency of the system. This is similar to the way you get data with EntityManager, but with added dependencies.

For more information, refer to the [`SystemState` API documentation](xref:Unity.Entities.SystemState).

## SystemBase

All the methods in `SystemState` are available in `SystemBase`, and they're prefixed with `this. `rather than `state`.

## Additional resources

* [Systems comparison](systems-comparison.md)
* [SystemAPI overview](systems-systemapi.md)