using Microsoft.CodeAnalysis;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.SystemGenerator
{
    public static class SystemGeneratorErrors
    {
        const string k_ErrorTitle = "System Error";

        public static void DC0063(SystemGeneratorContext context, Location location, string methodName, string componentDataName)
        {
            context.LogError(nameof(DC0063), k_ErrorTitle,
                $"Method {methodName} is giving write access to component data {componentDataName} in an Entities.ForEach.  The job system cannot guarantee the safety of that invocation.  Either change the scheduling from ScheduleParallel to Schedule or access through a captured ComponentDataFromEntity and mark it with WithNativeDisableParallelForRestriction if you are certain that this is safe.", location);
        }

        public static void DC0064(SystemGeneratorContext context, Location location)
        {
            context.LogError(nameof(DC0064), k_ErrorTitle, $"WithEntityQueryOptions must be used with a EntityQueryOption value as the argument.", location);
        }

        public static void DC0058(GeneratorExecutionContext context, Location location, string nonPartialSystemBaseDerivedClassName)
        {
            context.LogError(nameof(DC0058), k_ErrorTitle,
                $"All SystemBase-derived classes must be defined with the `partial` keyword, so that source generators can emit additional code into these classes. Please add the `partial` keyword to {nonPartialSystemBaseDerivedClassName}, as well as all the classes it is nested within.",
                location);
        }

        public static void DC0060(GeneratorExecutionContext context, Location location, string assemblyName)
        {
            context.LogError(nameof(DC0060), k_ErrorTitle,
                $"Assembly {assemblyName} contains Entities.ForEach or Entities.OnUpdate invocations that use burst but does not have a reference to Unity.Burst.  Please add an assembly reference to `Unity.Burst` in the asmdef for {assemblyName}.", location);
        }

        public static void DC0051(SystemGeneratorContext context, Location location, string argumentTypeName, string invokedMethodName)
        {
            context.LogError(nameof(DC0051), k_ErrorTitle,
                $"Type {argumentTypeName} cannot be used with {invokedMethodName} as generic types and parameters are not allowed", location);
        }
    }
}
