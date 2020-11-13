using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Experimental;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

namespace Unity.Scenes.Editor
{
    static class AssetBundleTypeCache
    {
        static bool s_Initialized = false;

        public static string TypeString(Type type) => $"UnityEngineType/{type.FullName}";

        public static void RegisterMonoScripts()
        {
            if (AssetDatabaseCompatibility.IsAssetImportWorkerProcess() || s_Initialized)
                return;
            s_Initialized = true;

            AssetDatabaseCompatibility.UnregisterCustomDependencyPrefixFilter("UnityEngineType/");

            var behaviours = TypeCache.GetTypesDerivedFrom<UnityEngine.MonoBehaviour>();
            var scripts = TypeCache.GetTypesDerivedFrom<UnityEngine.ScriptableObject>();

            for (int i = 0; i != behaviours.Count; i++)
            {
                var type = behaviours[i];
                if (type.IsGenericType)
                    continue;
                var hash = TypeHash.CalculateStableTypeHash(type);
                AssetDatabaseCompatibility.RegisterCustomDependency(TypeString(type),
                    new UnityEngine.Hash128(hash, hash));
            }

            for (int i = 0; i != scripts.Count; i++)
            {
                var type = scripts[i];
                if (type.IsGenericType)
                    continue;
                var hash = TypeHash.CalculateStableTypeHash(type);
                AssetDatabaseCompatibility.RegisterCustomDependency(TypeString(type),
                    new UnityEngine.Hash128(hash, hash));
            }
        }
    }

    [ScriptedImporter(19, "liveLinkBundles")]
    public class LiveLinkBuildImporter : ScriptedImporter
    {
        const int k_CurrentFileFormatVersion = 1;
        const string k_DependenciesExtension = "dependencies";
        public const string k_BundleExtension = "bundle";
        public const string k_ManifestExtension = "manifest";

        const string k_SceneExtension = ".unity";

        [Serializable]
        public struct BuildMetaData
        {
            public BlobArray<Hash128> Dependencies;
        }

        internal static Hash128 GetHash(string guid, BuildTarget target, ImportMode importMode)
        {
            LiveLinkBuildPipeline.RemapBuildInAssetGuid(ref guid);
            AssetBundleTypeCache.RegisterMonoScripts();

            // TODO: GetArtifactHash needs to take BuildTarget so we can get Artifacts for other than the ActiveBuildTarget
            return AssetDatabaseCompatibility.GetArtifactHash(guid, typeof(LiveLinkBuildImporter), importMode);
        }

        // Recursive until new SBP APIs land in 2020.1
        internal static Hash128[] GetDependenciesInternal(in Hash128 artifactHash, in Hash128 assetGUID)
        {
            AssetDatabaseCompatibility.GetArtifactPaths(artifactHash, out string[] paths);

            string metaPath = null;
            foreach (var path in paths)
            {
                if (path.EndsWith(k_DependenciesExtension))
                {
                    metaPath = path;
                }
            }

            if (metaPath == null)
            {
                // TODO: Remove this when public API is deprecated
                if (assetGUID.IsValid)
                {
                    UnityEngine.Debug.LogError($"No .{k_DependenciesExtension} in artifact paths! Hash={artifactHash.ToString()} | GUID={assetGUID.ToString()} | Path={AssetDatabase.GUIDToAssetPath(assetGUID.ToString())}");
                }
                return Array.Empty<Hash128>();
            }

            BlobAssetReference<BuildMetaData> buildMetaData;
            if (!BlobAssetReference<BuildMetaData>.TryRead(metaPath, k_CurrentFileFormatVersion, out buildMetaData))
            {
                UnityEngine.Debug.LogError($"Unable to load BuildMetaData file {metaPath}! Hash={artifactHash.ToString()} | GUID={assetGUID.ToString()} | Path={AssetDatabase.GUIDToAssetPath(assetGUID.ToString())}");
                return Array.Empty<Hash128>();
            }

            Hash128[] guids = buildMetaData.Value.Dependencies.ToArray();
            buildMetaData.Dispose();
            return guids;
        }

        internal static string GetBundlePathInternal(in Hash128 artifactHash, in Hash128 assetGUID)
        {
            AssetDatabaseCompatibility.GetArtifactPaths(artifactHash, out string[] paths);

            string bundlePath = null;
            foreach (var path in paths)
            {
                if (path.EndsWith(k_BundleExtension))
                {
                    bundlePath = path;
                }
            }

            if (bundlePath == null)
            {
                if (assetGUID.IsValid)
                {
                    UnityEngine.Debug.LogError($"No .{k_BundleExtension} in artifact paths! Hash={artifactHash.ToString()} | GUID={assetGUID.ToString()} | Path={AssetDatabase.GUIDToAssetPath(assetGUID.ToString())}");
                }
            }

            return bundlePath;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            ctx.DependsOnCustomDependency(LiveLinkUtility.livelinkBuildTargetDependencyName);
            ctx.DependsOnSourceAsset(EntitiesCacheUtility.globalLiveLinkAssetDependencyPath);

            if (ctx.assetPath.ToLower().EndsWith(k_SceneExtension))
                ImportSceneBundle(ctx);
            else
                ImportAssetBundle(ctx);
        }

        void WriteDependenciesResult(string resultPath, Hash128[] dependencies)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var metaData = ref builder.ConstructRoot<BuildMetaData>();

            builder.Construct(ref metaData.Dependencies, dependencies);
            BlobAssetReference<SceneMetaData>.Write(builder, resultPath, k_CurrentFileFormatVersion);
            builder.Dispose();
        }

        void AddImportDependencies(AssetImportContext ctx, IEnumerable<Hash128> dependencies, IEnumerable<Type> types)
        {
            EditorEntityScenes.DependOnSceneGameObjects(AssetDatabaseCompatibility.PathToGUID(ctx.assetPath), ctx);

            // All dependencies impact the build result until new SBP APIs land in 2020.1
            foreach (var dependency in dependencies)
            {
                var dependencyGuid = new GUID(dependency.ToString());
                // Built in asset bundles can be ignored
                if (GUIDHelper.IsBuiltin(dependencyGuid))
                    continue;

                if (LiveLinkBuildPipeline.TryRemapBuiltinExtraGuid(ref dependencyGuid, out _))
                    continue;

                ctx.DependsOnArtifact(dependencyGuid);
            }

            foreach (var type in types)
                ctx.DependsOnCustomDependency(AssetBundleTypeCache.TypeString(type));
        }

        private void ImportAssetBundle(AssetImportContext ctx)
        {
            var assetPath = ctx.assetPath;
            var fileIdent = LiveLinkBuildPipeline.RemapBuildInAssetPath(ref assetPath);
            var guid = new GUID(AssetDatabase.AssetPathToGUID(assetPath));

            var manifest = UnityEngine.ScriptableObject.CreateInstance<AssetObjectManifest>();
            AssetObjectManifestBuilder.BuildManifest(guid, manifest);
            var manifestPath = ctx.GetResultPath(k_ManifestExtension);
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new[] { manifest }, manifestPath, true);

            var bundlePath = ctx.GetResultPath(k_BundleExtension);
            var dependencies = new HashSet<Hash128>();
            var types = new HashSet<Type>();
            LiveLinkBuildPipeline.BuildAssetBundle(manifestPath, guid, bundlePath, ctx.selectedBuildTarget, dependencies, types, fileIdent);

            var dependenciesPath = ctx.GetResultPath(k_DependenciesExtension);
            WriteDependenciesResult(dependenciesPath, dependencies.ToArray());

            AddImportDependencies(ctx, dependencies, types);
        }

        private void ImportSceneBundle(AssetImportContext ctx)
        {
            var guid = new GUID(AssetDatabase.AssetPathToGUID(ctx.assetPath));
            var bundlePath = ctx.GetResultPath(k_BundleExtension);
            var dependencies = new HashSet<Hash128>();
            var types = new HashSet<Type>();
            LiveLinkBuildPipeline.BuildSceneBundle(guid, bundlePath, ctx.selectedBuildTarget, false, dependencies, types);

            var dependenciesPath = ctx.GetResultPath(k_DependenciesExtension);
            WriteDependenciesResult(dependenciesPath, dependencies.ToArray());

            AddImportDependencies(ctx, dependencies, types);
        }
    }
}
