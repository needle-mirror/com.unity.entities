using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.SystemGenerator
{
    public static class SystemGeneratorErrors
    {
        const string k_ErrorTitle = "System Error";
        const string k_SystemStateAccess = "System usage with Missing SystemState";

        public static void DC0051(SystemDescription context, Location location, string argumentTypeName, string invokedMethodName)
        {
            context.LogError(nameof(DC0051), k_ErrorTitle,
                $"Type {argumentTypeName} cannot be used with {invokedMethodName} as generic types and parameters are not allowed", location);
        }

        public static void DC0058(GeneratorExecutionContext context, Location location, string nonPartialSystemBaseDerivedClassName, SystemType systemType)
        {
            context.LogError(nameof(DC0058), k_ErrorTitle,
                $"All {systemType}-derived classes must be defined with the `partial` keyword, so that source generators can emit additional code into these classes. Please add the `partial` keyword to {nonPartialSystemBaseDerivedClassName}, as well as all the classes it is nested within.",
                location);
        }

        public static void DC0060(GeneratorExecutionContext context, Location location, string assemblyName)
        {
            context.LogError(nameof(DC0060), k_ErrorTitle,
                $"Assembly {assemblyName} contains Entities.ForEach or Entities.OnUpdate invocations that use burst but does not have a reference to Unity.Burst.  Please add an assembly reference to `Unity.Burst` in the asmdef for {assemblyName}.", location);
        }

        public static void DC0062(SystemDescription context, Location location, IEnumerable<string> invalidMethods, IEnumerable<string> validMethods, string invokedMethodName)
        {
            context.LogError(nameof(DC0062), k_ErrorTitle,
                $"{string.Join(", ", invalidMethods)} does not define a query, and cannot be used with {invokedMethodName}. Query defining functions are {string.Join(", ", validMethods)}", location);
        }

        public static void DC0063(SystemDescription context, Location location, string methodName, string componentDataName)
        {
            context.LogError(nameof(DC0063), k_ErrorTitle,
                $"Method {methodName} is giving write access to component data {componentDataName} in an Entities.ForEach.  The job system cannot guarantee the safety of that invocation.  Either change the scheduling from ScheduleParallel to Schedule or access through a captured ComponentLookup and mark it with WithNativeDisableParallelForRestriction if you are certain that this is safe.", location);
        }

        public static void DC0064(SystemDescription context, Location location)
        {
            context.LogError(nameof(DC0064), k_ErrorTitle, $"WithEntityQueryOptions must be used with a EntityQueryOption value as the argument.", location);
        }

        public static void DC0065(GeneratorExecutionContext context, Location location, string className)
        {
            context.LogError(nameof(DC0065), k_ErrorTitle, $"Only value type 'ISystem' types are allowed.  Make sure {className} is defined as a 'struct' or use 'SystemBase' if you want to use a class.", location);
        }

        public static void SGSG0001<T>(ISourceGeneratorDiagnosable systemDescription, T candidate) where T : ISystemCandidate {
            systemDescription.LogError(nameof(SGSG0001), k_SystemStateAccess,
                $"SystemState cannot passed in as an argument, as properties can't have parameters, as such {candidate.CandidateTypeName} access is not working on properties. Instead move it to a method passing in `ref SystemState`",
                candidate.Node.GetLocation());
        }

        public static void SGSG0002<T>(ISourceGeneratorDiagnosable systemDescription, T candidate) where T : ISystemCandidate {
            systemDescription.LogError(nameof(SGSG0002), k_SystemStateAccess,
                $"No reference to SystemState was found for function with {candidate.CandidateTypeName} access, add `ref SystemState ...` as method parameter. This will be used for updating handles and completing dependencies.",
                candidate.Node.GetLocation());
        }
    }
}
