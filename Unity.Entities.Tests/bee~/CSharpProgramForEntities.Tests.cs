using Bee.Core;
using JetBrains.Annotations;
using NiceIO;
using Bee.NativeProgramSupport;

[UsedImplicitly]
class CustomizerForEntitiesTests : DotsRuntimeCSharpProgramCustomizer
{
    /// <summary>
    /// Copies all test data from the "TestData" firectory to the output directory of
    /// Unity.Entities.Tests so the test exe can load data from it's application start path
    /// </summary>
    /// <param name="program"></param>
    public override void Customize(DotsRuntimeCSharpProgram program)
    {
        if (program.MainSourcePath.FileName == "Unity.Entities.Tests")
        {
            NPath dataPath = program.MainSourcePath.Combine("TestData");

            foreach (var filePath in dataPath.Files(true))
            {
                program.SupportFiles.Add(new DeployableFile(filePath));
            }
        }
    }
}
