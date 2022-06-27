using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    class LambdaJobsPatchableMethod
    {
        public delegate SyntaxNode GeneratePatchedReplacementSyntaxDelegate(IMethodSymbol methodSymbol, LambdaBodyRewriter rewriter, InvocationExpressionSyntax originalNode);

        public GeneratePatchedReplacementSyntaxDelegate GeneratePatchedReplacementSyntax;
        public ComponentAccessRights AccessRights { get; private set; }
        public string UnpatchedMethod { get; private set; }

        public enum AccessorDataType
        {
            ComponentDataFromEntity,
            BufferFromEntity
        }

        public enum ComponentAccessRights
        {
            ReadOnly,
            ReadWrite,
            GetFromFirstMethodParam
        }

        static string[] GetArguments(InvocationExpressionSyntax originalNode) =>
            originalNode.DescendantNodes().OfType<ArgumentListSyntax>().First().Arguments.Select(arg => arg.Expression.ToString()).ToArray();

        internal static readonly LambdaJobsPatchableMethod[] PatchableMethods =
        {
            new LambdaJobsPatchableMethod()
            {
                UnpatchedMethod = "GetComponent",
                AccessRights = ComponentAccessRights.ReadOnly,
                GeneratePatchedReplacementSyntax = (methodSymbol, rewriter, originalNode) =>
                {
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), true, AccessorDataType.ComponentDataFromEntity);
                    var entityArgument = GetArguments(originalNode)[0];
                    return SyntaxFactory.ParseExpression($"{dataAccessField.FieldName}[{entityArgument}]");
                }
            },
            new LambdaJobsPatchableMethod()
            {
                UnpatchedMethod = "SetComponent",
                AccessRights = ComponentAccessRights.ReadWrite,
                GeneratePatchedReplacementSyntax = (methodSymbol, rewriter, originalNode) =>
                {
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), false, AccessorDataType.ComponentDataFromEntity);
                    var arguments = originalNode.DescendantNodes().OfType<ArgumentListSyntax>().First().Arguments;
                    var (entityArgument, valueArgument) =
                        arguments[0].NameColon?.Name.Identifier.ValueText == "component" ?
                            (arguments[1], arguments[0]) : (arguments[0], arguments[1]);
                    return SyntaxFactory.ParseExpression($"{dataAccessField.FieldName}[{entityArgument.Expression}] = {valueArgument.Expression}");
                }
            },
            new LambdaJobsPatchableMethod()
            {
                UnpatchedMethod = "HasComponent",
                AccessRights = ComponentAccessRights.ReadOnly,
                GeneratePatchedReplacementSyntax = (methodSymbol, rewriter, originalNode) =>
                {
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), true, AccessorDataType.ComponentDataFromEntity);
                    var arguments = GetArguments(originalNode);
                    var entityArgument = arguments[0];
                    return SyntaxFactory.ParseExpression($"{dataAccessField.FieldName}.HasComponent({entityArgument})");
                }
            },
            new LambdaJobsPatchableMethod()
            {
                UnpatchedMethod = "GetComponentDataFromEntity",
                AccessRights = ComponentAccessRights.GetFromFirstMethodParam,
                GeneratePatchedReplacementSyntax = (methodSymbol, rewriter, originalNode) =>
                {
                    var arguments = GetArguments(originalNode);
                    var isReadOnly = arguments.Length > 0 && bool.Parse(arguments[0].ToLower());
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), isReadOnly, AccessorDataType.ComponentDataFromEntity);
                    return SyntaxFactory.ParseExpression($"{dataAccessField.FieldName}");
                }
            },
            new LambdaJobsPatchableMethod()
            {
                UnpatchedMethod = "GetBuffer",
                AccessRights = ComponentAccessRights.ReadWrite,
                GeneratePatchedReplacementSyntax = (methodSymbol, rewriter, originalNode) =>
                {
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), false, AccessorDataType.BufferFromEntity);
                    var entityArgument = GetArguments(originalNode).First();
                    return SyntaxFactory.ParseExpression($"{dataAccessField.FieldName}[{entityArgument}]");
                }
            },
            new LambdaJobsPatchableMethod()
            {
                UnpatchedMethod = "GetBufferFromEntity",
                AccessRights =  ComponentAccessRights.GetFromFirstMethodParam,
                GeneratePatchedReplacementSyntax = (methodSymbol, rewriter, originalNode) =>
                {
                    var arguments = GetArguments(originalNode);
                    var isReadOnly = arguments.Length > 0 && bool.Parse(arguments[0].ToLower());
                    var dataAccessField = rewriter.GetOrCreateDataAccessField(methodSymbol.TypeArguments.First(), isReadOnly, AccessorDataType.BufferFromEntity);
                    return SyntaxFactory.ParseExpression($"{dataAccessField.FieldName}");
                }
            }
        };
    }
}
