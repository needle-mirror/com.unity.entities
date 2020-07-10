#if !UNITY_DOTSRUNTIME
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Properties;
using Unity.Serialization.Binary;
using Unity.Serialization.Binary.Adapters;

[assembly: InternalsVisibleTo("Unity.Scenes")]

[assembly: GeneratePropertyBagsForTypesQualifiedWith(typeof(Unity.Entities.ISharedComponentData))]
[assembly: GeneratePropertyBagsForTypesQualifiedWith(typeof(Unity.Entities.IComponentData), TypeOptions.ReferenceType)]

namespace Unity.Entities.Serialization
{
    /// <summary>
    /// Writer to write managed objects to a <see cref="UnsafeAppendBuffer"/> stream.
    /// </summary>
    /// <remarks>
    /// This is used as a wrapper around <see cref="Unity.Serialization.Binary.BinarySerialization"/> with a custom layer for <see cref="UnityEngine.Object"/>.
    /// </remarks>
    unsafe class ManagedObjectBinaryWriter : Unity.Serialization.Binary.Adapters.Contravariant.IBinaryAdapter<UnityEngine.Object>
    {
        static readonly UnityEngine.Object[] s_EmptyUnityObjectTable = new UnityEngine.Object[0];

        readonly UnsafeAppendBuffer* m_Stream;
        readonly BinarySerializationParameters m_Params;

        List<UnityEngine.Object> m_UnityObjects;
        Dictionary<UnityEngine.Object, int> m_UnityObjectsMap;

        /// <summary>
        /// Initializes a new instance of <see cref="ManagedObjectBinaryWriter"/> which can be used to write managed objects to the given stream.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        public ManagedObjectBinaryWriter(UnsafeAppendBuffer* stream)
        {
            m_Stream = stream;
            m_Params = new BinarySerializationParameters
            {
                UserDefinedAdapters = new List<IBinaryAdapter> {this},
                Context = new BinarySerializationContext(),
            };
        }

        /// <summary>
        /// Adds a custom adapter to the writer.
        /// </summary>
        /// <param name="adapter">The custom adapter to add.</param>
        public void AddAdapter(IBinaryAdapter adapter) => m_Params.UserDefinedAdapters.Add(adapter);

        /// <summary>
        /// Gets all <see cref="UnityEngine.Object"/> types encountered during serialization.
        /// </summary>
        /// <returns>A set of all <see cref="UnityEngine.Object"/> types encountered during serialization</returns>
        public UnityEngine.Object[] GetUnityObjects() => m_UnityObjects?.ToArray() ?? s_EmptyUnityObjectTable;

        /// <summary>
        /// Writes the given boxed object to the binary stream.
        /// </summary>
        /// <remarks>
        /// Any <see cref="UnityEngine.Object"/> references are added to the object table and can be retrieved by calling <see cref="GetUnityObjects"/>.
        /// </remarks>
        /// <param name="obj">The object to serialize.</param>
        public void WriteObject(object obj)
        {
            var parameters = m_Params;
            parameters.SerializedType = obj?.GetType();
            BinarySerialization.ToBinary(m_Stream, obj, parameters);
        }

        void Unity.Serialization.Binary.Adapters.Contravariant.IBinaryAdapter<UnityEngine.Object>.Serialize(UnsafeAppendBuffer* writer, UnityEngine.Object value)
        {
            var index = -1;

            if (value != null)
            {
                if (null == m_UnityObjects)
                    m_UnityObjects = new List<UnityEngine.Object>();

                if (null == m_UnityObjectsMap)
                    m_UnityObjectsMap = new Dictionary<UnityEngine.Object, int>();

                if (!m_UnityObjectsMap.TryGetValue(value, out index))
                {
                    index = m_UnityObjects.Count;
                    m_UnityObjectsMap.Add(value, index);
                    m_UnityObjects.Add(value);
                }
            }

            writer->Add(index);
        }

        object Unity.Serialization.Binary.Adapters.Contravariant.IBinaryAdapter<UnityEngine.Object>.Deserialize(UnsafeAppendBuffer.Reader* reader)
        {
            throw new InvalidOperationException($"Deserialize should never be invoked by {nameof(ManagedObjectBinaryWriter)}");
        }
    }

    /// <summary>
    /// Reader to read managed objects from a <see cref="UnsafeAppendBuffer.Reader"/> stream.
    /// </summary>
    /// <remarks>
    /// This is used as a wrapper around <see cref="Unity.Serialization.Binary.BinarySerialization"/> with a custom layer for <see cref="UnityEngine.Object"/>.
    /// </remarks>
    unsafe class ManagedObjectBinaryReader : Unity.Serialization.Binary.Adapters.Contravariant.IBinaryAdapter<UnityEngine.Object>
    {
        readonly UnsafeAppendBuffer.Reader* m_Stream;
        readonly BinarySerializationParameters m_Params;
        readonly UnityEngine.Object[] m_UnityObjects;

        /// <summary>
        /// Initializes a new instance of <see cref="ManagedObjectBinaryReader"/> which can be used to read managed objects from the given stream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="unityObjects">The table containing all <see cref="UnityEngine.Object"/> references. This is produce by the <see cref="ManagedObjectBinaryWriter"/>.</param>
        public ManagedObjectBinaryReader(UnsafeAppendBuffer.Reader* stream, UnityEngine.Object[] unityObjects)
        {
            m_Stream = stream;
            m_Params = new BinarySerializationParameters
            {
                UserDefinedAdapters = new List<IBinaryAdapter> {this},
                Context = new BinarySerializationContext(),
            };
            m_UnityObjects = unityObjects;
        }

        /// <summary>
        /// Adds a custom adapter to the reader.
        /// </summary>
        /// <param name="adapter">The custom adapter to add.</param>
        public void AddAdapter(IBinaryAdapter adapter) => m_Params.UserDefinedAdapters.Add(adapter);

        /// <summary>
        /// Reads from the binary stream and returns the next object.
        /// </summary>
        /// <remarks>
        /// The type is given as a hint to the serializer to avoid writing root type information.
        /// </remarks>
        /// <param name="type">The root type.</param>
        /// <returns>The deserialized object value.</returns>
        public object ReadObject(Type type)
        {
            var parameters = m_Params;
            parameters.SerializedType = type;
            return BinarySerialization.FromBinary<object>(m_Stream, parameters);
        }

        void Unity.Serialization.Binary.Adapters.Contravariant.IBinaryAdapter<UnityEngine.Object>.Serialize(UnsafeAppendBuffer* writer, UnityEngine.Object value)
        {
            throw new InvalidOperationException($"Serialize should never be invoked by {nameof(ManagedObjectBinaryReader)}.");
        }

        object Unity.Serialization.Binary.Adapters.Contravariant.IBinaryAdapter<UnityEngine.Object>.Deserialize(UnsafeAppendBuffer.Reader* reader)
        {
            var index = reader->ReadNext<int>();

            if (index == -1)
                return null;

            if (m_UnityObjects == null)
                throw new ArgumentException("We are reading a UnityEngine.Object however no ObjectTable was provided to the ManagedObjectBinaryReader.");

            if ((uint)index >= m_UnityObjects.Length)
                throw new ArgumentException("We are reading a UnityEngine.Object but the deserialized index is out of range for the given object table.");

            return m_UnityObjects[index];
        }
    }
}
#endif
