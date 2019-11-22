using UnityEditor.Experimental.AssetImporters;

namespace Unity.Build
{
    [ScriptedImporter(1, new[] { BuildSettings.AssetExtension })]
    class BuildSettingsScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var asset = BuildSettings.CreateInstance();
            if (BuildSettings.DeserializeFromPath(asset, context.assetPath))
            {
                context.AddObjectToAsset("asset", asset/*, icon*/);
                context.SetMainObject(asset);
            }
        }
    }
}
