using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    public static partial class EntitiesSourceFactory
    {
        public static class Common
        {
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
                        $@"[AOT.MonoPInvokeCallback(typeof(Unity.Entities.InternalCompilerInterface.JobChunkRunWithoutJobSystemDelegate))]" :
                        $@"[AOT.MonoPInvokeCallback(typeof(Unity.Entities.InternalCompilerInterface.JobRunWithoutJobSystemDelegate))]";

            public static SyntaxNode SchedulingInvocationFor(LambdaJobDescription description)
            {
                static string ExecuteMethodArgs(LambdaJobDescription description)
                {
                    var argStrings = new HashSet<string>();
                    foreach (var variable in description.VariablesCaptured)
                    {
                        if (!variable.IsThis)
                            argStrings.Add(description.Schedule.Mode == ScheduleMode.Run && variable.IsWritable
                                ? $"ref {variable.OriginalVariableName}"
                                : variable.OriginalVariableName);
                    }

                    if (description.Schedule.DependencyArgument != null)
                        argStrings.Add($@"{description.Schedule.DependencyArgument.ToString()}");

                    if (description.WithFilterEntityArray != null)
                        argStrings.Add($@"{description.WithFilterEntityArray.ToString()}");

                    foreach (var argument in description.AdditionalVariablesCapturedForScheduling)
                        argStrings.Add(argument.Name);

                    if (description.InStructSystem)
                        argStrings.Add($"ref {description.SystemStateParameterName}");

                    return argStrings.SeparateByComma();
                }

                var template = $@"{description.ExecuteInSystemMethodName}({ExecuteMethodArgs(description)}));";
                return SyntaxFactory.ParseStatement(template).DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            }

            public static string SharedComponentFilterInvocations(LambdaJobDescription description)
            {
                return
                    description
                        .WithSharedComponentFilterArgumentSyntaxes
                        .Select(arg => $@"{description.EntityQueryFieldName}.SetSharedComponentFilter({arg});")
                        .SeparateByNewLine();
            }
        }
    }
}
