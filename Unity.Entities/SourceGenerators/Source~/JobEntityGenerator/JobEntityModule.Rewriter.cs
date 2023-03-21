using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.SystemGenerator.Common;

namespace Unity.Entities.SourceGen.JobEntity
{
    public partial class JobEntityModule
    {
        class Rewriter : SystemRewriter
        {
            readonly Dictionary<SyntaxNode, KnownJobEntityInfo> m_ScheduleToKnownCandidate;
            readonly IReadOnlyCollection<KnownJobEntityInfo> m_KnownJobEntityInfos;
            SystemDescription m_SystemDescription;

            bool m_HasChangedMember;
            SyntaxNode m_ReplacementNode;
            SyntaxNode m_NodeToReplace;

            public override IEnumerable<SyntaxNode> NodesToTrack => m_KnownJobEntityInfos.Select(info => info.Node);
            public Rewriter(ref SystemDescription systemDescription, IReadOnlyCollection<KnownJobEntityInfo> knownJobEntityInfos)
            {
                m_SystemDescription = systemDescription;
                m_KnownJobEntityInfos = knownJobEntityInfos;
                m_ScheduleToKnownCandidate = new Dictionary<SyntaxNode, KnownJobEntityInfo>();
            }

            // By the time this method is invoked, the system has already been rewritten at least once.
            // In other words, the `systemRootNode` argument passed to this method is the root node of the REWRITTEN system --
            // i.e., a copy of the original system with changes applied.
            public override SyntaxNode VisitTrackedSystem(SyntaxNode systemRootNode, string originalFilePath)
            {
                m_OriginalFilePath = originalFilePath;

                foreach (var knownJobEntityInfo in m_KnownJobEntityInfos)
                {
                    var nodeInNewestTreeVersion = systemRootNode.GetCurrentNodes(knownJobEntityInfo.Node).FirstOrDefault() ?? knownJobEntityInfo.Node;
                    m_ScheduleToKnownCandidate[nodeInNewestTreeVersion] = knownJobEntityInfo;
                }

                return Visit(systemRootNode);
            }

            int m_UniqueIndex;
            public override SyntaxNode Visit(SyntaxNode syntaxNode)
            {
                if (syntaxNode == null)
                    return null;

                var replacedNodeAndChildren = base.Visit(syntaxNode);

                // If the current node is a node we want to replace -- e.g. `someJob.Schedule()`
                if (replacedNodeAndChildren is MemberAccessExpressionSyntax newestSchedulingNode && m_ScheduleToKnownCandidate.TryGetValue(syntaxNode, out var description))
                {
                    // Replace the current node
                    var replacementNode = description.GetAndAddScheduleExpression(ref m_SystemDescription, m_UniqueIndex++, newestSchedulingNode);
                    if (replacementNode != null)
                    {
                        m_ReplacementNode = replacementNode;
                        m_NodeToReplace = syntaxNode.Parent;
                    }
                }

                if (syntaxNode == m_NodeToReplace)
                {
                    replacedNodeAndChildren = m_ReplacementNode;
                    m_HasChangedMember = true;
                }

                // If we have performed any replacements, we need to update the `RewrittenMemberHashCodeToSyntaxNode` dictionary accordingly
                if (replacedNodeAndChildren is MemberDeclarationSyntax memberDeclarationSyntax && m_HasChangedMember)
                {
                    RecordChangedMember(memberDeclarationSyntax);
                    m_HasChangedMember = false;
                }
                return replacedNodeAndChildren;
            }
        }
    }
}
