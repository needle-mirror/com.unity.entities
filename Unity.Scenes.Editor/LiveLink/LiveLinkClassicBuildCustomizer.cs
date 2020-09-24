using System;
using System.IO;
using System.Linq;
using Unity.Build.Classic;
using Unity.Scenes.Editor.Build;
using UnityEditor;

namespace Unity.Scenes.Editor
{
    class LiveLinkClassicBuildCustomizer : ClassicBuildPipelineCustomizer
    {
        // This is a dirty dirty hack to do type preservation only for LL IL2CPP builds
        // TODO: Currently don't have a BuildContext in IUnityLinkerProcessor to know if we're doing a LiveLink build
        // incremental team/build settings team need to add this, or give a better way to do preservation for IL2CPP
        // That can be customised per build
        public static bool IsLiveLinkBuild = false;

        public override Type[] UsedComponents { get; } =
        {
            typeof(LiveLink)
        };

        public override void RegisterAdditionalFilesToDeploy(Action<string, string> registerAdditionalFileToDeploy)
        {
            if (!Context.HasComponent<LiveLink>())
                return;

            var tempFile = Path.Combine(WorkingDirectory, LiveLinkUtility.LiveLinkBootstrapFileName);
            LiveLinkUtility.WriteBootstrap(tempFile, new GUID(Context.BuildConfigurationAssetGUID));
            registerAdditionalFileToDeploy(tempFile, Path.Combine(StreamingAssetsDirectory, LiveLinkUtility.LiveLinkBootstrapFileName));
        }

        const string k_EmptyScenePath = "Packages/com.unity.entities/Unity.Scenes.Editor/LiveLink/Assets/empty.unity";

        public override string[] ModifyEmbeddedScenes(string[] scenes)
        {
            if (!Context.HasComponent<LiveLink>())
                return scenes;

            var nonLiveLinkable = scenes.Where(s => !SceneImporterData.CanLiveLinkScene(s)).ToArray();

            if (nonLiveLinkable.Length > 0)
                return nonLiveLinkable;

            return new[] { k_EmptyScenePath};
        }

        public override BuildOptions ProvideBuildOptions()
        {
            if (!Context.HasComponent<LiveLink>())
                return BuildOptions.None;
            return BuildTarget == BuildTarget.Android
                ? BuildOptions.WaitForPlayerConnection
                : BuildOptions.ConnectToHost;
        }

        public override void OnBeforeBuild()
        {
            if (Context.HasComponent<LiveLink>())
            {
                IsLiveLinkBuild = true;
                if (Context.HasComponent<ClassicCodeStrippingOptions>())
                {
                    UnityEngine.Debug.LogWarning($"ClassicCodeStrippingOptions are not compatible with LiveLink! LiveLink will always set ManagedStrippingLevel to {ManagedStrippingLevel.Disabled}. This component will be ignored.");
                }
                Context.SetComponent(new ClassicCodeStrippingOptions() { ManagedStrippingLevel = ManagedStrippingLevel.Disabled });

#if PLATFORM_SWITCH
                throw new InvalidOperationException("LiveLink is not yet supported on Switch.");
#endif
            }
        }
    }
}
