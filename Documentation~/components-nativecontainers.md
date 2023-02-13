# Native container component support

The [Collections package](https://docs.unity3d.com/Packages/com.unity.collections@latest) provides [native container](xref:JobSystemNativeContainer) types such as `NativeList` and `NativeHashMap`, plus unsafe containers such as `UnsafeList`. You can use these container types in components.

Both native and unsafe containers are value types rather than reference types. The key differences between `Unsafe` containers and `Native` containers are:

* You can only use the Jobs Debugger with native containers. 
* Native containers copy a reference to its underlying data.

A `NativeContainer` is safer and consistently meets expectations than an `UnsafeContainer`. 

## Component limitations

If you put a container types in a component, they have the following limitations:

|**Functionality**|**Native containers**|**Unsafe containers**|
|--|--|--|
|Compatible with Jobs Debugger|Yes|No|
|Can be used in job worker threads|Yes|Yes|
|Can be used on main thread|Yes|Yes|
|Usable with [ComponentLookup](xref:Unity.Entities.ComponentLookup`1) on main thread|Yes|Yes|
|Usable with [ComponentLookup](xref:Unity.Entities.ComponentLookup`1) in job worker threads|No|Yes|
|Can contain other `NativeContainers`|No|Yes<br/><br/>This is technically supported but impacts performance.|
|Can contain other `UnsafeContainers`|Yes|Yes|

These restrictions don't apply to usage of native containers outside of components. For example, `NativeContainers` can nest other `NativeContainers` on the main thread (though it isn't possible to do this in job structs).
