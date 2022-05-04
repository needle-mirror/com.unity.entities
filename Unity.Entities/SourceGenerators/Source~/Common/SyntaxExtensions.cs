using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Unity.Entities.SourceGen.Common
{
    public static class SyntaxExtensions
    {
        public static bool HasAttribute(this TypeDeclarationSyntax typeDeclarationSyntax, string attributeName)
        {
            return typeDeclarationSyntax.AttributeLists
                                        .SelectMany(list => list.Attributes.Select(a => a.Name.ToString()))
                                        .SingleOrDefault(a => a == attributeName) != null;
        }

        public static SyntaxList<UsingDirectiveSyntax> AddUsingStatements(
            this SyntaxList<UsingDirectiveSyntax> currentUsings, IEnumerable<string> newUsings)
        {
            return currentUsings.AddRange(newUsings.Where(n => currentUsings.All(c => c.Name.ToFullString() != n))
                                .Select(u => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(u))));
        }

        public static MemberDeclarationSyntax AddNamespaces(
            this TypeDeclarationSyntax typeDeclarationSyntax,
            IEnumerable<NamespaceDeclarationSyntax> namespacesFromMostToLeastNested)
        {
            NamespaceDeclarationSyntax[] namespaces = namespacesFromMostToLeastNested.ToArray();

            if (!namespaces.Any())
            {
                return typeDeclarationSyntax;
            }

            return
                namespaces.Aggregate<NamespaceDeclarationSyntax, MemberDeclarationSyntax>(
                    typeDeclarationSyntax,
                    (current, nds) =>
                        NamespaceDeclaration(nds.Name).AddMembers(current));
        }

        public static (bool Success, InvocationExpressionSyntax invocationExpressionSyntax)
            FindMemberInvocationWithName(this SyntaxNode node, string memberName)
        {
            InvocationExpressionSyntax memberInvocation =
                node.Ancestors()
                    .OfType<InvocationExpressionSyntax>()
                    .SingleOrDefault(i => i.Expression is MemberAccessExpressionSyntax m
                                          && m.Name.Identifier.ValueText == memberName);

            return (memberInvocation != null, memberInvocation);
        }

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

        public static string GetGeneratedSourceFileName(this SyntaxTree syntaxTree, string generatorName)
        {
            var (isSuccess, fileName) = TryGetFileNameWithoutExtension(syntaxTree);
            var stableHashCode = SourceGenHelpers.GetStableHashCode(syntaxTree.FilePath) & 0x7fffffff;

            var postfix = generatorName.Length > 0 ? $"__{generatorName}" : String.Empty;

            if (isSuccess)
                fileName = $"{fileName}{postfix}_{stableHashCode}.g.cs";
            else
                fileName = Path.Combine($"{Path.GetRandomFileName()}{postfix}", ".g.cs");

            return fileName;
        }

        public static string GetGeneratedSourceFilePath(this SyntaxTree syntaxTree, IAssemblySymbol assembly, string generatorName)
        {
            var fileName = GetGeneratedSourceFileName(syntaxTree, generatorName);

            if (SourceGenHelpers.CanWriteToProjectPath)
            {
                var saveToDirectory = Path.Combine(SourceGenHelpers.ProjectPath, "Temp", "GeneratedCode", assembly.Name);
                Directory.CreateDirectory(saveToDirectory);
                return Path.Combine(saveToDirectory, fileName);
            }
            else
                return Path.Combine("Temp", "GeneratedCode", assembly.Name);
        }

        static (bool IsSuccess, string FileName) TryGetFileNameWithoutExtension(SyntaxTree syntaxTree)
        {
            var fileName = Path.GetFileNameWithoutExtension(syntaxTree.FilePath);
            return (IsSuccess: true, fileName);
        }

        public class PreprocessorTriviaRemover : CSharpSyntaxRewriter
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
            return (T) preprocessorTriviaRemover.Visit(node);
        }

        // Lambda body can exist as block, statement or just expression (create BodyBlock in the last two cases)
        public static BlockSyntax ToBlockSyntax(this ParenthesizedLambdaExpressionSyntax node)
        {
            if (node.Block != null)
                return (BlockSyntax)node.Block;
            else if (node.Body is StatementSyntax lambdaBodyStatement)
                return SyntaxFactory.Block(lambdaBodyStatement);
            else if (node.Body is ExpressionSyntax lambdaBodyExpression)
                return SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(lambdaBodyExpression));
            throw new InvalidOperationException($"Invalid lambda body: {node.Body}");
        }

        public static ITypeSymbol GetDerivedReturnType(this PropertyDeclarationSyntax prop, SemanticModel model, CancellationToken cancel = default)
        {
            var expr = prop.ExpressionBody?.Expression;
            if (expr is null)
            {
                var getter = prop.AccessorList?.Accessors.FirstOrDefault(acc => acc.Kind() == SyntaxKind.GetAccessorDeclaration);
                if (getter == null)
                    return null;

                expr = getter.ExpressionBody?.Expression;
                if (expr is null)
                {
                    expr = getter.Body?.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault()?.Expression;
                    if (expr is null)
                        return null;
                }
            }
            return model.GetTypeInfo(expr, cancel).Type;
        }

        public static InvocationExpressionSyntax WithArgs(this InvocationExpressionSyntax invoke, ExpressionSyntax arg)
            => invoke.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(arg))));

        public static InvocationExpressionSyntax WithArgs(this InvocationExpressionSyntax invoke, IEnumerable<ExpressionSyntax> args)
            => invoke.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args.Select(SyntaxFactory.Argument))));

        public static bool ContainsDynamicCode(this InvocationExpressionSyntax invoke)
        {
            var argumentList = invoke.DescendantNodes().OfType<ArgumentListSyntax>().LastOrDefault();
            return argumentList?.DescendantNodes().OfType<ConditionalExpressionSyntax>().FirstOrDefault() != null;
        }

        public static bool HasModifier(this ClassDeclarationSyntax cls, SyntaxKind modifier)
            => cls.Modifiers.Any(m => m.IsKind(modifier));

        public static SyntaxNode WithLineTrivia(this SyntaxNode node)
        {
            return WithLineTrivia(node, node);
        }

        public static IEnumerable<SyntaxNode> AncestorsOfKind<TResult1, TResult2>(this SyntaxNode node)
        {
            void AncestorsOfKindRecurse(SyntaxNode currentNode, List<SyntaxNode> currentParentList)
            {
                var parent = currentNode.Parent;
                if (parent is TResult1 || parent is TResult2)
                {
                    currentParentList.Add(parent);
                    AncestorsOfKindRecurse(parent, currentParentList);
                }
            }

            var ancestorsList = new List<SyntaxNode>();
            AncestorsOfKindRecurse(node, ancestorsList);
            return ancestorsList;
        }

        public static SyntaxNode AddMemberToClassOrNamespace(this SyntaxNode node, SyntaxNode newNode)
        {
            if (node is ClassDeclarationSyntax classNode)
                return classNode.AddMembers((MemberDeclarationSyntax)newNode);
            else if (node is NamespaceDeclarationSyntax namespaceNode)
                return namespaceNode.AddMembers((MemberDeclarationSyntax)newNode);

            throw new InvalidOperationException("Node must be class or namespace declaration syntax");
        }

        public static SyntaxNode NodeAfter(this SyntaxNode node, Func<SyntaxNodeOrToken, bool> predicate)
        {
            bool nodeFound = false;
            var descendents = node.DescendantNodesAndTokens().ToArray();
            for (var i = 0; i < descendents.Count(); ++i)
            {
                if (nodeFound && descendents[i].IsNode)
                    return descendents[i].AsNode();
                if (predicate(descendents[i]))
                    nodeFound = true;
            }

            return null;
        }

        public static SyntaxNode WithLineTrivia(this SyntaxNode node, SyntaxNode originalLineNode)
        {
            var nodeLocation = originalLineNode.GetLocation();
            if (string.IsNullOrEmpty(nodeLocation.SourceTree.FilePath))
                return node;

            var lineTrivia = SyntaxFactory.Comment($"#line {nodeLocation.GetLineSpan().StartLinePosition.Line + 1} \"{nodeLocation.SourceTree.FilePath}\"");
            return node.WithLeadingTrivia(new []{ lineTrivia, SyntaxFactory.CarriageReturnLineFeed });
        }

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
                    if (result.ContainsKey(memberName))
                        result[memberName].Add(invocationExpression);
                    else
                        result[memberName] = new List<InvocationExpressionSyntax> { invocationExpression };
                    parent = parent.Parent;
                }
                else if (!(parent is MemberAccessExpressionSyntax))
                    break;
            }

            return result;
        }

        public static string GetModifierString(this ParameterSyntax parameter)
        {
            if (parameter.Modifiers.Any(mod => mod.IsKind(SyntaxKind.InKeyword)))
                return "in";
            if (parameter.Modifiers.Any(mod => mod.IsKind(SyntaxKind.RefKeyword)))
                return "ref";
            return "";
        }

        public static bool DoesPerformStructuralChange(this InvocationExpressionSyntax syntax, SemanticModel model)
        {
            return model.GetSymbolInfo(syntax.Expression).Symbol is IMethodSymbol methodSymbol &&
                   methodSymbol.ContainingType.Is("Unity.Entities.EntityManager") &&
                   methodSymbol.HasAttribute("Unity.Entities.EntityManager.StructuralChangeMethodAttribute");
        }

        public static bool CheckIsForEach(this InvocationExpressionSyntax syntax, SemanticModel model)
        {
            var methodSymbol = model.GetSymbolInfo(syntax.Expression).Symbol as IMethodSymbol;
            if (methodSymbol == null || methodSymbol.Name != "ForEach")
            {
                return false;
            }

            if (methodSymbol.ContainingType.Is("LambdaForEachDescriptionConstructionMethods"))
            {
                return true;
            }
            return false;
        }
    }
}
