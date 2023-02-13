using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGenerator;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen
{
    public class EntityQueryModule : ISystemModule
    {
        public IEnumerable<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> Candidates
        {
            get
            {
                foreach (var kvp in EntityQueryCandidatesGroupedBySystemType)
                    foreach (var candidate in kvp.Value)
                        yield return (candidate.EntitiesSyntaxNode, candidate.ContainingSystemType);
            }
        }
        // internal List<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> AllCandidates = new List<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)>();

        public bool RequiresReferenceToBurst => false;

        public struct QueryCandidate
        {
            // Entities.WithAll<Foo, Bar>().ToQuery();
            // ^--EntitiesSyntaxNode        ^--OperationSyntaxNode
            public SyntaxNode EntitiesSyntaxNode;
            public SyntaxNode OperationSyntaxNode;
            public TypeDeclarationSyntax ContainingSystemType;
            public string MethodName;
        }

        private Dictionary<TypeDeclarationSyntax, List<QueryCandidate>> EntityQueryCandidatesGroupedBySystemType { get; } =
            new Dictionary<TypeDeclarationSyntax, List<QueryCandidate>>();

        static string[] s_OperationNames { get; } =
        {
            "AddChunkComponentData",
            "AddComponent",
            "AddComponentData",
            "AddSharedComponent",
            "AddSharedComponentManaged",
            "DestroyEntity",
            "RemoveChunkComponentData",
            "RemoveComponent",
            "SetSharedComponent",
            "SetSharedComponentManaged",
            "ToQuery",
        };

        static string[] s_QueryNames { get; } =
        {
            "WithAll",
            "WithAny",
            "WithNone",
            "WithChangeFilter",
        };

        static string FindOperationName(in IEnumerable<string> operationNames)
        {
            return FindSearchWordInCollection(operationNames, s_OperationNames);
        }

        // Returns the first word from searchWords that occurs in the collection
        static string FindSearchWordInCollection(in IEnumerable<string> searchWords, in IEnumerable<string> collection)
        {
            foreach (var word in searchWords)
            {
                if (collection.Contains(word))
                {
                    return word;
                }
            }
            return "";
        }

        public void OnReceiveSyntaxNode(SyntaxNode entitiesSyntaxNode)
        {
            if (entitiesSyntaxNode is IdentifierNameSyntax identifierNameSyntax
                && identifierNameSyntax.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                && identifierNameSyntax.Identifier.Text == "Entities")
            {
                var methodInvocations = entitiesSyntaxNode.GetMethodInvocations();

                var methodName = FindOperationName(methodInvocations.Keys);
                if (methodName != "")
                {
                    var operationSyntaxNode = methodInvocations[methodName].First();
                    var newQueryCandidate = new QueryCandidate
                    {
                        EntitiesSyntaxNode = entitiesSyntaxNode,
                        OperationSyntaxNode = operationSyntaxNode,
                        ContainingSystemType = entitiesSyntaxNode.Ancestors().OfType<TypeDeclarationSyntax>().First(),
                        MethodName = methodName
                    };
                    EntityQueryCandidatesGroupedBySystemType.Add(newQueryCandidate.ContainingSystemType, newQueryCandidate);
                }
            }
        }

        public bool RegisterChangesInSystem(SystemDescription systemDescription)
        {
            var success = true;

            foreach (var candidate in EntityQueryCandidatesGroupedBySystemType[systemDescription.SystemTypeSyntax])
            {
                var methodInvocations = candidate.EntitiesSyntaxNode.GetMethodInvocations();

                // Check if there are any unsupported invocations, such as "WithName", "WithoutBurst", or "Jabberwocky"
                var invalidInvocations = methodInvocations
                    .Select(keyValuePair => keyValuePair.Key)
                    .Where(s => !s_QueryNames.Contains(s) && !s_OperationNames.Contains(s)).ToArray();

                if (invalidInvocations.Length > 0)
                    SystemGeneratorErrors.DC0062(systemDescription, candidate.OperationSyntaxNode.GetLocation(), invalidInvocations, s_QueryNames, candidate.MethodName);

                var entitiesSyntaxNode = candidate.EntitiesSyntaxNode;

                success &= SourceGenHelpers.TryGetAllTypeArgumentSymbolsOfMethod(systemDescription, entitiesSyntaxNode, "WithAll", QueryType.All, out var withAllTypes);
                success &= SourceGenHelpers.TryGetAllTypeArgumentSymbolsOfMethod(systemDescription, entitiesSyntaxNode, "WithAny", QueryType.Any, out var withAnyTypes);
                success &= SourceGenHelpers.TryGetAllTypeArgumentSymbolsOfMethod(systemDescription, entitiesSyntaxNode, "WithNone", QueryType.None, out var withNoneTypes);
                success &= SourceGenHelpers.TryGetAllTypeArgumentSymbolsOfMethod(systemDescription, entitiesSyntaxNode, "WithDisabled", QueryType.Disabled, out var withDisabledTypes);
                success &= SourceGenHelpers.TryGetAllTypeArgumentSymbolsOfMethod(systemDescription, entitiesSyntaxNode, "WithAbsent", QueryType.Absent, out var withAbsentTypes);
                success &= SourceGenHelpers.TryGetAllTypeArgumentSymbolsOfMethod(systemDescription, entitiesSyntaxNode, "WithChangeFilter", QueryType.ChangeFilter, out var withChangeFilterTypes);

                // Create query
                var queryDescription =
                    new SingleArchetypeQueryFieldDescription(new Archetype(withAllTypes, withAnyTypes, withNoneTypes, withDisabledTypes, withAbsentTypes), changeFilterTypes: withChangeFilterTypes);

                // Create member variable and initialization in OnCreateForCompiler
                var queryText = systemDescription.HandlesDescription.GetOrCreateQueryField(queryDescription);

                var invocationText = queryText;
                if (candidate.MethodName != "ToQuery") // "ToQuery" is special: it uses the naked queryText "__query_0"
                {
                    var methodName = FindOperationName(methodInvocations.Keys);
                    if (methodName != "")
                    {
                        // Turn "Entities.WithAll<Foo>().AddComponent<Bar>(myComponentArray)"
                        // into "EntityManager.AddComponent<Bar>(__query_0, myComponentArray)"
                        var v = methodInvocations[methodName];
                        // Assert v.Count == 1
                        invocationText = BuildBulkOperationSyntax(v[0], queryText);
                    }
                }

                // Generate invocation node
                var newExpression = SyntaxFactory.ParseExpression(invocationText);

                // Get owning method and replace calling expression
                systemDescription.ReplaceNodeNonNested(candidate.OperationSyntaxNode, newExpression);
            }

            return success;
        }

        public static string BuildBulkOperationSyntax(InvocationExpressionSyntax node, string queryText)
        {
            var children = new List<SyntaxNode>();
            children.AddRange(node.Expression.ChildNodes().OfType<IdentifierNameSyntax>());
            children.AddRange( node.Expression.ChildNodes().OfType<GenericNameSyntax>());

            var operationText = children.First(); // e.g. "AddComponent<Foo>"

            var argTextList = new List<string>(node.ArgumentList.Arguments.Count+1) { queryText }; // e.g. "__query_0"
            foreach (var arg in node.ArgumentList.Arguments)
                argTextList.Add(arg.ToString()); // Append original arguments, e.g. "__query_0", "typeof(Foo)"

            return $"EntityManager.{operationText}({string.Join(", ", argTextList)})";
        }
    }
}
