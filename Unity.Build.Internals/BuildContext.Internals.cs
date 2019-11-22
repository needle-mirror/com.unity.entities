using UnityEditor;

namespace Unity.Build.Internals
{
    internal static class BuildContextInternals
    {
        internal static BuildSettings GetBuildSettings(BuildContext context)
        {
            return context.BuildSettings;
        }

        internal static string GetBuildSettingsGUID(BuildContext context)
        {
            var assetPath = AssetDatabase.GetAssetPath(context.BuildSettings);
            return AssetDatabase.AssetPathToGUID(assetPath);
        }
    }
}
