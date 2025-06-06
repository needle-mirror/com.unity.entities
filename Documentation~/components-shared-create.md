# Create a shared component

You can create both [managed](components-managed.md) and [unmanaged](components-unmanaged.md) shared components.

## Create an unmanaged shared component

To create an unmanaged shared component, create a struct that implements the marker interface `ISharedComponentData`.

The following code sample shows an unmanaged shared component:

[!code-cs[Create an unmanaged shared component](../DocCodeSamples.Tests/CreateComponentExamples.cs#shared-unmanaged)]

To override the way that a shared component is checked for equality, you can implement the `IEquatable<>` interface, and ensure `public override int GetHashCode()` is implemented. Entities then internally uses these methods to compare shared components for equality, and therefore partitions entities differently that way. You can also put `[BurstCompile]` on these methods, and they will be compiled with Burst if they comply with Burst's restrictions.

## Create a managed shared component

If you create a shared component struct that has any managed fields (such as class types like strings), that component will be treated as a managed shared component. In that case, you also must implement `IEquatable<>`, and ensure `public override int GetHashCode()` is implemented. The equality methods are necessary to ensure comparisons don't generate unnecessary managed allocations due to implicit boxing when using the default `Equals` and `GetHashCode` implementations.

In contrast to IComponentData components, all shared components must be `struct`s, irrespective of whether they are managed or unmanaged. 

The following code sample shows a managed shared component:

[!code-cs[Create a managed shared component](../DocCodeSamples.Tests/CreateComponentExamples.cs#shared-managed)]
