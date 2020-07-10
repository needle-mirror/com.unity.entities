using System.IO;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

namespace Unity.Scenes.Tests
{
    [ScriptedImporter(2, "extDontMatter_TestImporter")]
    internal class TestImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var path = ctx.GetResultPath("output");
            File.WriteAllBytes(path, File.ReadAllBytes(ctx.assetPath));

        }
    }
}
