using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.SystemGenerator
{
    public static class SystemGeneratorHelper
    {
        public static IEnumerable<SyntaxTree> GetSyntaxTreesWithCandidates(IEnumerable<ISystemModule> allModules)
            => allModules.SelectMany(module => module.Candidates).Select(node => node.SyntaxNode.SyntaxTree).Distinct();

        public static IEnumerable<TypeDeclarationSyntax> GetSystemTypeInTreeWithCandidate(SyntaxTree syntaxTree, IEnumerable<ISystemModule> allModules)
        {
            var resultSet = new HashSet<TypeDeclarationSyntax>();
            foreach (var module in allModules)
            {
                foreach (var (syntaxNode, systemType) in module.Candidates)
                {
                    if (syntaxNode.SyntaxTree == syntaxTree)
                        resultSet.Add(systemType);
                }
            }

            return resultSet;
        }

        public static IEnumerable<ISystemModule> GetAllModulesAffectingSystemType(TypeDeclarationSyntax systemTypeWithCandidate, List<ISystemModule> allModules,
            GeneratorExecutionContext generatorExecutionContext)
        {
            var resultSet = new HashSet<ISystemModule>();
            foreach (var module in allModules)
            {
                if (module.ShouldRun(generatorExecutionContext.ParseOptions))
                    foreach (var candidate in module.Candidates)
                    {
                        if (candidate.SystemType == systemTypeWithCandidate)
                        {
                            resultSet.Add(module);
                        }
                    }
            }

            return resultSet;
        }
    }
}
