#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.JobEntity
{
    public partial class JobEntityModule
    {
        struct KnownJobEntityInfo : ISystemCandidate
        {
            JobEntityCandidate m_Candidate;
            TypeInfo m_TypeInfo;
            bool m_IsExtensionMethodUsed;

            public static bool TryCreate(ref SystemDescription systemDescription, JobEntityCandidate candidate, out KnownJobEntityInfo result)
            {
                result = new KnownJobEntityInfo
                {
                    m_Candidate = candidate
                };

                // Checks if the Candidate is JobEntity and Get Type info
                ExpressionSyntax? jobEntityInstance = candidate.MemberAccessExpressionSyntax.Expression as IdentifierNameSyntax;
                jobEntityInstance ??= candidate.MemberAccessExpressionSyntax.Expression as ObjectCreationExpressionSyntax;
                if (jobEntityInstance == null)
                {
                    return false;
                }

                result.m_TypeInfo = systemDescription.SemanticModel.GetTypeInfo(jobEntityInstance);
                bool isJobEntity;
                (isJobEntity, result.m_IsExtensionMethodUsed) = DoesTypeInheritJobEntityAndUseExtensionMethod(result.m_TypeInfo);
                if (!isJobEntity)
                    return false;
                if (ErrorOnNoPartial(ref systemDescription, result.m_TypeInfo))
                    return false;

                return true;
            }

            static (bool IsKnownCandidate, bool IsExtensionMethodUsed) DoesTypeInheritJobEntityAndUseExtensionMethod(TypeInfo typeInfo) =>
                typeInfo.Type.InheritsFromInterface("Unity.Entities.IJobEntity")
                    ? (IsKnownCandidate: true, IsExtensionMethodUsed: false) // IsExtensionMethodUsed is ignored if IsCandidate is false, so we don't need to test the same thing twice
                    : (IsKnownCandidate: typeInfo.Type.ToFullName() == "global::Unity.Entities.IJobEntityExtensions", IsExtensionMethodUsed: true);

            static bool ErrorOnNoPartial(ref SystemDescription systemDescription, TypeInfo typeInfo)
            {
                if (typeInfo.Type?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is TypeDeclarationSyntax declaringSystemType)
                {
                    if (!declaringSystemType.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                    {
                        JobEntityGeneratorErrors.SGJE0004(systemDescription, declaringSystemType.GetLocation(), declaringSystemType.Identifier.ValueText);
                        return true;
                    }
                }

                return false;
            }

            /// <returns> Replaced Invocation Scheduling Expression, null if invalid </returns>
            public ExpressionSyntax? GetAndAddScheduleExpression(ref SystemDescription systemDescription, int i, MemberAccessExpressionSyntax currentSchedulingNode)
            {
                // Create JobEntityDescription
                var (jobEntityType, jobArgumentUsedInSchedulingMethod) = GetIJobEntityTypeDeclarationAndArgument(currentSchedulingNode, systemDescription.SemanticModel, m_TypeInfo, m_IsExtensionMethodUsed);
                var jobEntityDesc =
                    new JobEntityDescription(
                        null,
                        jobEntityType,
                        systemDescription.IsUnityCollectionChecksEnabled,
                        systemDescription);

                if (jobEntityDesc.Invalid) return null;

                var scheduleMode = ScheduleModeHelpers.GetScheduleModeFromNameOfMemberAccess(m_Candidate.MemberAccessExpressionSyntax);
                if (!systemDescription.TryGetSystemStateParameterName(m_Candidate, out var systemStateExpression))
                    return null;
                var (scheduleMethodName, generatedQueryField) = AddSchedulingMethodToSystem(ref systemDescription, scheduleMode, i, jobEntityDesc);
                var (entityQuery, dependency) = GetEntityQueryAndDependencyExpressions(ref systemDescription, m_IsExtensionMethodUsed, (currentSchedulingNode.Parent as InvocationExpressionSyntax)!, m_Candidate.Invocation!, SyntaxFactory.IdentifierName(generatedQueryField), scheduleMode);
                return new SchedulingExpressionCreateInfo(
                    scheduleMethodName, scheduleMode,
                    jobArgumentUsedInSchedulingMethod, entityQuery, dependency,
                    systemStateExpression,
                    m_Candidate.Invocation?.Parent is MemberAccessExpressionSyntax
                ).ToExpressionSyntax();
            }

            static (INamedTypeSymbol jobSymbol, ExpressionSyntax PassedArgument) GetIJobEntityTypeDeclarationAndArgument(MemberAccessExpressionSyntax schedulingNode, SemanticModel semanticModel, TypeInfo typeInfo, bool isExtensionMethodUsed)
            {
                if (isExtensionMethodUsed)
                {
                    // Get JobEntity Symbol Passed To ExtensionMethod
                    ArgumentSyntax? jobEntityArgument = null;
                    if (schedulingNode.Parent is InvocationExpressionSyntax invocationExpression)
                        foreach (var argument in invocationExpression.ArgumentList.Arguments)
                        {
                            if (argument.NameColon == null)
                                jobEntityArgument = argument;
                            else if (argument.NameColon.Name.Identifier.ValueText == "jobData")
                                jobEntityArgument = argument;
                        }

                    if (jobEntityArgument != null)
                        return jobEntityArgument.Expression switch
                        {
                            ObjectCreationExpressionSyntax objectCreationExpressionSyntax
                                when semanticModel.GetSymbolInfo(objectCreationExpressionSyntax).Symbol is IMethodSymbol
                                {
                                    ReceiverType: INamedTypeSymbol namedTypeSymbol
                                } => (namedTypeSymbol, jobEntityArgument.Expression),

                            IdentifierNameSyntax identifierNameSyntax
                                when semanticModel.GetSymbolInfo(identifierNameSyntax).Symbol is ILocalSymbol
                                {
                                    Type: INamedTypeSymbol namedTypeSymbol
                                } => (namedTypeSymbol, jobEntityArgument.Expression),
                            _ => default
                        };
                }

                return (jobSymbol: (INamedTypeSymbol)typeInfo.Type!, PassedArgument: schedulingNode.Expression);
            }

            enum ArgumentType
            {
                EntityQuery,
                Dependency,
                JobEntity
            }

            static (ExpressionSyntax EntityQuery, ExpressionSyntax? Dependency) GetEntityQueryAndDependencyExpressions(ref SystemDescription context, bool isExtensionMethodUsed, InvocationExpressionSyntax giveBackInvocation, InvocationExpressionSyntax semanticOriginalInvocation,  ExpressionSyntax defaultEntityQueryName, ScheduleMode scheduleMode)
            {
                var entityQueryArgument = defaultEntityQueryName;
                ExpressionSyntax? dependencyArgument = null;

                for (var i = 0; i < semanticOriginalInvocation.ArgumentList.Arguments.Count; i++)
                {
                    var semanticOriginalArgument = semanticOriginalInvocation.ArgumentList.Arguments[i];
                    var giveBackArgument = giveBackInvocation.ArgumentList.Arguments[i];

                    var type = semanticOriginalArgument.NameColon == null
                        ? ParseUnnamedArgument(ref context, i, scheduleMode.IsSchedule())
                        : ParseNamedArgument();

                    ArgumentType ParseUnnamedArgument(ref SystemDescription context, int argumentPosition, bool methodOverloadAcceptsDependencyAsOnlyArgument)
                    {
                        switch (argumentPosition+(isExtensionMethodUsed?0:1))
                        {
                            case 0:
                            {
                                return ArgumentType.JobEntity;
                            }
                            case 1: // Could be EntityQuery or dependsOn
                            {
                                if (semanticOriginalArgument.Expression.IsKind(SyntaxKind.DefaultLiteralExpression))
                                    return methodOverloadAcceptsDependencyAsOnlyArgument ? ArgumentType.Dependency : ArgumentType.EntityQuery;

                                return context.SemanticModel.GetTypeInfo(semanticOriginalArgument.Expression).Type.ToFullName() == "global::Unity.Entities.EntityQuery"
                                    ? ArgumentType.EntityQuery : ArgumentType.Dependency;
                            }
                            case 2: // dependsOn
                                return ArgumentType.Dependency;
                        }

                        throw new ArgumentOutOfRangeException("The IJobEntityExtensions class does not contain methods accepting more than 4 arguments.");
                    }

                    ArgumentType ParseNamedArgument()
                        => semanticOriginalArgument.NameColon.Name.Identifier.ValueText switch
                        {
                            "query" => ArgumentType.EntityQuery,
                            "dependsOn" => ArgumentType.Dependency,
                            "jobData" => ArgumentType.JobEntity,
                            _ => throw new ArgumentOutOfRangeException("The IJobEntityExtensions class does not contain methods accepting more than 4 arguments.")
                        };

                    switch (type)
                    {
                        case ArgumentType.EntityQuery:
                            entityQueryArgument = giveBackArgument.Expression;
                            continue;
                        case ArgumentType.Dependency:
                            dependencyArgument = giveBackArgument.Expression;
                            continue;
                    }
                }

                return (entityQueryArgument, dependencyArgument);
            }

            static (string Name, string generatedQueryField)
                AddSchedulingMethodToSystem(ref SystemDescription systemDescription, ScheduleMode scheduleMode, int methodId, JobEntityDescription jobEntityDescription)
            {
                var (assignments, generatedQueryFieldName) = GetJobEntityAssignmentsAndGeneratedQueryFieldName(ref systemDescription, jobEntityDescription);

                var containsReturn = !scheduleMode.IsRun();

                var methodName = $"__ScheduleViaJobChunkExtension_{methodId}";

                var hasManagedComponents = jobEntityDescription.HasManagedComponents();
                var hasEntityIndexInQuery = jobEntityDescription.HasEntityIndexInQuery();

                var isRunWithoutJobs = scheduleMode.IsRun() && hasManagedComponents;
                var needsInternalScheduleParallel =
                    (scheduleMode == ScheduleMode.ScheduleParallel || scheduleMode == ScheduleMode.ScheduleParallelByRef) &&
                    hasEntityIndexInQuery;
                // Certain schedule paths require .Schedule/.Run calls that aren't in the IJobChunk public API,
                // and only appear in InternalCompilerInterface
                var staticExtensionsClass = (isRunWithoutJobs || needsInternalScheduleParallel)
                    ? "InternalCompilerInterface.JobChunkInterface" : "JobChunkExtensions";

                var methodWithArguments = scheduleMode.GetScheduleMethodWithArguments(hasManagedComponents, hasEntityIndexInQuery);
                var returnType = containsReturn ? "Unity.Jobs.JobHandle" : "void";
                var returnExpression = $"{"return ".EmitIfTrue(containsReturn)}Unity.Entities.{staticExtensionsClass}.{methodWithArguments}";

                var method =
                    $@"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                    {returnType} {methodName}({"ref ".EmitIfTrue(scheduleMode.IsByRef())}{jobEntityDescription.FullTypeName} job, Unity.Entities.EntityQuery entityQuery, Unity.Jobs.JobHandle dependency, ref Unity.Entities.SystemState state)
                    {{
                        {jobEntityDescription.FullTypeName}.InternalCompiler.CheckForErrors({scheduleMode.GetScheduleTypeAsNumber()});
                        {GenerateUpdateCalls(assignments).SeparateBySemicolonAndNewLine()};{Environment.NewLine}
                        {GenerateAssignments(assignments, jobEntityDescription.RequiresEntityManagerAccess).SeparateBySemicolonAndNewLine()};{Environment.NewLine}
                        {GeneratePreHelperJob(hasEntityIndexInQuery, scheduleMode)}
                        {returnExpression};
                    }}";

                systemDescription.NewMiscellaneousMembers.Add(SyntaxFactory.ParseMemberDeclaration(method));
                return (methodName, generatedQueryFieldName);
            }

            static (List<(JobEntityParam, string)> jobEntityAssignments, string generatedQueryFieldName) GetJobEntityAssignmentsAndGeneratedQueryFieldName(ref SystemDescription systemDescription, JobEntityDescription jobEntityDesc)
            {
                var queryTypes = new List<Query>();
                var jobEntityAssignments = new List<(JobEntityParam, string)>();

                foreach (var param in jobEntityDesc.UserExecuteMethodParams)
                {
                    if (param.IsQueryableType)
                        queryTypes.Add(
                            new Query
                            {
                                IsReadOnly = param.IsReadOnly,
                                Type = QueryType.All,
                                TypeSymbol = param.TypeSymbol
                            });

                    // Managed types do not use
                    if (!param.RequiresTypeHandleFieldInSystemBase)
                    {
                        if (string.IsNullOrEmpty(param.TypeHandleFieldAssignment))
                            continue;
                        jobEntityAssignments.Add((param, param.TypeHandleFieldAssignment));
                        continue;
                    }

                    var typeField = param is JobEntityParam_Entity
                        ? systemDescription.HandlesDescription.GetOrCreateEntityTypeHandleField()
                        : systemDescription.HandlesDescription.GetOrCreateTypeHandleField(param.TypeSymbol, param.IsReadOnly);
                    jobEntityAssignments.Add((param, typeField));
                }

                var generatedQueryField = systemDescription.HandlesDescription.GetOrCreateQueryField(
                    new SingleArchetypeQueryFieldDescription(
                        new Archetype(
                            queryTypes.Concat(jobEntityDesc.QueryAllTypes).ToArray(),
                            jobEntityDesc.QueryAnyTypes,
                            jobEntityDesc.QueryNoneTypes,
                            jobEntityDesc.QueryDisabledTypes,
                            jobEntityDesc.QueryAbsentTypes,
                            options: jobEntityDesc.EntityQueryOptions),
                        changeFilterTypes: jobEntityDesc.QueryChangeFilterTypes));
                return (jobEntityAssignments, generatedQueryField);
            }

            static IEnumerable<string> GenerateUpdateCalls(IEnumerable<(JobEntityParam JobEntityFieldToAssignTo, string ComponentTypeField)> assignments)
                => assignments
                    .Where(assignment => assignment.JobEntityFieldToAssignTo.RequiresTypeHandleFieldInSystemBase)
                    .Select(assignment => $"{assignment.ComponentTypeField}.Update(ref state)");

            static IEnumerable<string> GenerateAssignments(IEnumerable<(JobEntityParam JobEntityFieldToAssignTo, string AssignmentValue)> assignments, bool needsEntityManager)
            {
                var valueTuples = assignments as (JobEntityParam JobEntityFieldToAssignTo, string AssignmentValue)[] ?? assignments.ToArray();
                if (needsEntityManager)
                    yield return "job.__EntityManager = state.EntityManager";

                foreach (var (jobEntityFieldToAssignTo, assignmentValue) in valueTuples)
                {
                    if (jobEntityFieldToAssignTo.RequiresTypeHandleFieldInSystemBase)
                        yield return $"job.{jobEntityFieldToAssignTo.TypeHandleFieldName} = {assignmentValue}";
                    else
                        yield return $"job.{jobEntityFieldToAssignTo.TypeHandleFieldName} = state.{assignmentValue}";
                }
            }

            static string GeneratePreHelperJob(bool hasEntityIndexInQuery, ScheduleMode scheduleMode)
            {
                if (!hasEntityIndexInQuery) return "";

                const string runReturn = @"var baseEntityIndexArray =  entityQuery.CalculateBaseEntityIndexArray(state.WorldUpdateAllocator);
           job.__ChunkBaseEntityIndices = baseEntityIndexArray;";

                const string dependReturn = @"var baseEntityIndexArray = entityQuery.CalculateBaseEntityIndexArrayAsync(state.WorldUpdateAllocator, dependency, out var indexDependency);
                job.__ChunkBaseEntityIndices = baseEntityIndexArray;
                 dependency = indexDependency;";

                var output = scheduleMode switch
                {
                    ScheduleMode.Schedule => dependReturn,
                    ScheduleMode.ScheduleByRef => dependReturn,
                    ScheduleMode.ScheduleParallel => dependReturn,
                    ScheduleMode.ScheduleParallelByRef => dependReturn,
                    ScheduleMode.Run => runReturn,
                    ScheduleMode.RunByRef => runReturn,
                    _ => throw new ArgumentOutOfRangeException()
                };

                return output;
            }

            public string CandidateTypeName => m_Candidate.CandidateTypeName;
            public SyntaxNode Node => m_Candidate.Node;
        }

        readonly ref struct SchedulingExpressionCreateInfo
        {
            readonly string m_SchedulingMethodName;
            readonly ScheduleMode m_ScheduleMode;
            readonly ExpressionSyntax m_Job;
            readonly ExpressionSyntax m_EntityQuery;
            readonly ExpressionSyntax? m_Dependency;
            readonly ExpressionSyntax m_SystemStateExpression;
            readonly bool m_IsReturnValueUsedDirectly;
            public SchedulingExpressionCreateInfo(string schedulingMethodName, ScheduleMode scheduleMode, ExpressionSyntax job, ExpressionSyntax entityQuery, ExpressionSyntax? dependency, ExpressionSyntax systemStateExpression, bool isReturnValueUsedDirectly)
            {
                m_SchedulingMethodName = schedulingMethodName;
                m_ScheduleMode = scheduleMode;
                m_Job = job;
                m_EntityQuery = entityQuery;
                m_Dependency = dependency;
                m_SystemStateExpression = systemStateExpression;
                m_IsReturnValueUsedDirectly = isReturnValueUsedDirectly;
            }

            /// <summary>
            /// Creates ExpressionSyntax for scheduling, e.g. `someSchedulingMethod(ref SomeJob, someQuery, someDependency, ref systemState)`
            /// </summary>
            /// <returns>The created ExpressionSyntax</returns>
            public ExpressionSyntax ToExpressionSyntax()
            {
                // Get dependencyNode
                var shouldAddSystemDependencyAtAssignment = m_Dependency == null;
                shouldAddSystemDependencyAtAssignment &= !m_ScheduleMode.IsRun();
                shouldAddSystemDependencyAtAssignment &= !m_IsReturnValueUsedDirectly; // Support e.g. SomeJob.Schedule().Complete()
                // systemState.Dependency
                var dependencyNode = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, m_SystemStateExpression, SyntaxFactory.IdentifierName("Dependency"));

                // ref SomeJob, someQuery, someDependency, ref systemState
                var argumentList = SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.Argument(null, m_ScheduleMode.IsByRef() ? SyntaxFactory.Token(SyntaxKind.RefKeyword) : SyntaxFactory.Token(SyntaxKind.None), m_Job.WithoutLeadingTrivia()),
                    SyntaxFactory.Argument(m_EntityQuery),
                    SyntaxFactory.Argument(m_Dependency ?? dependencyNode),
                    SyntaxFactory.Argument(null, SyntaxFactory.Token(SyntaxKind.RefKeyword), m_SystemStateExpression)
                });
                // someSchedulingMethod(ref SomeJob, someQuery, someDependency, ref systemState)
                ExpressionSyntax replaceToNode = SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(m_SchedulingMethodName), SyntaxFactory.ArgumentList(argumentList));
                if (shouldAddSystemDependencyAtAssignment)
                    // systemState.Dependency = someSchedulingMethod(ref SomeJob, someQuery, someDependency, ref systemState)
                    replaceToNode = SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, dependencyNode, replaceToNode);
                return replaceToNode.WithLeadingTrivia(m_Job.GetLeadingTrivia()); // Leading trivia here includes
            }
        }
    }
}
