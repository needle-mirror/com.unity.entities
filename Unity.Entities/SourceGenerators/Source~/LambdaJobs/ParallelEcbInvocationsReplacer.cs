using System;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static Unity.Entities.SourceGen.LambdaJobs.ArgumentParserForEntityCommandBufferMethods;
using static Unity.Entities.SourceGen.LambdaJobs.LambdaParamDescription_EntityCommandBuffer;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    internal static class ParallelEcbInvocationsReplacer
    {
        public static InvocationExpressionSyntax CreateReplacement(InvocationExpressionSyntax originalNode, IInvocationOperation invocationOperation)
        {
            string replacementCode;
            var parsedArguments = Parse(originalNode, invocationOperation.TargetMethod);

            switch (invocationOperation.TargetMethod.Name)
            {
                case "AddComponent":
                {
                    replacementCode = GetReplacementCodeForAddComponentInvocation(parsedArguments);
                    break;
                }
                case "AddBuffer":
                {
                    replacementCode = $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.{parsedArguments.GenericTypeArgument}(__sortKey, e: {parsedArguments.Entity})";
                    break;
                }
                case "AddSharedComponent":
                {
                    replacementCode =
                        String.IsNullOrEmpty(parsedArguments.EntitiesNativeArray)
                            ? $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.AddSharedComponent(__sortKey, e: {parsedArguments.Entity}, sharedComponent: {parsedArguments.SharedComponentData})"
                            : $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.AddSharedComponent(__sortKey, entities: {parsedArguments.EntitiesNativeArray}, sharedComponent: {parsedArguments.SharedComponentData})";
                    break;
                }
                case "AppendToBuffer":
                {
                    replacementCode = $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.AppendToBuffer(__sortKey, e: {parsedArguments.Entity}, element: {parsedArguments.BufferElementData})";
                    break;
                }
                case "CreateEntity":
                {
                    replacementCode =
                        parsedArguments.EntityArchetype == null
                            ? $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.CreateEntity(__sortKey)"
                            : $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.CreateEntity(__sortKey, {parsedArguments.EntityArchetype})";
                    break;
                }
                case "DestroyEntity":
                {
                    replacementCode =
                        String.IsNullOrEmpty(parsedArguments.EntitiesNativeArray)
                            ? $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.DestroyEntity(__sortKey, e: {parsedArguments.Entity})"
                            : $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.DestroyEntity(__sortKey, entities: {parsedArguments.EntitiesNativeArray})";
                    break;
                }
                case "Instantiate":
                {
                    replacementCode =
                        string.IsNullOrEmpty(parsedArguments.EntitiesNativeArray)
                            ? $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.Instantiate(__sortKey, e: {parsedArguments.Entity})"
                            : $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.Instantiate(__sortKey, e: {parsedArguments.Entity}, entities: {parsedArguments.EntitiesNativeArray})";
                    break;
                }
                case "RemoveComponent":
                {
                    replacementCode = GetReplacementCodeForRemoveComponentInvocation(parsedArguments);
                    break;
                }
                case "SetBuffer":
                {
                    replacementCode = $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.{parsedArguments.GenericTypeArgument}(__sortKey, e: {parsedArguments.Entity})";
                    break;
                }
                case "SetComponent":
                {
                    replacementCode = $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.SetComponent(__sortKey, e: {parsedArguments.Entity}, component: {parsedArguments.ComponentData})";
                    break;
                }
                case "SetComponentEnabled":
                {
                    replacementCode =
                        !String.IsNullOrEmpty(parsedArguments.GenericTypeArgument)
                            ? $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.{parsedArguments.GenericTypeArgument}(__sortKey, e: {parsedArguments.Entity}, value: {parsedArguments.BooleanValue})"
                            : $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.SetComponentEnabled(__sortKey, e: {parsedArguments.Entity}, componentType: {parsedArguments.ComponentType}, value: {parsedArguments.BooleanValue})";
                    break;
                }
                case "SetName":
                {
                    replacementCode = $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.SetName(__sortKey, e: {parsedArguments.Entity}, name: {parsedArguments.FixedString64Bytes})";
                    break;
                }
                case "SetSharedComponent":
                {
                    replacementCode =
                        String.IsNullOrEmpty(parsedArguments.EntitiesNativeArray)
                            ? $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.SetSharedComponent(__sortKey, e: {parsedArguments.Entity}, sharedComponent: {parsedArguments.SharedComponentData})"
                            : $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.SetSharedComponent(__sortKey, entities: {parsedArguments.EntitiesNativeArray}, sharedComponent: {parsedArguments.SharedComponentData})";
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return (InvocationExpressionSyntax)SyntaxFactory.ParseExpression(replacementCode);
        }

        static string GetReplacementCodeForAddComponentInvocation(ArgumentValues parsedArguments)
        {
            const string errorMessage = "Failed to parse invocation of RemoveComponent() with a generic type argument, an IComponentData argument, a ComponentType argument, OR a ComponentTypeSet argument.";

            if (String.IsNullOrEmpty(parsedArguments.EntitiesNativeArray))
            {
                if (!String.IsNullOrEmpty(parsedArguments.GenericTypeArgument))
                    return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.{parsedArguments.GenericTypeArgument}(__sortKey, e: {parsedArguments.Entity})";

                if (!String.IsNullOrEmpty(parsedArguments.ComponentType))
                    return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.AddComponent(__sortKey, e: {parsedArguments.Entity}, componentType: {parsedArguments.ComponentType})";

                if (!String.IsNullOrEmpty(parsedArguments.ComponentTypeSet))
                    return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.AddComponent(__sortKey, e: {parsedArguments.Entity}, typeSet: {parsedArguments.ComponentTypeSet})";

                if (!String.IsNullOrEmpty(parsedArguments.ComponentData))
                    return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.AddComponent(__sortKey, e: {parsedArguments.Entity}, component: {parsedArguments.ComponentData})";

                throw new ParserException(errorMessage);
            }

            if (!String.IsNullOrEmpty(parsedArguments.GenericTypeArgument))
                return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.{parsedArguments.GenericTypeArgument}(__sortKey, entities: {parsedArguments.EntitiesNativeArray})";

            if (!String.IsNullOrEmpty(parsedArguments.ComponentType))
                return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.AddComponent(__sortKey, entities: {parsedArguments.EntitiesNativeArray}, componentType: {parsedArguments.ComponentType})";

            if (!String.IsNullOrEmpty(parsedArguments.ComponentTypeSet))
                return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.AddComponent(__sortKey, entities: {parsedArguments.EntitiesNativeArray}, typeSet: {parsedArguments.ComponentTypeSet})";

            if (!String.IsNullOrEmpty(parsedArguments.ComponentData))
                return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.AddComponent(__sortKey, entities: {parsedArguments.EntitiesNativeArray}, component: {parsedArguments.ComponentData})";

            throw new ParserException(errorMessage);
        }

        static string GetReplacementCodeForRemoveComponentInvocation(ArgumentValues parsedArguments)
        {
            const string errorMessage = "Failed to parse invocation of RemoveComponent with a generic type argument, a ComponentType argument, OR a ComponentTypeSet argument.";

            if (String.IsNullOrEmpty(parsedArguments.EntitiesNativeArray))
            {
                if (!String.IsNullOrEmpty(parsedArguments.GenericTypeArgument))
                    return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.{parsedArguments.GenericTypeArgument}(__sortKey, e: {parsedArguments.Entity})";

                if (!String.IsNullOrEmpty(parsedArguments.ComponentType))
                    return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.RemoveComponent(__sortKey, e: {parsedArguments.Entity}, componentType: {parsedArguments.ComponentType})";

                if (!String.IsNullOrEmpty(parsedArguments.ComponentTypeSet))
                    return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.RemoveComponent(__sortKey, e: {parsedArguments.Entity}, typeSet: {parsedArguments.ComponentTypeSet})";

                throw new ParserException(errorMessage);
            }

            if (!String.IsNullOrEmpty(parsedArguments.GenericTypeArgument))
                return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.{parsedArguments.GenericTypeArgument}(__sortKey, entities: {parsedArguments.EntitiesNativeArray})";

            if (!String.IsNullOrEmpty(parsedArguments.ComponentType))
                return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.RemoveComponent(__sortKey, entities: {parsedArguments.EntitiesNativeArray}, componentType: {parsedArguments.ComponentType})";

            if (!String.IsNullOrEmpty(parsedArguments.ComponentTypeSet))
                return $"{GeneratedParallelWriterFieldNameInJobEntityBatchType}.RemoveComponent(__sortKey, entities: {parsedArguments.EntitiesNativeArray}, typeSet: {parsedArguments.ComponentTypeSet})";

            throw new ParserException(errorMessage);
        }
    }
}
