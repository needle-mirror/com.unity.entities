using System.IO;
using UnityEditor;
using Unity.Build;
using Unity.Build.Common;
using BuildPipeline = Unity.Build.BuildPipeline;

namespace Unity.Scenes.Editor
{
    public static class MenuItemBuildSettings
    {
        const string kBuildSettingsClassic = "Assets/Create/Build/BuildSettings Hybrid";
        const string kBuildPipelineClassicAssetPath = "Packages/com.unity.entities/Unity.Build.Common/Assets/Hybrid.buildpipeline";

        //@TODO: Use ProjectWindowUtil for better creation workflows

        [MenuItem(kBuildSettingsClassic, true)]
        static bool CreateNewBuildSettingsAssetValidationClassic()
        {
            return Directory.Exists(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        [MenuItem(kBuildSettingsClassic)]
        static void CreateNewBuildSettingsAssetClassic()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<BuildPipeline>(kBuildPipelineClassicAssetPath);
            Selection.activeObject = CreateNewBuildSettingsAsset("Classic", new ClassicBuildProfile { Pipeline = pipeline });
        }

        public static BuildSettings CreateNewBuildSettingsAsset(string prefix, params IBuildSettingsComponent[] components)
        {
            var dependency = Selection.activeObject as BuildSettings;
            var path = CreateAssetPathInActiveDirectory(prefix + $"BuildSettings{BuildSettings.AssetExtension}");
            return BuildSettings.CreateAsset(path, (bs) =>
            {
                if (dependency != null)
                {
                    bs.AddDependency(dependency);
                }
                bs.SetComponent(new GeneralSettings());
                bs.SetComponent(new SceneList());
                foreach (var component in components)
                {
                    bs.SetComponent(component.GetType(), component);
                }
            });
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
                        path = Path.Combine(aoPath, defaultFilename);
                    else
                        path = Path.Combine(Path.GetDirectoryName(aoPath), defaultFilename);
                }
            }
            return AssetDatabase.GenerateUniqueAssetPath(path);
        }
    }
}