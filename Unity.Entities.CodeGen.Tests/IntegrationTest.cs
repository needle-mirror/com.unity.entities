using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using NUnit.Framework;
using Unity.Entities.Editor;

namespace Unity.Entities.CodeGen.Tests
{
    [TestFixture]
    public abstract class IntegrationTest : LambdaJobsPostProcessorTestBase
    {
        // Make sure to not check this in with true or your tests will always pass!
        readonly bool overwriteExpectationWithReality = false;

        protected abstract string ExpectedPath { get; }
        protected virtual string AdditionalIL => string.Empty;

        static bool IsAssemblyBuiltAsDebug()
        {
            return typeof(IntegrationTest).Assembly
                                          .GetCustomAttributes(typeof(DebuggableAttribute), false)
                                          .Any(debuggableAttribute => ((DebuggableAttribute)debuggableAttribute).IsJITTrackingEnabled);
        }

        protected struct GeneratedType
        {
            public string Name;
            public bool IsNestedType;
            public string ParentTypeName;
        }

        protected void RunSourceGenerationTest(GeneratedType[] generatedTypes, string generatedTypesDllFullPath)
        {
            // Ideally these tests to run in Release codegen or otherwise the generated IL won't be deterministic (due to differences between /optimize+ and /optimize-).
            // We attempt to make the tests generate the same decompiled C# in any case (by making sure all variables are used).
            if (IsAssemblyBuiltAsDebug())
            {
                UnityEngine.Debug.LogWarning(
                    "Integration tests should only be run with release code optimizations turned on for consistent codegen.  " +
                    "Switch your settings in Preferences->General->Code Optimization " +
                    "On Startup (in 2020.1+) to be able to run these tests.");
            }

            foreach (GeneratedType generatedType in generatedTypes)
            {
                string expectationFilePath =
                    generatedType.IsNestedType
                        ? $"{ExpectedPath}/{TrimEnd(generatedType.ParentTypeName, "Authoring")}.expectation.txt"
                        : $"{ExpectedPath}/{TrimEnd(generatedType.Name, "Authoring")}.expectation.txt";

                string expectationFile = Path.GetFullPath(expectationFilePath);

                string typeToDecompile = generatedType.IsNestedType ? generatedType.ParentTypeName : generatedType.Name;
                string decompiledCode = Decompiler.DecompileIntoCSharp(typeToDecompile, generatedTypesDllFullPath).StandardOutput.ReadToEnd();
                string[] decompiledCodeLines = decompiledCode.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

                if (overwriteExpectationWithReality)
                    File.WriteAllText(expectationFile, decompiledCode);
                else
                {
                    if (!File.Exists(expectationFile))
                        Assert.Fail($"Expectation file {expectationFile} does not exist.");
                }

                string expectedCode = File.ReadAllText(expectationFile);
                string[] expectedCodeLines = expectedCode.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

                var validationResult = Validate(expectedCodeLines, decompiledCodeLines);

                if (!validationResult.Success || overwriteExpectationWithReality)
                {
                    LogDebugMessages(decompiledCode);
                }
                if (overwriteExpectationWithReality)
                {
                    continue;
                }
                Assert.IsTrue(validationResult.Success, $"Test failed: {validationResult.FailureReason}.");
            }

            string TrimEnd(string source, string value)
            {
                return !source.EndsWith(value) ? source : source.Remove(source.LastIndexOf(value));
            }
        }

        void LogDebugMessages(string decompiledCode)
        {
            string tempFolder = Path.GetTempPath();
            string path = $@"{tempFolder}decompiled.cs";

            File.WriteAllText(path, decompiledCode + Environment.NewLine + Environment.NewLine + AdditionalIL);

            Console.WriteLine("Actual Decompiled C#: ");
            Console.WriteLine(decompiledCode);

            if (!String.IsNullOrEmpty(AdditionalIL))
            {
                Console.WriteLine("Addition IL: ");
                Console.WriteLine(AdditionalIL);
            }
            UnityEngine.Debug.Log($"Wrote expected C# code to editor log and to {path}");
        }

        static (bool Success, string FailureReason) Validate(IReadOnlyList<string> expectedLines, IReadOnlyList<string> actualLines)
        {
            string failureReason = default;
            bool success = expectedLines.Count == actualLines.Count;

            if (!success)
            {
                failureReason = $"Incorrect number of lines. Make sure the expectation file contains only the C#, not the IL. Expected lines: {expectedLines.Count}, actual lines: {actualLines.Count}";
                Console.WriteLine(failureReason);

                return (false, failureReason);
            }

            Regex attributeRegex = new Regex(@"^[\t, ]*\[[\w]+(\(.*\))*\][\s]*$");

            var actualAttributes = new List<string>();
            var expectedAttributes = new List<string>();

            for (int i = 0; i < actualLines.Count; ++i)
            {
                string actualLine = actualLines[i];
                string expectedLine = expectedLines[i];

                if (attributeRegex.IsMatch(actualLine))
                {
                    actualAttributes.Add(actualLine.Trim());
                    expectedAttributes.Add(expectedLine.Trim());
                    continue;
                }

                if (expectedLine == actualLine)
                {
                    continue;
                }

                failureReason = $"Mismatched line at {i}.\nExpected line:\n\n{expectedLine}\n\nActual line:\n\n{actualLine}\n\n";
                Console.WriteLine(failureReason);

                return (false, failureReason);
            }

            actualAttributes.Sort();
            expectedAttributes.Sort();

            if (expectedAttributes.SequenceEqual(actualAttributes))
            {
                return (true, string.Empty);
            }

            string expectedAttributesStr = String.Join("\n", expectedAttributes);
            string actualAttributesStr = String.Join("\n", actualAttributes);

            failureReason = $"Mismatched attributes.\nExpected attributes:\n\n{expectedAttributesStr}\n\nActual attributes:\n\n {actualAttributesStr}\n\n";
            Console.WriteLine(failureReason);

            return (false, failureReason);
        }

        // TODO: Remove this method once all tests are updated to use its overload, RunTest(string authoringTypeName, string authoringTypeDllFullPath)
        protected void RunPostprocessingTest(TypeReference type)
        {
            // Ideally these tests to run in Release codegen or otherwise the generated IL won't be deterministic (due to differences between /optimize+ and /optimize-).
            // We attempt to make the tests generate the same decompiled C# in any case (by making sure all variables are used).
            if (IsAssemblyBuiltAsDebug())
                UnityEngine.Debug.LogWarning("Integration tests should only be run with release code optimizations turned on for consistent codegen.  Switch your settings in Preferences->External Tools->Editor Attaching (in 2019.3) or Preferences->General->Code Optimization On Startup (in 2020.1+) to be able to run these tests.");

            var expectationFile = Path.GetFullPath($"{ExpectedPath}/{GetType().Name}.expectation.txt");
            var jobCSharp = Decompiler.DecompileIntoCSharpAndIL(type, DecompiledLanguage.CSharpOnly).CSharpCode;
            var actualLines = jobCSharp.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            var shouldOverWrite = overwriteExpectationWithReality;

            if (shouldOverWrite)
                File.WriteAllText(expectationFile, jobCSharp);
            else
            {
                if (!File.Exists(expectationFile))
                    Assert.Fail($"Expectation file {expectationFile} does not exist.");
            }


            string expected = File.ReadAllText(expectationFile);
            var expectedLines = expected.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            var validationResult = Validate(expectedLines, actualLines);

            if (!validationResult.Success || overwriteExpectationWithReality)
            {
                LogDebugMessages(jobCSharp);
            }

            if (shouldOverWrite)
                return;

            Assert.IsTrue(validationResult.Success, $"Test failed: {validationResult.FailureReason}.");
        }
    }
}
