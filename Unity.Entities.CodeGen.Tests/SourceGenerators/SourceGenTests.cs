using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Entities.CodeGen.Tests.SourceGenerationTests;
using UnityEditor.Compilation;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
    public abstract class SourceGenTests
    {
        protected abstract Type[] DefaultCompilationReferenceTypes { get; }
        protected abstract string[] DefaultUsings { get; }

        (bool IsSuccess, CompilerMessage[] CompilerMessages) Compile(string compilationSource, Type[] compilationReferenceTypes = null, bool allowUnsafe = true)
        {
            var compilationSourceWithUsings = $"{string.Join(Environment.NewLine, DefaultUsings.Select(str => $"using {str};"))} {Environment.NewLine} {compilationSource}";
            return TestCompiler.Compile(compilationSourceWithUsings, compilationReferenceTypes ?? DefaultCompilationReferenceTypes, allowUnsafe);
        }

        protected void AssertProducesError(
            string compilationSource,
            string errorName,
            IEnumerable<string> shouldContains,
            Type[] overridingDefaultCompilationReferenceTypes = null,
            bool isWarning = false,
            bool allowMultiple = false)
        {
            var (isSuccess, compilerMessages) = Compile(compilationSource, overridingDefaultCompilationReferenceTypes);

            Assert.IsFalse(isSuccess, "No error messages reported.");
            if (!allowMultiple)
                Assert.That(compilerMessages.Length == 1, $"More than one error message when checking for error \"{errorName}\". {Environment.NewLine} {string.Join(Environment.NewLine, compilerMessages.Select(m => m.message))}");
            Assert.That(compilerMessages.Any(msg => msg.type == (isWarning ? CompilerMessageType.Warning : CompilerMessageType.Error)));
            Assert.That(compilerMessages.Any(msg => msg.message.Contains(errorName)), $"No error message contains error \"{errorName}\" {Environment.NewLine} {string.Join(Environment.NewLine, compilerMessages.Select(m => m.message))}.");

            foreach (var str in shouldContains)
                Assert.That(compilerMessages.Any(msg => msg.message.Contains(str)), $"No error message contains text \"{str}\" {Environment.NewLine} {string.Join(Environment.NewLine, compilerMessages.Select(m => m.message))}.");
        }

        protected void AssertProducesError(string compilationSource, string errorName, params string[] shouldContains)
        {
            AssertProducesError(compilationSource, errorName, shouldContains, null);
        }

        protected void AssertProducesWarning(string compilationSource, string errorName, params string[] shouldContains)
        {
            AssertProducesError(compilationSource, errorName, shouldContains, null, true);
        }

        protected void AssertProducesNoError(string compilationSource, IEnumerable<string> overrideDefaultUsings = null, bool allowUnsafe = false)
        {
            var usings = overrideDefaultUsings ?? DefaultUsings;
            var compilationSourceWithUsings = $"{string.Join(Environment.NewLine, usings.Select(str => $"using {str};"))} {Environment.NewLine} {compilationSource}";

            var (isSuccess, compilerMessages) = TestCompiler.Compile(compilationSourceWithUsings, DefaultCompilationReferenceTypes, allowUnsafe);

            Assert.IsTrue(isSuccess, $"Messages: {Environment.NewLine} {string.Join(Environment.NewLine, compilerMessages.Select(m => m.message))}");
        }

        protected void AssertProducesNoError(IEnumerable<string> overrideDefaultUsings = null, bool allowUnsafe = false,
            params (string filePath, string sourceCode)[] source)
        {
            var usings = overrideDefaultUsings ?? DefaultUsings;
            var sourcesWithUsings = source.Select(source =>
                (source.filePath, $"{string.Join(Environment.NewLine, usings.Select(str => $"using {str};"))} {Environment.NewLine} {source.sourceCode}"));

            var (isSuccess, compilerMessages) = TestCompiler.Compile(DefaultCompilationReferenceTypes, allowUnsafe, sourcesWithUsings.ToArray());

            Assert.IsTrue(isSuccess, $"Messages: {Environment.NewLine} {string.Join(Environment.NewLine, compilerMessages.Select(m => m.message))}");
        }
    }
}
