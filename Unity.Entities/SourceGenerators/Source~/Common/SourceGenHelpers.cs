using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;

namespace Unity.Entities.SourceGen.Common
{
    public static class SourceGenHelpers
    {
        static string s_ProjectPath = Environment.CurrentDirectory;
        public static void SetProjectPath(string projectPath) => s_ProjectPath = projectPath;
        public static string GetProjectPath() => s_ProjectPath;

        public static string GetAccessModifiers(this ISourceGenerationDescription sourceGenerationDescription)
        {
            // Access must be protected unless this assembly has InternalsVisibleTo access to Unity.Entities,
            // in which case it should be `protected internal`
            IAssemblySymbol currentAssembly =
                sourceGenerationDescription.Context.Compilation.Assembly;
            IAssemblySymbol entitiesAssembly =
                currentAssembly
                    .Modules
                    .First()
                    .ReferencedAssemblySymbols
                    .First(asm => asm.Name == "Unity.Entities");

            return entitiesAssembly.GivesAccessTo(currentAssembly) ? "protected internal" : "protected";
        }

        public static string GetTempGeneratedPathToFile(string fileNameWithExtension)
        {
            var tempFileDirectory = Path.Combine(s_ProjectPath, "Temp", "GeneratedCode");
            Directory.CreateDirectory(tempFileDirectory);
            return Path.Combine(tempFileDirectory, fileNameWithExtension);
        }

        public static void WaitForDebugger(this GeneratorExecutionContext context, string inAssembly = null)
        {
            if (inAssembly != null && !context.Compilation.Assembly.Name.Contains(inAssembly))
                return;

            // Debugger.Launch only works on Windows and not in Rider
            while (!Debugger.IsAttached)
                Task.Delay(500).Wait();

            LogInfo($"Debugger attached to assembly: {context.Compilation.Assembly.Name}");
        }

        public static SyntaxList<AttributeListSyntax> AttributeListFromAttributeName(string attributeName) =>
            new SyntaxList<AttributeListSyntax>(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(attributeName)))));

        public static void LogInfo(string message)
        {
            // Ignore IO exceptions in case there is already a lock, could use a named mutex but don't want to eat the performance cost
            try
            {
                using (StreamWriter w = File.AppendText(GetTempGeneratedPathToFile("SourceGen.log")))
                {
                    w.WriteLine(message);
                }
            }
            catch (IOException) { }
        }

        public static void LogError(this GeneratorExecutionContext context, string errorCode, string title, string errorMessage, Location location, string description = "")
        {
            LogInfo($"Error: {errorCode}, {title}, {errorMessage}");
            var message = errorMessage;
            if (errorCode.Contains("ICE"))
                errorMessage = $"Seeing this error indicates a bug in the dots compiler. We'd appreciate a bug report (About->Report a Bug...). Thnx! <3 {message}";

            var rule = new DiagnosticDescriptor(errorCode, title, message, "Source Generator", DiagnosticSeverity.Error, true, description);
            context.ReportDiagnostic(Diagnostic.Create(rule, location));
        }

        public static bool ContainsId(this ImmutableArray<Diagnostic> diags, string id)
            => diags.Any(diag => diag.Id == id);

        public static bool TryParseQualifiedEnumValue<TEnum>(string value, out TEnum result) where TEnum : struct
        {
            string parseString = value;
            int loc = value.LastIndexOf('.');
            if (loc > 0)
                parseString = value.Substring(loc + 1);
            return Enum.TryParse(parseString, out result);
        }

        public static IEnumerable<Enum> GetFlags(this Enum e)
        {
            return Enum.GetValues(e.GetType()).Cast<Enum>().Where(e.HasFlag);
        }

        public static SourceText WithInitialLineDirectiveToGeneratedSource(this SourceText sourceText, string generatedSourceFilePath)
        {
            var firstLine = sourceText.Lines.FirstOrDefault();
            return sourceText.WithChanges(new TextChange(firstLine.Span, $"#line 1 \"{generatedSourceFilePath}\"" + Environment.NewLine + firstLine));
        }
        public static SourceText WithIgnoreUnassignedVariableWarning(this SourceText sourceText)
        {
            var firstLine = sourceText.Lines.FirstOrDefault();
            return sourceText.WithChanges(new TextChange(firstLine.Span, $"#pragma warning disable 0219" + Environment.NewLine + firstLine));
        }
    }
}
