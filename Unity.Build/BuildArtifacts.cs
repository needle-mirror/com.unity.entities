using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Serialization.Json;
using UnityEditor;
using UnityEngine;

namespace Unity.Build
{
    /// <summary>
    /// API for managing build artifacts.
    /// </summary>
    public static class BuildArtifacts
    {
        static readonly Dictionary<string, ArtifactData> s_ArtifactDataCache = new Dictionary<string, ArtifactData>();

        class ArtifactData
        {
            public BuildPipelineResult Result;
            public IBuildArtifact[] Artifacts;
        }

        /// <summary>
        /// Get the value of the first <see cref="IBuildArtifact"/> that is assignable to type <see cref="Type"/>.
        /// </summary>
        /// <param name="settings">The <see cref="BuildSettings"/> that was used to store the <see cref="IBuildArtifact"/>.</param>
        /// <param name="type">The type of the <see cref="IBuildArtifact"/>.</param>
        /// <returns>The <see cref="IBuildArtifact"/> if found, <see langword="null"/> otherwise.</returns>
        public static IBuildArtifact GetBuildArtifact(BuildSettings settings, Type type)
        {
            if (settings == null || !settings)
            {
                return null;
            }

            if (type == null || type == typeof(object))
            {
                return null;
            }

            if (!typeof(IBuildArtifact).IsAssignableFrom(type))
            {
                return null;
            }

            var artifactData = GetArtifactData(settings);
            if (artifactData == null || artifactData.Artifacts == null || artifactData.Artifacts.Length == 0)
            {
                return null;
            }

            return artifactData.Artifacts.FirstOrDefault(a => type.IsAssignableFrom(a.GetType()));
        }

        /// <summary>
        /// Get the value of the first <see cref="IBuildArtifact"/> that is assignable to type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of the <see cref="IBuildArtifact"/>.</typeparam>
        /// <param name="settings">The <see cref="BuildSettings"/> that was used to store the <see cref="IBuildArtifact"/>.</param>
        /// <returns>The <see cref="IBuildArtifact"/> if found, <see langword="null"/> otherwise.</returns>
        public static T GetBuildArtifact<T>(BuildSettings settings) where T : class, IBuildArtifact => (T)GetBuildArtifact(settings, typeof(T));

        /// <summary>
        /// Get the last <see cref="BuildPipelineResult"/> from building the <see cref="BuildSettings"/> specified.
        /// </summary>
        /// <param name="settings">The <see cref="BuildSettings"/> that was used to store the <see cref="IBuildArtifact"/>.</param>
        /// <returns>The <see cref="BuildPipelineResult"/> if found, <see langword="null"/> otherwise.</returns>
        public static BuildPipelineResult GetBuildResult(BuildSettings settings) => GetArtifactData(settings)?.Result;

        internal static void Store(BuildPipelineResult result, IBuildArtifact[] artifacts) => SetArtifactData(result, artifacts);

        static string GetBuildSettingsName(BuildSettings settings)
        {
            var name = settings.name;
            if (string.IsNullOrEmpty(name))
            {
                name = GlobalObjectId.GetGlobalObjectIdSlow(settings).ToString();
            }
            return name;
        }

        static string GetArtifactsPath(string name) => Path.Combine("Library/BuildArtifacts", name + ".json").ToForwardSlash();

        static ArtifactData GetArtifactData(BuildSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            var name = GetBuildSettingsName(settings);
            var assetPath = GetArtifactsPath(name);
            if (!File.Exists(assetPath))
            {
                if (s_ArtifactDataCache.ContainsKey(name))
                {
                    s_ArtifactDataCache.Remove(name);
                }
                return null;
            }

            if (!s_ArtifactDataCache.TryGetValue(name, out var artifactData))
            {
                try
                {
                    artifactData = new ArtifactData();
                    using (var result = JsonSerialization.DeserializeFromPath(assetPath, ref artifactData))
                    {
                        if (!result.Succeeded)
                        {
                            var errors = result.AllEvents.Select(e => e.ToString());
                            LogDeserializeError(string.Join("\n", errors), artifactData, assetPath);
                            artifactData = null;
                        }
                    }
                }
                catch (Exception e)
                {
                    LogDeserializeError(e.Message, artifactData, assetPath);
                    artifactData = null;
                }

                s_ArtifactDataCache.Add(name, artifactData);
            }

            return artifactData;
        }

        static void LogDeserializeError(string message, ArtifactData container, string assetPath)
        {
            var what = !string.IsNullOrEmpty(assetPath) ? assetPath.ToHyperLink() : $"memory container of type '{container.GetType().FullName}'";
            Debug.LogError($"Failed to deserialize {what}:\n{message}");
        }

        static void SetArtifactData(BuildPipelineResult result, IBuildArtifact[] artifacts)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (result.BuildSettings == null)
            {
                throw new ArgumentNullException(nameof(result.BuildSettings));
            }

            if (artifacts == null)
            {
                throw new ArgumentNullException(nameof(artifacts));
            }

            var name = GetBuildSettingsName(result.BuildSettings);
            if (!s_ArtifactDataCache.TryGetValue(name, out var artifactData) || artifactData == null)
            {
                artifactData = new ArtifactData();
                s_ArtifactDataCache.Add(name, artifactData);
            }

            artifactData.Result = result;
            artifactData.Artifacts = artifacts;

            var assetPath = GetArtifactsPath(name);
            var assetDir = Path.GetDirectoryName(assetPath);
            if (!Directory.Exists(assetDir))
            {
                Directory.CreateDirectory(assetDir);
            }

            var json = JsonSerialization.Serialize(artifactData, new BuildJsonVisitor());
            File.WriteAllText(assetPath, json);
        }
    }
}
