using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
#if !UNITY_2021_1_OR_NEWER
using ExternalCSharpCompiler;
#endif
using Unity.CompilationPipeline.Common;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public static class TestCompiler
    {
        internal static string DirectoryForTestDll { get; } = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "IntegrationTestDlls");
        internal const string OutputDllName = "SourceGenerationIntegrationTests.dll";

        static ExternalCompiler Compile(AssemblyInfo assemblyInfo)
        {
            var compiler = new ExternalCompiler();
            compiler.BeginCompiling(assemblyInfo, Array.Empty<string>(), SystemInfo.operatingSystemFamily, Array.Empty<string>());
            return compiler;
        }

        static AssemblyInfo CreateAssemblyInfo(string[] SourceFilesPaths, IEnumerable<string> referencedTypesDllPaths, bool allowUnsafe = false)
        {
            var scriptDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup).Split(';').Where(str => !string.IsNullOrEmpty(str)).ToArray();
            return new AssemblyInfo
            {
                Files = SourceFilesPaths,
                References = ExternalCompiler.GetReferencedSystemDllFullPaths().Concat(referencedTypesDllPaths).ToArray(),
                OutputDirectory = DirectoryForTestDll,
                AllowUnsafeCode = allowUnsafe,
                Name = OutputDllName,
                Defines = scriptDefines
            };
        }

        public static void CleanUp()
        {
            Directory.Delete(DirectoryForTestDll, true);
        }

        public static (bool IsSuccess, CompilerMessage[] CompilerMessages) Compile(string cSharpCode, IEnumerable<Type> referencedTypes, bool allowUnsafe = false)
        {
            return Compile(referencedTypes, allowUnsafe, ($"{AssetDatabase.GenerateUniqueAssetPath(nameof(TestCompiler))}.cs", cSharpCode));
        }

        public static (bool IsSuccess, CompilerMessage[] CompilerMessages) Compile(IEnumerable<Type> referencedTypes, bool allowUnsafe = false, params (string sourceFilePath, string sourceCode)[] cSharpFilePathAndCode)
        {
            var sourceFilePaths = cSharpFilePathAndCode.Select(filePathAndSource => SaveFileAndReturnPath(filePathAndSource.sourceFilePath, filePathAndSource.sourceCode));
            var assemblyInfo = CreateAssemblyInfo(sourceFilePaths.ToArray(), referencedTypes.Select(r => r.Assembly.Location), allowUnsafe);
            var compiler = Compile(assemblyInfo);

            while (!compiler.Poll())
            {
                Task.Delay(TimeSpan.FromMilliseconds(200)).Wait();
            }

            var compilerMessages = compiler.GetCompilerMessages();
            return (IsSuccess: !compilerMessages.Any(), CompilerMessages: compilerMessages);
        }

        static string SaveFileAndReturnPath(string filePath, string cSharpCode)
        {
            var directoryPath = Path.Combine(DirectoryForTestDll, Path.GetDirectoryName(filePath));
            Directory.CreateDirectory(directoryPath);

            var entirePath = Path.Combine(DirectoryForTestDll, filePath);
            File.WriteAllText(entirePath, cSharpCode);

            return entirePath;
        }
    }

    // Dummy type just to make C# happy while we move these tests over to xUnit tests
    // All the test code that uses this type are actually ignored in 2021+.
    // DOTS-5904
#if UNITY_2021_1_OR_NEWER
    class ExternalCompiler
    {
        public void BeginCompiling(AssemblyInfo assemblyInfo, string[] empty, OperatingSystemFamily operatingSystemFamily, string[] strings) =>
            throw new NotImplementedException();

        public CompilerMessage[] GetCompilerMessages() =>
            throw new NotImplementedException();

        public bool Poll() =>
            throw new NotImplementedException();

        public static List<string> GetReferencedSystemDllFullPaths() =>
            throw new NotImplementedException();
    }
#endif
}
