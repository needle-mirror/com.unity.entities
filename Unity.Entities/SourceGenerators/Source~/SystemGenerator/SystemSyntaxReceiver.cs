using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.SystemGenerator.SystemAPI.Query;
using Unity.Entities.SourceGen.JobEntity;
using Unity.Entities.SourceGen.LambdaJobs;
using Unity.Entities.SourceGen.SystemGenerator.SystemAPI.QueryBuilder;
using Unity.Entities.SourceGen.SystemGenerator.SystemAPI;
using Unity.Entities.SourceGen.SystemGenerator.Common;

namespace Unity.Entities.SourceGen.SystemGenerator.EntityQueryBulkOperations
{
    public class SystemSyntaxReceiver : ISyntaxReceiver
    {
        internal HashSet<TypeDeclarationSyntax> ISystemDefinedAsClass = new HashSet<TypeDeclarationSyntax>();
        readonly CancellationToken _cancelationToken;

        internal IReadOnlyCollection<ISystemModule> SystemModules { get; }

        public SystemSyntaxReceiver(CancellationToken cancellationToken)
        {
            _cancelationToken = cancellationToken;
            SystemModules = new ISystemModule[]
            {
                new LambdaJobsModule(),
                new JobEntityModule(),
                new EntityQueryModule(),
                new IdiomaticCSharpForEachModule(),
                new SystemContextSystemModule(),
                new SystemAPIQueryBuilderModule()
            };
        }

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (IsValidSystemType(syntaxNode))
            {
                foreach (var module in SystemModules)
                {
                    _cancelationToken.ThrowIfCancellationRequested();
                    module.OnReceiveSyntaxNode(syntaxNode);
                }
            }

            _cancelationToken.ThrowIfCancellationRequested();
        }

        bool IsValidSystemType(SyntaxNode syntaxNode)
        {
            if (syntaxNode is TypeDeclarationSyntax typeSyntax)
            {
                if (typeSyntax.BaseList == null)
                    return false;

                var hasPartial = false;
                foreach (var modifier in typeSyntax.Modifiers)
                    if (modifier.IsKind(SyntaxKind.PartialKeyword))
                    {
                        hasPartial = true;
                        break;
                    }

                if (!hasPartial)
                    return false;

                // error if class ISystem
                // this can also be an analyzer with codefix in future
                if (syntaxNode is ClassDeclarationSyntax && BaseListContains(typeSyntax, "ISystem"))
                {
                    ISystemDefinedAsClass.Add(typeSyntax);
                    return false;
                }
            }

            return true;
        }

        static bool BaseListContains(TypeDeclarationSyntax typeSyntax, string typeOrInterfaceName)
        {
            Debug.Assert(typeSyntax.BaseList != null, "typeSyntax.BaseList != null");
            foreach (var t in typeSyntax.BaseList.Types)
            {
                if (t.Type.ToString().Split('.').Last() == typeOrInterfaceName)
                    return true;
            }
            return false;
        }
    }
}
