using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    public enum SystemType
    {
        SystemBase,
        ISystem
    }

    public class LambdaJobsModule : ISystemModule
    {
        public bool ShouldRun(ParseOptions parseOptions) => true;
        public bool RequiresReferenceToBurst { get; private set; }

        public IEnumerable<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> Candidates
        {
            get
            {
                var allCandidates = new List<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)>();
                allCandidates.AddRange(EntitiesForEachCandidates.Select(candidate => (candidate.SyntaxNode, candidate.ContainingSystemType)));
                allCandidates.AddRange(JobWithCodeCandidates.Select(candidate => (candidate.SyntaxNode, candidate.ContainingSystemType)));
                allCandidates.AddRange(SingletonAccessCandidates.Select(candidate => (candidate.SyntaxNode, candidate.ContainingSystemType)));
                return allCandidates;
            }
        }

        List<LambdaJobsCandidate> EntitiesForEachCandidates { get; } = new List<LambdaJobsCandidate>();
        List<LambdaJobsCandidate> JobWithCodeCandidates { get; } = new List<LambdaJobsCandidate>();
        List<ICandidate> SingletonAccessCandidates { get; } = new List<ICandidate>();

        static SingletonAccessType GetSingletonAccessType(InvocationExpressionSyntax syntax)
        {
            switch (syntax.Expression)
            {
                case GenericNameSyntax genericNameSyntax:
                    switch (genericNameSyntax.Identifier.ValueText)
                    {
                        case "GetSingleton":
                            return SingletonAccessType.GetSingleton;
                        case "GetSingletonEntity":
                            return SingletonAccessType.GetSingletonEntity;
                        default:
                            return SingletonAccessType.None;
                    }
                case IdentifierNameSyntax identifierNameSyntax:
                    return identifierNameSyntax.Identifier.ValueText == "SetSingleton" ? SingletonAccessType.Set : SingletonAccessType.None;
                default:
                    return SingletonAccessType.None;
            }
        }

        public void OnReceiveSyntaxNode(SyntaxNode node)
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocationExpressionSyntax:
                {
                    var singletonAccessType = GetSingletonAccessType(invocationExpressionSyntax);
                    if (singletonAccessType != SingletonAccessType.None)
                    {
                        SingletonAccessCandidates.Add(
                            new SingletonAccessCandidate
                            {
                                SyntaxNode = node,
                                ContainingSystemType = node.Ancestors().OfType<TypeDeclarationSyntax>().First(),
                                SingletonAccessType = singletonAccessType
                            });
                    }
                }
                break;

                case IdentifierNameSyntax identifierNameSyntax
                    when identifierNameSyntax.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                {
                    if (identifierNameSyntax.Identifier.Text != "Entities" && identifierNameSyntax.Identifier.Text != "Job")
                        break;

                    var methodInvocations = node.GetMethodInvocations();

                    switch (identifierNameSyntax.Identifier.Text)
                    {
                        case "Entities" when methodInvocations.ContainsKey("ForEach"):
                            {
                                var isNestedForEachInvocation = methodInvocations["ForEach"].Count > 1;
                                if (isNestedForEachInvocation)
                                {
                                    break;
                                }

                                EntitiesForEachCandidates.Add(
                                    new LambdaJobsCandidate
                                    {
                                        SyntaxNode = node,
                                        ContainingSystemType = node.Ancestors().OfType<TypeDeclarationSyntax>().First(),
                                        MethodInvocations = methodInvocations,
                                        LambdaJobKind = LambdaJobKind.Entities
                                    });
                            }
                            break;

                        case "Job" when methodInvocations.ContainsKey("WithCode"):
                            JobWithCodeCandidates.Add(
                                new LambdaJobsCandidate
                                {
                                    SyntaxNode = node,
                                    ContainingSystemType = node.Ancestors().OfType<TypeDeclarationSyntax>().First(),
                                    MethodInvocations = methodInvocations,
                                    LambdaJobKind = LambdaJobKind.Job
                                });
                            break;
                    }
                    break;
                }
            }
        }

        public bool GenerateSystemType(SystemGeneratorContext systemGeneratorContext)
        {
            var lambdaJobDescriptions = new List<LambdaJobDescription>();
            var singletonAccessDescriptions = new List<SingletonAccessDescription>();

            var lambdaJobsIndex = 0;
            foreach (var lambdaJobCandidate in EntitiesForEachCandidates.Where(candidate => candidate.ContainingSystemType == systemGeneratorContext.SystemType)
                .Concat(JobWithCodeCandidates.Where(candidate => candidate.ContainingSystemType == systemGeneratorContext.SystemType)))
            {
                var lambdaJobDescription = new LambdaJobDescription(systemGeneratorContext, lambdaJobCandidate, lambdaJobCandidate.ContainingSystemType,
                    lambdaJobCandidate.SyntaxNode.Ancestors().OfType<MethodDeclarationSyntax>().First(), systemGeneratorContext.SemanticModel, lambdaJobsIndex);

                if (lambdaJobDescription.Burst.IsEnabled)
                    RequiresReferenceToBurst = true;

                if (lambdaJobDescription.Success)
                {
                    lambdaJobDescriptions.Add(lambdaJobDescription);
                    lambdaJobsIndex++;
                }
            }

            foreach (var singletonAccess in SingletonAccessCandidates.Where(candidate => candidate.ContainingSystemType == systemGeneratorContext.SystemType))
            {
                var singletonAccessCandidate = (SingletonAccessCandidate)singletonAccess;
                var singletonAccessDescription = new SingletonAccessDescription(singletonAccessCandidate, systemGeneratorContext.SemanticModel);

                if (singletonAccessDescription.Success)
                {
                    singletonAccessDescriptions.Add(singletonAccessDescription);
                }
            }

            // Check to make sure we have no systems with duplicate names
            var descriptionsWithDuplicateNames = lambdaJobDescriptions.GroupBy(desc => desc.Name).Where(g => g.Count() > 1);
            foreach (var descriptionWithDuplicateName in descriptionsWithDuplicateNames)
            {
                LambdaJobsErrors.DC0003(systemGeneratorContext, descriptionWithDuplicateName.First().Location, descriptionWithDuplicateName.First().Name);
                return false;
            }

            foreach (var lambdaJobDescription in lambdaJobDescriptions)
            {
                if (lambdaJobDescription.LambdaJobKind == LambdaJobKind.Entities)
                {
                    systemGeneratorContext.CreateUniqueQueryField(
                        new EntityQueryDescription(
                            lambdaJobDescription.LambdaParameters
                                .Where(param => param is IParamDescription)
                                .Select(param => ((INamedTypeSymbol)(param as IParamDescription).EntityQueryTypeSymbol, param.QueryTypeIsReadOnly()))
                                .Concat(lambdaJobDescription.WithAllTypes.Select(typeSymbol => (typeSymbol, true)))
                                .Concat(lambdaJobDescription.WithSharedComponentFilterTypes.Select(symbol => (symbol, true))).ToArray(),
                            lambdaJobDescription.WithAnyTypes.Select(typeSymbol => (typeSymbol, true)).ToArray(),
                            lambdaJobDescription.WithNoneTypes.Select(typeSymbol => (typeSymbol, true)).ToArray(),
                            lambdaJobDescription.WithChangeFilterTypes.Select(typeSymbol => (typeSymbol, false)).ToArray(),
                            lambdaJobDescription.EntityQueryOptions, lambdaJobDescription.WithStoreEntityQueryInFieldArgumentSyntaxes.FirstOrDefault()?.Expression.ToString()),
                        lambdaJobDescription.EntityQueryFieldName);

                    foreach (var lambdaParameter in lambdaJobDescription.LambdaParameters.Where(param => param.ComponentTypeHandleFieldDeclaration() != null))
                        lambdaParameter.ComponentTypeHandleFieldName = systemGeneratorContext.GetOrCreateComponentTypeField(lambdaParameter.TypeSymbol, lambdaParameter.QueryTypeIsReadOnly());
                }

                systemGeneratorContext.NewMembers.Add(EntitiesSourceFactory.LambdaJobs.JobStructFor(lambdaJobDescription));

                if (lambdaJobDescription.WithStructuralChangesAndLambdaBodyInSystem)
                    systemGeneratorContext.NewMembers.Add(EntitiesSourceFactory.LambdaJobs.LambdaBodyMethodFor(lambdaJobDescription));

                if (lambdaJobDescription.NeedsJobFunctionPointers)
                {
                    string delegateName = lambdaJobDescription.LambdaJobKind switch
                    {
                        LambdaJobKind.Job => "Unity.Entities.InternalCompilerInterface.JobRunWithoutJobSystemDelegate",
                        LambdaJobKind.Entities when lambdaJobDescription.WithFilterEntityArray != null => "Unity.Entities.InternalCompilerInterface.JobEntityBatchRunWithoutJobSystemDelegateLimitEntities",
                        LambdaJobKind.Entities => "Unity.Entities.InternalCompilerInterface.JobEntityBatchRunWithoutJobSystemDelegate",
                        _ => string.Empty
                    };

                    if (lambdaJobDescription.InStructSystem)
                    {
                        systemGeneratorContext.AddOnCreateForCompilerSyntax(
                            @$"{lambdaJobDescription.JobStructName}.FunctionPtrFieldNoBurst.Data =
                                new Unity.Burst.FunctionPointer<{delegateName}>(
                                    global::System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(({delegateName}){lambdaJobDescription.JobStructName}.RunWithoutJobSystem));");
                        if (lambdaJobDescription.Burst.IsEnabled)
                        {
                            systemGeneratorContext.AddOnCreateForCompilerSyntax(
                                @$"{lambdaJobDescription.JobStructName}.FunctionPtrFieldBurst.Data = Unity.Burst.BurstCompiler
                                .CompileFunctionPointer<{delegateName}>({lambdaJobDescription.JobStructName}.RunWithoutJobSystem);");
                        }
                    }
                    else
                    {
                        systemGeneratorContext.AddOnCreateForCompilerSyntax(
                            $"{lambdaJobDescription.JobStructName}.FunctionPtrFieldNoBurst = {lambdaJobDescription.JobStructName}.RunWithoutJobSystem;");
                        if (lambdaJobDescription.Burst.IsEnabled)
                        {
                            systemGeneratorContext.AddOnCreateForCompilerSyntax(
                                $"{lambdaJobDescription.JobStructName}.FunctionPtrFieldBurst = Unity.Entities.InternalCompilerInterface.BurstCompile({lambdaJobDescription.JobStructName}.FunctionPtrFieldNoBurst);");
                        }
                    }
                }

                systemGeneratorContext.NewMembers.Add(EntitiesSourceFactory.LambdaJobs.CreateExecuteMethod(lambdaJobDescription));
            }

            foreach (var singletonAccessDescription in singletonAccessDescriptions)
            {
                singletonAccessDescription.EntityQueryFieldName = systemGeneratorContext.GetOrCreateQueryField(new EntityQueryDescription()
                {
                    All = new (INamedTypeSymbol typeInfo, bool isReadOnly)[]
                        {
                            (singletonAccessDescription.SingletonType, singletonAccessDescription.AccessType != SingletonAccessType.Set)
                        }
                });
            }

            if (singletonAccessDescriptions.Any(desc => desc.ContainedIn == SingletonAccessDescription.ContainingType.Property))
            {
                foreach (var propertyDeclarationSyntax in systemGeneratorContext.SystemType.DescendantNodes().OfType<PropertyDeclarationSyntax>())
                {
                    var singletonDescriptionsInProperty =
                        singletonAccessDescriptions.Where(desc => desc.ContainingProperty == propertyDeclarationSyntax);

                    foreach (var description in singletonDescriptionsInProperty)
                    {
                        systemGeneratorContext.ReplaceNodeInProperty(description.ContainingProperty, description.OriginalNode, description.GenerateReplacementNode());
                    }
                }
            }

            // Go through all methods containing descriptions and register syntax replacements with SystemGeneratorContext
            foreach (var methodDeclarationSyntax in systemGeneratorContext.SystemType.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var lambdaJobDescriptionsInMethods = lambdaJobDescriptions.Where(desc => desc.ContainingMethod == methodDeclarationSyntax).ToArray();
                var singletonDescriptionsInMethods = singletonAccessDescriptions.Where(desc => desc.ContainingMethod == methodDeclarationSyntax).ToArray();

                if (!lambdaJobDescriptionsInMethods.Any() && !singletonDescriptionsInMethods.Any())
                    continue;

                foreach (var lambdaJobDescriptionInMethod in lambdaJobDescriptionsInMethods)
                {
                    // Replace original invocation expressions for scheduling with replacement syntax
                    systemGeneratorContext.ReplaceNodeInMethod(lambdaJobDescriptionInMethod.ContainingInvocationExpression,
                        EntitiesSourceFactory.Common.SchedulingInvocationFor(lambdaJobDescriptionInMethod));

                    // Also need to replace local functions that are used in the lambda but nowhere else
                    foreach (var localMethodUsedOnlyInLambda in lambdaJobDescriptionInMethod.LocalFunctionUsedInLambda.Where(tuple => tuple.onlyUsedInLambda))
                        systemGeneratorContext.ReplaceNodeInMethod(localMethodUsedOnlyInLambda.localFunction, null);
                }

                // Replace singleton access methods with optimized version through saved EntityQuery
                foreach (var singletonDescriptionInMethod in singletonDescriptionsInMethods)
                    systemGeneratorContext.ReplaceNodeInMethod(singletonDescriptionInMethod.OriginalNode, singletonDescriptionInMethod.GenerateReplacementNode());
            }

            return true;
        }
    }
}
