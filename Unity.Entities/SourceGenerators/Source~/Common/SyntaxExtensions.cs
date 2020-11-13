using System;
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
        private static readonly Dictionary<string, int> FileNameToDuplicateCount = new Dictionary<string, int>();

        public static bool HasAttribute(this TypeDeclarationSyntax typeDeclarationSyntax, string attributeName)
        {
            return typeDeclarationSyntax.AttributeLists
                                        .SelectMany(list => list.Attributes.Select(a => a.Name.ToString()))
                                        .SingleOrDefault(a => a == attributeName) != null;
        }

        public static SyntaxList<UsingDirectiveSyntax> AddUsingStatements(
            this SyntaxList<UsingDirectiveSyntax> currentUsings, params string[] newUsings)
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

        public static bool IsReadOnly(this ParameterSyntax parameter) => parameter.Modifiers.Any(mod => mod.IsKind(SyntaxKind.InKeyword));

        public static IEnumerable<NamespaceDeclarationSyntax> GetNamespacesFromMostToLeastNested(this SyntaxNode syntaxNode)
        {
            var namespaces = new List<NamespaceDeclarationSyntax>();

            SyntaxNode current = syntaxNode;
            while (current.Parent != null && current.Parent is NamespaceDeclarationSyntax nds)
            {
                namespaces.Add(nds);
                current = current.Parent;
            }
            return namespaces;
        }

        public static string GetGeneratedSourceFileName(this SyntaxTree syntaxTree, IAssemblySymbol assembly)
        {
            var (isSuccess, fileName) = TryGetFileNameWithExtension(syntaxTree);

            return
                isSuccess
                    ? Path.ChangeExtension(fileName, ".g.cs")
                    : Path.Combine(Path.GetRandomFileName(), ".g.cs");
        }

        public static string GetGeneratedSourceFilePath(this SyntaxTree syntaxTree, IAssemblySymbol assembly)
        {
            var fileName = GetGeneratedSourceFileName(syntaxTree, assembly);

            var saveToDirectory = Path.Combine(SourceGenHelpers.GetProjectPath(), "Temp", "GeneratedCode", assembly.Name);
            Directory.CreateDirectory(saveToDirectory);

            return Path.Combine(saveToDirectory, fileName);
        }

        static (bool IsSuccess, string FileName) TryGetFileNameWithExtension(SyntaxTree syntaxTree)
        {
            var fileName = Path.GetFileNameWithoutExtension(syntaxTree.FilePath);
            return (IsSuccess: true, $"{fileName}{Path.GetExtension(syntaxTree.FilePath)}");

            // This is a good idea but doesn't quite work yet as we don't flush Temp/GeneratedCode between Unity runs
            /*
            string fileName = Path.GetFileNameWithoutExtension(syntaxTree.FilePath);

            if (string.IsNullOrEmpty(fileName))
            {
                return (IsSuccess: false, fileName);
            }

            if (FileNameToDuplicateCount.TryGetValue(fileName, out int count))
            {
                int nextCount = count + 1;
                fileName = $"{fileName}_{nextCount}";

                FileNameToDuplicateCount[fileName] = nextCount;
            }
            else
            {
                FileNameToDuplicateCount.Add(fileName, 0);
            }

            return (IsSuccess: true, $"{fileName}{Path.GetExtension(syntaxTree.FilePath)}");
            */
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

        public static Dictionary<string, List<InvocationExpressionSyntax>> GetMethodInvocations(this SyntaxNode node)
        {
            var result = new Dictionary<string, List<InvocationExpressionSyntax>>();
            var invocationExpressions = node.Ancestors().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocationExpressions)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var memberName = memberAccess.Name.Identifier.ValueText;
                    if (result.ContainsKey(memberName))
                        result[memberName].Add(invocation);
                    else
                        result[memberName] = new List<InvocationExpressionSyntax>() { invocation };
                }
            }
            return result;
        }

        public static int GetSymbolHashCode(this SemanticModel model, SyntaxNode node)
        {
            var nodeSymbol = model.GetDeclaredSymbol(node);
            return SymbolEqualityComparer.Default.GetHashCode(nodeSymbol);
        }
    }
}
