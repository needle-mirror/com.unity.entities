using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

/*
    JobEntityGenerator and JobEntityModule (A System Generator Module) work together to provide the IJobEntity feature.

    For an explanation of the feature, see: https://unity.slack.com/archives/CQ811KGJJ/p1620212365031500

    -- JobEntityGenerator --
    A user writes an IJobEntity with `partial`:

    public partial struct MyJob : IJobEntity
    {
        public void Execute(ref Translation translation, in Velocity velocity)
        {
            ...
        }
    }

    The **JobEntityGenerator** will generate code that extends MyJob into a working IJobEntityBatch:

    //Generated
    public partial struct MyJob : IJobEntity, IJobEntityBatch
    {
        ComponentTypeHandle<Translation> __TranslationTypeHandle;
        [ReadOnly]
        ComponentTypeHandle<Velocity> __VelocityTypeHandle;

        public void Execute(ArchetypeChunk batch, int batchIndex)
        {
            var translationData = UnsafeGetChunkNativeArrayIntPtr<Rotation>(batch, __TranslationTypeHandle);
            var velocityData  = UnsafeGetChunkNativeArrayIntPtr<Rotation>(batch, __VelocityTypeHandle);
            int count = batch.Count;
            for (int i = 0; i < count; ++i)
            {
                ref var translationData__ref = ref UnsafeGetRefToNativeArrayPtrElement<Translation>(translationData, i);
                ref var velocityData__ref = ref UnsafeGetRefToNativeArrayPtrElement<Velocity>(velocityData, i);
                Execute(ref translationData__ref, in velocityData__ref);
            }
        }
    }

    -- JobEntityModule --
    A user wants to create and schedule an IJobEntity, so after writing the above struct they write this in a System:
    public partial MySystem : SystemBase
    {
        public void OnUpdate()
        {
            var myJob = new MyJob();
            Dependency = myJob.Schedule(Dependency);
        }
    }

    In this case, **JobEntityModule** will generate changes to the System to allow this generated job to be scheduled normally:

    // Generated
    public partial class MySystem : SystemBase
    {
        protected void __OnUpdate_2C361387()
        {
            var myJob = new MyJob();
            Dependency = __ScheduleViaJobEntityBatchExtension_0(myJob, __query_0, 1, Dependency);
        }

        public JobHandle __ScheduleViaJobEntityBatchExtension_0(MyJob job, EntityQuery entityQuery, int batchesPerChunk, JobHandle dependency)
        {
            Unity_Transforms_Translation_RW_ComponentTypeHandle.Update(this);
            Velocity_RO_ComponentTypeHandle.Update(this);
            job.__TranslationTypeHandle = Unity_Transforms_Translation_RW_ComponentTypeHandle;
            job.__VelocityTypeHandle = Velocity_RO_ComponentTypeHandle;
            return JobEntityBatchExtensions.Schedule(job, entityQuery, dependency);
        }
    }

    This is why we have two different generators. One is about generating the extension to your job struct and the other is generating the new callsite in a system.
    JobEntityGenerator- extending Job struct (once per struct)
    JobEntityModule- extending callsite in System (once per Job invocation in a system)
*/

namespace Unity.Entities.SourceGen.JobEntity
{
    public class JobEntityModule : ISystemModule
    {
        List<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> m_Candidates = new List<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)>();
        Dictionary<TypeDeclarationSyntax, List<MemberAccessExpressionSyntax>> m_JobEntityInvocationCandidates = new Dictionary<TypeDeclarationSyntax, List<MemberAccessExpressionSyntax>>();
        List<TypeDeclarationSyntax> nonPartialJobEntityTypes = new List<TypeDeclarationSyntax>();

        public IEnumerable<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> Candidates => m_Candidates;
        public bool RequiresReferenceToBurst => false;

        enum ScheduleMode
        {
            Schedule,
            ScheduleByRef,
            ScheduleParallel,
            ScheduleParallelByRef,
            Run,
            RunByRef
        }

        enum ExtensionType
        {
            Batch,
            BatchIndex
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
                    var containingType = node.Ancestors().OfType<TypeDeclarationSyntax>().First();

                    // Discard if no base type, meaning it can't possible inherit from a System
                    if (containingType.BaseList == null || containingType.BaseList.Types.Count == 0)
                        return;
                    m_JobEntityInvocationCandidates.Add(containingType, memberAccessExpressionSyntax);
                    m_Candidates.Add((node, containingType));
                }
            }
        }

        static (bool IsCandidate, bool IsExtensionMethodUsed) IsIJobEntityCandidate(TypeInfo typeInfo)
        {
            if (typeInfo.Type.InheritsFromInterface("Unity.Entities.IJobEntity"))
            {
                return (IsCandidate: typeInfo.Type.InheritsFromInterface("Unity.Entities.IJobEntity"), IsExtensionMethodUsed: false);
            }

            // IsExtensionMethodUsed is ignored if IsCandidate is false, so we don't need to test the same thing twice
            return (IsCandidate: typeInfo.Type.ToFullName() == "Unity.Entities.IJobEntityExtensions", IsExtensionMethodUsed: true);
        }

        static (ExpressionSyntax Argument, INamedTypeSymbol Symbol)
            GetJobEntitySymbolPassedToExtensionMethod(MemberAccessExpressionSyntax candidate, SemanticModel semanticModel)
        {
            var arguments = candidate.Ancestors().OfType<InvocationExpressionSyntax>().First()
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

        public bool GenerateSystemType(SystemGeneratorContext context)
        {
            foreach (var nonPartialType in nonPartialJobEntityTypes)
                JobEntityGeneratorErrors.SGJE0004(context, nonPartialType.GetLocation(), nonPartialType.Identifier.ValueText);

            var candidates = m_JobEntityInvocationCandidates[context.SystemType];

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                ExpressionSyntax jobEntityInstance = candidate.Expression is IdentifierNameSyntax identifierNameSyntax ? identifierNameSyntax : null;
                jobEntityInstance ??= candidate.Expression as ObjectCreationExpressionSyntax;

                var typeInfo = context.SemanticModel.GetTypeInfo(jobEntityInstance);
                var (isCandidate, isExtensionMethodUsed) = IsIJobEntityCandidate(typeInfo);

                if (!isCandidate)
                    continue;

                if (typeInfo.Type?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is TypeDeclarationSyntax typeDeclarationSyntax)
                {
                    bool isPartial = typeDeclarationSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
                    if (!isPartial)
                    {
                        JobEntityGeneratorErrors.SGJE0004(context, typeDeclarationSyntax.GetLocation(), typeDeclarationSyntax.Identifier.ValueText);
                        continue;
                    }
                }

                var (jobEntityType, jobArgumentUsedInSchedulingMethod) =
                    GetIJobEntityTypeDeclarationAndArgument(candidate, context.SemanticModel, typeInfo, isExtensionMethodUsed);

                var jobEntityDesc = new JobEntityDescription(jobEntityType, context);
                if (!jobEntityDesc.Valid) continue;

                var queryTypes = new List<(INamedTypeSymbol, bool)>();
                var jobEntityAssignments = new List<(JobEntityParam, string)>();

                foreach (var param in jobEntityDesc.UserExecuteMethodParams)
                {
                    if(param.IsQueryableType)
                        queryTypes.Add(((INamedTypeSymbol)param.TypeSymbol, param.IsReadOnly));

                    // Managed types do not use
                    if (!param.RequiresTypeHandleFieldInSystemBase)
                    {
                        if (string.IsNullOrEmpty(param.JobEntityFieldAssignment))
                            continue;
                        jobEntityAssignments.Add((param, param.JobEntityFieldAssignment));
                        continue;
                    }

                    var componentTypeField = context.GetOrCreateComponentTypeField(param.TypeSymbol, param.IsReadOnly);
                    jobEntityAssignments.Add((param, componentTypeField));
                }

                var generatedQueryField = context.GetOrCreateQueryField(new EntityQueryDescription(
                        queryTypes.Concat(jobEntityDesc.QueryAllTypes).ToArray(),
                        jobEntityDesc.QueryAnyTypes.ToArray(),
                        jobEntityDesc.QueryNoneTypes.ToArray(),
                        jobEntityDesc.QueryChangeFilterTypes.ToArray(),
                        jobEntityDesc.EntityQueryOptions));

                var invocationExpression = candidate.Parent as InvocationExpressionSyntax;
                var (entityQuery, dependency, scheduleMode) = GetArguments(context, isExtensionMethodUsed, invocationExpression, candidate.Name.Identifier.ValueText, generatedQueryField);

                var (syntax, name) = CreateSchedulingMethod(jobEntityAssignments, jobEntityDesc.FullTypeName, scheduleMode,
                    i, jobEntityDesc.HasEntityInQueryIndex() ? ExtensionType.BatchIndex : ExtensionType.Batch, jobEntityDesc);

                context.NewMembers.Add(syntax);

                // Generate code for callsite
                var shouldAddDependencySnippet = dependency == null;
                shouldAddDependencySnippet &= !(scheduleMode == ScheduleMode.Run || scheduleMode == ScheduleMode.RunByRef);
                shouldAddDependencySnippet &= !(invocationExpression.Parent is MemberAccessExpressionSyntax); // Support e.g. SomeJob.Schedule().Complete()

                context.ReplaceNodeInMethod(invocationExpression,
                    SyntaxFactory.ParseExpression($"{"Dependency =".EmitIfTrue(shouldAddDependencySnippet)} {name}({jobArgumentUsedInSchedulingMethod}, {entityQuery}, {dependency ?? "Dependency"})"));
            }

            return true;
        }

        public bool ShouldRun(ParseOptions parseOptions) => true;

        enum ArgumentType
        {
            EntityQuery,
            Dependency,
            JobEntity
        }

        static (string EntityQuery, string Dependency, ScheduleMode ScheduleMode) GetArguments(
            SystemGeneratorContext context, bool isExtensionMethodUsed, InvocationExpressionSyntax invocationExpression, string methodName, string defaultEntityQueryName)
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

        static (ArgumentType Type, string Value) ParseUnnamedArgument(ArgumentSyntax argument, int argumentPosition, bool isExtensionMethodUsed, bool methodOverloadAcceptsDependencyAsOnlyArgument, SystemGeneratorContext context)
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

        static (MemberDeclarationSyntax Syntax, string Name) CreateSchedulingMethod(
            IReadOnlyCollection<(JobEntityParam, string)> assignments, string fullTypeName, ScheduleMode scheduleMode, int methodId, ExtensionType extensionType, JobEntityDescription jobEntityDescription)
        {
            var containsReturn = !(scheduleMode == ScheduleMode.Run || scheduleMode == ScheduleMode.RunByRef);

            var methodName = extensionType switch
            {
                ExtensionType.BatchIndex => $"__ScheduleViaJobEntityBatchIndexExtension_{methodId}",
                ExtensionType.Batch => $"__ScheduleViaJobEntityBatchExtension_{methodId}",
                _ => throw new ArgumentOutOfRangeException(nameof(extensionType), extensionType, null)
            };

            var staticExtensionsClass = extensionType switch
            {
                ExtensionType.BatchIndex => "JobEntityBatchIndexExtensions",
                ExtensionType.Batch => "JobEntityBatchExtensions",
                _ => throw new ArgumentOutOfRangeException(nameof(extensionType), extensionType, null)
            };

            var methodAndArguments = GetMethodAndArguments(scheduleMode, jobEntityDescription.HasManagedComponents());
            var returnType = containsReturn ? "Unity.Jobs.JobHandle" : "void";
            var returnExpression = $"{"return ".EmitIfTrue(containsReturn)}Unity.Entities.{staticExtensionsClass}.{methodAndArguments};";

            var method =
                $@"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                    {returnType} {methodName}({fullTypeName} job, Unity.Entities.EntityQuery entityQuery, Unity.Jobs.JobHandle dependency)
                    {{
                        {GenerateUpdateCalls(assignments).SeparateBySemicolonAndNewLine()};{Environment.NewLine}
                        {GenerateAssignments(assignments).SeparateBySemicolonAndNewLine()};{Environment.NewLine}
                        {returnExpression};
                    }}";

            return (Syntax: (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(method), Name: methodName);
        }

        static string GetMethodAndArguments(ScheduleMode scheduleMode, bool hasManagedComponent) =>
            scheduleMode switch
            {
                ScheduleMode.Schedule => "Schedule(job, entityQuery, dependency)",
                ScheduleMode.ScheduleByRef => "ScheduleByRef(ref job, entityQuery, dependency)",
                ScheduleMode.ScheduleParallel => "ScheduleParallel(job, entityQuery, dependency)",
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
                .Select(assignment => $"{assignment.ComponentTypeField}.Update(this)");
        }

        static IEnumerable<string> GenerateAssignments(IEnumerable<(JobEntityParam JobEntityFieldToAssignTo, string AssignmentValue)> assignments)
        {
            var valueTuples = assignments as (JobEntityParam JobEntityFieldToAssignTo, string AssignmentValue)[] ?? assignments.ToArray();
            if (valueTuples.Any(assignment => assignment.JobEntityFieldToAssignTo.RequiresEntityManagerAccess))
                yield return "job.__EntityManager = EntityManager";

            foreach (var (jobEntityFieldToAssignTo, assignmentValue) in valueTuples)
                yield return $"job.{jobEntityFieldToAssignTo.FieldName} = {assignmentValue}";
        }
    }
}
