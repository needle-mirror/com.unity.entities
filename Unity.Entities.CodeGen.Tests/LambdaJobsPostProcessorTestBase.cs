using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.Entities.CodeGen;

namespace Unity.Entities.CodeGen.Tests
{
    public class PostProcessorTestBase
    {
        protected AssemblyDefinition AssemblyDefinitionFor(Type type)
        {
            var assemblyLocation = type.Assembly.Location;

            var resolver = new LambdaJobsPostProcessorTestBase.OnDemandResolver();
            
            var ad = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(assemblyLocation)), 
                new ReaderParameters(ReadingMode.Immediate)
                {
                    ReadSymbols = true,
                    ThrowIfSymbolsAreNotMatching = true,
                    SymbolReaderProvider = new PortablePdbReaderProvider(),
                    AssemblyResolver = resolver,
                    SymbolStream = PdbStreamFor(assemblyLocation)
                }
            );

            if (!ad.MainModule.HasSymbols)
                throw new Exception("NoSymbols");
            return ad;
        }

        protected TypeDefinition TypeDefinitionFor(Type type)
        {
            var ad = AssemblyDefinitionFor(type);
            var fullName = type.FullName.Replace("+", "/");
            return ad.MainModule.GetType(fullName).Resolve();
        }

        protected TypeDefinition TypeDefinitionFor(string typeName, Type nextToType)
        {
            var ad = AssemblyDefinitionFor(nextToType);
            var fullName = nextToType.FullName.Replace("+", "/");
            fullName = fullName.Replace(nextToType.Name, typeName);
            return ad.MainModule.GetType(fullName).Resolve();
        }

        protected MethodDefinition MethodDefinitionForOnlyMethodOf(Type type)
        {
            return MethodDefinitionForOnlyMethodOfDefinition(TypeDefinitionFor(type));
        }

        protected MethodDefinition MethodDefinitionForOnlyMethodOfDefinition(TypeDefinition typeDefinition)
        {
            var a = typeDefinition.GetMethods().Where(m => !m.IsConstructor && !m.IsStatic).ToList();
            return a.Count == 1 ? a.Single() : a.Single(m=>m.Name == "Test");
        }

        static MemoryStream PdbStreamFor(string assemblyLocation)
        {
            var file = Path.ChangeExtension(assemblyLocation, ".pdb");
            if (!File.Exists(file))
                return null;
            return new MemoryStream(File.ReadAllBytes(file));
        }
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static T EnsureNotOptimizedAway<T>(T x) { return x; }

        private class OnDemandResolver : IAssemblyResolver
        {
            public void Dispose()
            {
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name.Name);
                var fileName = assembly.Location;
                parameters.AssemblyResolver = this;
                parameters.SymbolStream = PdbStreamFor(fileName);
                var bytes = File.ReadAllBytes(fileName);
                return AssemblyDefinition.ReadAssembly(new MemoryStream(bytes), parameters);
            }
        }

        protected static void AssertDiagnosticHasSufficientFileAndLineInfo(List<DiagnosticMessage> errors)
        {
            string diagnostic = errors.Select(dm=>dm.MessageData).SeparateByComma();
            if (!diagnostic.Contains(".cs"))
                Assert.Fail("Diagnostic message had no file info: " + diagnostic);

            var match = Regex.Match(diagnostic, "\\.cs:?\\((?<line>.*?),(?<column>.*?)\\)");
            if (!match.Success)
                Assert.Fail("Diagnostic message had no line info: " + diagnostic);

            var line = int.Parse(match.Groups["line"].Value);
            if (line > 1000)
                Assert.Fail("Unreasonable line number in errormessage: " + diagnostic);
        }
    }

    public class LambdaJobsPostProcessorTestBase : PostProcessorTestBase
    {
        protected void AssertProducesNoError(Type systemType)
        {
            Assert.DoesNotThrow(() =>
            {
                var assemblyDefinition = AssemblyDefinitionFor(systemType);
                var testSystemType = assemblyDefinition.MainModule
                    .GetAllTypes()
                    .Where(TypeDefinitionExtensions.IsComponentSystem)
                    .FirstOrDefault(t => t.Name == systemType.Name);

                foreach (var methodToAnalyze in testSystemType.Methods.ToList())
                {
                    foreach (var forEachDescriptionConstruction in LambdaJobDescriptionConstruction.FindIn(methodToAnalyze))
                    {
                        LambdaJobsPostProcessor.Rewrite(methodToAnalyze, forEachDescriptionConstruction, null);
                    }
                }

                // Write out assembly to memory stream
                // Missing ImportReference errors for types only happens here. 
                var pe = new MemoryStream();
                var pdb = new MemoryStream();
                var writerParameters = new WriterParameters
                {
                    SymbolWriterProvider = new PortablePdbWriterProvider(), SymbolStream = pdb, WriteSymbols = true
                };
                assemblyDefinition.Write(pe, writerParameters);
            });
        }

        void AssertProducesInternal(Type systemType, DiagnosticType type, string[] shouldContains)
        {
            var methodToAnalyze = MethodDefinitionForOnlyMethodOf(systemType);
            var errors = new List<DiagnosticMessage>();

            try
            {
                foreach (var forEachDescriptionConstruction in LambdaJobDescriptionConstruction.FindIn(methodToAnalyze))
                {
                    LambdaJobsPostProcessor.Rewrite(methodToAnalyze, forEachDescriptionConstruction, errors);
                }
            }
            catch (FoundErrorInUserCodeException exc)
            {
                errors.AddRange(exc.DiagnosticMessages);
            }

            Assert.AreEqual(type, errors[0].DiagnosticType);
            

            AssertDiagnosticHasSufficientFileAndLineInfo(errors);
        }

        protected void AssertProducesWarning(Type systemType, params string[] shouldContainErrors)
        {
            AssertProducesInternal(systemType, DiagnosticType.Warning, shouldContainErrors);
        }

        protected void AssertProducesError(Type systemType, params string[] shouldContainErrors)
        {
            AssertProducesInternal(systemType, DiagnosticType.Error, shouldContainErrors);
        }
    }
}