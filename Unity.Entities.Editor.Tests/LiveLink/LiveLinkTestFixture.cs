using NUnit.Framework;
using System.IO;
using Unity.Build;
using UnityEditor;
using BuildPipeline = Unity.Build.BuildPipeline;

namespace Unity.Entities.Editor.Tests.LiveLink
{
    abstract class LiveLinkTestFixture
    {
        string m_ValueBeforeTests;

        protected DirectoryInfo TestDirectory;

        [SetUp]
        public void Setup()
        {
            m_ValueBeforeTests = LiveLinkSettings.Instance.SelectedBuildSettingsAssetGuid;
            TestDirectory = new DirectoryInfo("Assets/TestBuildSettings");
            if (!TestDirectory.Exists)
                TestDirectory.Create();
        }

        [TearDown]
        public void Teardown()
        {
            LiveLinkSettings.Instance.SelectedBuildSettingsAssetGuid = m_ValueBeforeTests;
            AssetDatabase.DeleteAsset(TestDirectory.GetRelativePath());
        }

        string GetRandomTestAssetPath(string extension)
        {
            return Path.Combine(TestDirectory.GetRelativePath(), Path.ChangeExtension(Path.GetRandomFileName(), extension));
        }

        protected BuildSettings CreateBuildSettingsAssetWith(params IBuildSettingsComponent[] components)
        {
            return BuildSettings.CreateAsset(GetRandomTestAssetPath(BuildSettings.AssetExtension), (settings) =>
            {
                foreach (var component in components)
                {
                    settings.SetComponent(component.GetType(), component);
                }
            });
        }

        protected BuildPipeline CreateBuildPipelineAssetWith(params IBuildStep[] steps)
        {
            return BuildPipeline.CreateAsset(GetRandomTestAssetPath(BuildPipeline.AssetExtension), (pipeline) =>
            {
                pipeline.BuildSteps.AddRange(steps);
            });
        }
    }
}
