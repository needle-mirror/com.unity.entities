using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Unity.Entities.SourceGen.Common.SourceGenHelpers;

namespace Unity.Entities.SourceGen.SystemGenerator.Common
{
    public abstract class SystemRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// Nodes that require tracking. NOTE: These are NOT the nodes you replace, but the ones on which you need to call `trackedSystem.CurrentNode(...)`.
        /// </summary>
        public abstract IEnumerable<SyntaxNode> NodesToTrack { get; }

        /// <summary>
        /// `systemRootNode` is the root node of an `ISystem`/`SystemBase` type where replacements/additions of syntax nodes are required.
        /// </summary>
        /// <returns>The rewritten system.</returns>
        public abstract SyntaxNode VisitTrackedSystem(SyntaxNode systemRootNode, string originalFilePath);
        /// <summary>
        /// Original FilePath a.k.a. non-generated version (used for line directives)
        /// </summary>
        protected string m_OriginalFilePath;

        Dictionary<SyntaxAnnotation, MemberDeclarationSyntax> m_RewrittenMemberAnnotationToSyntaxNode = new Dictionary<SyntaxAnnotation, MemberDeclarationSyntax>();
        public IReadOnlyDictionary<SyntaxAnnotation, MemberDeclarationSyntax> RewrittenMemberAnnotationToSyntaxNode => m_RewrittenMemberAnnotationToSyntaxNode;

        /// <summary>
        /// Updates `RewrittenMemberHashCodeToSyntaxNode` dictionary with info about the newest version of input MemberDeclarationSyntax
        /// </summary>
        protected void RecordChangedMember(MemberDeclarationSyntax changedMember)
        {
            var syntaxAnnotation = changedMember.GetAnnotations(TrackedNodeAnnotationUsedByRoslyn).First();
            m_RewrittenMemberAnnotationToSyntaxNode[syntaxAnnotation] = changedMember;
        }
    }
}
