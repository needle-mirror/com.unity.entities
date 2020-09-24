using Bee.Core;
using JetBrains.Annotations;

[UsedImplicitly]
class CustomizerForUnityCore : AsmDefCSharpProgramCustomizer
{
    public override string CustomizerFor => "Unity.Entities";

    public override void CustomizeSelf(AsmDefCSharpProgram program)
    {
        program.NativeProgram.Defines.Add(c => c.Platform == Platform.Windows, "LZ4_DLL_EXPORT");
    }
}
