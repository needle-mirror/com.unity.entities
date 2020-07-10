using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities.Serialization;
#if !NET_DOTS
using Unity.Properties;
using Unity.Properties.Adapters;
using Unity.Properties.Internal;
#endif

namespace Unity.Entities
{
#if !UNITY_DOTSRUNTIME
    unsafe class ManagedObjectBlobs :
        IPropertyBagVisitor,
        IPropertyVisitor,
        IVisit<BlobAssetReferenceData>
    {
        /// <summary>
        /// Set used to track already visited references.
        /// </summary>
        HashSet<object> m_References;

        // Blob Asset Ptr Storage
        NativeList<BlobAssetPtr> m_BlobAssets;
        NativeHashMap<BlobAssetPtr, int> m_BlobAssetMap;

        /// <summary>
        /// Gathers all blob asset references within the specified object.
        /// </summary>
        /// <param name="obj">The object to extract all blob asset references from.</param>
        /// <param name="blobAssets">The array where new blob asset references are added.</param>
        /// <param name="blobAssetMap">Mapping to track existing blob asset references encountered.</param>
        /// <exception cref="ArgumentNullException">The given object was null.</exception>
        /// <exception cref="MissingPropertyBagException">The given object has no property bag associated with it.</exception>
        public void GatherBlobAssetReferences(object obj, NativeList<BlobAssetPtr> blobAssets, NativeHashMap<BlobAssetPtr, int> blobAssetMap)
        {
            if (null == obj)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var type = obj.GetType();
            var properties = PropertyBagStore.GetPropertyBag(type);

            if (null == properties)
            {
                throw new MissingPropertyBagException(type);
            }

            m_References?.Clear();

            m_BlobAssets = blobAssets;
            m_BlobAssetMap = blobAssetMap;

            properties.Accept(this, ref obj);
        }

        /// <summary>
        /// Invoked by Unity.Properties for each container type (i.e. struct or class).
        /// </summary>
        /// <remarks>
        /// We do not explicitly override collection visitation. Instead it will simply fall through to this call and enumerate all elements.
        /// </remarks>
        /// <param name="properties">The property bag being visited.</param>
        /// <param name="container">The container being visited.</param>
        /// <typeparam name="TContainer">The container type.</typeparam>
        void IPropertyBagVisitor.Visit<TContainer>(IPropertyBag<TContainer> properties, ref TContainer container)
        {
            foreach (var property in properties.GetProperties(ref container))
                ((IPropertyAccept<TContainer>)property).Accept(this, ref container);
        }

        /// <summary>
        /// Invoked by Unity.Properties for any non-collection property.
        /// </summary>
        /// <param name="property">The property being visited.</param>
        /// <param name="container">The source container.</param>
        /// <typeparam name="TContainer">The container type.</typeparam>
        /// <typeparam name="TValue">The value type.</typeparam>
        void IPropertyVisitor.Visit<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container)
        {
            var value = property.GetValue(ref container);

            if (RuntimeTypeInfoCache<TValue>.CanBeNull && null == value)
                return;

            if (!RuntimeTypeInfoCache<TValue>.IsValueType && typeof(string) != typeof(TValue))
            {
                if (m_References == null)
                    m_References = new HashSet<object>();

                if (!m_References.Add(value))
                    return;
            }

#if !UNITY_DOTSRUNTIME
            if (value is UnityEngine.Object)
                return;
#endif

            if (this is IVisit<TValue> typed)
            {
                typed.Visit(property, ref container, ref value);
                property.SetValue(ref container, value);
                return;
            }

            property.Visit(this, ref value);
        }

        /// <summary>
        /// Invoked for each <see cref="Entity"/> member encountered.
        /// </summary>
        /// <param name="property">The property being visited.</param>
        /// <param name="container">The source container.</param>
        /// <param name="value">The entity value.</param>
        /// <typeparam name="TContainer">The container type.</typeparam>
        /// <returns>The status of the adapter visit.</returns>
        VisitStatus IVisit<BlobAssetReferenceData>.Visit<TContainer>(Property<TContainer, BlobAssetReferenceData> property, ref TContainer container, ref BlobAssetReferenceData value)
        {
            if (null == value.m_Ptr)
                return VisitStatus.Stop;

            var blobAssetPtr = new BlobAssetPtr(value.Header);

            if (!m_BlobAssetMap.TryGetValue(blobAssetPtr, out _))
            {
                var index = m_BlobAssets.Length;
                m_BlobAssets.Add(blobAssetPtr);
                m_BlobAssetMap.Add(blobAssetPtr, index);
            }
            return VisitStatus.Stop;
        }
    }
#else
    class ManagedObjectBlobs
    {
        public void GatherBlobAssetReferences(object obj, NativeList<BlobAssetPtr> blobAssets, NativeHashMap<BlobAssetPtr, int> blobAssetMap)
        {
            // Not supported in DOTS Runtime.
        }
    }
#endif
}
