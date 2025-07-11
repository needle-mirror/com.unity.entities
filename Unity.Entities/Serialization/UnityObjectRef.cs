using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace Unity.Entities
{
    internal struct UnityObjectRefMap : IDisposable
    {
        public NativeHashMap<int, int> InstanceIDMap;
        public NativeList<int> InstanceIDs;

        public bool IsCreated => InstanceIDs.IsCreated && InstanceIDMap.IsCreated;

        public UnityObjectRefMap(Allocator allocator)
        {
            InstanceIDMap = new NativeHashMap<int, int>(0, allocator);
            InstanceIDs = new NativeList<int>(0, allocator);
        }

        public void Dispose()
        {
            InstanceIDMap.Dispose();
            InstanceIDs.Dispose();
        }

        public UnityEngine.Object[] ToObjectArray()
        {
            var objects = new List<UnityEngine.Object>();

            if (IsCreated && InstanceIDs.Length > 0)
                Resources.InstanceIDToObjectList(InstanceIDs.AsArray(), objects);

            return objects.ToArray();
        }

        public int Add(int instanceId)
        {
            var index = -1;
            if (instanceId != 0 && IsCreated)
            {
                if (!InstanceIDMap.TryGetValue(instanceId, out index))
                {
                    index = InstanceIDs.Length;
                    InstanceIDMap.Add(instanceId, index);
                    InstanceIDs.Add(instanceId);
                }
            }

            return index;
        }
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    internal static class UnityObjectRefUtility
    {
#if UNITY_EDITOR
        static UnityObjectRefUtility()
        {
            EditorInitializeOnLoadMethod();
        }
#endif

        unsafe class ManagedUnityObjectRefCollector:
            Properties.IPropertyBagVisitor,
            Properties.IPropertyVisitor,
            ITypedVisit<UntypedUnityObjectRef>,
            IDisposable
        {
            UnsafeHashSet<int>* m_InstanceIDRefs;

            public ManagedUnityObjectRefCollector(UnsafeHashSet<int>* instanceIDRefs)
            {
                m_InstanceIDRefs = instanceIDRefs;
            }

            IPropertyBag GetPropertyBag(object obj)
            {
                var properties = PropertyBag.GetPropertyBag(obj.GetType());
                return properties;
            }

            public void CollectReferences(ref object obj)
            {
                GetPropertyBag(obj).Accept(this, ref obj);
            }

            void Properties.IPropertyBagVisitor.Visit<TContainer>(Properties.IPropertyBag<TContainer> properties, ref TContainer container)
            {
                foreach (var property in properties.GetProperties(ref container))
                    ((Properties.IPropertyAccept<TContainer>)property).Accept(this, ref container);
            }

            void Properties.IPropertyVisitor.Visit<TContainer, TValue>(Properties.Property<TContainer, TValue> property,
                ref TContainer container)
            {
                if (Properties.TypeTraits<TValue>.CanBeNull || !Properties.TypeTraits<TValue>.IsValueType)
                    return;

                var value = property.GetValue(ref container);

                if (this is ITypedVisit<TValue> typed)
                {
                    typed.Visit(property, ref container, ref value);
                    return;
                }

                Properties.PropertyContainer.Accept(this, ref value);
            }

            void ITypedVisit<UntypedUnityObjectRef>.Visit<TContainer>(Properties.Property<TContainer, UntypedUnityObjectRef> property, ref TContainer container, ref UntypedUnityObjectRef value)
            {
                m_InstanceIDRefs->Add(value.instanceId);
            }

            public void Dispose() { }
        }

        private static unsafe void AddInstanceIDRefsFromComponent(byte* componentData, TypeManager.EntityOffsetInfo* unityObjectRefOffsets, int unityObjectRefCount, UnsafeHashSet<int>* instanceIDRefs)
        {
            for (int i = 0; i < unityObjectRefCount; ++i)
            {
                var unityObjectRefOffset = unityObjectRefOffsets[i].Offset;
                var unityObjectRefPtr = (UntypedUnityObjectRef*)(componentData + unityObjectRefOffset);
                instanceIDRefs->Add(unityObjectRefPtr->instanceId);
            }
        }

        static unsafe void AddInstanceIDRefsFromChunk(Archetype* archetype, int entityCount, byte* chunkBuffer, UnsafeHashSet<int>* instanceIDRefs)
        {
            var typeCount = archetype->TypesCount;

            for (var unordered_ti = 0; unordered_ti < typeCount; ++unordered_ti)
            {
                var ti = archetype->TypeMemoryOrderIndexToIndexInArchetype[unordered_ti];
                var type = archetype->Types[ti];
                if (type.IsZeroSized || type.IsManagedComponent || type.TypeIndex == ManagedComponentStore.CompanionLinkTypeIndex || type.TypeIndex == ManagedComponentStore.CompanionLinkTransformTypeIndex)
                    continue;

                ref readonly var ct = ref TypeManager.GetTypeInfo(type.TypeIndex);
                var unityObjectRefCount = ct.UnityObjectRefOffsetCount;

                if (unityObjectRefCount == 0)
                    continue;

                var unityObjectRefOffsets = TypeManager.GetUnityObjectRefOffsets(ct);
                int subArrayOffset = archetype->Offsets[ti];
                byte* componentArrayStart = chunkBuffer + subArrayOffset;

                if (type.IsBuffer)
                {
                    BufferHeader* header = (BufferHeader*)componentArrayStart;
                    int strideSize = archetype->SizeOfs[ti];
                    var elementSize = ct.ElementSize;

                    for (int bi = 0; bi < entityCount; ++bi)
                    {
                        var bufferStart = BufferHeader.GetElementPointer(header);
                        var bufferEnd = bufferStart + header->Length * elementSize;
                        for (var componentData = bufferStart; componentData < bufferEnd; componentData += elementSize)
                        {
                            AddInstanceIDRefsFromComponent(componentData, unityObjectRefOffsets,unityObjectRefCount, instanceIDRefs);
                        }

                        header = (BufferHeader*)((byte*)header + strideSize);
                    }
                }
                else
                {
                    int size = archetype->SizeOfs[ti];
                    byte* end = componentArrayStart + size * entityCount;
                    for (var componentData = componentArrayStart; componentData < end; componentData += size)
                    {
                        AddInstanceIDRefsFromComponent(componentData, unityObjectRefOffsets, unityObjectRefCount, instanceIDRefs);
                    }
                }
            }
        }

        static unsafe void AddInstanceIDRefsFromAllChunks(Archetype* archetype, UnsafeHashSet<int>* instanceIDRefs)
        {
            for (var chunkIndex = 0; chunkIndex < archetype->Chunks.Count; chunkIndex++)
            {
                var chunk = archetype->Chunks[chunkIndex];
                var chunkPtr = chunk.GetPtr();
                var chunkBuffer = chunkPtr->Buffer;

                AddInstanceIDRefsFromChunk(archetype, chunk.Count, chunkBuffer, instanceIDRefs);
            }
        }

        static unsafe void AddInstanceIDRefsFromUnmanagedSharedComponents(EntityDataAccess* access, Archetype* archetype, UnsafeHashSet<int>* instanceIDRefs)
        {
            int numSharedComponents = archetype->NumSharedComponents;
            for (int iType = 0; iType < numSharedComponents; iType++)
            {
                var sharedComponents = archetype->Chunks.GetSharedComponentValueArrayForType(iType);

                for (int chunkIndex = 0; chunkIndex < archetype->Chunks.Count; chunkIndex++)
                {
                    var sharedComponentIndex = sharedComponents[chunkIndex];

                    if (EntityComponentStore.IsUnmanagedSharedComponentIndex(sharedComponentIndex))
                    {
                        var typeIndex = EntityComponentStore.GetComponentTypeFromSharedComponentIndex(sharedComponentIndex);
                        ref readonly var typeInfo = ref TypeManager.GetTypeInfo(typeIndex);

                        var unityObjectRefCount = typeInfo.UnityObjectRefOffsetCount;
                        if (unityObjectRefCount == 0)
                            continue;

                        var unityObjectRefOffsets = TypeManager.GetUnityObjectRefOffsets(typeInfo);
                        var dataPtr = (byte*)access->EntityComponentStore->GetSharedComponentDataAddr_Unmanaged(sharedComponentIndex, typeIndex);
                        AddInstanceIDRefsFromComponent(dataPtr, unityObjectRefOffsets, unityObjectRefCount, instanceIDRefs);
                    }
                }
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        static unsafe void AddInstanceIDRefsFromManagedComponents(EntityDataAccess* access, UnsafeHashSet<int>* instanceIDRefs)
        {
            using var managedObjectRefWalker = new ManagedUnityObjectRefCollector(instanceIDRefs);

            // Managed components
            s_AddFromManagedComponents.Begin();
            for (int i = 0; i < access->ManagedComponentStore.m_ManagedComponentData.Length; i++)
            {
                var managedComponent = access->ManagedComponentStore.m_ManagedComponentData[i];
                if (managedComponent == null)
                    continue;

                ref readonly var typeInfo = ref TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(managedComponent.GetType()));

                if (!typeInfo.HasUnityObjectRefs || typeInfo.TypeIndex == ManagedComponentStore.CompanionReferenceTypeIndex )
                    continue;

                managedObjectRefWalker.CollectReferences(ref managedComponent);
            }
            s_AddFromManagedComponents.End();

            // Managed shared components
            s_AddFromManagedSharedComponents.Begin();
            int sharedComponentCount = access->ManagedComponentStore.GetSharedComponentCount();
            for (int i = 0; i < sharedComponentCount; i++)
            {
                var managedSharedComponent = access->ManagedComponentStore.m_SharedComponentData[i];
                if (managedSharedComponent == null)
                    continue;

                ref readonly var typeInfo = ref TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(managedSharedComponent.GetType()));

                if (!typeInfo.HasUnityObjectRefs)
                    continue;

                managedObjectRefWalker.CollectReferences(ref managedSharedComponent);
            }
            s_AddFromManagedSharedComponents.End();
        }
#endif

        private static ProfilerMarker s_AddFromChunks = new ProfilerMarker("AddFromChunks");
        private static ProfilerMarker s_AddFromUnmanagedSharedComponents = new ProfilerMarker("AddFromUnmanagaedSharedComponents");
        private static ProfilerMarker s_AddFromManagedComponents = new ProfilerMarker("AddFromManagedComponents");
        private static ProfilerMarker s_AddFromManagedSharedComponents = new ProfilerMarker("AddFromManagedSharedComponents");

        static unsafe void AdditionalRootsHandlerDelegate(IntPtr state)
        {
            using var instanceIDRefs = new UnsafeHashSet<int>(256, Allocator.Temp);
            foreach (var world in World.s_AllWorlds)
            {
                var access = world.EntityManager.GetCheckedEntityDataAccessExclusive();
                access->CompleteAllTrackedJobs();
                for (var i = 0; i < access->EntityComponentStore->m_Archetypes.Length; ++i)
                {
                    var archetype = access->EntityComponentStore->m_Archetypes.Ptr[i];

                    if (!archetype->HasUnityObjectRefs)
                        continue;

                    s_AddFromChunks.Begin();
                    AddInstanceIDRefsFromAllChunks(archetype, &instanceIDRefs);
                    s_AddFromChunks.End();
                    s_AddFromUnmanagedSharedComponents.Begin();
                    AddInstanceIDRefsFromUnmanagedSharedComponents(access, archetype, &instanceIDRefs);
                    s_AddFromUnmanagedSharedComponents.End();
                    #if !UNITY_DISABLE_MANAGED_COMPONENTS
                    AddInstanceIDRefsFromManagedComponents(access, &instanceIDRefs);
                    #endif
                }
            }

            if (instanceIDRefs.Count == 0)
                return;

            using var instanceIDs = instanceIDRefs.ToNativeArray(Allocator.Temp);
#if (UNITY_2022_3 && UNITY_2022_3_43F1_OR_NEWER) || (UNITY_6000 && UNITY_6000_0_16F1_OR_NEWER)
            ResourcesAPIInternal.EntitiesAssetGC.MarkInstanceIDsAsRoot((IntPtr)instanceIDs.GetUnsafePtr(), instanceIDs.Length, state);
#endif
        }

#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod]
#endif
        static void EditorInitializeOnLoadMethod()
        {
            #if (UNITY_2022_3 && UNITY_2022_3_43F1_OR_NEWER) || (UNITY_6000 && UNITY_6000_0_16F1_OR_NEWER)
            ResourcesAPIInternal.EntitiesAssetGC.RegisterAdditionalRootsHandler(AdditionalRootsHandlerDelegate);
            #endif
        }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    internal struct UntypedUnityObjectRef : IEquatable<UntypedUnityObjectRef>
    {
        [SerializeField]
        internal int instanceId;

        public bool Equals(UntypedUnityObjectRef other)
        {
            return instanceId == other.instanceId;
        }

        public override bool Equals(object obj)
        {
            return obj is UntypedUnityObjectRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            return instanceId;
        }
    }

    /// <summary>
    /// A utility structure that stores a reference of an <see cref="UnityEngine.Object"/> for Entities. Allows references to be stored on unmanaged component.
    /// </summary>
    /// <typeparam name="T">Type of the Object that is going to be referenced by UnityObjectRef.</typeparam>
    /// <remarks>
    /// Stores the Object's instance ID. Also serializes asset references in subscenes the same way managed components 
    /// do with direct references to <see cref="UnityEngine.Object"/>. This is the recommended way to store references to Unity 
    /// assets in Entities because it remains unmanaged.
    /// 
    /// Serialization is supported on <see cref="IComponentData"/> <see cref="ISharedComponentData"/> and <see cref="IBufferElementData"/>.
    /// 
    /// Just as when referencing an asset in a Monobehaviour, the asset will not be collected by any asset garbage collection (such as calling <see cref="Resources.UnloadUnusedAssets()"/>).
    /// 
    /// For more information, refer to [Reference Unity objects in your code](xref:reference-unity-objects).
    /// </remarks>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct UnityObjectRef<T> : IEquatable<UnityObjectRef<T>>
        where T : Object
    {
        [SerializeField]
        internal UntypedUnityObjectRef Id;
        /// <summary>
        /// Implicitly converts an <see cref="UnityEngine.Object"/> to an <see cref="UnityObjectRef{T}"/>.
        /// </summary>
        /// <param name="instance">Instance of the Object to store as a reference.</param>
        /// <returns>A UnityObjectRef referencing instance</returns>
        public static implicit operator UnityObjectRef<T>(T instance)
        {
            var instanceId = instance == null ? 0 : instance.GetInstanceID();

            return FromInstanceID(instanceId);
        }

        internal static UnityObjectRef<T> FromInstanceID(int instanceId)
        {
            var result = new UnityObjectRef<T>{Id = new UntypedUnityObjectRef{ instanceId = instanceId }};
            return result;
        }

        /// <summary>
        /// Implicitly converts an <see cref="UnityObjectRef{T}"/> to an <see cref="UnityEngine.Object"/>.
        /// </summary>
        /// <param name="unityObjectRef">Reference used to access the Object.</param>
        /// <returns>The instance of type T referenced by unityObjectRef.</returns>
        public static implicit operator T(UnityObjectRef<T> unityObjectRef)
        {
            if (unityObjectRef.Id.instanceId == 0)
                return null;
            return (T) Resources.InstanceIDToObject(unityObjectRef.Id.instanceId);
        }

        /// <summary>
        /// Object being referenced by this <see cref="UnityObjectRef{T}"/>.
        /// </summary>
        public T Value
        {
            [ExcludeFromBurstCompatTesting("Returns managed object")]
            get => this;
            [ExcludeFromBurstCompatTesting("Sets managed object")]
            set => this = value;
        }

        /// <summary>
        /// Checks if this reference and another reference are equal.
        /// </summary>
        /// <param name="other">The UnityObjectRef to compare for equality.</param>
        /// <returns>True if the two lists are equal.</returns>
        public bool Equals(UnityObjectRef<T> other)
        {
            return Id.instanceId == other.Id.instanceId;
        }

        /// <summary>
        /// Checks if this object references the same UnityEngine.Object as another object.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns>True, if the <paramref name="obj"/> parameter is a UnityEngine.Object instance that points to the same
        /// instance as this.</returns>
        public override bool Equals(object obj)
        {
            return obj is UnityObjectRef<T> other && Equals(other);
        }


        /// <summary>
        /// Overload of the 'bool' operator to check for the validity of the instance ID.
        /// </summary>
        /// <param name="obj">The object to check for validity.</param>
        /// <returns>True, if the instance ID is valid.</returns>
        public static implicit operator bool(UnityObjectRef<T> obj)
        {
            return obj.IsValid();
        }

        /// <summary>
        /// Computes a hash code for this object.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return Id.instanceId.GetHashCode();
        }

        /// <summary>
        /// Returns 'true' if the UnityObjectRef is still valid, as in the Object still exists.
        /// </summary>
        /// <returns>Valid state.</returns>
        public bool IsValid()
        {
            return Resources.InstanceIDIsValid(Id.instanceId);
        }

        /// <summary>
        /// Returns true if two <see cref="UnityObjectRef{T}"/> are equal.
        /// </summary>
        /// <param name="left">The first reference to compare for equality.</param>
        /// <param name="right">The second reference to compare for equality.</param>
        /// <returns>True if the two references are equal.</returns>
        public static bool operator ==(UnityObjectRef<T> left, UnityObjectRef<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Returns true if two <see cref="UnityObjectRef{T}"/> are not equal.
        /// </summary>
        /// <param name="left">The first reference to compare for equality.</param>
        /// <param name="right">The second reference to compare for equality.</param>
        /// <returns>True if the two references are not equal.</returns>
        public static bool operator !=(UnityObjectRef<T> left, UnityObjectRef<T> right)
        {
            return !left.Equals(right);
        }
    }
}
