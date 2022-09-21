# Create a cleanup component

To create a cleanup component, create a struct that inherits from `ICleanupComponentData`. 

The following code sample shows an empty cleanup component:

[!code-cs[Create a cleanup Component](../DocCodeSamples.Tests/CreateComponentExamples.cs#system-state)]

> [!NOTE]
> Empty cleanup components are often sufficient, but you can add properties to store information required to cleanup the target archetypes.

