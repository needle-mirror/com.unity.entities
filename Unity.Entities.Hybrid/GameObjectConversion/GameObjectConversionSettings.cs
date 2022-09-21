using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
#endif
using ConversionFlags = Unity.Entities.GameObjectConversionUtility.ConversionFlags;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities
{
    /// <summary>
    /// Contains settings that control the conversion of GameObject scenes to runtime data.
    /// </summary>
    public class GameObjectConversionSettings
    {
        /// <summary>
        /// The world to contain the result of the conversion.
        /// </summary>
        public World                    DestinationWorld;
        /// <summary>
        /// The GUID of the converted scene.
        /// </summary>
        public Hash128                  SceneGUID;
        /// <summary>
        /// Debug string applied to the conversion world.
        /// </summary>
        public string                   DebugConversionName = "";
        /// <summary>
        /// <see cref="ConversionFlags"/> to apply during the conversion.
        /// </summary>
        public ConversionFlags          ConversionFlags;
#if UNITY_EDITOR
        /// <summary>
        /// The GUID of the active <see cref="Unity.Build.BuildConfiguration"/>.
        /// </summary>
        public UnityEditor.GUID         BuildConfigurationGUID;
        /// <summary>
        /// The active build configuration.
        /// </summary>
        public Unity.Build.BuildConfiguration BuildConfiguration;
        /// <summary>
        /// The context of the importer.
        /// </summary>
        public AssetImportContext       AssetImportContext;
        /// <summary>
        /// The root GameObject of the converted prefab.
        /// </summary>
        public GameObject               PrefabRoot;
#endif
        /// <summary>
        /// The system filter flags to control where the systems are created. By default they are <see cref="WorldSystemFilterFlags.GameObjectConversion"/>.
        /// </summary>
        public WorldSystemFilterFlags FilterFlags = WorldSystemFilterFlags.GameObjectConversion;
        /// <summary>
        /// The array of extra systems injected during conversion.
        /// </summary>
        public Type[]                   ExtraSystems = Array.Empty<Type>();
        /// <summary>
        /// The list of systems run during conversion.
        /// </summary>
        public List<Type>               Systems;
        internal byte                   NamespaceID;
        /// <summary>
        /// Callback invoked after the conversion world is created and the systems have been added to it.
        /// </summary>
        public Action<World>            ConversionWorldCreated;        // get a callback right after the conversion world is created and systems have been added to it (good for tests that want to inject something)
        /// <summary>
        /// Callback invoked before the conversion world is disposed.
        /// </summary>
        /// <remarks>This can be used to validate the contents of the conversion world.</remarks>
        public Action<World>            ConversionWorldPreDispose;     // get a callback right before the conversion world gets disposed (good for tests that want to validate world contents)

        /// <summary>
        /// The <see cref="BlobAssetStore"/> used during conversion.
        /// </summary>
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

        /// <summary>
        /// Initializes and returns an instance of GameObjectConversionSettings.
        /// </summary>
        public GameObjectConversionSettings() {}

        // ** CONFIGURATION **

        /// <summary>
        /// Initializes and returns an instance of GameObjectConversionSettings.
        /// </summary>
        /// <param name="destinationWorld">The <see cref="World"/> where the result of the conversion is stored.</param>
        /// <param name="conversionFlags">The conversion flags.</param>
        /// <param name="blobAssetStore">The <see cref="BlobAssetStore"/> used during conversion.</param>
        public GameObjectConversionSettings(World destinationWorld, ConversionFlags conversionFlags, BlobAssetStore blobAssetStore)
        {
            DestinationWorld = destinationWorld;
            ConversionFlags = conversionFlags;
            BlobAssetStore = blobAssetStore;
        }

        /// <summary>
        /// Creates a new <see cref="GameObjectConversionSettings"/> with a set <see cref="World"/>.
        /// </summary>
        /// <param name="destinationWorld">The <see cref="World"/> where the result of the conversion is stored.</param>
        /// <param name="blobAssetStore">The <see cref="BlobAssetStore"/> used during conversion.</param>
        /// <returns>Returns a new instance of <see cref="GameObjectConversionSettings"/>.</returns>
        public static GameObjectConversionSettings FromWorld(World destinationWorld, BlobAssetStore blobAssetStore) => new GameObjectConversionSettings { DestinationWorld = destinationWorld, BlobAssetStore = blobAssetStore};
        /// <summary>
        /// Creates a new <see cref="GameObjectConversionSettings"/> with a set scene GUID.
        /// </summary>
        /// <param name="hash">The GUID of the converted scene.</param>
        /// <param name="blobAssetStore">The <see cref="BlobAssetStore"/> used during conversion.</param>
        /// <returns>Returns a new instance of <see cref="GameObjectConversionSettings"/>.</returns>
        public static GameObjectConversionSettings FromHash(Hash128 hash, BlobAssetStore blobAssetStore) => new GameObjectConversionSettings { SceneGUID = hash, BlobAssetStore = blobAssetStore};
    #if UNITY_EDITOR
        /// <summary>
        /// Creates a new <see cref="GameObjectConversionSettings"/> with a set scene GUID.
        /// </summary>
        /// <param name="guid">The GUID of the converted scene.</param>
        /// <param name="blobAssetStore">The <see cref="BlobAssetStore"/> used during conversion.</param>
        /// <returns>Returns a new instance of <see cref="GameObjectConversionSettings"/>.</returns>
        public static GameObjectConversionSettings FromGUID(UnityEditor.GUID guid, BlobAssetStore blobAssetStore) => new GameObjectConversionSettings { SceneGUID = guid, BlobAssetStore = blobAssetStore};
    #endif

        /// <summary>
        /// Injects additional systems during conversion.
        /// </summary>
        /// <remarks>Use this to inject systems into the conversion world (for example for testing).</remarks>
        /// <param name="extraSystems">The list of extra system types to be instantiated during conversion.</param>
        /// <returns>Returns the modified instance of <see cref="GameObjectConversionSettings"/>.</returns>
        /// <exception cref="InvalidOperationException">Throws if the <see cref="ExtraSystems"/> is already initialized.</exception>
        public GameObjectConversionSettings WithExtraSystems(params Type[] extraSystems)
        {
            if (ExtraSystems != null && ExtraSystems.Length > 0)
                throw new InvalidOperationException($"{nameof(ExtraSystems)} already initialized");
            ExtraSystems = extraSystems;
            return this;
        }

        /// <summary>
        /// Injects an additional system during conversion.
        /// </summary>
        /// <typeparam name="T">The type of the injected system.</typeparam>
        /// <returns>Returns the modified instance of <see cref="GameObjectConversionSettings"/>.</returns>
        public GameObjectConversionSettings WithExtraSystem<T>()
            => WithExtraSystems(typeof(T));

        /// <summary>
        /// Injects additional systems during conversion.
        /// </summary>
        /// <typeparam name="T1">The type of the first injected system.</typeparam>
        /// <typeparam name="T2">The type of the second injected system.</typeparam>
        /// <returns>Returns the modified instance of <see cref="GameObjectConversionSettings"/>.</returns>
        public GameObjectConversionSettings WithExtraSystems<T1, T2>()
            => WithExtraSystems(typeof(T1), typeof(T2));

        /// <summary>
        /// Injects additional systems during conversion.
        /// </summary>
        /// <typeparam name="T1">The type of the first injected system.</typeparam>
        /// <typeparam name="T2">The type of the second injected system.</typeparam>
        /// <typeparam name="T3">The type of the third injected system.</typeparam>
        /// <returns>Returns the modified instance of <see cref="GameObjectConversionSettings"/>.</returns>
        public GameObjectConversionSettings WithExtraSystems<T1, T2, T3>()
            => WithExtraSystems(typeof(T1), typeof(T2), typeof(T3));

        // ** CONVERSION **

        /// <summary>
        /// Creates the conversion world applying the current settings.
        /// </summary>
        /// <returns>Returns a new <see cref="World"/> instance.</returns>
        public World CreateConversionWorld()
            => GameObjectConversionUtility.CreateConversionWorld(this);

        // ** EXPORTING **

        /// <summary>
        /// Specifies if the conversion supports exporting UnityEngine.Object objects as separate files.
        /// </summary>
        public bool SupportsExporting => (GetType() != typeof(GameObjectConversionSettings) || FilterFlags == WorldSystemFilterFlags.DotsRuntimeGameObjectConversion);

        /// <summary>
        /// Gets a GUID for exporting a UnityEngine.Object object.
        /// </summary>
        /// <param name="uobject">The UnityEngine.Object object.</param>
        /// <returns>Returns a GUID.</returns>
        /// <exception cref="ArgumentNullException">Throws if <paramref name="uobject"/> is null.</exception>
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
#if UNITY_2022_1_OR_NEWER
                        var exportFileInfo = AssetImportContext.GetOutputArtifactFilePath(guid.ToString());
#else
                        var exportFileInfo = AssetImportContext.GetResultPath(guid.ToString());
#endif
                        var assetPath = AssetDatabase.GetAssetPath(uobject);
                        m_ExportedAssets.Add(uobject, found = new ExportedAsset
                        {
                            Guid = guid,
                            AssetPath = assetPath,
                            ExportFileInfo = new FileInfo(exportFileInfo),
                        });
                    }
                    //TODO: Set the exported asset path for LiveConversion case because AssetImportContext might still be null
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

        /// <summary>
        /// Creates a <see cref="Stream"/> for exporting a <see cref="UnityEngine.Object"/>.
        /// </summary>
        /// <param name="uobject">The <see cref="UnityEngine.Object"/> to be exported.</param>
        /// <returns>Returns a Stream to which the the object can be serialized.</returns>
        /// <exception cref="ArgumentNullException">Throws if <paramref name="uobject"/> is null.</exception>
        /// <exception cref="Exception">Throws if <paramref name="uobject"/> has not been registered to be exported during conversion.</exception>
        public virtual Stream TryCreateAssetExportWriter(UnityObject uobject)
        {
            if (uobject == null)
                throw new ArgumentNullException(nameof(uobject));

#if UNITY_EDITOR
            // if there's no import context then we're not actually writing anything
            if (AssetImportContext == null)
                return null;
#endif

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
