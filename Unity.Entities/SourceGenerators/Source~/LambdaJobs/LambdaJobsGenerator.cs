using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    [Generator]
    public class LambdaJobsGenerator : ISourceGenerator
    {
        List<IdentifierNameSyntax> FilterCandidates(GeneratorExecutionContext context, IEnumerable<IdentifierNameSyntax> candidates)
        {
            var filteredCandidates = new List<IdentifierNameSyntax>();

            foreach (var candidate in candidates)
            {
                var model = context.Compilation.GetSemanticModel(candidate.SyntaxTree);
                var containingClassDeclaration = candidate.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (containingClassDeclaration != null)
                {
                    var candidateContainingClassSymbol = model.GetDeclaredSymbol(containingClassDeclaration);
                    if (candidateContainingClassSymbol != null &&
                        (candidateContainingClassSymbol.Is("Unity.Entities.SystemBase") || candidateContainingClassSymbol.Is("Unity.Entities.JobComponentSystem")))
                    {
                        if (!containingClassDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                        {
                            var lambdaJobName = "Entities.ForEach";
                            if (candidate.Identifier.ToString() == "Job")
                                lambdaJobName = "Job.WithCode";
                            context.LogError("SG0001", "LambdaJobs",
                                $"{lambdaJobName} is in system {candidateContainingClassSymbol.Name}, but {candidateContainingClassSymbol.Name} is not defined with partial.  Source generated lambda jobs must exist in a partial system class.  Please add the `partial` keyword as part of the class definition.",
                                candidate.GetLocation());
                        }
                        else
                            filteredCandidates.Add(candidate);
                    }
                }
            }

            return filteredCandidates;
        }

        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                if (context.Compilation.ReferencedAssemblyNames.All(n => n.Name != "Unity.Entities") ||
                    context.Compilation.Assembly.Name.Contains("CodeGen.Tests"))
                    return;

                SourceGenHelpers.LogInfo($"Source generating assembly {context.Compilation.Assembly.Name} for lambda jobs...");
                var stopwatch = Stopwatch.StartNew();;
                //context.WaitForDebugger("HelloCube");

                // Setup project path for logging and emitting debug source
                // This isn't fantastic but I haven't come up with a better way to get the project path since we might be running out of process
                if (context.AdditionalFiles.Any())
                    SourceGenHelpers.SetProjectPath(context.AdditionalFiles[0].Path);

                // Do initial filter for early out
                var entitiesSyntaxReceiver = (EntitiesSyntaxReceiver)context.SyntaxReceiver;
                var lambdaJobCandidates = FilterCandidates(context, entitiesSyntaxReceiver.EntitiesGetterCandidates.Concat(entitiesSyntaxReceiver.JobGetterCandidates));
                if (!lambdaJobCandidates.Any())
                    return;

                // Create map from SyntaxTrees to candidates
                var syntaxTreeToCandidates = lambdaJobCandidates.GroupBy(c => c.SyntaxTree).ToDictionary(group => group.Key, group => group.ToList());

                // Outer loop - iterate over syntax tree
                foreach (var treeKVP in syntaxTreeToCandidates)
                {
                    var syntaxTree = treeKVP.Key;
                    var treeCandidates = treeKVP.Value;

                    try
                    {
                        // Build up list of job descriptions inside of containing class declarations
                        var classDeclarationToDescriptions = new Dictionary<ClassDeclarationSyntax, List<LambdaJobDescription>>();
                        var jobIndex = 0;
                        foreach (var candidate in treeCandidates)
                        {
                            var declaringType = candidate.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                            var containingMethod = candidate.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                            if (declaringType == null || containingMethod == null)
                                continue;

                            SourceGenHelpers.LogInfo($"Parsing LambdaJobDescription in {declaringType.Identifier}.{containingMethod.Identifier}");
                            var jobDescription = LambdaJobDescription.From(candidate, context, jobIndex++);
                            if (jobDescription == null)
                                continue;

                            if (classDeclarationToDescriptions.ContainsKey(declaringType))
                                classDeclarationToDescriptions[declaringType].Add(jobDescription);
                            else
                                classDeclarationToDescriptions[declaringType] = new List<LambdaJobDescription>() {jobDescription};
                        }

                        // Inner loop - iterate through class descriptions with lambda jobs and generate new class declaration nodes
                        var originalToGeneratedNode = new Dictionary<SyntaxNode, MemberDeclarationSyntax>();
                        foreach (var kvp in classDeclarationToDescriptions)
                        {
                            var classDeclaration = kvp.Key;
                            var descriptionsInClass = kvp.Value;
                            SourceGenHelpers.LogInfo($"Generating code for system type: {classDeclaration.Identifier}");
                            var newClassDeclaration = GenerateNewClassDeclarationForDescriptions(classDeclaration, descriptionsInClass);
                            //SourceGenHelpers.LogInfo(newClassDeclaration.ToString());
                            originalToGeneratedNode[classDeclaration] = newClassDeclaration;
                        }

                        // recurse and create nodes down to our created system nodes
                        var rootNodesWithGenerated = new List<MemberDeclarationSyntax>();
                        foreach (var child in syntaxTree.GetRoot().ChildNodes())
                        {
                            if (child is NamespaceDeclarationSyntax || child is ClassDeclarationSyntax)
                            {
                                var generatedRootNode = ConstructTreeWithGeneratedNodes(child, originalToGeneratedNode);
                                if (generatedRootNode != null)
                                    rootNodesWithGenerated.Add(generatedRootNode);
                            }
                        }

                        if (rootNodesWithGenerated.Any())
                            OutputGeneratedSyntaxTreeNodes(context, syntaxTree, rootNodesWithGenerated);
                    }
                    catch (Exception exception)
                    {
                        context.LogError("SGICE001", "LambdaJobs", exception.ToString(), syntaxTree.GetRoot().GetLocation());
                    }
                }

                stopwatch.Stop();
                SourceGenHelpers.LogInfo($"TIME : LambdaJobs : {context.Compilation.Assembly.Name} : {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception exception)
            {
                context.LogError("SGICE001", "LambdaJobs", exception.ToString(), context.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
            }
        }

        // Construct root of generated tree by recursing
        static MemberDeclarationSyntax ConstructTreeWithGeneratedNodes(SyntaxNode currentNode, Dictionary<SyntaxNode, MemberDeclarationSyntax> originalToGeneratedNode)
        {
            // We have recursed to a generated node, just return that
            if (originalToGeneratedNode.ContainsKey(currentNode))
                return originalToGeneratedNode[currentNode];

            // Otherwise, check for generated children by recursing
            var generatedChildren = new List<MemberDeclarationSyntax>();
            foreach (var childNode in currentNode.ChildNodes())
            {
                var generatedChild = ConstructTreeWithGeneratedNodes(childNode, originalToGeneratedNode);
                if (generatedChild != null)
                    generatedChildren.Add(generatedChild);
            }

            // If we don't have any generated children, we don't need to generate nodes for this branch
            if (generatedChildren.Count == 0)
                return null;

            // Either get the generated node for this level - or create one - and add the generated children
            // No node found, need to create a new one to represent this node in the hierarchy
            MemberDeclarationSyntax newNode;
            if (currentNode is NamespaceDeclarationSyntax namespaceNode)
            {
                newNode = SyntaxFactory.NamespaceDeclaration(namespaceNode.Name)
                    .AddMembers(generatedChildren.ToArray())
                    .WithModifiers(namespaceNode.Modifiers)
                    .WithUsings(namespaceNode.Usings);
            }
            else if (currentNode is ClassDeclarationSyntax classNode)
            {
                newNode = SyntaxFactory.ClassDeclaration(classNode.Identifier)
                    .AddMembers(generatedChildren.ToArray())
                    .WithBaseList(classNode.BaseList)
                    .WithModifiers(classNode.Modifiers)
                    .WithAttributeLists(SourceGenHelpers.AttributeListFromAttributeName("System.Runtime.CompilerServices.CompilerGenerated"));
            }
            else
                throw new InvalidOperationException(
                    $"Expecting class or namespace declaration in syntax tree for {currentNode} but found {currentNode.Kind()}");

            return newNode;
        }

        static ClassDeclarationSyntax GenerateNewClassDeclarationForDescriptions(BaseTypeDeclarationSyntax classDeclaration,
            IReadOnlyCollection<LambdaJobDescription> descriptionsInClass)
        {
            var modifierList = new SyntaxTokenList();
            if (classDeclaration.Modifiers.All(modifier => modifier.Kind() != SyntaxKind.UnsafeKeyword))
                modifierList = modifierList.Add(SyntaxFactory.Token(SyntaxKind.UnsafeKeyword));
            modifierList = modifierList.AddRange(classDeclaration.Modifiers);

            // Create new partial class and mark it as [CompilerGenerated]
            var newClassDeclaration = SyntaxFactory.ClassDeclaration(classDeclaration.Identifier)
                .WithBaseList(classDeclaration.BaseList)
                .WithModifiers(modifierList)
                .WithAttributeLists(SourceGenHelpers.AttributeListFromAttributeName("System.Runtime.CompilerServices.CompilerGenerated"));

            // Replace class methods containing lambda job descriptions
            foreach (var methodDeclaration in classDeclaration.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var descriptionsInMethod = descriptionsInClass.Where(desc => desc.ContainingMethod == methodDeclaration);
                if (!descriptionsInMethod.Any())
                    continue;

                SourceGenHelpers.LogInfo($"  Generating code for method: {methodDeclaration.Identifier}");

                // Generate partial classes and methods for found lambda jobs
                var methodReplacements = new Dictionary<SyntaxNode, SyntaxNode>();
                foreach (var description in descriptionsInMethod)
                    methodReplacements.Add(description.ContainingInvocationExpression, EntitiesSourceFactory.SchedulingInvocationFor(description));
                var methodReplacer = new SyntaxNodeReplacer(methodReplacements);

                var rewrittenMethod = (MethodDeclarationSyntax) methodReplacer.Visit(methodDeclaration);
                if (rewrittenMethod == methodDeclaration)
                    continue;

                // Add rewritten method
                var newModifiers = new SyntaxTokenList(rewrittenMethod.Modifiers.Where(m => !m.IsKind(SyntaxKind.OverrideKeyword)));
                var dotsCompilerPatchedMethodArguments = SyntaxFactory.ParseAttributeArgumentList($"(\"{methodDeclaration.Identifier.Text}\")");
                var dotsCompilerPatchedMethodAttribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Unity.Entities.DOTSCompilerPatchedMethod"), dotsCompilerPatchedMethodArguments);
                var attributeList = new SyntaxList<AttributeListSyntax>();
                attributeList = attributeList.Add(SyntaxFactory.AttributeList(SyntaxFactory.SeparatedList(new[] {dotsCompilerPatchedMethodAttribute})));

                newClassDeclaration = newClassDeclaration.AddMembers(
                    rewrittenMethod
                        .WithoutPreprocessorTrivia()
                        .WithIdentifier(SyntaxFactory.Identifier(methodDeclaration.Identifier.Text + $"_{Path.GetFileNameWithoutExtension(Path.GetRandomFileName())}"))
                        .WithModifiers(newModifiers)
                        .WithAttributeLists(attributeList)
                    );

                // Add all lambdajob members
                foreach (var description in descriptionsInClass.Where(desc => desc.ContainingMethod == methodDeclaration))
                {
                    newClassDeclaration = newClassDeclaration.AddMembers(EntitiesSourceFactory.JobStructFor(description));
                    if (description.LambdaJobKind == LambdaJobKind.Entities)
                        newClassDeclaration = newClassDeclaration.AddMembers(EntitiesSourceFactory.EntityQueryFieldFor(description));
                    newClassDeclaration = newClassDeclaration.AddMembers(EntitiesSourceFactory.ExecuteMethodFor(description));
                    if (description.WithStructuralChangesAndLambdaBodyInSystem)
                        newClassDeclaration = newClassDeclaration.AddMembers(EntitiesSourceFactory.LambdaBodyMethodFor(description));
                }
            }

            // Add OnCreateForCompiler method
            newClassDeclaration = newClassDeclaration.AddMembers(EntitiesSourceFactory.OnCreateForCompilerMethodFor(descriptionsInClass));
            return newClassDeclaration;
        }

        static void OutputGeneratedSyntaxTreeNodes(GeneratorExecutionContext context, SyntaxTree syntaxTree, List<MemberDeclarationSyntax> generatedRootNodes)
        {
            // Create compilation unit
            var compilationUnit = SyntaxFactory.CompilationUnit()
                .AddMembers(generatedRootNodes.ToArray())
                .WithoutPreprocessorTrivia()
                .WithUsings(syntaxTree.GetCompilationUnitRoot(context.CancellationToken).WithoutPreprocessorTrivia().Usings).NormalizeWhitespace();

            // Get generated temp file path
            var generatedSourceHint = syntaxTree.GetGeneratedSourceFileName(context.Compilation.Assembly);
            var generatedSourceFilePath = syntaxTree.GetGeneratedSourceFilePath(context.Compilation.Assembly);

            // Output as source
            var sourceTextForNewClass = compilationUnit.GetText(Encoding.UTF8);
            sourceTextForNewClass = sourceTextForNewClass
                .WithInitialLineDirectiveToGeneratedSource(generatedSourceFilePath)
                .WithIgnoreUnassignedVariableWarning();
            var textChanges = new List<TextChange>();
            foreach (var line in sourceTextForNewClass.Lines)
            {
                var lineText = line.ToString();
                if (lineText.Contains("// __generatedline__"))
                    textChanges.Add(new TextChange(line.Span, lineText.Replace("// __generatedline__", $"#line {line.LineNumber + 2} \"{generatedSourceFilePath}\"")));
                else if (lineText.Contains("#line") && lineText.TrimStart().IndexOf("#line") != 0)
                {
                    var indexOfLineDirective = lineText.IndexOf("#line", StringComparison.Ordinal);
                    textChanges.Add(new TextChange(line.Span,
                        lineText.Substring(0, indexOfLineDirective - 1) + Environment.NewLine + lineText.Substring(indexOfLineDirective)));
                }
            }

            sourceTextForNewClass = sourceTextForNewClass.WithChanges(textChanges);
            //SourceGenHelpers.LogInfo($"*** Outputting source to: {generatedSourceFilePath}");
            //SourceGenHelpers.LogInfo(sourceTextForNewClass.ToString());
            context.AddSource(generatedSourceHint, sourceTextForNewClass);

            // Output as debug file
            File.WriteAllText(generatedSourceFilePath, sourceTextForNewClass.ToString());
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new EntitiesSyntaxReceiver());
        }

        public class EntitiesSyntaxReceiver : ISyntaxReceiver
        {
            public readonly List<IdentifierNameSyntax> EntitiesGetterCandidates = new List<IdentifierNameSyntax>();
            public readonly List<IdentifierNameSyntax> JobGetterCandidates = new List<IdentifierNameSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is IdentifierNameSyntax identifierNameSyntax)
                {
                    if (identifierNameSyntax.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    {
                        if (identifierNameSyntax.Identifier.ValueText == "Entities")
                            EntitiesGetterCandidates.Add(identifierNameSyntax);
                        else if (identifierNameSyntax.Identifier.ValueText == "Job")
                            JobGetterCandidates.Add(identifierNameSyntax);
                    }
                }
            }
        }
    }
}
