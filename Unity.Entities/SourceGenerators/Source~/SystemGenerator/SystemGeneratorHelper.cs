using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.SystemGenerator.Common;

namespace Unity.Entities.SourceGen.SystemGenerator
{
    public static class SystemGeneratorHelper
    {
        public static IEnumerable<SyntaxTree> GetSyntaxTreesWithCandidates(IEnumerable<ISystemModule> allModules)
            => allModules.SelectMany(module => module.Candidates).Select(node => node.SyntaxNode.SyntaxTree).Distinct();

        public static IEnumerable<TypeDeclarationSyntax> GetSystemTypesInTree(SyntaxTree syntaxTree, IEnumerable<ISystemModule> allModules)
        {
            return
                allModules
                    .SelectMany(m => m.Candidates)
                    .Where(c => c.SyntaxNode.SyntaxTree == syntaxTree)
                    .Select(c => c.SystemType)
                    .Distinct();
        }

        public static IEnumerable<ISystemModule> GetAllModulesWithCandidatesInSystemType(
            TypeDeclarationSyntax systemTypeWithCandidate, IEnumerable<ISystemModule> allModules, GeneratorExecutionContext generatorExecutionContext)
        {
            return
                allModules
                    .Where(m => m.Candidates.Any(c => c.SystemType == systemTypeWithCandidate))
                    .Distinct();
        }
    }
}
