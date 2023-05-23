using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGenerator.Common;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    public class LambdaJobsModule : ISystemModule
    {
        public bool RequiresReferenceToBurst { get; private set; }

        public IEnumerable<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> Candidates
        {
            get
            {
                var allCandidates = new List<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)>();
                allCandidates.AddRange(EntitiesForEachCandidates.Select(candidate =>
                    (SyntaxNode: candidate.Node, candidate.ContainingSystemType)));
                allCandidates.AddRange(JobWithCodeCandidates.Select(candidate =>
                    (SyntaxNode: candidate.Node, candidate.ContainingSystemType)));
                return allCandidates;
            }
        }

        List<LambdaJobsCandidate> EntitiesForEachCandidates { get; } = new List<LambdaJobsCandidate>();
        List<LambdaJobsCandidate> JobWithCodeCandidates { get; } = new List<LambdaJobsCandidate>();

        public void OnReceiveSyntaxNode(SyntaxNode node)
        {
            switch (node)
            {
                case IdentifierNameSyntax identifierNameSyntax
                    when identifierNameSyntax.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                {
                    if (identifierNameSyntax.Identifier.Text != "Entities" &&
                        identifierNameSyntax.Identifier.Text != "Job")
                        break;

                    var methodInvocations = node.GetMethodInvocations();

                    var jobKind = identifierNameSyntax.Identifier.Text switch
                    {
                        "Entities" => LambdaJobKind.Entities,
                        "Job" => LambdaJobKind.Job,
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    var methodInvocationName = jobKind switch
                    {
                        LambdaJobKind.Entities => "ForEach",
                        LambdaJobKind.Job => "WithCode",
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    if (methodInvocations.ContainsKey(methodInvocationName))
                    {
                        var isNestedInvocation = methodInvocations[methodInvocationName].Count > 1;
                        if (isNestedInvocation) break;

                        var candidateList = jobKind switch
                        {
                            LambdaJobKind.Entities => EntitiesForEachCandidates,
                            LambdaJobKind.Job => JobWithCodeCandidates,
                            _ => throw new ArgumentOutOfRangeException()
                        };

                        candidateList.Add(
                            new LambdaJobsCandidate
                            {
                                Node = node,
                                ContainingSystemType = node.AncestorOfKind<TypeDeclarationSyntax>(),
                                MethodInvocations = methodInvocations,
                                LambdaJobKind = jobKind
                            });
                    }

                    break;
                }
            }
        }

        public bool RegisterChangesInSystem(SystemDescription systemDescription)
        {
            var lambdaJobDescriptions = new List<LambdaJobDescription>();

            var candidatesGroupedBySystemType =
                EntitiesForEachCandidates
                    .Concat(JobWithCodeCandidates)
                    .GroupBy(candidate => candidate.ContainingSystemType)
                    .ToDictionary(g => g.Key, g => g.ToArray());

            foreach (var lambdaJobCandidate in candidatesGroupedBySystemType[systemDescription.SystemTypeSyntax])
            {
                var lambdaJobDescription =
                    new LambdaJobDescription(systemDescription, lambdaJobCandidate,
                        lambdaJobCandidate.Node.AncestorOfKind<MethodDeclarationSyntax>(),
                        lambdaJobDescriptions.Count);

                if (lambdaJobDescription.Burst.IsEnabled)
                    RequiresReferenceToBurst = true;

                if (lambdaJobDescription.Success) lambdaJobDescriptions.Add(lambdaJobDescription);
            }

            // Check to make sure we have no systems with duplicate names
            var descriptionsWithDuplicateNames =
                lambdaJobDescriptions.GroupBy(desc => desc.Name).Where(g => g.Count() > 1);

            foreach (var descriptionWithDuplicateName in descriptionsWithDuplicateNames)
            {
                LambdaJobsErrors.DC0003(systemDescription, descriptionWithDuplicateName.First().Location, descriptionWithDuplicateName.First().Name);
                return false;
            }

            foreach (var lambdaJobDescription in lambdaJobDescriptions)
            {
                if (lambdaJobDescription.LambdaJobKind == LambdaJobKind.Entities)
                {
                    lambdaJobDescription.EntityQueryFieldName =
                        systemDescription.HandlesDescription.GetOrCreateQueryField(
                            new SingleArchetypeQueryFieldDescription(
                                new Archetype(
                                    lambdaJobDescription.LambdaParameters
                                        .Where(param => param is IParamDescription && param.IsQueryableType)
                                        .Select(param =>
                                            new Query
                                            {
                                                TypeSymbol = param.EntityQueryTypeSymbol,
                                                Type = QueryType.All,
                                                IsReadOnly = param.QueryTypeIsReadOnly()
                                            })
                                        .Concat(lambdaJobDescription.WithAllTypes)
                                        .Concat(lambdaJobDescription.WithSharedComponentFilterTypes)
                                        .ToArray(),
                                    lambdaJobDescription.WithAnyTypes,
                                    lambdaJobDescription.WithNoneTypes,
                                    lambdaJobDescription.WithDisabledTypes,
                                    lambdaJobDescription.WithAbsentTypes,
                                    lambdaJobDescription.EntityQueryOptions),
                                lambdaJobDescription.WithChangeFilterTypes,
                                queryStorageFieldName: lambdaJobDescription.WithStoreEntityQueryInFieldArgumentSyntaxes.FirstOrDefault()?.Expression.ToString())
                            );

                    foreach (var lambdaParameter in lambdaJobDescription.LambdaParameters.OfType<IParamRequireUpdate>())
                    {
                        lambdaParameter.FieldName = lambdaParameter switch
                        {
                            LambdaParamDescription_Entity _ => systemDescription.HandlesDescription.GetOrCreateEntityTypeHandleField(),
                            LambdaParamDescription_DynamicBuffer dynamicBuffer => systemDescription.HandlesDescription.GetOrCreateTypeHandleField(dynamicBuffer.EntityQueryTypeSymbol, lambdaParameter.IsReadOnly),
                            _ => systemDescription.HandlesDescription.GetOrCreateTypeHandleField(lambdaParameter.TypeSymbol, lambdaParameter.IsReadOnly)
                        };
                    }

                    if (lambdaJobDescription.EntityCommandBufferParameter is { Playback: { SystemType: { } } })
                        lambdaJobDescription.EntityCommandBufferParameter.GeneratedEcbFieldNameInSystemBaseType =
                            systemDescription.GetOrCreateEntityCommandBufferSystemField(lambdaJobDescription.EntityCommandBufferParameter.Playback.SystemType);
                }

                foreach (var lambdaParameter in lambdaJobDescription.AdditionalFields)
                {
                    switch (lambdaParameter.AccessorDataType)
                    {
                        case LambdaJobsPatchableMethod.AccessorDataType.ComponentLookup:
                            systemDescription.HandlesDescription.GetOrCreateComponentLookupField(
                                lambdaParameter.Type,
                                lambdaParameter.IsReadOnly);
                            break;
                        case LambdaJobsPatchableMethod.AccessorDataType.BufferLookup:
                            systemDescription.HandlesDescription.GetOrCreateBufferLookupField(
                                lambdaParameter.Type,
                                lambdaParameter.IsReadOnly);
                            break;
                        case LambdaJobsPatchableMethod.AccessorDataType.AspectLookup:
                            systemDescription.HandlesDescription.GetOrCreateAspectLookup(
                                lambdaParameter.Type,
                                lambdaParameter.IsReadOnly);
                            break;
                        case LambdaJobsPatchableMethod.AccessorDataType.EntityStorageInfoLookup:
                            systemDescription.HandlesDescription.GetOrCreateEntityStorageInfoLookupField();
                            break;
                    }
                }

                systemDescription.NewMiscellaneousMembers.Add(EntitiesSourceFactory.LambdaJobs.JobStructFor(lambdaJobDescription));

                if (lambdaJobDescription.NeedsJobFunctionPointers)
                {
                    systemDescription.AdditionalStatementsInOnCreateForCompilerMethod.Add(
                        $"{lambdaJobDescription.JobStructName}.FunctionPtrFieldNoBurst = {lambdaJobDescription.JobStructName}.RunWithoutJobSystem;");
                    if (lambdaJobDescription.Burst.IsEnabled)
                    {
                        systemDescription.AdditionalStatementsInOnCreateForCompilerMethod.Add(
                            $"{lambdaJobDescription.JobStructName}.FunctionPtrFieldBurst = Unity.Entities.Internal.InternalCompilerInterface.BurstCompile({lambdaJobDescription.JobStructName}.FunctionPtrFieldNoBurst);");
                    }
                }

                if (lambdaJobDescription.EntityCommandBufferParameter is { Playback: { IsImmediate: false } })
                {
                    systemDescription.AdditionalStatementsInOnCreateForCompilerMethod.Add(
                        $"{lambdaJobDescription.EntityCommandBufferParameter.GeneratedEcbFieldNameInSystemBaseType} = " +
                        $"World.GetOrCreateSystemManaged<{lambdaJobDescription.EntityCommandBufferParameter.Playback.SystemType.ToFullName()}>();");
                }

                systemDescription.NewMiscellaneousMembers.Add(
                    EntitiesSourceFactory.LambdaJobs.CreateExecuteMethod(lambdaJobDescription));
            }

            // Go through all methods containing descriptions and register syntax replacements with SystemGeneratorContext
	        foreach (var methodDeclarationSyntax in systemDescription.SystemTypeSyntax.DescendantNodes().OfType<MethodDeclarationSyntax>())
	        {
	            var lambdaJobDescriptionsInMethods = lambdaJobDescriptions
	                .Where(desc => desc.ContainingMethod == methodDeclarationSyntax).ToArray();

                if (!lambdaJobDescriptionsInMethods.Any())
	                continue;

	            // Replace original invocation expressions for scheduling with replacement syntax
	            foreach (var lambdaJobDescriptionInMethod in lambdaJobDescriptionsInMethods)
                    systemDescription.ReplaceNodeNonNested(lambdaJobDescriptionInMethod.ContainingInvocationExpression, EntitiesSourceFactory.Common.SchedulingInvocationFor(lambdaJobDescriptionInMethod));
            }
            return true;
        }
    }
}
