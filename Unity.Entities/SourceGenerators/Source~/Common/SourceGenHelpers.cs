using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Microsoft.CodeAnalysis.Text;

namespace Unity.Entities.SourceGen.Common
{
    public static class SourceGenHelpers
    {
        static string s_ProjectPath = string.Empty;
        public static string ProjectPath
        {
            get
            {
                if (string.IsNullOrEmpty(s_ProjectPath))
                    throw new Exception("ProjectPath must set before use, this is also only permitted before 2020.");
                return s_ProjectPath;
            }
            private set
            {
                s_ProjectPath = value;
            }
        }

        public static bool InUnity2021OrNewer { get; private set; }
        public static bool CanWriteToProjectPath { get => !string.IsNullOrEmpty(s_ProjectPath); }

        public static void Setup(GeneratorExecutionContext context)
        {
            // needs to be disabled for e.g. Sonarqube static code analysis (which also uses analyzers)
            if (Environment.GetEnvironmentVariable("SOURCEGEN_DISABLE_PROJECT_PATH_OUTPUT") == "1")
            {
                return;
            }

            InUnity2021OrNewer = context.ParseOptions.PreprocessorSymbolNames.Contains("UNITY_2021_1_OR_NEWER");

            if (context.AdditionalFiles.Any() && !string.IsNullOrEmpty(context.AdditionalFiles[0].Path))
            {
                if (InUnity2021OrNewer)
                {
                    // With 2021+, the content of the first additional file contains the project path
                    ProjectPath = context.AdditionalFiles[0].GetText().ToString();
                }
                else
                {
                    // Before 2021, the first additional file path points to the project path
                    ProjectPath = context.AdditionalFiles[0].Path;
                }
            }
        }

        public static string GetTempGeneratedPathToFile(string fileNameWithExtension)
        {
            if (CanWriteToProjectPath)
            {
                var tempFileDirectory = Path.Combine(ProjectPath, "Temp", "GeneratedCode");
                Directory.CreateDirectory(tempFileDirectory);
                return Path.Combine(tempFileDirectory, fileNameWithExtension);
            }
            else
            {
                return Path.Combine("Temp", "GeneratedCode");
            }
        }

        public static void WaitForDebugger(this GeneratorExecutionContext context, string inAssembly = null)
        {
            if (inAssembly != null && !context.Compilation.Assembly.Name.Contains(inAssembly)) return;

            // Debugger.Launch only works on Windows and not in Rider
            SpinWait.SpinUntil(() => Debugger.IsAttached);

            LogInfo($"DEBUG: Connected for assembly: {context.Compilation.Assembly.Name}");
        }

        public static SyntaxList<AttributeListSyntax> GetCompilerGeneratedAttribute()
        {
            return AttributeListFromAttributeName("global::System.Runtime.CompilerServices.CompilerGenerated");
        }

        public static SyntaxList<AttributeListSyntax> AttributeListFromAttributeName(string attributeName) =>
            new SyntaxList<AttributeListSyntax>(AttributeList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(attributeName)))));

        public static void LogInfo(string message)
        {
            if (CanWriteToProjectPath)
            {
                // Ignore IO exceptions in case there is already a lock, could use a named mutex but don't want to eat the performance cost
                try
                {
                    using StreamWriter w = File.AppendText(GetTempGeneratedPathToFile("SourceGen.log"));
                    w.WriteLine(message);
                }
                catch (IOException) { }
            }
        }

        public static void LogError(this GeneratorExecutionContext context, string errorCode, string title, string errorMessage, Location location, string description = "")
        {
            if (errorCode.Contains("ICE"))
                errorMessage = $"Seeing this error indicates a bug in the dots compiler. We'd appreciate a bug report (About->Report a Bug...). Thnx! <3 {errorMessage}";

            context.Log(DiagnosticSeverity.Error, errorCode, title, errorMessage, location, description);
        }

        public static void LogWarning(this GeneratorExecutionContext context, string errorCode, string title, string errorMessage, Location location, string description = "")
            => context.Log(DiagnosticSeverity.Warning, errorCode, title, errorMessage, location, description);

        public static void LogInfo(this GeneratorExecutionContext context, string errorCode, string title, string errorMessage, Location location, string description = "")
            => context.Log(DiagnosticSeverity.Info, errorCode, title, errorMessage, location, description);

        static void Log(this GeneratorExecutionContext context, DiagnosticSeverity diagnosticSeverity, string errorCode, string title, string errorMessage, Location location, string description = "")
        {
            LogInfo($"{diagnosticSeverity}: {errorCode}, {title}, {errorMessage}");
            var rule = new DiagnosticDescriptor(errorCode, title, errorMessage, "Source Generator", diagnosticSeverity, true, description);
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
            return Enum.TryParse(parseString, out result) && Enum.IsDefined(typeof(TEnum), result);
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

        // Stable version of String.GetHashCode
        public static int GetStableHashCode(string str)
        {
            unchecked
            {
                var hash1 = 5381;
                var hash2 = hash1;

                for(var i = 0; i < str.Length && str[i] != '\0'; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1 || str[i+1] == '\0')
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i+1];
                }

                return hash1 + (hash2*1566083941);
            }
        }
    }
}
