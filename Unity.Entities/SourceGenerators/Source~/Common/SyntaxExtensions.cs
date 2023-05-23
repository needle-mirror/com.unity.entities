using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Unity.Entities.SourceGen.Common
{
    public static class SyntaxExtensions
    {
        public static IEnumerable<MemberDeclarationSyntax> GetContainingTypesAndNamespacesFromMostToLeastNested(
            this SyntaxNode syntaxNode)
        {
            SyntaxNode current = syntaxNode;
            while (current.Parent != null && (current.Parent is NamespaceDeclarationSyntax || current.Parent is ClassDeclarationSyntax || current.Parent is StructDeclarationSyntax))
            {
                yield return current.Parent as MemberDeclarationSyntax;
                current = current.Parent;
            }
        }

        public static bool IsReadOnly(this ParameterSyntax parameter) => parameter.Modifiers.Any(mod => mod.IsKind(SyntaxKind.InKeyword));
        public static bool IsReadOnly(this IParameterSymbol parameter) => parameter.RefKind == RefKind.In;

        public static IEnumerable<NamespaceDeclarationSyntax> GetNamespacesFromMostToLeastNested(this SyntaxNode syntaxNode)
        {
            SyntaxNode current = syntaxNode;
            while (current.Parent != null && current.Parent is NamespaceDeclarationSyntax nds)
            {
                yield return nds;
                current = current.Parent;
            }
        }

        class PreprocessorTriviaRemover : CSharpSyntaxRewriter
        {
            public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
            {
                if (trivia.Kind() == SyntaxKind.DisabledTextTrivia ||
                    trivia.Kind() == SyntaxKind.PreprocessingMessageTrivia ||
                    trivia.Kind() == SyntaxKind.IfDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.ElifDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.ElseDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.EndIfDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.RegionDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.EndRegionDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.DefineDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.UndefDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.ErrorDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.WarningDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.PragmaWarningDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.PragmaChecksumDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.ReferenceDirectiveTrivia ||
                    trivia.Kind() == SyntaxKind.BadDirectiveTrivia)
                    return default;
                return trivia;
            }
        }

        public static T WithoutPreprocessorTrivia<T>(this T node) where T : SyntaxNode
        {
            var preprocessorTriviaRemover = new PreprocessorTriviaRemover();
            return (T)preprocessorTriviaRemover.Visit(node);
        }

        // Lambda body can exist as block, statement or just expression (create BodyBlock in the last two cases)
        public static BlockSyntax ToBlockSyntax(this ParenthesizedLambdaExpressionSyntax node)
        {
            if (node.Block != null)
                return node.Block;
            if (node.Body is StatementSyntax lambdaBodyStatement)
                return Block(lambdaBodyStatement);
            if (node.Body is ExpressionSyntax lambdaBodyExpression)
                return Block(SyntaxFactory.ExpressionStatement(lambdaBodyExpression));
            throw new InvalidOperationException($"Invalid lambda body: {node.Body}");
        }

        public static bool ContainsDynamicCode(this InvocationExpressionSyntax invoke)
        {
            var argumentList = invoke.DescendantNodes().OfType<ArgumentListSyntax>().LastOrDefault();
            return argumentList?.DescendantNodes().OfType<ConditionalExpressionSyntax>().FirstOrDefault() != null;
        }

        public static T AncestorOfKind<T>(this SyntaxNode node) where T : SyntaxNode
        {
            foreach (var ancestor in node.Ancestors())
                if (ancestor is T t)
                    return t;
            throw new InvalidOperationException($"No Ancestor {nameof(T)} found.");
        }

        public static T AncestorOfKindOrDefault<T>(this SyntaxNode node) where T : SyntaxNode
        {
            foreach (var ancestor in node.Ancestors())
                if (ancestor is T t)
                    return t;
            return null;
        }

        public static SyntaxNode WithLineTrivia(this SyntaxNode node, string originalFilePath, int originalLineNumber, int offsetLineNumber = 1)
        {
            if (string.IsNullOrEmpty(originalFilePath))
                return node;

            var lineTrivia = Comment($"#line {originalLineNumber + offsetLineNumber} \"{originalFilePath}\"");
            return node.WithLeadingTrivia(lineTrivia, CarriageReturnLineFeed);
        }

        public static int GetLineNumber(this SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line;

        public static SyntaxNode WithHiddenLineTrivia(this SyntaxNode node)
            => node.WithLeadingTrivia(Comment($"#line hidden"), CarriageReturnLineFeed);

        // Walk direct ancestors that are MemberAccessExpressionSyntax and InvocationExpressionSyntax and collect invocations
        // This collects things like Entities.WithAll().WithNone().Run() without getting additional ancestor invocations.
        public static Dictionary<string, List<InvocationExpressionSyntax>> GetMethodInvocations(this SyntaxNode node)
        {
            var result = new Dictionary<string, List<InvocationExpressionSyntax>>();
            var parent = node.Parent;

            while (parent is MemberAccessExpressionSyntax memberAccessExpression)
            {
                parent = parent.Parent;
                if (parent is InvocationExpressionSyntax invocationExpression)
                {
                    var memberName = memberAccessExpression.Name.Identifier.ValueText;
                    result.Add(memberName, invocationExpression);
                    parent = parent.Parent;
                }
                else if (!(parent is MemberAccessExpressionSyntax))
                    break;
            }

            return result;
        }

        public static string GetModifierString(this ParameterSyntax parameter)
        {
            foreach (var mod in parameter.Modifiers)
            {
                if (mod.IsKind(SyntaxKind.InKeyword))
                    return "in";
                if (mod.IsKind(SyntaxKind.RefKeyword))
                    return "ref";
            }
            return "";
        }

        public static bool DoesPerformStructuralChange(this InvocationExpressionSyntax syntax, SemanticModel model)
        {
            return model.GetSymbolInfo(syntax.Expression).Symbol is IMethodSymbol methodSymbol &&
                   methodSymbol.ContainingType.Is("Unity.Entities.EntityManager") &&
                   methodSymbol.HasAttribute("Unity.Entities.EntityManager.StructuralChangeMethodAttribute");
        }
    }
}
