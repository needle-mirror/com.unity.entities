using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.IdiomaticCSharpForEach;
using Unity.Entities.SourceGen.JobEntity;
using Unity.Entities.SourceGen.LambdaJobs;
using Unity.Entities.SourceGen.SystemAPIQueryBuilder;
using Unity.Entities.SourceGen.SystemCodegenContext;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.SystemGenerator
{
    public class SystemSyntaxReceiver : ISyntaxReceiver
    {
        internal HashSet<TypeDeclarationSyntax> SystemBaseDerivedTypesWithoutPartialKeyword = new HashSet<TypeDeclarationSyntax>();
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
            if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax)
            {
                if (classDeclarationSyntax.BaseList == null)
                    return false;

                if (BaseListContains(classDeclarationSyntax, "SystemBase") && !classDeclarationSyntax.HasModifier(SyntaxKind.PartialKeyword))
                {
                    SystemBaseDerivedTypesWithoutPartialKeyword.Add(classDeclarationSyntax);
                    return false;
                }

                if (BaseListContains(classDeclarationSyntax, "ISystem"))
                {
                    ISystemDefinedAsClass.Add(classDeclarationSyntax);
                    return false;
                }
            }

            return true;
        }

        static bool BaseListContains(ClassDeclarationSyntax classDeclarationSyntax, string typeOrInterfaceName)
        {
            return classDeclarationSyntax.BaseList.Types.Any(t => t.Type.ToString().Split('.').Last() == typeOrInterfaceName);
        }
    }
}
