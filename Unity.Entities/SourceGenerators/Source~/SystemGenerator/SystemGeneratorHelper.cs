using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.SystemGenerator
{
    public static class SystemGeneratorHelper
    {
        public static IEnumerable<SyntaxTree> GetSyntaxTreesWithCandidates(IEnumerable<ISystemModule> allModules)
            => allModules.SelectMany(module => module.Candidates).Select(node => node.SyntaxNode.SyntaxTree).Distinct();

        public static IEnumerable<TypeDeclarationSyntax> GetSystemTypeInTreeWithCandidate(SyntaxTree syntaxTree, IEnumerable<ISystemModule> allModules)
        {
            var resultSet = new HashSet<TypeDeclarationSyntax>();
            foreach (var module in allModules)
            {
                foreach (var (syntaxNode, systemType) in module.Candidates)
                {
                    if (syntaxNode.SyntaxTree == syntaxTree)
                        resultSet.Add(systemType);
                }
            }

            return resultSet;
        }

        public static IEnumerable<ISystemModule> GetAllModulesAffectingSystemType(TypeDeclarationSyntax systemTypeWithCandidate, List<ISystemModule> allModules,
            GeneratorExecutionContext generatorExecutionContext)
        {
            var resultSet = new HashSet<ISystemModule>();
            foreach (var module in allModules)
            {
                if (module.ShouldRun(generatorExecutionContext.ParseOptions))
                    foreach (var candidate in module.Candidates)
                    {
                        if (candidate.SystemType == systemTypeWithCandidate)
                        {
                            resultSet.Add(module);
                        }
                    }
            }

            return resultSet;
        }

        // Construct root of generated tree by recursing downwards
        internal static MemberDeclarationSyntax ConstructGeneratedTree(SyntaxNode currentNode,
            IDictionary<SyntaxNode, SyntaxNode> originalToGeneratedNode,
            ImmutableHashSet<SyntaxNode> allOriginalNodesAlsoInGeneratedTree)
        {
            // We have recursed to a generated node, just return that
            if (originalToGeneratedNode.ContainsKey(currentNode))
                return (MemberDeclarationSyntax)originalToGeneratedNode[currentNode];

            // If this node shouldn't exist in generated tree, early out
            if (!allOriginalNodesAlsoInGeneratedTree.Contains(currentNode))
                return null;

            // Otherwise, check for generated children by recursing
            var generatedChildren =
                currentNode
                    .ChildNodes()
                    .Select(childNode => ConstructGeneratedTree(childNode, originalToGeneratedNode, allOriginalNodesAlsoInGeneratedTree))
                    .Where(generatedChild => generatedChild != null);

            // If we don't have any generated children, we don't need to generate nodes for this branch
            if (!generatedChildren.Any())
                return null;

            var generatedChildrenArray = generatedChildren.ToArray();

            // Either get the generated node for this level - or create one - and add the generated children
            // No node found, need to create a new one to represent this node in the hierarchy
            MemberDeclarationSyntax newNode = currentNode switch
            {
                NamespaceDeclarationSyntax namespaceNode =>
                    SyntaxFactory.NamespaceDeclaration(namespaceNode.Name)
                        .AddMembers(generatedChildrenArray)
                        .WithModifiers(namespaceNode.Modifiers)
                        .WithUsings(namespaceNode.Usings),

                ClassDeclarationSyntax classNode =>
                    SyntaxFactory.ClassDeclaration(classNode.Identifier)
                        .AddMembers(generatedChildrenArray)
                        .WithBaseList(classNode.BaseList)
                        .WithModifiers(classNode.Modifiers)
                        .WithAttributeLists(SourceGenHelpers.AttributeListFromAttributeName("System.Runtime.CompilerServices.CompilerGenerated")),

                StructDeclarationSyntax structNode =>
                    SyntaxFactory.StructDeclaration(structNode.Identifier)
                        .AddMembers(generatedChildrenArray)
                        .WithBaseList(structNode.BaseList)
                        .WithModifiers(structNode.Modifiers)
                        .WithAttributeLists(SourceGenHelpers.AttributeListFromAttributeName("System.Runtime.CompilerServices.CompilerGenerated")),

                _ => throw new InvalidOperationException(
                    $"Expecting class or namespace declaration in syntax tree for {currentNode} but found {currentNode.Kind()}")
            };

            return newNode;
        }
    }
}
