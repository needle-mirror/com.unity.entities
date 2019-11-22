using System.IO;
using UnityEditor;

namespace Unity.Build.Common
{
    static class MenuItemBuildPipeline
    {
        const string k_CreateBuildPipelineAsset = "Assets/Create/Build/BuildPipeline";

        [MenuItem(k_CreateBuildPipelineAsset, true)]
        static bool CreateBuildPipelineAsset_Validation()
        {
            return Directory.Exists(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        [MenuItem(k_CreateBuildPipelineAsset)]
        static void CreateBuildPipelineAsset()
        {
            Selection.activeObject = BuildPipeline.CreateAsset(CreateAssetPathInActiveDirectory($"BuildPipeline{BuildPipeline.AssetExtension}"));
        }

        static string CreateAssetPathInActiveDirectory(string defaultFilename)
        {
            string path = null;
            if (Selection.activeObject != null)
            {
                var aoPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (!string.IsNullOrEmpty(aoPath))
                {
                    if (Directory.Exists(aoPath))
                    {
                        path = Path.Combine(aoPath, defaultFilename);
                    }
                    else
                    {
                        path = Path.Combine(Path.GetDirectoryName(aoPath), defaultFilename);
                    }
                }
            }
            return AssetDatabase.GenerateUniqueAssetPath(path);
        }
    }
}
