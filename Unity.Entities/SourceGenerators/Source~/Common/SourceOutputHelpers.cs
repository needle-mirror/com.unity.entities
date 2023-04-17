using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Unity.Entities.SourceGen.Common
{
    public static class SourceOutputHelpers
    {
        // Please do not make these public, everything should depend calling Setup on this static type correctly.
        // These are typically setup the first time the generator runs (or every run), they rely on common state
        // across a compilation so there should be no issue if they are setup multiple times from multiple generators.
        static string s_ProjectPath = string.Empty;
        static bool CanWriteToProjectPath => !string.IsNullOrEmpty(s_ProjectPath);
        static bool s_OutputSourceGenFiles;

        // All setup should happen here for source output
        public static void Setup(ParseOptions parseOptions, ImmutableArray<AdditionalText> additionalFiles = default)
        {
            // needs to be disabled for e.g. Sonarqube static code analysis (which also uses analyzers)
            if (Environment.GetEnvironmentVariable("SOURCEGEN_DISABLE_PROJECT_PATH_OUTPUT") == "1")
                return;

            var isDotsRuntime = parseOptions.PreprocessorSymbolNames.Contains("UNITY_DOTSRUNTIME");
            var outputSourceGenFiles = parseOptions.PreprocessorSymbolNames.Contains("DOTS_OUTPUT_SOURCEGEN_FILES");

            if (additionalFiles == null || !additionalFiles.Any() || string.IsNullOrEmpty(additionalFiles[0].Path))
                return;

            // Only output source in the case where we have the symbol define enabled
            if (outputSourceGenFiles)
            {
                s_ProjectPath =
                    (!isDotsRuntime
                        ? additionalFiles[0].GetText().ToString()
                        : additionalFiles[0].Path).Replace('\\', '/');
                s_OutputSourceGenFiles = true;
            }
            else
                s_OutputSourceGenFiles = false;
        }

        static string GetGeneratedCodePath()
        {
            string generatedCodePath;
            if (!CanWriteToProjectPath)
                generatedCodePath = Path.Combine("Temp", "GeneratedCode");
            else
            {
                generatedCodePath = Path.Combine(s_ProjectPath, "Temp", "GeneratedCode");
                Directory.CreateDirectory(generatedCodePath);
            }
            return generatedCodePath;
        }

        // Output as generated source file for debugging/inspection (used by incremental generators)
        public static void OutputSourceToFile(string generatedSourceFilePath, Func<string> sourceTextProvider)
        {
            if (!CanWriteToProjectPath || !s_OutputSourceGenFiles)
                return;

            try
            {
                LogInfoToSourceGenLog($"Outputting generated source to file {generatedSourceFilePath}...");
                File.WriteAllText(generatedSourceFilePath, sourceTextProvider());
            }
            catch (IOException ioException)
            {
                // emit Entities exceptions as info but don't block compilation or generate error to fail tests
                LogInfoToSourceGenLog(
                    @$"Error trying to write generated source for file {generatedSourceFilePath}
                    {ioException.ToUnityPrintableString()}...");
            }
        }

        public static string GetGeneratedSourceFileName(this SyntaxTree syntaxTree, string generatorName, SyntaxNode node)
            => GetGeneratedSourceFileName(syntaxTree, generatorName, node.GetLocation().GetLineSpan().StartLinePosition.Line);

        public static string GetGeneratedSourceFileName(this SyntaxTree syntaxTree, string generatorName, int salting = 0)
        {
            var (isSuccess, fileName) = TryGetFileNameWithoutExtension(syntaxTree);
            var stableHashCode = SourceGenHelpers.GetStableHashCode(syntaxTree.FilePath) & 0x7fffffff;
            var postfix = generatorName.Length > 0 ? $"__{generatorName}" : String.Empty;

            if (isSuccess)
                fileName = $"{fileName}{postfix}_{stableHashCode}{salting}.g.cs";
            else
                fileName = Path.Combine($"{Path.GetRandomFileName()}{postfix}", ".g.cs");

            return fileName;
        }

        public static string GetGeneratedSourceFilePath(this SyntaxTree syntaxTree, string assemblyName, string generatorName)
        {
            var fileName = GetGeneratedSourceFileName(syntaxTree, generatorName);

            if (CanWriteToProjectPath && s_OutputSourceGenFiles)
            {
                var generatedCodePath = GetGeneratedCodePath();
                var saveToDirectory = $"{generatedCodePath}/{assemblyName}/";
                Directory.CreateDirectory(saveToDirectory);
                return $"{saveToDirectory}/{fileName}";
            }

            return $"Temp/GeneratedCode/{assemblyName}/{fileName}";
        }

        static (bool IsSuccess, string FileName) TryGetFileNameWithoutExtension(SyntaxTree syntaxTree)
        {
            var fileName = Path.GetFileNameWithoutExtension(syntaxTree.FilePath);
            return (IsSuccess: true, fileName);
        }

        public static void LogInfoToSourceGenLog(string message)
        {
            if (!s_OutputSourceGenFiles || !CanWriteToProjectPath)
                return;

            // Ignore IO exceptions in case there is already a lock, could use a named mutex but don't want to eat the performance cost
            try
            {
                string generatedCodePath = GetGeneratedCodePath();
                var sourceGenLogPath = Path.Combine(generatedCodePath, "SourceGen.log");
                using var writer = File.AppendText(sourceGenLogPath);
                writer.WriteLine(message);
            }
            catch (IOException) { }
        }
    }
}
