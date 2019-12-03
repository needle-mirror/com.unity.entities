using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Serialization.Json;
using UnityEditor;
using UnityEngine;

namespace Unity.Build
{
    /// <summary>
    /// Provides the necessary implementation to use properties and serialization with a <see cref="ScriptableObject"/> of type <typeparamref name="TContainer"/>.
    /// </summary>
    /// <typeparam name="TContainer">The type of the container.</typeparam>
    [Serializable]
    public abstract class ScriptableObjectPropertyContainer<TContainer> : ScriptableObject, ISerializationCallbackReceiver
        where TContainer : ScriptableObjectPropertyContainer<TContainer>
    {
        [SerializeField] string m_AssetContent;

        /// <summary>
        /// Event invoked when the container registers <see cref="JsonVisitor"/> used for serialization.
        /// It provides an opportunity to register additional property visitor adapters.
        /// </summary>
        public static event Action<JsonVisitor> JsonVisitorRegistration;

        /// <summary>
        /// Event invoked when the asset changed on disk.
        /// </summary>
        public static event Action<TContainer> AssetChanged;

        /// <summary>
        /// Reset this asset in preparation for deserialization.
        /// </summary>
        protected virtual void Reset()
        {
            m_AssetContent = null;
        }

        /// <summary>
        /// Sanitize this asset after deserialization.
        /// </summary>
        protected virtual void Sanitize() { }

        /// <summary>
        /// Create a new asset instance.
        /// </summary>
        /// <param name="mutator">Optional mutator that can be used to modify the asset.</param>
        /// <returns>The new asset instance.</returns>
        public static TContainer CreateInstance(Action<TContainer> mutator = null)
        {
            var asset = CreateInstance<TContainer>();
            mutator?.Invoke(asset);
            return asset;
        }

        /// <summary>
        /// Create a new asset instance saved to disk.
        /// </summary>
        /// <param name="assetPath">The location where to create the asset.</param>
        /// <param name="mutator">Optional mutator that can be used to modify the asset.</param>
        /// <returns>The new asset instance.</returns>
        public static TContainer CreateAsset(string assetPath, Action<TContainer> mutator = null)
        {
            var asset = CreateInstance(mutator);
            if (asset != null && asset)
            {
                asset.SerializeToPath(assetPath);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                return AssetDatabase.LoadAssetAtPath<TContainer>(assetPath);
            }
            return null;
        }

        /// <summary>
        /// Load an asset from the specified asset path.
        /// </summary>
        /// <param name="assetPath">The asset path to load from.</param>
        /// <returns>The loaded asset if successful, <see langword="null"/> otherwise.</returns>
        public static TContainer LoadAsset(string assetPath) => AssetDatabase.LoadAssetAtPath<TContainer>(assetPath);

        /// <summary>
        /// Load an asset from the specified asset <see cref="GUID"/>.
        /// </summary>
        /// <param name="assetGuid">The asset <see cref="GUID"/> to load from.</param>
        /// <returns>The loaded asset if successful, <see langword="null"/> otherwise.</returns>
        public static TContainer LoadAsset(GUID assetGuid) => LoadAsset(AssetDatabase.GUIDToAssetPath(assetGuid.ToString()));

        /// <summary>
        /// Save this asset to disk.
        /// If no asset path is provided, asset is saved at its original location.
        /// </summary>
        /// <param name="assetPath">Optional file path where to save the asset.</param>
        /// <returns><see langword="true"/> if the operation is successful, <see langword="false"/> otherwise.</returns>
        public bool SaveAsset(string assetPath = null)
        {
            assetPath = assetPath ?? AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            try
            {
                SerializeToPath(assetPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to serialize {name.ToHyperLink()} to '{assetPath}'.\n{e.Message}");
                return false;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return true;
        }

        /// <summary>
        /// Restore this asset from disk.
        /// </summary>
        /// <returns><see langword="true"/> if the operation is successful, <see langword="false"/> otherwise.</returns>
        public bool RestoreAsset()
        {
            var assetPath = AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }
            return DeserializeFromPath(this as TContainer, assetPath);
        }

        /// <summary>
        /// Serialize this container to a JSON <see cref="string"/>.
        /// </summary>
        /// <returns>The container as a JSON <see cref="string"/> if the serialization is successful, <see langword="null"/> otherwise.</returns>
        public string SerializeToJson()
        {
            var visitor = new BuildJsonVisitor();
            JsonVisitorRegistration?.Invoke(visitor);
            return JsonSerialization.Serialize(this, visitor);
        }

        /// <summary>
        /// Deserialize from a JSON <see cref="string"/> into the container.
        /// </summary>
        /// <param name="container">The container to deserialize into.</param>
        /// <param name="json">The JSON string to deserialize from.</param>
        /// <returns><see langword="true"/> if the operation is successful, <see langword="false"/> otherwise.</returns>
        public static bool DeserializeFromJson(TContainer container, string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return TryDeserialize(container, stream);
            }
        }

        /// <summary>
        /// Serialize this container to a file.
        /// </summary>
        /// <param name="path">The file path to write into.</param>
        public void SerializeToPath(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, SerializeToJson());
        }

        /// <summary>
        /// Deserialize from a file into the container.
        /// </summary>
        /// <param name="container">The container to deserialize into.</param>
        /// <param name="path">The file path to deserialize from.</param>
        /// <returns><see langword="true"/> if the operation is successful, <see langword="false"/> otherwise.</returns>
        public static bool DeserializeFromPath(TContainer container, string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                return TryDeserialize(container, stream, path);
            }
        }

        /// <summary>
        /// Serialize this container to a stream.
        /// </summary>
        /// <param name="stream">The stream the serialize into.</param>
        public void SerializeToStream(Stream stream)
        {
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(SerializeToJson());
            }
        }

        /// <summary>
        /// Deserialize from a stream into the container.
        /// </summary>
        /// <param name="container">The container to deserialize into.</param>
        /// <param name="stream">The stream to deserialize from.</param>
        /// <returns><see langword="true"/> if the operation is successful, <see langword="false"/> otherwise.</returns>
        public static bool DeserializeFromStream(TContainer container, Stream stream)
        {
            return TryDeserialize(container, stream);
        }

        public void OnBeforeSerialize()
        {
            m_AssetContent = SerializeToJson();
        }

        public void OnAfterDeserialize()
        {
            // Can't deserialize here, throws: "CreateJobReflectionData is not allowed to be called during serialization, call it from OnEnable instead."
        }

        public void OnEnable()
        {
            var container = this as TContainer;
            var assetPath = AssetDatabase.GetAssetPath(this);
            var assetContent = m_AssetContent;
            if ((!string.IsNullOrEmpty(assetPath) && DeserializeFromPath(container, assetPath)) ||
                (!string.IsNullOrEmpty(assetContent) && DeserializeFromJson(container, assetContent)))
            {
                if (assetContent != m_AssetContent)
                {
                    AssetChanged?.Invoke(container);
                }
            }
        }

        static bool TryDeserialize(TContainer container, Stream stream, string assetPath = null)
        {
            try
            {
                container.Reset();
                using (var result = JsonSerialization.DeserializeFromStream(stream, ref container))
                {
                    if (result.Succeeded)
                    {
                        container.m_AssetContent = !string.IsNullOrEmpty(assetPath) ? File.ReadAllText(assetPath) : null;
                        container.Sanitize();
                        return true;
                    }
                    else
                    {
                        var errors = result.AllEvents.Select(e => e.ToString());
                        LogDeserializeError(string.Join("\n", errors), container, assetPath);
                        container.Sanitize();
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                LogDeserializeError(e.Message, container, assetPath);
                container.Sanitize();
                return false;
            }
        }

        static void LogDeserializeError(string message, TContainer container, string assetPath)
        {
            var what = !string.IsNullOrEmpty(assetPath) ? assetPath.ToHyperLink() : $"memory container of type '{container.GetType().FullName}'";
            Debug.LogError($"Failed to deserialize {what}:\n{message}");
        }
    }
}
