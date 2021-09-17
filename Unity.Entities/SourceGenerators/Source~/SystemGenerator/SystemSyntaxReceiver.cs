using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.JobEntity;
using Unity.Entities.SourceGen.LambdaJobs;
using Unity.Entities.SourceGen.Sample;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.SystemGenerator
{
    public class SystemSyntaxReceiver : ISyntaxReceiver
    {
        internal HashSet<TypeDeclarationSyntax> SystemBaseDerivedTypesWithoutPartialKeyword = new HashSet<TypeDeclarationSyntax>();

        internal List<ISystemModule> SystemModules { get; }

        public SystemSyntaxReceiver()
        {
            SystemModules = new List<ISystemModule>()
            {
                new LambdaJobsModule(),
                new JobEntityModule(),
                new SampleModule()
            };
        }

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            foreach (var module in SystemModules)
                module.OnReceiveSyntaxNode(syntaxNode);

            CheckForSystemWithoutPartial(syntaxNode);
        }

        void CheckForSystemWithoutPartial(SyntaxNode syntaxNode)
        {
            switch (syntaxNode)
            {
                case ClassDeclarationSyntax classDeclarationSyntax:
                {
                    if (classDeclarationSyntax.BaseList == null)
                    {
                        break;
                    }

                    var isDerivedFromSystemBase = classDeclarationSyntax.BaseList.Types.Any(t => t.Type.ToString().Split('.').Last() == "SystemBase");
                    if (isDerivedFromSystemBase && !classDeclarationSyntax.HasModifier(SyntaxKind.PartialKeyword))
                    {
                        SystemBaseDerivedTypesWithoutPartialKeyword.Add(classDeclarationSyntax);
                    }

                    break;
                }
            }
        }
    }
}
