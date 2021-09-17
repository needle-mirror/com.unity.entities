#if ENABLE_ACTIVE_BUILD_CONFIG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Build;
using Unity.Build.Classic;
using UnityEditor;

namespace Unity.Entities.Editor
{
    static class ActiveBuildConfiguration
    {
        static readonly string s_MissingPlatformPackage = L10n.Tr("Missing Platform Package");
        static readonly string s_MissingPlatformModule = L10n.Tr("Missing Platform Module");
        static readonly string s_InstallPackage = L10n.Tr("Install Package");
        static readonly string s_InstallWithUnityHub = L10n.Tr("Install with Unity Hub");
        static readonly string s_OpenDownloadPage = L10n.Tr("Open Download Page");
        static readonly string s_Close = L10n.Tr("Close");

        static List<BuildConfiguration> m_BuildConfigs;

        public static BuildConfiguration Current
        {
            get => BuildConfiguration.GetActive();
            set
            {
                var result = BuildConfiguration.SetActive(value);
                if (result)
                    return;

                var platform = value.GetPlatform();
                if (platform == null)
                    return;

                if (result is ResultPackageNotInstalled)
                {
                    var message = $"The selected build configuration platform requires {platform.DisplayName} platform package to be installed. If you don't want to, you can hide this build configuration in the play options menu.";
                    var choice = EditorUtility.DisplayDialogComplex(s_MissingPlatformPackage, message, s_InstallPackage, s_Close, ActiveBuildConfigurationWindow.Title);
                    switch (choice)
                    {
                        case 0: // Install
                            platform.InstallPackage();
                            break;
                        case 1: // Close
                            break;
                        case 2: // Play Options
                            ActiveBuildConfigurationWindow.GetOrCreate().Show();
                            break;
                    }
                }

                if (result is ResultModuleNotInstalled)
                {
                    var message = $"The selected build configuration platform requires {platform.DisplayName} platform module to be installed. If you don't want to, you can hide this build configuration in the play options menu.";
                    var installFromHub = PlatformExtensions.IsEditorInstalledWithHub && platform.IsPublic;
                    var choice = EditorUtility.DisplayDialogComplex(s_MissingPlatformModule, message, installFromHub ? s_InstallWithUnityHub : s_OpenDownloadPage, s_Close, ActiveBuildConfigurationWindow.Title);
                    switch (choice)
                    {
                        case 0: // Install
                            platform.InstallModule();
                            break;
                        case 1: // Close
                            break;
                        case 2: // Play Options
                            ActiveBuildConfigurationWindow.GetOrCreate().Show();
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Retrieve all build configurations that are buildable.
        /// A build configuration requires a build pipeline to be buildable.
        /// </summary>
        /// <remarks>
        /// This method uses <see cref="AssetDatabase.LoadAssetAtPath"/>.
        /// If called immediately after a domain reload, some assets might not be fully loaded.
        /// </remarks>
        /// <returns>Enumeration of build configurations.</returns>
        public static IEnumerable<BuildConfiguration> GetBuildConfigurations()
        {
            if (m_BuildConfigs != null)
                return m_BuildConfigs;

            //@TODO: Time slice this and turn it into an update callback
            m_BuildConfigs = AssetDatabase.FindAssets($"t:{nameof(BuildConfiguration)}", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<BuildConfiguration>)
                .Where(c => c.GetBuildPipeline() != null).ToList();

            return m_BuildConfigs;
        }

        /// <summary>
        /// Retrieve all build configurations that are visible in active build configuration dropdown.
        /// Visible build configurations must be buildable and have their Show property set to true.
        /// </summary>
        /// <remarks>
        /// This method uses <see cref="AssetDatabase.LoadAssetAtPath"/>.
        /// If called immediately after a domain reload, some assets might not be fully loaded.
        /// </remarks>
        /// <returns>Enumeration of build configurations.</returns>
        public static IEnumerable<BuildConfiguration> GetVisibleBuildConfigurations()
        {
            return GetBuildConfigurations().Where(c => c.Show);
        }

        /// <summary>
        /// Filter build configuration enumeration by matching name case insensitive.
        /// </summary>
        /// <param name="configs">Build configuration enumeration.</param>
        /// <param name="search">The build configuration name to match.</param>
        /// <returns>Filtered enumeration of build configurations that matches.</returns>
        public static IEnumerable<BuildConfiguration> GetMatchingBuildConfigurations(IEnumerable<BuildConfiguration> configs, string search)
        {
            return configs.Where(c => c.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        class DetectAssetChanges : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            {
                if (importedAssets.Any(IsBuildConfigurationExtension) ||
                    deletedAssets.Any(IsBuildConfigurationExtension) ||
                    movedAssets.Any(IsBuildConfigurationExtension))
                {
                    // Destroy build config list cache
                    m_BuildConfigs = null;

                    // Update active build config dropdown
                    var config = BuildConfiguration.GetActive();
                    var configs = GetVisibleBuildConfigurations();
                    if ((config == null || !config) && configs.Any())
                    {
#if UNITY_EDITOR_WIN
                        var platform = KnownPlatforms.Windows.GetPlatform();
#elif UNITY_EDITOR_OSX
                        var platform = KnownPlatforms.macOS.GetPlatform();
#elif UNITY_EDITOR_LINUX
                        var platform = KnownPlatforms.Linux.GetPlatform();
#endif
                        config = configs.FirstOrDefault(c => c.GetPlatform() == platform);
                        if (config != null && config)
                        {
                            var result = BuildConfiguration.SetActive(config);
                            if (!result)
                                result.LogResult();
                        }
                    }
                    ActiveBuildConfigurationDropdown.Instance.Update(BuildConfiguration.GetActive());

                    // Update active build config window
                    if (EditorWindow.HasOpenInstances<ActiveBuildConfigurationWindow>())
                        EditorWindow.GetWindow<ActiveBuildConfigurationWindow>()?.Refresh();
                }
            }

            static bool IsBuildConfigurationExtension(string path) => Path.GetExtension(path) == BuildConfiguration.AssetExtension;
        }
    }
}
#endif
