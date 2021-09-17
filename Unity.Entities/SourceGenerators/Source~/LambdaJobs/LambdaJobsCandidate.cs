using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    public enum LambdaJobKind
    {
        Job,
        Entities
    }

    interface ICandidate
    {
        SyntaxNode SyntaxNode { get; }
        public TypeDeclarationSyntax ContainingSystemType { get; }
    }

    interface ISystemCandidate : ICandidate
    {
        Dictionary<string, List<InvocationExpressionSyntax>> MethodInvocations { get; }
    }

    struct SingletonAccessCandidate : ICandidate
    {
        public SyntaxNode SyntaxNode { get; set; }
        public TypeDeclarationSyntax ContainingSystemType { get; set; }
        public SingletonAccessType SingletonAccessType { get; set; }
    }

    public struct LambdaJobsCandidate : ISystemCandidate
    {
        public LambdaJobKind LambdaJobKind { get; set; }
        public SyntaxNode SyntaxNode { get; set; }
        public TypeDeclarationSyntax ContainingSystemType { get; set; }
        public Dictionary<string, List<InvocationExpressionSyntax>> MethodInvocations { get; set; }
    }
}
