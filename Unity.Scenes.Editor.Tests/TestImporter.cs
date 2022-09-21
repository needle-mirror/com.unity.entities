using System.IO;
using UnityEditor.AssetImporters;

namespace Unity.Scenes.Tests
{
    [ScriptedImporter(2, "extDontMatter_TestImporter")]
    internal class TestImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
#if UNITY_2022_1_OR_NEWER
            var path = ctx.GetOutputArtifactFilePath("output");
#else
            var path = ctx.GetResultPath("output");
#endif
            File.WriteAllBytes(path, File.ReadAllBytes(ctx.assetPath));

        }
    }
}
