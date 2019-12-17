using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PropertyAttribute = Unity.Properties.PropertyAttribute;

namespace Unity.Build.Common
{
    public sealed class ClassicBuildProfile : IBuildPipelineComponent
    {
        BuildTarget m_Target;
        List<string> m_ExcludedAssemblies;

        /// <summary>
        /// Retrieve <see cref="BuildTypeCache"/> for this build profile.
        /// </summary>
        public BuildTypeCache TypeCache { get; } = new BuildTypeCache();

        /// <summary>
        /// Gets or sets which <see cref="BuildTarget"/> this profile is going to use for the build.
        /// Used for building classic Unity standalone players.
        /// </summary>
        [Property]
        public BuildTarget Target
        {
            get => m_Target;
            set
            {
                m_Target = value;
                TypeCache.PlatformName = m_Target.ToString();
            }
        }

        /// <summary>
        /// Gets or sets which <see cref="Configuration"/> this profile is going to use for the build.
        /// </summary>
        [Property]
        public BuildConfiguration Configuration { get; set; } = BuildConfiguration.Develop;

        [Property]
        public BuildPipeline Pipeline { get; set; }

        /// <summary>
        /// List of assemblies that should be explicitly excluded for the build.
        /// </summary>
        [Property, HideInInspector]
        public List<string> ExcludedAssemblies
        {
            get => m_ExcludedAssemblies;
            set
            {
                m_ExcludedAssemblies = value;
                TypeCache.ExcludedAssemblies = value;
            }
        }

        public ClassicBuildProfile()
        {
            Target = BuildTarget.NoTarget;
            ExcludedAssemblies = new List<string>();
        }

        internal string GetExecutableExtension()
        {
#pragma warning disable CS0618
            switch (m_Target)
            {
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneOSXIntel:
                case BuildTarget.StandaloneOSXIntel64:
                    return ".app";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ".exe";
                case BuildTarget.NoTarget:
                case BuildTarget.StandaloneLinux64:
                case BuildTarget.Stadia:
                    return string.Empty;
                case BuildTarget.Android:
                    if (EditorUserBuildSettings.exportAsGoogleAndroidProject)
                        return "";
                    else if (EditorUserBuildSettings.buildAppBundle)
                        return ".aab";
                    else
                        return ".apk";
                case BuildTarget.Lumin:
                    return ".mpk";   
                case BuildTarget.iOS:
                case BuildTarget.tvOS:
                default:
                    return "";
            }
#pragma warning restore CS0618
        }
    }
}
