# Create a managed component

To create a managed component, create a class that inherits from `IComponentData` and either has no constructor, or includes a parameterless constructor.

The following code sample shows a managed component:
[!code-cs[Create a managed Component](../DocCodeSamples.Tests/CreateComponentExamples.cs#managed)]

## Manage the lifecycle of external resources

For managed components that reference external resources, it's best practice to implement [ICloneable](https://docs.microsoft.com/en-us/dotnet/api/system.icloneable) and [IDisposable](https://docs.microsoft.com/en-us/dotnet/api/system.idisposable), for example, for a managed component that stores a reference to a [`ParticleSystem`](https://docs.unity3d.com/Manual/class-ParticleSystem.html). 

If you duplicate this managed component's entity, by default this creates two managed components that both reference the same particle system. If you implement `ICloneable` for the managed component, you can duplicate the particle system for the second managed component. If you destroy the managed component, by default the particle system remains behind. If you implement `IDisposable` for the managed component, you can destroy the particle system when the component is destroyed.

[!code-cs[Implement ICloneable and IDisposable](../DocCodeSamples.Tests/GeneralComponentExamples.cs#managed-component-external-resource)]