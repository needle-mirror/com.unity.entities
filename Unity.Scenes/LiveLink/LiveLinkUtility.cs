#if !UNITY_DOTSRUNTIME
using System;
using System.IO;
using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    internal struct LiveLinkHandshake
    {
        public long LiveLinkId;
        public Hash128 LiveLinkCacheGUID;
        public LiveLinkHandshake(long liveLinkId, Hash128 liveLinkCacheGUID)
        {
            LiveLinkId = liveLinkId;
            LiveLinkCacheGUID = liveLinkCacheGUID;
        }
    }

    #if UNITY_EDITOR
    [InitializeOnLoad]
    #endif
    internal class LiveLinkUtility
    {
        #if UNITY_EDITOR
        public static unsafe void WriteBootstrap(string path, Hash128 buildConfigurationGUID)
        {
            long handshakeId = GetEditorLiveLinkId();
            using (var stream = new StreamBinaryWriter(path))
            {
                stream.WriteBytes(&buildConfigurationGUID, sizeof(Hash128));
                stream.WriteBytes(&handshakeId, sizeof(long));
            }
        }

        public static long GetEditorLiveLinkId()
        {
            return Application.dataPath.GetHashCode();
        }

        const string k_LiveLinkEditorCacheDir = "Library/LiveLinkCache";
        const string k_LiveLinkEditorCacheGUIDPath = k_LiveLinkEditorCacheDir + "/" + "livelinkcacheguid";
        public const string livelinkBuildTargetDependencyName = "LiveLinkBuildTarget";

        static LiveLinkUtility()
        {
            RegisterPlatformDependency();
        }

        static unsafe void RegisterPlatformDependency()
        {
            var activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
            UnityEngine.Hash128 hash = default;
            fixed(char* str = activeBuildTarget)
            {
                HashUnsafeUtilities.ComputeHash128(str, (ulong)(sizeof(char) * activeBuildTarget.Length), &hash);
            }

            AssetDatabaseCompatibility.RegisterCustomDependency(livelinkBuildTargetDependencyName, hash);
        }

        static unsafe void WriteEditorLiveLinkCacheGUID()
        {
            Directory.CreateDirectory(k_LiveLinkEditorCacheDir);

            using (var stream = new StreamBinaryWriter(k_LiveLinkEditorCacheGUIDPath))
            {
                Hash128 guid = s_CacheGUID;
                stream.WriteBytes(&guid, sizeof(Hash128));
            }
        }

        static unsafe void ReadEditorLiveLinkCacheGUID()
        {
            if (File.Exists(k_LiveLinkEditorCacheGUIDPath))
            {
                using (var rdr = new StreamBinaryReader(k_LiveLinkEditorCacheGUIDPath))
                {
                    Hash128 guid = default;
                    rdr.ReadBytes(&guid, sizeof(Hash128));
                    s_CacheGUID = guid;
                }
            }
            else
            {
                GenerateNewEditorLiveLinkCacheGUID();
            }
        }

        static void ValidateEditorLiveLinkCacheGUID()
        {
            if (!s_CacheGUID.IsValid)
            {
                ReadEditorLiveLinkCacheGUID();
            }
        }

        internal static void GenerateNewEditorLiveLinkCacheGUID()
        {
            s_CacheGUID = GUID.Generate();
            WriteEditorLiveLinkCacheGUID();
        }

        #else
        public static unsafe void WritePlayerLiveLinkCacheGUID()
        {
            CreatePlayerLiveLinkCacheDir();
            var cacheGUIDPath = $"{EntityScenesPaths.GetLiveLinkCacheDirPath()}/livelinkcacheguid";
            using (var stream = new StreamBinaryWriter(cacheGUIDPath))
            {
                Hash128 guid = LiveLinkCacheGUID;
                stream.WriteBytes(&guid, sizeof(Hash128));
            }
        }

        public static unsafe void ReadPlayerLiveLinkCacheGUID()
        {
            var cacheGUIDPath = $"{EntityScenesPaths.GetLiveLinkCacheDirPath()}/livelinkcacheguid";
            if (File.Exists(cacheGUIDPath))
            {
                using (var rdr = new StreamBinaryReader(cacheGUIDPath))
                {
                    Hash128 guid = default;
                    rdr.ReadBytes(&guid, sizeof(Hash128));
                    LiveLinkCacheGUID = guid;
                }
            }
        }

        public static void ClearPlayerLiveLinkCache()
        {
            var cachePath = EntityScenesPaths.GetLiveLinkCacheDirPath();

            if (Directory.Exists(cachePath))
            {
                try
                {
                    Directory.Delete(cachePath, true);
                }
                catch (SystemException exception)
                {
                    Debug.LogError($"Failed to delete LiveLink cache! {exception.Message}");
                }
            }
        }

        static void CreatePlayerLiveLinkCacheDir()
        {
            if(!Directory.Exists(EntityScenesPaths.GetLiveLinkCacheDirPath()))
            {
                Directory.CreateDirectory(EntityScenesPaths.GetLiveLinkCacheDirPath());
            }
        }

        #endif

        static Hash128 s_CacheGUID = default;
        // Used to clear a Player's LiveLink local cache.
        // The Cache GUID is stored in the Library so if a user deletes the library (or this file) they get a clean experience
        // For now this is also exposed as a menu option to aid development and problem solving, one day this will not be a button
        public static Hash128 LiveLinkCacheGUID
        {
            get
            {
                #if UNITY_EDITOR
                ValidateEditorLiveLinkCacheGUID();
                #endif
                return s_CacheGUID;
            }

            internal set { s_CacheGUID = value; }
        }

        public const string LiveLinkBootstrapFileName = "livelink-bootstrap";
        // Used to verify the connection between a LiveLink Player and Editor is valid.
        // In this case we use the Application's data path (hashed) and store it in the bootstrap file.
        // If the LiveLink player connects to a different Editor instance or the Editor was launched with the wrong project, it will display an error on the Player.
        public static long LiveLinkId { get; private set; }
        public static bool LiveLinkEnabled { get; private set; }
        public static Hash128 BuildConfigurationGUID { get; private set; }

        static bool liveLinkBooted = false;
        static string bootstrapPath;

        public static void DisableLiveLink()
        {
            LiveLinkEnabled = false;
        }

        public static void LiveLinkBoot()
        {
            if (liveLinkBooted)
                return;

            liveLinkBooted = true;
            bootstrapPath = Path.Combine(Application.streamingAssetsPath, LiveLinkBootstrapFileName);

            LiveLinkEnabled = FileUtilityHybrid.FileExists(bootstrapPath);

            if (LiveLinkEnabled)
            {
                if (!UnityEngine.Networking.PlayerConnection.PlayerConnection.instance.isConnected)
                    Debug.LogError(
                        "Failed to connect to the Editor.\nAn Editor connection is required for LiveLink to work.");

                ReadBootstrap();
            }

            #if !UNITY_EDITOR
            ReadPlayerLiveLinkCacheGUID();
            #endif
        }

        static unsafe void ReadBootstrap()
        {
            var path = bootstrapPath;
            using (var rdr = new StreamBinaryReader(path))
            {
                Hash128 guid = default;
                long id = 0;

                rdr.ReadBytes(&guid, sizeof(Hash128));
                rdr.ReadBytes(&id, sizeof(long));

                BuildConfigurationGUID = guid;
                LiveLinkId = id;
            }
        }
    }
}
#endif
