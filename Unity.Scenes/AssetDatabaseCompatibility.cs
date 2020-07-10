
using Unity.Collections;
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Experimental;
using Hash128 = UnityEngine.Hash128;

namespace Unity.Scenes
{
    enum ImportMode
    {
        Synchronous,
        Asynchronous,
        NoImport
    }

    static class AssetDatabaseCompatibility
    {
        internal static string GuidToPath(GUID guid)
        {
#if UNITY_2020_2_OR_NEWER
            return AssetDatabase.GUIDToAssetPath(guid);
#else
            return AssetDatabase.GUIDToAssetPath(guid.ToString());
#endif
        }

        internal static Hash128 GetArtifactHash(string guid, Type importerType, ImportMode mode)
        {
            switch (mode)
            {
#if UNITY_2020_2_OR_NEWER
                case ImportMode.Asynchronous:
                    return AssetDatabaseExperimental.ProduceArtifactAsync(new ArtifactKey(new GUID(guid), importerType)).value;
                case ImportMode.Synchronous:
                    return AssetDatabaseExperimental.ProduceArtifact(new ArtifactKey(new GUID(guid), importerType)).value;
                case ImportMode.NoImport:
                    return AssetDatabaseExperimental.LookupArtifact(new ArtifactKey(new GUID(guid), importerType)).value;
#else
                case ImportMode.Asynchronous:
                    return AssetDatabaseExperimental.GetArtifactHash(guid, importerType, AssetDatabaseExperimental.ImportSyncMode.Queue);
                case ImportMode.Synchronous:
                    return AssetDatabaseExperimental.GetArtifactHash(guid, importerType);
                case ImportMode.NoImport:
                    return AssetDatabaseExperimental.GetArtifactHash(guid, importerType, AssetDatabaseExperimental.ImportSyncMode.Poll);
#endif
            }

            return default;
        }

        internal static bool GetArtifactPaths(Hash128 artifactHash, out string[] paths)
        {
#if UNITY_2020_2_OR_NEWER
            return AssetDatabaseExperimental.GetArtifactPaths(new ArtifactID
            {
                value = artifactHash
            }, out paths);
#else
            return AssetDatabaseExperimental.GetArtifactPaths(artifactHash, out paths);
#endif
        }

        internal static void ProduceArtifactsAsync(NativeArray<GUID> guids, Type assetImportType, NativeList<Hash128> artifacts)
        {
            artifacts.ResizeUninitialized(guids.Length);

    #if UNITY_2020_2_OR_NEWER
            //@TODO: use batch API when it lands in trunk
            var res = AssetDatabaseExperimental.ProduceArtifactsAsync(guids.ToArray(), assetImportType);
            for (int i = 0; i != guids.Length; i++)
                artifacts[i] = res[i].value;
    #else
            for (int i = 0; i != guids.Length; i++)
                artifacts[i] = AssetDatabaseExperimental.GetArtifactHash(guids[i].ToString(), assetImportType, AssetDatabaseExperimental.ImportSyncMode.Queue);
    #endif
        }

        static void ProduceArtifacts(NativeArray<GUID> guids, Type assetImportType, NativeArray<Hash128> artifacts)
        {
#if UNITY_2020_2_OR_NEWER
            //@TODO: use batch API when it lands in trunk
            for (int i = 0; i != guids.Length; i++)
                artifacts[i] = AssetDatabaseExperimental.ProduceArtifact(new ArtifactKey(guids[i], assetImportType))
                    .value;
#else
            for (int i = 0; i != guids.Length; i++)
                artifacts[i] =
            AssetDatabaseExperimental.GetArtifactHash(guids[i].ToString(), assetImportType, AssetDatabaseExperimental.ImportSyncMode.Block);
#endif
        }

        internal static bool ProduceArtifactsRefreshIfNecessary(NativeArray<GUID> guids, Type assetImportType, NativeArray<Hash128> artifacts)
        {
            ProduceArtifacts(guids, assetImportType, artifacts);

            bool hasFailedArtifacts = false;
            foreach (var artifact in artifacts)
            {
                if (!artifact.isValid)
                    hasFailedArtifacts = true;
            }

            if (hasFailedArtifacts)
            {
                // ProduceArtifact can fail if the assets have changed while importing or since last refresh.
                // Try at least once to get into a correct state.
                AssetDatabase.Refresh();

                ProduceArtifacts(guids, assetImportType, artifacts);
            }

            foreach (var artifact in artifacts)
            {
                if (!artifact.isValid)
                    return false;
            }

            return true;
        }

        internal static bool ProduceArtifactsRefreshIfNecessary(NativeArray<GUID> guids, Type assetImportType, NativeList<Hash128> artifacts)
        {
            artifacts.ResizeUninitialized(guids.Length);
            return ProduceArtifactsRefreshIfNecessary(guids, assetImportType, artifacts.AsArray());
        }

        internal static void LookupArtifacts(NativeArray<GUID> guids, Type assetImportType, NativeList<Hash128> artifacts)
        {
            artifacts.ResizeUninitialized(guids.Length);

    #if UNITY_2020_2_OR_NEWER
            //@TODO: use batch API when it lands in trunk
            for (int i = 0; i != guids.Length; i++)
                artifacts[i] = AssetDatabaseExperimental.LookupArtifact(new ArtifactKey(guids[i], assetImportType)).value;
    #else
            for (int i = 0; i != guids.Length; i++)
                artifacts[i] = AssetDatabaseExperimental.GetArtifactHash(guids[i].ToString(), assetImportType, AssetDatabaseExperimental.ImportSyncMode.Poll);
    #endif
        }


        internal static bool AssetExists(GUID guid)
        {
#if UNITY_2020_2_OR_NEWER
            return AssetDatabase.GetAssetDependencyHash(guid) != default;
#else
            var assetPath = AssetDatabase.GUIDToAssetPath(guid.ToString());
            if (string.IsNullOrEmpty(assetPath))
                return false;
            else
                return AssetDatabase.GetAssetDependencyHash(assetPath) != default;
#endif
        }

        internal static ulong GetArtifactDependencyVersion()
        {
            #if UNITY_2020_2_OR_NEWER
            return AssetDatabase.GlobalArtifactDependencyVersion;
            #else
            var globalArtifactDependencyVersion = DetectRefreshVersion.Version +
                (ulong)AssetDatabaseExperimental.counters.import.refresh.total;

            return globalArtifactDependencyVersion;
            #endif
        }

        internal static ulong GetArtifactProcessedVersion()
        {
            //@TODO: AssetDatabase.GlobalArtifactProcessedVersion doesn't work correctly, it doesn't get bumped when out of process assets complete.
            //       This is fixed in trunk since May 15th. So once this has been released in an alpha build we should remove this workaround
            var globalArtifactProcessedVersion = (ulong)AssetDatabaseExperimental.counters.import.importedOutOfProcess.total + (ulong)AssetDatabaseExperimental.counters.cacheServer.artifactsDownloaded.total;
            return globalArtifactProcessedVersion;
        }
    }

#if !UNITY_2020_2_OR_NEWER
    class DetectRefreshVersion : AssetPostprocessor
    {
        public static ulong Version;
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            Version++;
        }
    }
#endif

}
#endif
