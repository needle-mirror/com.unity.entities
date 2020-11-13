using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif
#endif
using ConversionFlags = Unity.Entities.GameObjectConversionUtility.ConversionFlags;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities
{
    public class GameObjectConversionSettings
    {
        public World                    DestinationWorld;
        public Hash128                  SceneGUID;
        public string                   DebugConversionName = "";
        public ConversionFlags          ConversionFlags;
#if UNITY_EDITOR
        public UnityEditor.GUID         BuildConfigurationGUID;
        public Build.BuildConfiguration BuildConfiguration;
        public AssetImportContext       AssetImportContext;
#endif
        public WorldSystemFilterFlags FilterFlags = WorldSystemFilterFlags.GameObjectConversion;
        public Type[]                   ExtraSystems = Array.Empty<Type>();
        public List<Type>               Systems;
        [Obsolete("This functionality is no longer supported. (RemovedAfter 2021-01-09).")]
        public byte                     NamespaceID; // this must be internal
        public Action<World>            ConversionWorldCreated;        // get a callback right after the conversion world is created and systems have been added to it (good for tests that want to inject something)
        public Action<World>            ConversionWorldPreDispose;     // get a callback right before the conversion world gets disposed (good for tests that want to validate world contents)

        public BlobAssetStore BlobAssetStore { get; protected internal set; }

        //Export fields
        class ExportedAsset
        {
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
            public Hash128 Guid;
            public string AssetPath;
            public FileInfo ExportFileInfo;
            public bool Exported;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
        }
        Dictionary<UnityObject, ExportedAsset> m_ExportedAssets = new Dictionary<UnityObject, ExportedAsset>();

        public GameObjectConversionSettings() {}

        // not a clone - only copies what makes sense for creating entities into a separate guid namespace
        [Obsolete("This functionality is no longer supported. (RemovedAfter 2021-01-09).")]
        public GameObjectConversionSettings Fork(byte entityGuidNamespaceID)
        {
            if (entityGuidNamespaceID == 0)
                throw new ArgumentException("0 is reserved for the default", nameof(entityGuidNamespaceID));

            return new GameObjectConversionSettings
            {
                DestinationWorld = DestinationWorld,
                SceneGUID = SceneGUID,
                DebugConversionName = $"{DebugConversionName}:{entityGuidNamespaceID:x2}",
                ConversionFlags = ConversionFlags,
                NamespaceID = entityGuidNamespaceID,
                BlobAssetStore = BlobAssetStore,
#if UNITY_EDITOR
                BuildConfiguration = BuildConfiguration,
                BuildConfigurationGUID = BuildConfigurationGUID,
                AssetImportContext = AssetImportContext,
#endif
            };
        }

        // ** CONFIGURATION **

        public GameObjectConversionSettings(World destinationWorld, ConversionFlags conversionFlags, BlobAssetStore blobAssetStore = null)
        {
            DestinationWorld = destinationWorld;
            ConversionFlags = conversionFlags;
            if (blobAssetStore != null)
            {
                BlobAssetStore = blobAssetStore;
            }
        }

        public static GameObjectConversionSettings FromWorld(World destinationWorld, BlobAssetStore blobAssetStore) => new GameObjectConversionSettings { DestinationWorld = destinationWorld, BlobAssetStore = blobAssetStore};
        public static GameObjectConversionSettings FromHash(Hash128 hash, BlobAssetStore blobAssetStore) => new GameObjectConversionSettings { SceneGUID = hash, BlobAssetStore = blobAssetStore};
    #if UNITY_EDITOR
        public static GameObjectConversionSettings FromGUID(UnityEditor.GUID guid, BlobAssetStore blobAssetStore) => new GameObjectConversionSettings { SceneGUID = guid, BlobAssetStore = blobAssetStore};
    #endif

        // use this to inject systems into the conversion world (good for testing)
        public GameObjectConversionSettings WithExtraSystems(params Type[] extraSystems)
        {
            if (ExtraSystems != null && ExtraSystems.Length > 0)
                throw new InvalidOperationException($"{nameof(ExtraSystems)} already initialized");
            ExtraSystems = extraSystems;
            return this;
        }

        public GameObjectConversionSettings WithExtraSystem<T>()
            => WithExtraSystems(typeof(T));

        public GameObjectConversionSettings WithExtraSystems<T1, T2>()
            => WithExtraSystems(typeof(T1), typeof(T2));

        public GameObjectConversionSettings WithExtraSystems<T1, T2, T3>()
            => WithExtraSystems(typeof(T1), typeof(T2), typeof(T3));

        // ** CONVERSION **

        public World CreateConversionWorld()
            => GameObjectConversionUtility.CreateConversionWorld(this);

        // ** EXPORTING **

        public bool SupportsExporting => (GetType() != typeof(GameObjectConversionSettings) || FilterFlags == WorldSystemFilterFlags.DotsRuntimeGameObjectConversion);

        public virtual Hash128 GetGuidForAssetExport(UnityObject uobject)
        {
            if (uobject == null)
                throw new ArgumentNullException(nameof(uobject));
#if UNITY_EDITOR
            if (!m_ExportedAssets.TryGetValue(uobject, out var found))
            {
                var guid = GetGuidForUnityObject(uobject);
                if (guid.IsValid)
                {
                    //Use the guid as an extension and retrieve the path to where to save in the AssetDataBase
                    if (AssetImportContext != null)
                    {
                        var exportFileInfo = AssetImportContext.GetResultPath(guid.ToString());
                        var assetPath = AssetDatabase.GetAssetPath(uobject);
                        m_ExportedAssets.Add(uobject, found = new ExportedAsset
                        {
                            Guid = guid,
                            AssetPath = assetPath,
                            ExportFileInfo = new FileInfo(exportFileInfo),
                        });
                    }
                    //TODO: Set the exported asset path for LiveLink case because AssetImportContext might still be null
                }
            }
            if(found != null)
                return found.Guid;
#endif
            return new Hash128();
        }

        internal Hash128 GetGuidForUnityObject(UnityObject obj)
        {
#if UNITY_EDITOR
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out var guid, out long fileId))
                return new Hash128();

            if (!new Hash128(guid).IsValid)
            {
                // Special case for memory textures
                if (obj is UnityEngine.Texture texture)
                {
                    return texture.imageContentsHash;
                }
                UnityEngine.Debug.LogWarning($"Could not get a Guid for object type '{obj.GetType().FullName}'.", obj);
                return new Hash128();
            }

            // Merge asset database guid and file identifier
            var hash = UnityEngine.Hash128.Compute(guid);
            hash.Append(fileId);
            return hash;
#else
            return new Hash128();
#endif
        }

        public virtual Stream TryCreateAssetExportWriter(UnityObject uobject)
        {
            if (uobject == null)
                throw new ArgumentNullException(nameof(uobject));

            if (!m_ExportedAssets.TryGetValue(uobject, out var item))
            {
                throw new Exception($"Trying to create export writer for asset {uobject}, but it has never been registered to be exported." +
                    $"Make sure {nameof(GetGuidForAssetExport)} is being called in a conversion system first before using {nameof(TryCreateAssetExportWriter)} in a conversion system from the {nameof(GameObjectExportGroup)}");
            }

            //if the asset has already been exported, no need to export it twice
            if (item.Exported)
                return null;

            item.Exported = true;
            item.ExportFileInfo.Directory.Create();

            UnityEngine.Debug.Log("Exported Asset: " + item.Guid.ToString() + " = " + item.AssetPath);

            return item.ExportFileInfo.Create();
        }
    }
}
