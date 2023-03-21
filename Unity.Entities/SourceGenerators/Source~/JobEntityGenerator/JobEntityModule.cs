#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGenerator.Common;

namespace Unity.Entities.SourceGen.JobEntity
{
    public partial class JobEntityModule : ISystemModule
    {
        Dictionary<TypeDeclarationSyntax, List<JobEntityCandidate>> m_JobEntityInvocationCandidates = new Dictionary<TypeDeclarationSyntax, List<JobEntityCandidate>>();
        public IEnumerable<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> Candidates
        {
            get
            {
                foreach (var kvp in m_JobEntityInvocationCandidates)
                    foreach (var candidate in kvp.Value)
                        yield return (candidate.Node, kvp.Key);
            }
        }
        public bool RequiresReferenceToBurst => true;

        public void OnReceiveSyntaxNode(SyntaxNode node)
        {
            if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccessExpressionSyntax }
                && memberAccessExpressionSyntax.Kind() == SyntaxKind.SimpleMemberAccessExpression
                && (memberAccessExpressionSyntax.Expression is IdentifierNameSyntax || memberAccessExpressionSyntax.Expression is ObjectCreationExpressionSyntax))
            {
                var schedulingMethodName = memberAccessExpressionSyntax.Name.Identifier.ValueText;

                if (Enum.GetNames(typeof(ScheduleMode)).Contains(schedulingMethodName))
                {
                    var containingType = node.AncestorOfKind<TypeDeclarationSyntax>();

                    // Discard if no base type, meaning it can't possible inherit from a System
                    if (containingType.BaseList == null || containingType.BaseList.Types.Count == 0)
                        return;
                    m_JobEntityInvocationCandidates.Add(containingType, new JobEntityCandidate(memberAccessExpressionSyntax));
                }
            }
        }

        readonly struct JobEntityCandidate : ISystemCandidate
        {
            public JobEntityCandidate(MemberAccessExpressionSyntax node) => MemberAccessExpressionSyntax = node;
            public string CandidateTypeName => "IJobEntity";
            public MemberAccessExpressionSyntax MemberAccessExpressionSyntax { get; }
            public SyntaxNode Node => MemberAccessExpressionSyntax;
            public InvocationExpressionSyntax? Invocation => MemberAccessExpressionSyntax.Parent as InvocationExpressionSyntax;
        }

        public bool RegisterChangesInSystem(SystemDescription systemDescription)
        {
            var candidates = m_JobEntityInvocationCandidates[systemDescription.SystemTypeSyntax];
            var validCandidates = new List<KnownJobEntityInfo>(candidates.Count);
            foreach (var candidate in candidates)
                if (KnownJobEntityInfo.TryCreate(ref systemDescription, candidate, out var knownJobEntityInfo))
                    validCandidates.Add(knownJobEntityInfo);

            if (validCandidates.Count > 0)
                systemDescription.Rewriters.Add(new Rewriter(ref systemDescription, validCandidates));

            return validCandidates.Count > 0;
        }
    }
}
