using System;
using Microsoft.CodeAnalysis;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    public static class LambdaJobsErrors
    {
        const string k_ErrorTitle = "Lambda Jobs Error";

        public static void DC0003(SystemGeneratorContext context, Location location, string name)
        {
            context.LogError(nameof(DC0003), k_ErrorTitle,
                $"The name '{name}' is already used in this system.", location);
        }

        public static void DC0004(SystemGeneratorContext context, Location location, string variableName)
        {
            context.LogError(nameof(DC0004), k_ErrorTitle,
                $"Entities.ForEach Lambda expression captures a non-value type '{variableName}'. This is only allowed with .WithoutBurst() and .Run()", location);
        }

        public static void DC0005(SystemGeneratorContext context, Location location, string parameterName, string parameterTypeName)
        {
            context.LogError(nameof(DC0005), k_ErrorTitle,
                $"Entities.ForEach Lambda expression parameter '{parameterName}' with type {parameterTypeName} is not supported", location);
        }

        public static void DC0008(SystemGeneratorContext context, Location location, string methodName)
        {
            context.LogError(nameof(DC0008), k_ErrorTitle,
                $"The argument to {methodName} needs to be a literal value.", location);
        }

        public static void DC0009(SystemGeneratorContext context, Location location, string methodName)
        {
            context.LogError(nameof(DC0009), k_ErrorTitle,
                $"{methodName} is only allowed to be called once.", location);
        }

        public static void DC0010(SystemGeneratorContext context, Location location, string methodName)
        {
            context.LogError(nameof(DC0010), k_ErrorTitle,
                $"The Entities.ForEach statement contains dynamic code in {methodName} that cannot be statically analyzed.", location);
        }

        public static void DC0011(SystemGeneratorContext context, Location location)
        {
            context.LogError(nameof(DC0011), k_ErrorTitle,
                $"Every Entities.ForEach statement needs to end with a .Schedule(), .ScheduleParallel() or .Run() invocation.", location);
        }

        public static void DC0012(SystemGeneratorContext context, Location location, string argumentName, string constructionMethodName)
        {
            context.LogError(nameof(DC0012), k_ErrorTitle,
                $"Entities.{constructionMethodName} is called with an invalid argument {argumentName}. You can only use Entities.{constructionMethodName} on local variables that are captured inside the lambda. Please assign the field to a local variable and use that instead.", location);
        }

        public static void DC0013(SystemGeneratorContext context, Location location, string capturedVariableName)
        {
            context.LogError(nameof(DC0013), k_ErrorTitle,
                $"Entities.ForEach Lambda expression writes to captured variable '{capturedVariableName}' that is then read outside. This is only supported when you use .Run().", location);
        }

        public static void DC0014(SystemGeneratorContext context, Location location, string parameterName, string[] supportedParameters)
        {
            context.LogError(nameof(DC0014), k_ErrorTitle,
                $"Execute() parameter '{parameterName}' is not a supported parameter in an IJobEntitiesForEach type. Supported `int` parameter names are {supportedParameters.SeparateByComma()}.", location);
        }

        public static void DC0223(SystemGeneratorContext context, Location location, string typeName, bool descriptionIsInSystemBase, bool isManagedIComponentData)
        {
            var componentText = isManagedIComponentData ? $"managed IComponentData `{typeName}`" : $"ISharedComponentData type {typeName}";

            var message = descriptionIsInSystemBase
                ? $"Entities.ForEach uses {componentText}. This is only supported when using .WithoutBurst() and .Run()."
                : $"Entities.ForEach uses {componentText}. This is not supported in ISystem systems.";
            context.LogError(nameof(DC0223), k_ErrorTitle, message, location);
        }

        public static void DC0020(SystemGeneratorContext context, Location location, string sharedComponentTypeName)
        {
            context.LogError(nameof(DC0020), k_ErrorTitle,
                $"ISharedComponentData type {sharedComponentTypeName} can not be received by ref. Use by value or in.", location);
        }

        public static void DC0021(SystemGeneratorContext context, Location location, string parameterName, string unsupportedTypeName)
        {
            context.LogError(nameof(DC0021), k_ErrorTitle,
                $"parameter '{parameterName}' has type {unsupportedTypeName}. This type is not a IComponentData / ISharedComponentData and is therefore not a supported parameter type for Entities.ForEach.", location);
        }

        public static void DC0024(SystemGeneratorContext context, Location location, string componentTypeName)
        {
            context.LogError(nameof(DC0024), k_ErrorTitle,
                $"Entities.ForEach uses managed IComponentData `{componentTypeName}` by ref. To get write access, receive it without the ref modifier.", location);
        }

        public static void DC0025(SystemGeneratorContext context, Location location, string typeName)
        {
            context.LogError(nameof(DC0025), k_ErrorTitle,
                $"Type `{typeName}` is not allowed to implement method `OnCreateForCompiler'.  This method is supplied by code generation.", location);
        }

        public static void DC0026(SystemGeneratorContext context, Location location, string allTypeName)
        {
            context.LogError(nameof(DC0026), k_ErrorTitle,
                $"Entities.ForEach lists has .WithAll<{allTypeName}>() and a .WithSharedComponentFilter method with a parameter of that type.  Remove the redundant WithAll method.", location);
        }

        public static void DC0027(SystemGeneratorContext context, Location location)
        {
            context.LogError(nameof(DC0027), k_ErrorTitle,
                $"Entities.ForEach Lambda expression makes a structural change. Use an EntityCommandBuffer to make structural changes or add a .WithStructuralChanges invocation to the Entities.ForEach to allow for structural changes.  Note: WithStructuralChanges is runs without burst and is only allowed with .Run().", location);
        }

        public static void DC0029(SystemGeneratorContext context, Location location)
        {
            context.LogError(nameof(DC0029), k_ErrorTitle, "Entities.ForEach Lambda expression has a nested Entities.ForEach Lambda expression. Only a single Entities.ForEach Lambda expression is currently supported.", location);
        }

        public static void DC0031(SystemGeneratorContext context, Location location)
        {
          context.LogError(nameof(DC0031), "Lambda Jobs Error",
                $"Entities.ForEach Lambda expression stores the EntityQuery with a .WithStoreEntityQueryInField invocation but does not store it in a valid field.  Entity Queries can only be stored in fields of the containing SystemBase.", location);
        }

        public static void DC0033(SystemGeneratorContext context, Location location, string parameterName, string unsupportedTypeName)
        {
            context.LogError(nameof(DC0033), k_ErrorTitle,
                $"{unsupportedTypeName} implements IBufferElementData and must be used as DynamicBuffer<{unsupportedTypeName}>. Parameter '{parameterName}' is not a IComponentData / ISharedComponentData and is therefore not a supported parameter type for Entities.ForEach.", location);
        }

        public static void DC0034(SystemGeneratorContext context, Location location, string argumentName, string unsupportedTypeName)
        {
            context.LogError(nameof(DC0034), k_ErrorTitle,
                $"Entities.WithReadOnly is called with an argument {argumentName} of unsupported type {unsupportedTypeName}. It can only be called with an argument that is marked with [NativeContainerAttribute] or a type that has a field marked with [NativeContainerAttribute].", location);
        }

        public static void DC0036(SystemGeneratorContext context, Location location, string argumentName, string unsupportedTypeName)
        {
            context.LogError(nameof(DC0036), k_ErrorTitle,
                $"Entities.WithNativeDisableContainerSafetyRestriction is called with an invalid argument {argumentName} of unsupported type {unsupportedTypeName}. It can only be called with an argument that is marked with [NativeContainerAttribute] or a type that has a field marked with [NativeContainerAttribute].", location);
        }

        public static void DC0037(SystemGeneratorContext context, Location location, string argumentName, string unsupportedTypeName)
        {
            context.LogError(nameof(DC0037), k_ErrorTitle,
                $"Entities.WithNativeDisableParallelForRestriction is called with an invalid argument {argumentName} of unsupported type {unsupportedTypeName}. It can only be called with an argument that is marked with [NativeContainerAttribute] or a type that has a field marked with [NativeContainerAttribute].", location);
        }

        public static void DC0043(SystemGeneratorContext context, Location location, string jobName)
        {
            context.LogError(nameof(DC0043), k_ErrorTitle,
                $"Entities.WithName cannot be used with name '{jobName}'. The given name must consist of letters, digits, and underscores only, and may not contain two consecutive underscores.", location);
        }

        public static void DC0044(SystemGeneratorContext context, Location location)
        {
            context.LogError(nameof(DC0044), k_ErrorTitle,
                $"Entities.ForEach can only be used with an inline lambda.  Calling it with a delegate stored in a variable, field, or returned from a method is not supported.", location);
        }

        public static void DC0046(SystemGeneratorContext context, Location location, string methodName, string typeName)
        {
            context.LogError(nameof(DC0046), k_ErrorTitle,
                $"Entities.ForEach cannot use component access method {methodName} that needs write access with the same type {typeName} that is used in lambda parameters.", location);
        }

        public static void DC0047(SystemGeneratorContext context, Location location, string methodName, string typeName)
        {
            context.LogError(nameof(DC0047), k_ErrorTitle,
                $"Entities.ForEach cannot use component access method {methodName} with the same type {typeName} that is used in lambda parameters with write access (as ref).", location);
        }

        public static void DC0050(SystemGeneratorContext context, Location location, string parameterTypeFullName)
        {
            context.LogError(nameof(DC0050), k_ErrorTitle,
                $"Type {parameterTypeFullName} cannot be used as an Entities.ForEach parameter as generic types and generic parameters are not currently supported in Entities.ForEach", location);
        }


        public static void DC0051(SystemGeneratorContext context, Location location, string argumentTypeName, string invokedMethodName)
        {
            context.LogError(nameof(DC0051), k_ErrorTitle,
                $"Type {argumentTypeName} cannot be used with {invokedMethodName} as generic types and parameters are not allowed", location);
        }


        public static void DC0052(SystemGeneratorContext context, Location location, string argumentTypeName, string invokedMethodName)
        {
            context.LogError(nameof(DC0052), k_ErrorTitle,
                $"Type {argumentTypeName} cannot be used with {invokedMethodName} as it is not a supported component type", location);
        }

        public static void DC0053(SystemGeneratorContext context, Location location, string systemTypeName)
        {
            context.LogError(nameof(DC0053), k_ErrorTitle,
                $"Entities.ForEach cannot be used in system {systemTypeName} as Entities.ForEach in generic system types are not supported.", location);
        }

        public static void DC0054(SystemGeneratorContext context, Location location, string methodName)
        {
            context.LogError(nameof(DC0054), k_ErrorTitle,
                $"Entities.ForEach is used in generic method {methodName}.  This is not currently supported.", location);
        }

        public static void DC0055(SystemGeneratorContext context, Location location, string lambdaParameterComponentTypeName)
        {
            context.LogWarning(nameof(DC0055), k_ErrorTitle,
                $"Entities.ForEach passes {lambdaParameterComponentTypeName} by value.  Any changes made will not be stored to the underlying component.  Please specify the access you require. Use 'in' for read-only access or `ref` for read-write access.", location);
        }

        public static void DC0056(SystemGeneratorContext context, Location location, string typeGroup1Name, string typeGroup2Name, string componentTypeName)
        {
            context.LogError(nameof(DC0056), k_ErrorTitle,
                $"Entities.ForEach has component {componentTypeName} in both {typeGroup1Name} and {typeGroup2Name}.  This is not permitted.", location);
        }

        public static void DC0057(SystemGeneratorContext context, Location location)
        {
            context.LogError(nameof(DC0057), k_ErrorTitle,
                $"WithStructuralChanges cannot be used with Job.WithCode.  WithStructuralChanges should instead be used with Entities.ForEach.", location);
        }

        public static void DC0059(SystemGeneratorContext context, Location location, string methodName)
        {
            context.LogError(nameof(DC0059), k_ErrorTitle,
                $"The argument indicating read only access to {methodName} cannot be dynamic and must to be a boolean literal `true` or `false` value when used inside of an Entities.ForEach.", location);
        }

        public static void DC0048(SystemGeneratorContext context, Location location,
            string jobEntitiesForEachTypeName)
        {
            context.LogError(
                nameof(DC0048),
                "IJobEntitiesForEach Error",
                $"IJobEntitiesForEach types may only contain value-type fields, but {jobEntitiesForEachTypeName} contains non-value type fields.",
                location);
        }

        public static void DC0059(SystemGeneratorContext context, Location location)
        {
            context.LogError(
                nameof(DC0059),
                "IJobEntitiesForEach Error",
                "IJobEntitiesForEach types may not have the [Unity.Burst.BurstCompile] attribute. IJobEntityBatch types generated from IJobEntitiesForEach types" +
                "will be bursted (or not) depending on whether WithoutBurst() is invoked when calling Entities.ForEach(IJobEntitiesForEach job).",
                location);
        }

        public static void DC0070(SystemGeneratorContext context, Location location, ITypeSymbol duplicateType)
        {
            context.LogError(
                nameof(DC0070), k_ErrorTitle,
                $"{duplicateType.Name} is used multiple times as a lambda parameter. Each IComponentData, ISharedComponentData, DynamicBuffer<T> type may only be used once in Entities.ForEach().", location);
        }

        public static void DC0071(SystemGeneratorContext context, Location location, string methodName)
        {
            context.LogError(nameof(DC0071), k_ErrorTitle,
                $"Invocation {methodName} cannot be used in an Entities.ForEach in system implementing ISystem.", location);
        }

        public static void DC0072(SystemGeneratorContext context, Location location)
        {
            context.LogError(nameof(DC0072), k_ErrorTitle,
                $"Entities.ForEach in ISystem systems must be accessed through the SystemState argument passed into the containing method (state.Entities.ForEach(...).", location);
        }

        public static void DC0073(SystemGeneratorContext context, Location location)
        {
            context.LogError(nameof(DC0073), k_ErrorTitle,
                $"WithScheduleGranularity cannot be used with Schedule or Run as it controls how parallel job scheduling occurs. Use ScheduleParallel with this feature.", location);
        }
    }
}
