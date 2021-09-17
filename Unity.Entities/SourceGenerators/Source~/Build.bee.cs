using System;
using System.Collections.Generic;
using System.Linq;
using Bee;
using Bee.CSharpSupport;
using Bee.DotNet;
using Bee.Tools;
using Bee.VisualStudioSolution;
using NiceIO;

class Build
{
    // Add your new custom module or system generator module here
    static void AddAdditionalModules()
    {
        AddModule("AuthoringComponent");

        AddSystemGeneratorModule("Sample");
        AddSystemGeneratorModule("JobEntity");
        AddSystemGeneratorModule("LambdaJobs");
    }

    static void Main()
    {
        CSharpProgram.DefaultConfig = new CSharpProgramConfiguration(CSharpCodeGen.Release, Csc.Latest);

        // Create Common modules
        AddModuleInternal("Common");

        AddAdditionalModules();

        SetupSystemGenerator();
        SetupSolution();
    }

    static void SetupSolution()
    {
        var solution = new VisualStudioSolution
        {
            Path = "SourceGenerators.gen.sln",
            Projects =
            {
                StandaloneBeeDriver.BuildProgramProjectFile
            }
        };

        foreach(var module in Modules)
            solution.Projects.Add(module.Value);

        solution.Setup();
    }

    static void SetupSystemGenerator()
    {
        var references = new List<string>(SystemGeneratorModules) { "Common" };
        AddModuleInternal("SystemGenerator", references.ToArray());
    }

    static Dictionary<string, CSharpProgram> Modules = new Dictionary<string, CSharpProgram>();
    static List<string> SystemGeneratorModules = new List<string>();

    static void AddModule(string directoryName, params string[] extraReferences)
    {
        var references = new HashSet<string>(extraReferences) { "Common" };
        AddModuleInternal(directoryName, references.ToArray());
    }

    static void AddSystemGeneratorModule(string directoryName, params string[] extraReferences)
    {
        var references = new List<string>(extraReferences) { "Common" };
        AddModuleInternal(directoryName, references.ToArray());
        SystemGeneratorModules.Add(directoryName);
    }

    static void AddModuleInternal(string directoryName, params string[] extraReferences)
    {
        if (Modules.ContainsKey(directoryName))
            throw new InvalidOperationException($"Module {directoryName} was already added");

        var csharpProgram = new CSharpProgram
        {
            Path = $"../Unity.Entities.SourceGen.{directoryName}.dll",
            Sources = {directoryName},
            References = {new NPath("Infrastructure").Files("*.dll"), Framework.NetStandard20.ReferenceAssemblies},
            CopyReferencesNextToTarget = false,
            Framework = {Framework.FrameworkNone},
            WarningsAsErrors = false
        };

        var finalReferences = extraReferences.Select(reference => Modules[reference]).ToList();

        csharpProgram.References.Add(finalReferences);
        csharpProgram.SetupDefault();
        csharpProgram.ProjectFile.RedirectMSBuildBuildTargetToBee = true;

        var bee = new NPath(typeof(Bee.Core.Architecture).Assembly.Location);
        var isWin = Environment.OSVersion.Platform == PlatformID.Win32Windows || Environment.OSVersion.Platform == PlatformID.Win32NT;
        csharpProgram.ProjectFile.BuildCommand.Set(new Shell.ExecuteArgs()
        {
            Arguments = isWin ? "-no-colors" : "bee.exe -no-colors", Executable = isWin ? bee : "mono", WorkingDirectory = bee.Parent
        });

        Modules[directoryName] = csharpProgram;
    }
}
