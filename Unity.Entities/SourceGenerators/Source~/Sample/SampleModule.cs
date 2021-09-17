using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.Sample
{
    public class SampleModule : ISystemModule
    {
        public IEnumerable<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> Candidates => m_Candidates;

        public bool RequiresReferenceToBurst
        {
            get => false;
        }

        List<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> m_Candidates = new List<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)>();

        public void OnReceiveSyntaxNode(SyntaxNode node)
        {
            if (node.Kind() == SyntaxKind.InvocationExpression)
            {
                var invocationExpressionSyntax = (InvocationExpressionSyntax) node;
                if (!invocationExpressionSyntax.Expression.GetText().ToString().Contains("GetComponentDataPtrOfFirstChunk"))
                    return;

                var parent = node.Ancestors().OfType<TypeDeclarationSyntax>().First();
                if (parent.Identifier.ValueText == "SampleSystemModuleTest")
                    m_Candidates.Add((node, parent));
            }
        }

        public bool GenerateSystemType(SystemGeneratorContext systemGeneratorContext)
        {
            var count = 0;
            foreach (var candidate in m_Candidates)
            {
                var invocationExpressionSyntax = (InvocationExpressionSyntax) candidate.SyntaxNode;
                var symbolInfo = systemGeneratorContext.SemanticModel.GetSymbolInfo(invocationExpressionSyntax.Expression);
                var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
                var fullTypeName = methodSymbol?.TypeArguments[0].ToFullName();
                var typeSymbol = (INamedTypeSymbol) methodSymbol?.TypeArguments[0];

                // Create query and typehandle for type
                var queryField = systemGeneratorContext.GetOrCreateQueryField(
                    new EntityQueryDescription
                    {
                        All = new [] {(typeSymbol, false)}
                    }
                );
                var componentTypeHandleField = systemGeneratorContext.GetOrCreateComponentTypeField(typeSymbol, false);

                // Generate new method code
                var newMethodName = $"__GetComponentDataPtrOfFirstChunk__{count++}";
                var newMethodCode = @$"
                {fullTypeName}* {newMethodName}()
                {{
                    {componentTypeHandleField}.Update(this);
                    var archetypeChunks = {queryField}.CreateArchetypeChunkArray(Unity.Collections.Allocator.Temp);
                    var archetypeChunk = archetypeChunks[0];
                    var result = ({fullTypeName}*)archetypeChunk.GetComponentDataPtrRW(ref {componentTypeHandleField});
                    archetypeChunks.Dispose();
                    return result;
                }}";
                var newMethodNode = SyntaxFactory.ParseMemberDeclaration(newMethodCode);
                systemGeneratorContext.NewMembers.Add(newMethodNode);

                // Generate invocation node
                var invocationCode = $"{newMethodName}()";
                var newExpression = SyntaxFactory.ParseExpression(invocationCode);

                // Get owning method and replace calling expression
                systemGeneratorContext.ReplaceNodeInMethod(invocationExpressionSyntax, newExpression);
            }

            return true;
        }

        public bool ShouldRun(ParseOptions parseOptions) => true;
    }
}
