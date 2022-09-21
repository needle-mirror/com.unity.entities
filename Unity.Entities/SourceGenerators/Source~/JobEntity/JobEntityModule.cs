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
    public class JobEntityModule : ISystemModule
    {
        Dictionary<TypeDeclarationSyntax, List<JobEntityCandidate>> m_JobEntityInvocationCandidates = new Dictionary<TypeDeclarationSyntax, List<JobEntityCandidate>>();
        List<TypeDeclarationSyntax> nonPartialJobEntityTypes = new List<TypeDeclarationSyntax>();

        public IEnumerable<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> Candidates
        {
            get
            {
                foreach (var kvp in m_JobEntityInvocationCandidates)
                    foreach (var candidate in kvp.Value)
                        yield return (candidate.Node, kvp.Key);
            }
        }
        public bool RequiresReferenceToBurst => true;

        enum ScheduleMode
        {
            Schedule,
            ScheduleByRef,
            ScheduleParallel,
            ScheduleParallelByRef,
            Run,
            RunByRef
        }

        public void OnReceiveSyntaxNode(SyntaxNode node)
        {
            if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccessExpressionSyntax }
                && memberAccessExpressionSyntax.Kind() == SyntaxKind.SimpleMemberAccessExpression
                && (memberAccessExpressionSyntax.Expression is IdentifierNameSyntax || memberAccessExpressionSyntax.Expression is ObjectCreationExpressionSyntax))
            {
                var schedulingMethodName = memberAccessExpressionSyntax.Name.Identifier.ValueText;

                if (Enum.GetNames(typeof(ScheduleMode)).Contains(schedulingMethodName))
                {
                    var containingType = node.AncestorOfKind<TypeDeclarationSyntax>();

                    // Discard if no base type, meaning it can't possible inherit from a System
                    if (containingType.BaseList == null || containingType.BaseList.Types.Count == 0)
                        return;
                    m_JobEntityInvocationCandidates.Add(containingType, new JobEntityCandidate(memberAccessExpressionSyntax));
                }
            }
        }

        readonly struct JobEntityCandidate : ISystemCandidate
        {
            public JobEntityCandidate(MemberAccessExpressionSyntax node) => MemberAccessExpressionSyntax = node;
            public string CandidateTypeName => "IJobEntity";
            public MemberAccessExpressionSyntax MemberAccessExpressionSyntax { get; }
            public SyntaxNode Node => MemberAccessExpressionSyntax;
        }

        static (bool IsCandidate, bool IsExtensionMethodUsed) IsIJobEntityCandidate(TypeInfo typeInfo)
        {
            return typeInfo.Type.InheritsFromInterface("Unity.Entities.IJobEntity")
                ? (IsCandidate: true, IsExtensionMethodUsed: false)
                : (IsCandidate: typeInfo.Type.ToFullName() == "Unity.Entities.IJobEntityExtensions", IsExtensionMethodUsed: true);

            // IsExtensionMethodUsed is ignored if IsCandidate is false, so we don't need to test the same thing twice
        }

        static (ExpressionSyntax Argument, INamedTypeSymbol Symbol)
            GetJobEntitySymbolPassedToExtensionMethod(MemberAccessExpressionSyntax candidate, SemanticModel semanticModel)
        {
            var arguments = candidate.AncestorOfKind<InvocationExpressionSyntax>()
                .ChildNodes().OfType<ArgumentListSyntax>().SelectMany(a => a.Arguments);
            var jobEntityArgument = GetJobEntityArgumentPassedToExtensionMethod(arguments);

            return jobEntityArgument.Expression switch
            {
                ObjectCreationExpressionSyntax objectCreationExpressionSyntax
                    when ((IMethodSymbol) semanticModel.GetSymbolInfo(objectCreationExpressionSyntax).Symbol)?.ReceiverType
                    is INamedTypeSymbol namedTypeSymbol => (jobEntityArgument.Expression, namedTypeSymbol),

                IdentifierNameSyntax identifierNameSyntax
                    when ((ILocalSymbol) semanticModel.GetSymbolInfo(identifierNameSyntax).Symbol)?.Type
                    is INamedTypeSymbol namedTypeSymbol => (jobEntityArgument.Expression, namedTypeSymbol),

                _ => default
            };
        }

        static ArgumentSyntax GetJobEntityArgumentPassedToExtensionMethod(IEnumerable<ArgumentSyntax> arguments)
        {
            foreach (var argument in arguments)
            {
                if (argument.NameColon == null)
                    return argument;
                if (argument.NameColon.Name.Identifier.ValueText == "jobData")
                    return argument;
            }
            throw new ArgumentException("No IJobEntity argument found.");
        }

        static (INamedTypeSymbol jobSymbol, SyntaxNode PassedArgument) GetIJobEntityTypeDeclarationAndArgument(
            MemberAccessExpressionSyntax candidate, SemanticModel semanticModel, TypeInfo typeInfo, bool isExtensionMethodUsed)
        {
            if (isExtensionMethodUsed)
            {
                var (argument, symbol) = GetJobEntitySymbolPassedToExtensionMethod(candidate, semanticModel);
                return (jobSymbol: symbol, PassedArgument: argument);
            }

            return (jobSymbol: (INamedTypeSymbol)typeInfo.Type, PassedArgument: candidate.Expression);
        }

        public bool RegisterChangesInSystem(SystemDescription systemDescription)
        {
            foreach (var nonPartialType in nonPartialJobEntityTypes)
                JobEntityGeneratorErrors.SGJE0004(systemDescription, nonPartialType.GetLocation(), nonPartialType.Identifier.ValueText);

            var candidates = m_JobEntityInvocationCandidates[systemDescription.SystemTypeSyntax];

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                ExpressionSyntax jobEntityInstance = candidate.MemberAccessExpressionSyntax.Expression as IdentifierNameSyntax;
                jobEntityInstance ??= candidate.MemberAccessExpressionSyntax.Expression as ObjectCreationExpressionSyntax;

                var typeInfo = systemDescription.SemanticModel.GetTypeInfo(jobEntityInstance);
                var (isCandidate, isExtensionMethodUsed) = IsIJobEntityCandidate(typeInfo);

                if (!isCandidate)
                    continue;

                if (typeInfo.Type?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is TypeDeclarationSyntax declaringSystemType)
                {
                    if (!declaringSystemType.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                    {
                        JobEntityGeneratorErrors.SGJE0004(systemDescription, declaringSystemType.GetLocation(), declaringSystemType.Identifier.ValueText);
                        continue;
                    }
                }

                var (jobEntityType, jobArgumentUsedInSchedulingMethod) =
                    GetIJobEntityTypeDeclarationAndArgument(candidate.MemberAccessExpressionSyntax, systemDescription.SemanticModel, typeInfo, isExtensionMethodUsed);

                var jobEntityDesc = new JobEntityDescription(jobEntityType, systemDescription);
                if (jobEntityDesc.Invalid) continue;

                var queryTypes = new List<Query>();
                var jobEntityAssignments = new List<(JobEntityParam, string)>();

                foreach (var param in jobEntityDesc.UserExecuteMethodParams)
                {
                    if(param.IsQueryableType)
                        queryTypes.Add(
                            new Query
                            {
                                IsReadOnly = param.IsReadOnly,
                                Type = QueryType.All,
                                TypeSymbol = param is JobEntityParam_DynamicBuffer ? ((INamedTypeSymbol)param.TypeSymbol).TypeArguments.First() : param.TypeSymbol
                            });

                    // Managed types do not use
                    if (!param.RequiresTypeHandleFieldInSystemBase)
                    {
                        if (string.IsNullOrEmpty(param.JobEntityFieldAssignment))
                            continue;
                        jobEntityAssignments.Add((param, param.JobEntityFieldAssignment));
                        continue;
                    }

                    var typeField = param is JobEntityParam_Entity
                        ? systemDescription.GetOrCreateEntityTypeHandleField(param.TypeSymbol)
                        : systemDescription.GetOrCreateTypeHandleField(param.TypeSymbol, param.IsReadOnly);
                    jobEntityAssignments.Add((param, typeField));
                }

                var generatedQueryField = systemDescription.GetOrCreateQueryField(
                    new SingleArchetypeQueryFieldDescription(
                        new Archetype(
                            queryTypes.Concat(jobEntityDesc.QueryAllTypes).ToArray(),
                            jobEntityDesc.QueryAnyTypes,
                            jobEntityDesc.QueryNoneTypes,
                            options: jobEntityDesc.EntityQueryOptions),
                        changeFilterTypes: jobEntityDesc.QueryChangeFilterTypes));

                var invocationExpression = candidate.MemberAccessExpressionSyntax.Parent as InvocationExpressionSyntax;
                var (entityQuery, dependency, scheduleMode) = GetArguments(systemDescription, isExtensionMethodUsed, invocationExpression, candidate.MemberAccessExpressionSyntax.Name.Identifier.ValueText, generatedQueryField);

                if (!(scheduleMode == ScheduleMode.Run || scheduleMode == ScheduleMode.RunByRef)) // Schedule or ScheduleParallel
                {
                    var sgje0014S = new HashSet<string>();
                    var sgje0015S = new HashSet<string>();
                    foreach (var userExecuteMethodParam in jobEntityDesc.UserExecuteMethodParams)
                    {
                        switch (userExecuteMethodParam)
                        {
                            case JobEntityParam_SharedComponent sharedComponent: // Error if managed sharedcomponent is scheduled
                                if (sharedComponent.RequiresEntityManagerAccess)
                                    sgje0014S.Add(GetParameterName(sharedComponent.ParameterSymbol));
                                break;
                            case JobEntityParam_ManagedComponent managedComponent:
                                if (managedComponent.isUnityEngineComponent) { // Error if UnityEngine components are scheduled instead of run
                                    sgje0015S.Add(GetParameterName(managedComponent.ParameterSymbol));
                                } else { // Error if managed component is scheduled
                                    sgje0014S.Add(GetParameterName(managedComponent.ParameterSymbol));
                                }
                                break;
                        }
                    }

                    if (sgje0014S.Any())
                        JobEntityGeneratorErrors.SGJE0014(systemDescription, candidate.Node.GetLocation(), jobEntityDesc.FullTypeName, sgje0014S.SeparateByCommaAndSpace());
                    if (sgje0015S.Any())
                        JobEntityGeneratorErrors.SGJE0015(systemDescription, candidate.Node.GetLocation(), jobEntityDesc.FullTypeName, sgje0014S.SeparateByCommaAndSpace());
                }

                var isByRef = scheduleMode == ScheduleMode.RunByRef || scheduleMode == ScheduleMode.ScheduleByRef || scheduleMode == ScheduleMode.ScheduleParallelByRef;
                var (syntax, name) = CreateSchedulingMethod(jobEntityAssignments, jobEntityDesc.FullTypeName, scheduleMode, i, jobEntityDesc, systemDescription.SystemType, isByRef);

                systemDescription.NewMiscellaneousMembers.Add(syntax);

                // Generate code for callsite


                var systemStateParameterResult = systemDescription.TryGetSystemStateParameterName(candidate);
                if (!systemStateParameterResult.Success)
                    continue;


                var shouldAddDependencySnippetAtAssignment = dependency == null;
                shouldAddDependencySnippetAtAssignment &= !(scheduleMode == ScheduleMode.Run || scheduleMode == ScheduleMode.RunByRef);
                shouldAddDependencySnippetAtAssignment &= !(invocationExpression.Parent is MemberAccessExpressionSyntax); // Support e.g. SomeJob.Schedule().Complete()

                var dependencySnippet = $"{systemStateParameterResult.SystemStateName}.Dependency";

                systemDescription.ReplaceNodeNonNested(
                    invocationExpression,
                    SyntaxFactory.ParseExpression($"{(dependencySnippet+"=").EmitIfTrue(shouldAddDependencySnippetAtAssignment)} {name}({"ref ".EmitIfTrue(isByRef)}{jobArgumentUsedInSchedulingMethod}, {entityQuery}, {dependency ?? dependencySnippet}, ref {systemStateParameterResult.SystemStateName})"));
            }

            return true;
        }

        static string GetParameterName(IParameterSymbol parameterSymbol)
            => parameterSymbol.DeclaringSyntaxReferences.First().GetSyntax() is ParameterSyntax {Identifier: var i}
            ? i.ValueText
            : parameterSymbol.ToDisplayString();

        enum ArgumentType
        {
            EntityQuery,
            Dependency,
            JobEntity
        }

        static (string EntityQuery, string Dependency, ScheduleMode ScheduleMode) GetArguments(
            SystemDescription context, bool isExtensionMethodUsed, InvocationExpressionSyntax invocationExpression, string methodName, string defaultEntityQueryName)
        {
            var entityQueryArgument = defaultEntityQueryName;
            string dependencyArgument = null;

            var arguments = invocationExpression.ChildNodes().OfType<ArgumentListSyntax>().SelectMany(list => list.Arguments).ToArray();

            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                var namedArgument = argument.ChildNodes().OfType<NameColonSyntax>().SingleOrDefault();

                var (type, value) =
                    namedArgument == null
                        ? ParseUnnamedArgument(argument, i, isExtensionMethodUsed, methodName == "Schedule" || methodName == "ScheduleByRef", context)
                        : ParseNamedArgument(argument, namedArgument.Name.Identifier.ValueText);

                switch (type)
                {
                    case ArgumentType.EntityQuery:
                        entityQueryArgument = value;
                        continue;
                    case ArgumentType.Dependency:
                        dependencyArgument = value;
                        continue;
                }
            }

            ScheduleMode scheduleMode = methodName switch
            {
                "Run" => ScheduleMode.Run,
                "RunByRef" => ScheduleMode.RunByRef,
                "Schedule" => ScheduleMode.Schedule,
                "ScheduleByRef" => ScheduleMode.ScheduleByRef,
                "ScheduleParallel" => ScheduleMode.ScheduleParallel,
                "ScheduleParallelByRef" => ScheduleMode.ScheduleParallelByRef,
                _ => throw new ArgumentOutOfRangeException()
            };

            return (entityQueryArgument, dependencyArgument, scheduleMode);
        }

        static (ArgumentType Type, string Value) ParseNamedArgument(ArgumentSyntax argument, string argumentName)
        {
            return argumentName switch
            {
                "query" => (ArgumentType.EntityQuery, GetArgument(argument.Expression)),
                "dependsOn" => (ArgumentType.Dependency, GetArgument(argument.Expression)),
                "jobData" => (ArgumentType.JobEntity,
                    "Previously retrieved, so we are ignoring this value. We are addressing this case only for "
                    + "the purpose of avoiding the ArgumentOutOfRangeException at the end of this method."),
                _ => throw new ArgumentOutOfRangeException("The IJobEntityExtensions class does not contain methods accepting more than 4 arguments.")
            };
        }

        static (ArgumentType Type, string Value) ParseUnnamedArgument(ArgumentSyntax argument, int argumentPosition, bool isExtensionMethodUsed, bool methodOverloadAcceptsDependencyAsOnlyArgument, SystemDescription context)
        {
            switch (argumentPosition+(isExtensionMethodUsed?0:1))
            {
                case 0:
                {
                    return
                        (ArgumentType.JobEntity,
                        "Previously retrieved, so we are ignoring this value. We are addressing this case only for " +
                        "the purpose of avoiding the ArgumentOutOfRangeException at the end of this method.");
                }
                case 1: // Could be EntityQuery or dependsOn
                {
                    if (argument.Expression.IsKind(SyntaxKind.DefaultLiteralExpression))
                        return (methodOverloadAcceptsDependencyAsOnlyArgument ? ArgumentType.Dependency : ArgumentType.EntityQuery, "default");

                    var typeInfo = context.SemanticModel.GetTypeInfo(argument.Expression);
                    return
                        typeInfo.Type.ToFullName() == "Unity.Entities.EntityQuery"
                            ? (ArgumentType.EntityQuery, GetArgument(argument.Expression))
                            : (ArgumentType.Dependency, GetArgument(argument.Expression));
                }
                case 2: // dependsOn
                    return (ArgumentType.Dependency, GetArgument(argument.Expression));
            }

            throw new ArgumentOutOfRangeException("The IJobEntityExtensions class does not contain methods accepting more than 4 arguments.");
        }

        static string GetArgument(ExpressionSyntax argumentExpression) => argumentExpression switch
        {
            DefaultExpressionSyntax defaultExpressionSyntax => defaultExpressionSyntax.ToString(),
            LiteralExpressionSyntax literalExpressionSyntax => literalExpressionSyntax.Token.ValueText,
            ObjectCreationExpressionSyntax objectCreationExpressionSyntax => objectCreationExpressionSyntax.ToString(),
            IdentifierNameSyntax identifierNameSyntax => identifierNameSyntax.Identifier.ValueText,
            _ => default
        };

        static (MemberDeclarationSyntax Syntax, string Name) CreateSchedulingMethod(IReadOnlyCollection<(JobEntityParam, string)> assignments, string fullTypeName, ScheduleMode scheduleMode,
            int methodId, JobEntityDescription jobEntityDescription, SystemType systemType, bool isByRef)
        {
            var containsReturn = !(scheduleMode == ScheduleMode.Run || scheduleMode == ScheduleMode.RunByRef);

            var methodName = $"__ScheduleViaJobChunkExtension_{methodId}";

            bool hasManagedComponents = jobEntityDescription.HasManagedComponents();
            bool isRunWithoutJobs = (scheduleMode == ScheduleMode.Run || scheduleMode == ScheduleMode.RunByRef) &&
                                    hasManagedComponents;

            bool hasEntityInQueryIndex = jobEntityDescription.HasEntityInQueryIndex();
            bool needsInternalScheduleParallel =
                (scheduleMode == ScheduleMode.ScheduleParallel || scheduleMode == ScheduleMode.ScheduleParallelByRef) &&
                hasEntityInQueryIndex;
            // Certain schedule paths require .Schedule/.Run calls that aren't in the IJobChunk public API,
            // and only appear in InternalCompilerInterface
            var staticExtensionsClass = (isRunWithoutJobs || needsInternalScheduleParallel)
                ? "InternalCompilerInterface.JobChunkInterface" : "JobChunkExtensions";

            var methodAndArguments = GetMethodAndArguments(scheduleMode, hasManagedComponents, jobEntityDescription.HasEntityInQueryIndex());
            var returnType = containsReturn ? "Unity.Jobs.JobHandle" : "void";
            var returnExpression = $"{"return ".EmitIfTrue(containsReturn)}Unity.Entities.{staticExtensionsClass}.{methodAndArguments};";

            var needsEntityManager = jobEntityDescription.GetEntityManagerPermissionNeeded();

            var method =
                $@"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                    {returnType} {methodName}({"ref ".EmitIfTrue(isByRef)}{fullTypeName} job, Unity.Entities.EntityQuery entityQuery, Unity.Jobs.JobHandle dependency, ref Unity.Entities.SystemState state)
                    {{
                        {GenerateUpdateCalls(assignments).SeparateBySemicolonAndNewLine()};{Environment.NewLine}
                        {GenerateAssignments(assignments, needsEntityManager).SeparateBySemicolonAndNewLine()};{Environment.NewLine}
                        {GeneratePreHelperJob(hasEntityInQueryIndex,scheduleMode).EmitIfTrue(hasEntityInQueryIndex)}
                        {returnExpression};
                    }}";

            return (Syntax: (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(method), Name: methodName);
        }

        static string GetMethodAndArguments(ScheduleMode scheduleMode, bool hasManagedComponent, bool needsEntityInQueryIndex) =>
            scheduleMode switch
            {
                ScheduleMode.Schedule => "Schedule(job, entityQuery, dependency)",
                ScheduleMode.ScheduleByRef => "ScheduleByRef(ref job, entityQuery, dependency)",
                ScheduleMode.ScheduleParallel when needsEntityInQueryIndex => "ScheduleParallel(job, entityQuery, dependency, baseEntityIndexArray)",
                ScheduleMode.ScheduleParallel => "ScheduleParallel(job, entityQuery, dependency)",
                ScheduleMode.ScheduleParallelByRef when needsEntityInQueryIndex => "ScheduleParallelByRef(ref job, entityQuery, dependency, baseEntityIndexArray)",
                ScheduleMode.ScheduleParallelByRef => "ScheduleParallelByRef(ref job, entityQuery, dependency)",
                ScheduleMode.Run when hasManagedComponent => "RunWithoutJobs(ref job, entityQuery)",
                ScheduleMode.Run => "Run(job, entityQuery)",
                ScheduleMode.RunByRef when hasManagedComponent => "RunByRefWithoutJobs(ref job, entityQuery)",
                ScheduleMode.RunByRef => "RunByRef(ref job, entityQuery)",
                _ => throw new ArgumentOutOfRangeException()
            };

        static IEnumerable<string> GenerateUpdateCalls(IEnumerable<(JobEntityParam JobEntityFieldToAssignTo, string ComponentTypeField)> assignments)
        {
            return assignments
                .Where(assignment => assignment.JobEntityFieldToAssignTo.RequiresTypeHandleFieldInSystemBase)
                .Select(assignment => $"{assignment.ComponentTypeField}.Update(ref state)");
        }

        static IEnumerable<string> GenerateAssignments(IEnumerable<(JobEntityParam JobEntityFieldToAssignTo, string AssignmentValue)> assignments, bool needsEntityManager)
        {
            var valueTuples = assignments as (JobEntityParam JobEntityFieldToAssignTo, string AssignmentValue)[] ?? assignments.ToArray();
            if (needsEntityManager)
                yield return $"job.__EntityManager = state.EntityManager";

            foreach (var (jobEntityFieldToAssignTo, assignmentValue) in valueTuples)
            {
                if (jobEntityFieldToAssignTo.RequiresTypeHandleFieldInSystemBase)
                    yield return $"job.{jobEntityFieldToAssignTo.FieldName} = {assignmentValue}";
                else
                    yield return $"job.{jobEntityFieldToAssignTo.FieldName} = state.{assignmentValue}";
            }
        }

        static string GeneratePreHelperJob(bool hasEntityInQueryIndex, ScheduleMode scheduleMode)
        {
            if (!hasEntityInQueryIndex) return "";

            const string runReturn = @"var baseEntityIndexArray =  entityQuery.CalculateBaseEntityIndexArray(Unity.Collections.Allocator.TempJob);
           job.__ChunkBaseEntityIndices = baseEntityIndexArray;";

            const string dependReturn = @"var baseEntityIndexArray = entityQuery.CalculateBaseEntityIndexArrayAsync(Unity.Collections.Allocator.TempJob, dependency, out var indexDependency);
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

    }
}
