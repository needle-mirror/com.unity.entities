using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Unity.Entities.SourceGen.SystemGenerator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PartialStructWarningSuppressor : DiagnosticSuppressor
    {
        static readonly SuppressionDescriptor _partialStructWarningRule = new SuppressionDescriptor("SPDC0282", "CS0282",
            "Some DOTS types utilize codegen requiring the type to be partial.");

        static readonly string[] _allowedPartialStructInterfaces = { "ISystem", "ISystemBase", "IJobEntity" };

        public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => ImmutableArray.Create(_partialStructWarningRule);

        public override void ReportSuppressions(SuppressionAnalysisContext context)
        {
            foreach (var diagnostic in context.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == _partialStructWarningRule.SuppressedDiagnosticId))
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var node = diagnostic.Location.SourceTree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);
                if (node is StructDeclarationSyntax structSyntax)
                {
                    if (structSyntax.BaseList != null && structSyntax.BaseList.Types.Any(type => _allowedPartialStructInterfaces.Contains(type.ToString())))
                        context.ReportSuppression(Suppression.Create(_partialStructWarningRule, diagnostic));
                }
            }
        }
    }
}
