// Copyright (c) Tunnel Vision Laboratories, LLC. All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    // Find all cases where a member access could be used with explicit `this`
    sealed class LambdaBodySyntaxReplacer : CSharpSyntaxWalker
    {
        SemanticModel _model;
        readonly List<SyntaxNode> _lineStatementNodesNeedingReplacement = new List<SyntaxNode>();
        readonly List<SyntaxNode> _thisNodesNeedingReplacement = new List<SyntaxNode>();
        readonly List<SyntaxNode> _constantNodesNeedingReplacement = new List<SyntaxNode>();

        internal static (
                SyntaxNode rewrittenLambdaExpression,
                List<SyntaxNode> thisAccessNodesNeedingReplacement)
            Rewrite(SemanticModel model, SyntaxNode originalLambdaExpression)
        {
            var explicitThisRewriter = new LambdaBodySyntaxReplacer(model);
            explicitThisRewriter.Visit(originalLambdaExpression);
            var allTrackedNodes = explicitThisRewriter._lineStatementNodesNeedingReplacement.Concat(explicitThisRewriter._thisNodesNeedingReplacement);
            allTrackedNodes = allTrackedNodes.Concat(explicitThisRewriter._constantNodesNeedingReplacement);
            if (!allTrackedNodes.Any())
                return (originalLambdaExpression, new List<SyntaxNode>());

            // Track nodes for later rewriting
            var rewrittenBody = originalLambdaExpression.TrackNodes(allTrackedNodes);

            // Replace use of constants in the lambda with the actual constant value
            var currentConstantNodesNeedingReplacement = rewrittenBody.GetCurrentNodes(explicitThisRewriter._constantNodesNeedingReplacement);
            var oldToNewConstantNodesNeedingReplacement =
                currentConstantNodesNeedingReplacement.Zip(explicitThisRewriter._constantNodesNeedingReplacement,
                (k, v) => new {key = k, value = v}).ToDictionary(x => x.key, x => x.value);
            rewrittenBody = rewrittenBody.ReplaceNodes(currentConstantNodesNeedingReplacement, (originalNode, rewrittenNode) =>
                ReplaceConstantNodesWithConstantValue(model, originalNode, oldToNewConstantNodesNeedingReplacement));

            // Replace those locations to access through "__this" instead (since they need to access through a stored field on job struct).
            // Also replace data access methods on SystemBase that need to be patched (GetComponent/SetComponent/etc)
            var currentThisNodesNeedingReplacement = rewrittenBody.GetCurrentNodes(explicitThisRewriter._thisNodesNeedingReplacement);
            rewrittenBody = rewrittenBody.ReplaceNodes(currentThisNodesNeedingReplacement, (originalNode, rewrittenNode) =>
                ReplaceNodeWithAccessThroughLocalThis(originalNode));

            // Also rewrite any remaining references to `this` to our local this
            rewrittenBody = rewrittenBody.ReplaceNodes(rewrittenBody.DescendantNodes().OfType<ThisExpressionSyntax>(),
                (originalNode, rewrittenNode) => LocalThisFieldSyntax);

            // Replace line statements with one's with line directive trivia
            foreach (var originalNode in explicitThisRewriter._lineStatementNodesNeedingReplacement)
            {
                var currentNode = rewrittenBody.GetCurrentNode(originalNode);
                rewrittenBody = rewrittenBody.ReplaceNode(currentNode, currentNode.WithLineTrivia(originalNode));
            }

            return (rewrittenBody, explicitThisRewriter._thisNodesNeedingReplacement);
        }

        static readonly ExpressionSyntax LocalThisFieldSyntax = SyntaxFactory.IdentifierName("__this");

        static SyntaxNode ReplaceNodeWithAccessThroughLocalThis(SyntaxNode originalNode)
        {
            var newMemberAccessNode = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                LocalThisFieldSyntax,
                (SimpleNameSyntax) originalNode);
            return newMemberAccessNode;
        }

        static SyntaxNode ReplaceConstantNodesWithConstantValue(SemanticModel model, SyntaxNode node, Dictionary<SyntaxNode, SyntaxNode> oldToNewConstantNodesNeedingReplacement)
        {
            var originalNode = oldToNewConstantNodesNeedingReplacement[node];
            var symbolInfo = model.GetSymbolInfo(originalNode);
            if (symbolInfo.Symbol is ILocalSymbol localSymbol && localSymbol.HasConstantValue)
            {
                try
                {
                    return SyntaxFactory.ParseExpression(localSymbol.ConstantValue.ToString());
                }
                catch (Exception)
                {
                    throw new InvalidOperationException($"Could not parse constant literal expression for symbol in node {originalNode}");
                }
            }
            throw new InvalidOperationException($"Unable to find symbol info with constant value for symbol in node {originalNode}");
        }

        LambdaBodySyntaxReplacer(SemanticModel model)
        {
            _model = model;
        }

        public override void Visit(SyntaxNode node)
        {
            if (node is StatementSyntax statementSyntax && !(node is BlockSyntax))
            {
                HandleStatement(statementSyntax);
            }
            else if (node is SimpleNameSyntax simpleNameSyntax)
            {
                HandleSimpleName(simpleNameSyntax);
            }
            else if (node is MemberAccessExpressionSyntax memberAccessExpression)
            {
                IdentifierNameSyntax nameExpression = memberAccessExpression.Expression as IdentifierNameSyntax;
                HandleIdentifierNameImpl(nameExpression);
            }

            base.Visit(node);
        }

        void HandleStatement(StatementSyntax statementSyntax)
        {
            _lineStatementNodesNeedingReplacement.Add(statementSyntax);
        }

        void HandleSimpleName(SimpleNameSyntax node)
        {
            switch (node?.Parent?.Kind() ?? SyntaxKind.None)
            {
                case SyntaxKind.SimpleMemberAccessExpression:
                    // this is handled separately
                    return;

                case SyntaxKind.MemberBindingExpression:
                case SyntaxKind.NameColon:
                case SyntaxKind.PointerMemberAccessExpression:
                    // this doesn't need to be handled
                    return;

                case SyntaxKind.QualifiedCref:
                case SyntaxKind.NameMemberCref:
                    // documentation comments don't use 'this.'
                    return;

                case SyntaxKind.SimpleAssignmentExpression:
                    if (((AssignmentExpressionSyntax)node.Parent).Left == node
                        && (node.Parent.Parent?.IsKind(SyntaxKind.ObjectInitializerExpression) ?? true))
                    {
                        /* Handle 'X' in:
                         *   new TypeName() { X = 3 }
                         */
                        return;
                    }

                    break;

                case SyntaxKind.NameEquals:
                    if (((NameEqualsSyntax)node.Parent).Name != node)
                    {
                        break;
                    }

                    switch (node?.Parent?.Parent?.Kind() ?? SyntaxKind.None)
                    {
                        case SyntaxKind.AttributeArgument:
                        case SyntaxKind.AnonymousObjectMemberDeclarator:
                            return;
                    }

                    break;

                case SyntaxKind.Argument when IsPartOfConstructorInitializer((SimpleNameSyntax)node):
                    // constructor invocations cannot contain this.
                    return;
            }

            HandleIdentifierNameImpl((SimpleNameSyntax)node);
        }

        void HandleIdentifierNameImpl(SimpleNameSyntax nameExpression)
        {
            if (nameExpression == null)
                return;

            if (!HasThis(nameExpression))
                return;

            var symbolInfo = _model.GetSymbolInfo(nameExpression);

            if (symbolInfo.Symbol is ILocalSymbol localSymbol && localSymbol.IsConst && localSymbol.HasConstantValue)
            {
                _constantNodesNeedingReplacement.Add(nameExpression);
                return;
            }

            ImmutableArray<ISymbol> symbolsToAnalyze;
            if (symbolInfo.Symbol != null)
            {
                symbolsToAnalyze = ImmutableArray.Create(symbolInfo.Symbol);
            }
            else if (symbolInfo.CandidateReason == CandidateReason.MemberGroup)
            {
                // analyze the complete set of candidates, and use 'this.' if it applies to all
                symbolsToAnalyze = symbolInfo.CandidateSymbols;
            }
            else
            {
                return;
            }

            foreach (ISymbol symbol in symbolsToAnalyze)
            {
                if (symbol is ITypeSymbol)
                {
                    return;
                }

                if (symbol.IsStatic)
                {
                    return;
                }

                if (!(symbol.ContainingSymbol is ITypeSymbol))
                {
                    // covers local variables, parameters, etc.
                    return;
                }

                if (symbol is IMethodSymbol methodSymbol)
                {
                    switch (methodSymbol.MethodKind)
                    {
                        case MethodKind.Constructor:
                        case MethodKind.LocalFunction:
                            return;

                        default:
                            break;
                    }
                }

                // This is a workaround for:
                // - https://github.com/DotNetAnalyzers/StyleCopAnalyzers/issues/1501
                // - https://github.com/DotNetAnalyzers/StyleCopAnalyzers/issues/2093
                // and can be removed when the underlying bug in roslyn is resolved
                if (nameExpression.Parent is MemberAccessExpressionSyntax)
                {
                    var memberAccessSymbol = _model.GetSymbolInfo(nameExpression.Parent).Symbol;

                    switch (memberAccessSymbol?.Kind)
                    {
                        case null:
                            break;

                        case SymbolKind.Field:
                        case SymbolKind.Method:
                        case SymbolKind.Property:
                            if (memberAccessSymbol.IsStatic && (memberAccessSymbol.ContainingType.Name == symbol.Name))
                            {
                                return;
                            }

                            break;
                    }
                }

                // End of workaround
            }

            _thisNodesNeedingReplacement.Add(nameExpression);
        }

        static bool HasThis(SyntaxNode node)
        {
            for (; node != null; node = node.Parent)
            {
                switch (node.Kind())
                {
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.InterfaceDeclaration:
                    case SyntaxKind.StructDeclaration:
                    case SyntaxKind.DelegateDeclaration:
                    case SyntaxKind.EnumDeclaration:
                    case SyntaxKind.NamespaceDeclaration:
                        return false;

                    case SyntaxKind.FieldDeclaration:
                    case SyntaxKind.EventFieldDeclaration:
                        return false;

                    case SyntaxKind.EventDeclaration:
                    case SyntaxKind.IndexerDeclaration:
                        var basePropertySyntax = (BasePropertyDeclarationSyntax)node;
                        return !basePropertySyntax.Modifiers.Any(SyntaxKind.StaticKeyword);

                    case SyntaxKind.PropertyDeclaration:
                        var propertySyntax = (PropertyDeclarationSyntax)node;
                        return !propertySyntax.Modifiers.Any(SyntaxKind.StaticKeyword)
                            && propertySyntax.Initializer == null;

                    case SyntaxKind.MultiLineDocumentationCommentTrivia:
                    case SyntaxKind.SingleLineDocumentationCommentTrivia:
                        return false;

                    case SyntaxKind.ConstructorDeclaration:
                    case SyntaxKind.DestructorDeclaration:
                    case SyntaxKind.MethodDeclaration:
                        var baseMethodSyntax = (BaseMethodDeclarationSyntax)node;
                        return !baseMethodSyntax.Modifiers.Any(SyntaxKind.StaticKeyword);

                    case SyntaxKind.Attribute:
                        return false;

                    default:
                        continue;
                }
            }

            return false;
        }

        static bool IsPartOfConstructorInitializer(SyntaxNode node)
        {
            for (; node != null; node = node.Parent)
            {
                switch (node.Kind())
                {
                    case SyntaxKind.ThisConstructorInitializer:
                    case SyntaxKind.BaseConstructorInitializer:
                        return true;
                }
            }

            return false;
        }
    }
}
