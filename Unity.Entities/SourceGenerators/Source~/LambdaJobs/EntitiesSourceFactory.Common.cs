using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    public static partial class EntitiesSourceFactory
    {
        public static class Common
        {
            static string GetReadOnlyQueryType(INamedTypeSymbol type) => $@"ComponentType.ReadOnly<{type.ToFullName()}>()";

            public static string NoAliasAttribute(LambdaJobDescription description) =>
                description.Burst.IsEnabled ? @"[Unity.Burst.NoAlias]" : string.Empty;

            public static string BurstCompileAttribute(LambdaJobDescription description) =>
                description.Burst.IsEnabled
                    ? $@"[Unity.Burst.BurstCompile(FloatMode=Unity.Burst.FloatMode.{description.Burst.Settings.BurstFloatMode.ToString()},
                        FloatPrecision=Unity.Burst.FloatPrecision.{description.Burst.Settings.BurstFloatPrecision.ToString()},
                        CompileSynchronously={description.Burst.Settings.SynchronousCompilation.ToString().ToLower()})]"
                    : string.Empty;

            public static string MonoPInvokeCallbackAttributeAttribute(LambdaJobDescription description) =>
                description.LambdaJobKind == LambdaJobKind.Entities ?
                        $@"[AOT.MonoPInvokeCallback(typeof(Unity.Entities.InternalCompilerInterface.JobEntityBatchRunWithoutJobSystemDelegate))]" :
                        $@"[AOT.MonoPInvokeCallback(typeof(Unity.Entities.InternalCompilerInterface.JobRunWithoutJobSystemDelegate))]";

            public static SyntaxNode SchedulingInvocationFor(LambdaJobDescription description)
            {
                static string ExecuteMethodArgs(LambdaJobDescription systemBaseDescription)
                {
                    switch (systemBaseDescription)
                    {
                        case LambdaJobDescription lambdaJobDescription:
                        {
                            var argStrings =
                                lambdaJobDescription
                                    .VariablesCaptured
                                    .Where(variable => !variable.IsThis)
                                    .Select(variable =>
                                        systemBaseDescription.Schedule.Mode == ScheduleMode.Run && variable.IsWritable
                                            ? $"ref {variable.OriginalVariableName}"
                                            : variable.OriginalVariableName)
                                    .ToList();

                            if (systemBaseDescription.Schedule.DependencyArgument != null)
                            {
                                argStrings.Add($@"{systemBaseDescription.Schedule.DependencyArgument.ToString()}");
                            }

                            if (lambdaJobDescription.WithFilterEntityArray != null)
                            {
                                argStrings.Add($@"{lambdaJobDescription.WithFilterEntityArray.ToString()}");
                            }

                            foreach (var argument in lambdaJobDescription.AdditionalVariablesCapturedForScheduling)
                                argStrings.Add(argument.Name);

                            if (lambdaJobDescription.InStructSystem)
                                argStrings.Add($"ref {lambdaJobDescription.SystemStateParameterName}");

                            return argStrings.Distinct().SeparateByComma();
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                switch (description)
                {
                    case LambdaJobDescription lambdaJobDescription:
                        var template = $@"{description.ExecuteInSystemMethodName}({ExecuteMethodArgs(lambdaJobDescription)}));";
                        return SyntaxFactory.ParseStatement(template).DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault();
                    default:
                        throw new ArgumentOutOfRangeException($"{description.DeclaringSystemType.Identifier} is neither LambdaJobDescription nor EntitiesOnUpdateDescription.");
                }
            }

            public static string SharedComponentFilterInvocations(LambdaJobDescription description)
            {
                return
                    description
                        .WithSharedComponentFilterArgumentSyntaxes
                        .Select(arg => $@"{description.EntityQueryFieldName}.SetSharedComponentFilter({arg});")
                        .SeparateByNewLine();
            }

            // This later gets replaced with #line directives to correct place in generated source for debugging
            public static string GeneratedLineTriviaToGeneratedSource { get; } = "// __generatedline__";
        }
    }
}
