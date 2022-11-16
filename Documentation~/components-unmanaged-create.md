# Create an unmanaged component

To create an unmanaged component, create a struct that inherits from `IComponentData`.

The following code sample shows an unmanaged component:

[!code-cs[Create an unmanaged component](../DocCodeSamples.Tests/CreateComponentExamples.cs#unmanaged)]


Add properties that use compatible types to the struct to define data for the component. If you don't add any properties to the component, it acts as a [tag component](components-managed.md).