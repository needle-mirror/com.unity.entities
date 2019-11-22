using NUnit.Framework;
using System.Linq;
using Unity.Build.Common;
using UnityEditor;

namespace Unity.Entities.Editor.Tests.LiveLink
{
    [TestFixture]
    class LiveLinkBuildSettingsDropdownTests : LiveLinkTestFixture
    {
        [Test]
        public void Should_Filter_BuildSettings()
        {
            var invalid_Emtpy = CreateBuildSettingsAssetWith();
            var invalid_NoTarget = CreateBuildSettingsAssetWith(new ClassicBuildProfile { Target = BuildTarget.NoTarget, Pipeline = CreateBuildPipelineAssetWith(new BuildStepBuildClassicLiveLink()) }, new SceneList(), new GeneralSettings());
            var invalid_NoLiveLinkBuildStep = CreateBuildSettingsAssetWith(new ClassicBuildProfile { Target = BuildTarget.StandaloneWindows, Pipeline = CreateBuildPipelineAssetWith() }, new SceneList(), new GeneralSettings());
            var invalid_MissingRequiredComponents = CreateBuildSettingsAssetWith(new ClassicBuildProfile { Target = BuildTarget.StandaloneWindows, Pipeline = CreateBuildPipelineAssetWith(new BuildStepBuildClassicLiveLink()) });
            var valid = CreateBuildSettingsAssetWith(new ClassicBuildProfile { Target = BuildTarget.StandaloneWindows, Pipeline = CreateBuildPipelineAssetWith(new BuildStepBuildClassicLiveLink()) }, new SceneList(), new GeneralSettings());

            LiveLinkBuildSettingsDropdown.LiveLinkBuildSettingsCache.Reload();

            var validBuildSettings = LiveLinkBuildSettingsDropdown.LiveLinkBuildSettingsCache.BuildSettings.Select(b => b.Asset).ToArray();
            Assert.That(validBuildSettings, Does.Not.Contains(invalid_Emtpy));
            Assert.That(validBuildSettings, Does.Not.Contains(invalid_NoTarget));
            Assert.That(validBuildSettings, Does.Not.Contains(invalid_NoLiveLinkBuildStep));
            Assert.That(validBuildSettings, Does.Not.Contains(invalid_MissingRequiredComponents));
            Assert.That(validBuildSettings, Does.Contain(valid));
        }
    }
}
