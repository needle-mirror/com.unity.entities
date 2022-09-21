namespace Unity.Scenes.Editor
{
    // Collection of version-gated helpers to facilitate the move to Unity 2022.X
    static class TrunkExtensions
    {
#if !UNITY_2022_1_OR_NEWER
        // Allows the new version of the API to be backward-compatible
        public static string GetOutputArtifactFilePath(this UnityEditor.AssetImporters.AssetImportContext context, string fileName)
        {
            return context.GetResultPath(fileName);
        }
#endif
    }
}
