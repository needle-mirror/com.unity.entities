using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.Burst;
using Unity.Entities;
using Unity.Entities.Hybrid;
using Unity.Entities.SourceGen.Aspect;
using VerifyTests;
using VerifyXunit;
using Xunit;

public static class SourceGenTests
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Enable();
    }

    static bool _autoVerify = false;

    static VerifySettings GetSettings(string systemTypeName)
    {
        var settings = new VerifySettings();
        settings.UseDirectory($"{systemTypeName}/Verified");
        if (_autoVerify)
            settings.AutoVerify();

        // Ensure that line breaks and new lines are normalized across platforms
        settings.ScrubLinesWithReplace(
            line =>
            {
                var newLine = line;
                if (line.TrimStart().StartsWith("#line"))
                    newLine = newLine.Replace('/', '\\');

                newLine = newLine.ReplaceLineEndings("\\r\\n");
                return newLine;
            });

        return settings;
    }

    public static Task Verify<T>(string source) where T : ISourceGenerator, new()
    {
        var settings = GetSettings(typeof(T).Name);
        var (driver, compilation) = SetupGeneratorDriver<T>(source);
        var ranGenerator = driver.RunGenerators(compilation);
        var runResults = ranGenerator.GetRunResult();

        Assert.Empty(runResults.Diagnostics);
        Assert.Single(runResults.GeneratedTrees);
        return Verifier.Verify(ranGenerator, settings);
    }

    public static string PrefixSource(string source) =>
        @"
        using Unity.Entities;
        using Unity.Entities.Tests;
        using Unity.Collections;
        " + source;

    public static void CheckForError<T>(string source, string errorCode, string? dependsOnAspectGeneratedSource = null,
        bool referenceBurst = true) where T : ISourceGenerator, new()
    {
        CheckForDiagnostic<T>(source, errorCode, DiagnosticSeverity.Error, dependsOnAspectGeneratedSource, referenceBurst);
    }

    public static void CheckForWarning<T>(string source, string errorCode, string? dependsOnAspectGeneratedSource = null) where T : ISourceGenerator, new()
    {
        CheckForDiagnostic<T>(source, errorCode, DiagnosticSeverity.Warning, dependsOnAspectGeneratedSource, false);
    }

    public static void CheckForInfo<T>(string source, string errorCode, string? dependsOnAspectGeneratedSource = null) where T : ISourceGenerator, new()
    {
        CheckForDiagnostic<T>(source, errorCode, DiagnosticSeverity.Info, dependsOnAspectGeneratedSource, false);
    }

    static void CheckForDiagnostic<T>(string source, string errorCode, DiagnosticSeverity severity,
        string? dependsOnAspectGeneratedSource = null, bool referenceBurst = true) where T : ISourceGenerator, new()
    {
        source = PrefixSource(source);

        var aspectGeneratorCompilationResult = GetAspectGenerationCompilationResult(dependsOnAspectGeneratedSource);
        var (driver, compilation) = SetUpGeneratorDriverAndUpdateCompilation<T>(source,
            aspectGeneratorCompilationResult, referenceBurst);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var c, out var diagnostics);
        Assert.True(diagnostics.Any(diagnostic => diagnostic.Severity == severity && diagnostic.Id == errorCode),
            $"No diagnostic with correct error code: {errorCode}");
    }

    public static void CheckForNoError<T>(string source, string? dependsOnAspectGeneratedSource = null) where T : ISourceGenerator, new()
    {
        source = PrefixSource(source);

        var aspectGeneratorCompilationResult = GetAspectGenerationCompilationResult(dependsOnAspectGeneratedSource);
        var (driver, compilation) = SetUpGeneratorDriverAndUpdateCompilation<T>(source, aspectGeneratorCompilationResult);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var resultingCompilation, out _);

        var compilationDiagnostics = resultingCompilation.GetDiagnostics();
        Assert.False(compilationDiagnostics.Any(diagnostic =>
                diagnostic.Severity != DiagnosticSeverity.Info
                && diagnostic.Id != "CS8019"
                && diagnostic.Id != "CS0105"), // allow for redundant/repeated using statements
            $"Diagnostic found with: {compilationDiagnostics.First()}");

    }

    static readonly string[] s_IgnoreAssemblies =
    {
        "Unity.Entities",
        "Unity.Entities.Hybrid",
        "Unity.Burst"
    };

    static (CSharpGeneratorDriver, CSharpCompilation) SetUpGeneratorDriverAndUpdateCompilation<T>(string source,
        Compilation? aspectGeneratorCompilationResult, bool referenceBurst = true) where T : ISourceGenerator, new()
    {
        source += "\npublic static class __MainClass {public static void Main(){}}";

        var (driver, compilation) = SetupGeneratorDriver<T>(source, referenceBurst:referenceBurst);

        compilation =
            compilation.AddReferences(AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !s_IgnoreAssemblies.Contains(assembly.GetName().Name))
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location)));

        if (aspectGeneratorCompilationResult != null)
            compilation = compilation.AddReferences(aspectGeneratorCompilationResult.ToMetadataReference());

        return (driver, compilation);
    }

    static Compilation? GetAspectGenerationCompilationResult(string? dependentGeneratedSource)
    {
        if (string.IsNullOrEmpty(dependentGeneratedSource))
            return null;

        var (dependentDriver, dependentCompilation) = SetupGeneratorDriver<AspectGenerator>(dependentGeneratedSource, "SourceGen.GeneratedSourceAssembly");

        dependentCompilation =
            dependentCompilation.AddReferences(AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !s_IgnoreAssemblies.Contains(assembly.GetName().Name))
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location)));

        dependentDriver.RunGeneratorsAndUpdateCompilation(dependentCompilation, out Compilation dependentCompilationResult, out _);

        return dependentCompilationResult;
    }

    static (CSharpGeneratorDriver driver, CSharpCompilation compilation) SetupGeneratorDriver<T>(string source,
        string assemblyName = "SourceGen.VerifyTests", bool referenceBurst = true) where T : ISourceGenerator, new()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText($"{source}", path: "Verify.gen.cs");
        var assemblies = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(EntitiesMock).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(EntitiesHybridMock).Assembly.Location)
        };
        if (referenceBurst)
            assemblies.Add(MetadataReference.CreateFromFile(typeof(BurstMock).Assembly.Location));

        var compilation = CSharpCompilation.Create(assemblyName, new[] { syntaxTree }, assemblies);

        var generator = new T();
        var driver = CSharpGeneratorDriver.Create(generator);
        return (driver, compilation);
    }

    public static void Profile<T>(string testSource) where T : ISourceGenerator, new()
    {
        var (driver, compilation) = SetupGeneratorDriver<T>(testSource);

        var ranGenerator = driver.RunGenerators(compilation);

        Assert.Single(ranGenerator.GetRunResult().GeneratedTrees);
    }
}
