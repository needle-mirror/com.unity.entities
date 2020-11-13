using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Unity.Entities.SourceGen
{
    class PatchableMethod
    {
        public delegate SyntaxNode GeneratePatchedReplacementSyntaxDelegate(IMethodSymbol methodSymbol,
            MemberAccessExpressionSyntax replacementMemberAccessSyntax, LambdaBodyRewriter rewriter, InvocationExpressionSyntax originalNode);

        public GeneratePatchedReplacementSyntaxDelegate GeneratePatchedReplacementSyntax;
        public string UnpatchedMethod { get; private set; }

        AccessorDataType DataType { get; set; }

        public enum AccessorDataType
        {
            ComponentDataFromEntity,
            BufferFromEntity
        }

        internal static readonly PatchableMethod[] PatchableMethods =
        {
            new PatchableMethod()
            {
                UnpatchedMethod = "GetComponent",
                DataType = AccessorDataType.ComponentDataFromEntity,
                GeneratePatchedReplacementSyntax = (methodSymbol, replacementMemberAccessSyntax, rewriter, originalNode) =>
                {
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), true, AccessorDataType.ComponentDataFromEntity);
                    var entityArgument = originalNode.DescendantNodes().OfType<ArgumentSyntax>().First();
                    return SyntaxFactory.ParseExpression($"{dataAccessField.ToFieldName()}[{entityArgument}]");
                }
            },
            new PatchableMethod()
            {
                UnpatchedMethod = "SetComponent",
                DataType = AccessorDataType.ComponentDataFromEntity,
                GeneratePatchedReplacementSyntax = (methodSymbol, replacementMemberAccessSyntax, rewriter, originalNode) =>
                {
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), false, AccessorDataType.ComponentDataFromEntity);
                    var arguments = originalNode.DescendantNodes().OfType<ArgumentSyntax>().ToArray();
                    var entityArgument = arguments[0];
                    var valueArgument = arguments[1];
                    return SyntaxFactory.ParseExpression($"{dataAccessField.ToFieldName()}[{entityArgument}] = {valueArgument}");
                }
            },
            new PatchableMethod()
            {
                UnpatchedMethod = "HasComponent",
                DataType = AccessorDataType.ComponentDataFromEntity,
                GeneratePatchedReplacementSyntax = (methodSymbol, replacementMemberAccessSyntax, rewriter, originalNode) =>
                {
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), true, AccessorDataType.ComponentDataFromEntity);
                    var arguments = originalNode.DescendantNodes().OfType<ArgumentSyntax>().ToArray();
                    var entityArgument = arguments[0];
                    return SyntaxFactory.ParseExpression($"{dataAccessField.ToFieldName()}.HasComponent({entityArgument})");
                }
            },
            new PatchableMethod()
            {
                UnpatchedMethod = "GetComponentDataFromEntity",
                DataType = AccessorDataType.ComponentDataFromEntity,
                GeneratePatchedReplacementSyntax = (methodSymbol, replacementMemberAccessSyntax, rewriter, originalNode) =>
                {
                    var arguments = originalNode.DescendantNodes().OfType<ArgumentSyntax>().ToArray();
                    var isReadOnly = arguments.Length > 0 && bool.Parse(arguments[0].ToString().ToLower());
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), isReadOnly, AccessorDataType.ComponentDataFromEntity);
                    return SyntaxFactory.ParseExpression($"{dataAccessField.ToFieldName()}");
                }
            },
            new PatchableMethod()
            {
                UnpatchedMethod = "GetBuffer",
                DataType = AccessorDataType.BufferFromEntity,
                GeneratePatchedReplacementSyntax = (methodSymbol, replacementMemberAccessSyntax, rewriter, originalNode) =>
                {
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), false, AccessorDataType.BufferFromEntity);
                    var entityArgument = originalNode.DescendantNodes().OfType<ArgumentSyntax>().First();
                    return SyntaxFactory.ParseExpression($"{dataAccessField.ToFieldName()}[{entityArgument}]");
                }
            },
            new PatchableMethod()
            {
                UnpatchedMethod = "GetBufferFromEntity",
                DataType = AccessorDataType.BufferFromEntity,
                GeneratePatchedReplacementSyntax = (methodSymbol, replacementMemberAccessSyntax, rewriter, originalNode) =>
                {
                    var arguments = originalNode.DescendantNodes().OfType<ArgumentSyntax>().ToArray();
                    var isReadOnly = arguments.Length > 0 && bool.Parse(arguments[0].ToString().ToLower());
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), isReadOnly, AccessorDataType.BufferFromEntity);
                    return SyntaxFactory.ParseExpression($"{dataAccessField.ToFieldName()}");
                }
            }
        };
    }
}
