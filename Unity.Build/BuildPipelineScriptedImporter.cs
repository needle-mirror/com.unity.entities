using UnityEditor.Experimental.AssetImporters;

namespace Unity.Build
{
    [ScriptedImporter(1, new[] { BuildPipeline.AssetExtension })]
    class BuildPipelineScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var asset = BuildPipeline.CreateInstance();
            if (BuildPipeline.DeserializeFromPath(asset, context.assetPath))
            {
                context.AddObjectToAsset("asset", asset/*, icon*/);
                context.SetMainObject(asset);
            }
        }
    }
}
