using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Assertions;
using Unity.Entities.Serialization;
#if !NET_DOTS
using Unity.Properties;
#endif

namespace Unity.Entities
{
    public interface IRefCounted
    {
        void Retain();
        void Release();
    }

    internal unsafe class ManagedComponentStore
    {
        struct ManagedArrayStorage
        {
            public object[] ManagedArray;
        }

        UnsafeMultiHashMap<int, int> m_HashLookup = new UnsafeMultiHashMap<int, int>(128, Allocator.Persistent);

        List<object> m_SharedComponentData = new List<object>();

        struct SharedComponentInfo
        {
            public int RefCount;
            public int ComponentType;
            public int Version;
            public int HashCode;
        }

        UnsafeList m_SharedComponentInfo = new UnsafeList(Allocator.Persistent);

        private SharedComponentInfo* SharedComponentInfoPtr
        {
            get { return (SharedComponentInfo*) m_SharedComponentInfo.Ptr; }
        }

        int m_FreeListIndex;

        ManagedArrayStorage[] m_ManagedArrays = new ManagedArrayStorage[0];

        internal delegate bool InstantiateHybridComponentDelegate(object obj, ManagedComponentStore srcStore, Archetype* dstArch, ManagedComponentStore dstStore, int dstManagedArrayIndex, int dstChunkCapacity, Entity srcEntity, Entity* dstEntities, int entityCount, int dstTypeIndex, int dstBaseIndex, ref object[] gameObjectInstances);
        internal static InstantiateHybridComponentDelegate InstantiateHybridComponent;

        public ManagedComponentStore()
        {
            ResetSharedComponentData();
        }

        public void Dispose()
        {
            for (var i = 1; i != m_SharedComponentData.Count; i++)
                (m_SharedComponentData[i] as IRefCounted)?.Release();
            m_SharedComponentInfo.Dispose();
            m_SharedComponentData.Clear();
            m_SharedComponentData = null;
            m_HashLookup.Dispose();
        }

        void ResetSharedComponentData()
        {
            m_HashLookup.Clear();
            m_SharedComponentData.Clear();
            m_SharedComponentInfo.Clear();
            
            m_SharedComponentData.Add(null);
            m_SharedComponentInfo.Add(new SharedComponentInfo { RefCount = 1, ComponentType = -1, Version = 1, HashCode = 0});
            m_FreeListIndex = -1;
        }

        public void GetAllUniqueSharedComponents<T>(List<T> sharedComponentValues)
            where T : struct, ISharedComponentData
        {
            sharedComponentValues.Add(default(T));
            for (var i = 1; i != m_SharedComponentData.Count; i++)
            {
                var data = m_SharedComponentData[i];
                if (data != null && data.GetType() == typeof(T))
                    sharedComponentValues.Add((T)m_SharedComponentData[i]);
            }
        }

        public void GetAllUniqueSharedComponents<T>(List<T> sharedComponentValues, List<int> sharedComponentIndices)
            where T : struct, ISharedComponentData
        {
            sharedComponentValues.Add(default(T));
            sharedComponentIndices.Add(0);
            for (var i = 1; i != m_SharedComponentData.Count; i++)
            {
                var data = m_SharedComponentData[i];
                if (data != null && data.GetType() == typeof(T))
                {
                    sharedComponentValues.Add((T)m_SharedComponentData[i]);
                    sharedComponentIndices.Add(i);
                }
            }
        }

        public int GetSharedComponentCount()
        {
            return m_SharedComponentData.Count;
        }

        public int InsertSharedComponent<T>(T newData) where T : struct
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var index = FindSharedComponentIndex(TypeManager.GetTypeIndex<T>(), newData);

            if (index == 0) return 0;

            if (index != -1)
            {
                SharedComponentInfoPtr[index].RefCount++;
                return index;
            }

            var hashcode = TypeManager.GetHashCode<T>(ref newData);

            object newDataObj = newData;

            (newDataObj as IRefCounted)?.Retain();
            return Add(typeIndex, hashcode, newDataObj);
        }

        private int FindSharedComponentIndex<T>(int typeIndex, T newData) where T : struct
        {
            var defaultVal = default(T);
            if (TypeManager.Equals(ref defaultVal, ref newData))
                return 0;

            return FindNonDefaultSharedComponentIndex(typeIndex, TypeManager.GetHashCode(ref newData),
                UnsafeUtility.AddressOf(ref newData));
        }

        private int FindNonDefaultSharedComponentIndex(int typeIndex, int hashCode, void* newData)
        {
            int itemIndex;
            NativeMultiHashMapIterator<int> iter;

            if (!m_HashLookup.TryGetFirstValue(hashCode, out itemIndex, out iter))
                return -1;

            var infos = SharedComponentInfoPtr;
            do
            {
                var data = m_SharedComponentData[itemIndex];
                if (data != null && infos[itemIndex].ComponentType == typeIndex)
                {
                    if (TypeManager.Equals(data, newData, typeIndex))
                        return itemIndex;
                }
            }
            while (m_HashLookup.TryGetNextValue(out itemIndex, ref iter));

            return -1;
        }

        private int FindNonDefaultSharedComponentIndex(int typeIndex, int hashCode, object newData)
        {
            int itemIndex;
            NativeMultiHashMapIterator<int> iter;

            if (!m_HashLookup.TryGetFirstValue(hashCode, out itemIndex, out iter))
                return -1;

            var infos = SharedComponentInfoPtr;
            do
            {
                var data = m_SharedComponentData[itemIndex];
                if (data != null && infos[itemIndex].ComponentType == typeIndex)
                {
                    if (TypeManager.Equals(data, newData, typeIndex))
                        return itemIndex;
                }
            }
            while (m_HashLookup.TryGetNextValue(out itemIndex, ref iter));

            return -1;
        }

        internal int InsertSharedComponentAssumeNonDefault(int typeIndex, int hashCode, object newData)
        {
            var index = FindNonDefaultSharedComponentIndex(typeIndex, hashCode, newData);

            if (-1 == index)
            {
                (newData as IRefCounted)?.Retain();
                index = Add(typeIndex, hashCode, newData);
            }
            else
            {
                SharedComponentInfoPtr[index].RefCount++;
            }
    
            return index;
        }

        internal int InsertSharedComponentAssumeNonDefaultMove(int typeIndex, int hashCode, object newData)
        {
            var index = FindNonDefaultSharedComponentIndex(typeIndex, hashCode, newData);

            if (-1 == index)
                index = Add(typeIndex, hashCode, newData);
            else
                SharedComponentInfoPtr[index].RefCount++;

            return index;
        }

        private int Add(int typeIndex, int hashCode, object newData)
        {
            var info = new SharedComponentInfo
            {
                RefCount = 1,
                Version = 1,
                ComponentType = typeIndex,
                HashCode = hashCode
            };
            
            if (m_FreeListIndex != -1)
            {
                var infos = SharedComponentInfoPtr;

                int index = m_FreeListIndex;
                m_FreeListIndex = infos[index].Version;

                Assert.IsTrue(m_SharedComponentData[index] == null);

                m_HashLookup.Add(hashCode, index);
                m_SharedComponentData[index] = newData;
                infos[index] = info;
                return index;
            }
            else
            {
                int index = m_SharedComponentData.Count;
                m_HashLookup.Add(hashCode, index);
                m_SharedComponentData.Add(newData);
                m_SharedComponentInfo.Add(info);
                return index;
            }
        }

        public void IncrementSharedComponentVersion(int index)
        {
            SharedComponentInfoPtr[index].Version++;
        }
        
        public int GetSharedComponentVersion<T>(T sharedData) where T : struct
        {
            var index = FindSharedComponentIndex(TypeManager.GetTypeIndex<T>(), sharedData);
            return index == -1 ? 0 : SharedComponentInfoPtr[index].Version;
        }

        public T GetSharedComponentData<T>(int index) where T : struct
        {
            if (index == 0)
                return default(T);

            return (T)m_SharedComponentData[index];
        }

        public object GetSharedComponentDataBoxed(int index, int typeIndex)
        {
#if !NET_DOTS
            if (index == 0)
                return Activator.CreateInstance(TypeManager.GetType(typeIndex));
#else
            if (index == 0)
                throw new InvalidOperationException("Implement TypeManager.GetType(typeIndex).DefaultValue");
#endif
            return m_SharedComponentData[index];
        }

        public object GetSharedComponentDataNonDefaultBoxed(int index)
        {
            Assert.AreNotEqual(0, index);
            return m_SharedComponentData[index];
        }

        public void AddReference(int index, int numRefs = 1)
        {
            if (index == 0)
                return;
            Assert.IsTrue(numRefs >= 0);
            SharedComponentInfoPtr[index].RefCount += numRefs;
        }

        public void RemoveReference(int index, int numRefs = 1)
        {
            if (index == 0)
                return;

            var infos = SharedComponentInfoPtr;
            
            var newCount = infos[index].RefCount -= numRefs;
            Assert.IsTrue(newCount >= 0);

            if (newCount != 0)
                return;

            var hashCode = infos[index].HashCode;

            object sharedComponent = m_SharedComponentData[index];
            Assert.IsFalse(ReferenceEquals(sharedComponent, null));
            (sharedComponent as IRefCounted)?.Release();

            m_SharedComponentData[index] = null;
            infos[index].ComponentType = -1;
            infos[index].Version = m_FreeListIndex;
            m_FreeListIndex = index;

            int itemIndex;
            NativeMultiHashMapIterator<int> iter;
            if (m_HashLookup.TryGetFirstValue(hashCode, out itemIndex, out iter))
            {
                do
                {
                    if (itemIndex == index)
                    {
                        m_HashLookup.Remove(iter);
                        return;
                    }
                }
                while (m_HashLookup.TryGetNextValue(out itemIndex, ref iter));
            }
            
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            throw new System.InvalidOperationException("shared component couldn't be removed due to internal state corruption");
            #endif
        }

        public void CheckInternalConsistency()
        {
            var infos = SharedComponentInfoPtr;

            int refcount = 0;
            for (int i = 0; i < m_SharedComponentData.Count; ++i)
            {
                if (m_SharedComponentData[i] != null)
                {
                    refcount++;
                    
                    var hashCode = infos[i].HashCode;

                    bool found = false;
                    int itemIndex;
                    NativeMultiHashMapIterator<int> iter;
                    if (m_HashLookup.TryGetFirstValue(hashCode, out itemIndex, out iter))
                    {
                        do
                        {
                            if (itemIndex == i)
                                found = true;
                        }
                        while (m_HashLookup.TryGetNextValue(out itemIndex, ref iter));
                    }

                    Assert.IsTrue(found);
                }
            }

            Assert.AreEqual(refcount, m_HashLookup.Length);
        }

        public bool IsEmpty()
        {
            var infos = SharedComponentInfoPtr;

            for (int i = 1; i < m_SharedComponentData.Count; ++i)
            {
                if (m_SharedComponentData[i] != null)
                    return false;

                if (infos[i].ComponentType != -1)
                    return false;

                if (infos[i].RefCount != 0)
                    return false;
            }

            if (m_SharedComponentData[0] != null)
                return false;

            if (m_HashLookup.Length != 0)
                return false;

            return true;
        }
        
        public void CopySharedComponents(ManagedComponentStore srcManagedComponents, int* sharedComponentIndices, int sharedComponentIndicesCount)
        {
            var srcInfos = srcManagedComponents.SharedComponentInfoPtr;

            for (var i = 0; i != sharedComponentIndicesCount; i++)
            {
                var srcIndex = sharedComponentIndices[i];
                if (srcIndex == 0)
                    continue;

                var srcData = srcManagedComponents.m_SharedComponentData[srcIndex];
                var typeIndex = srcInfos[srcIndex].ComponentType;
                var hashCode = srcInfos[srcIndex].HashCode;
                var dstIndex = InsertSharedComponentAssumeNonDefault(typeIndex, hashCode, srcData);

                sharedComponentIndices[i] = dstIndex;
            }
        }

        public bool AllSharedComponentReferencesAreFromChunks(EntityComponentStore* entityComponentStore)
        {
            using (var refCounts = new NativeArray<int>(m_SharedComponentInfo.Length, Allocator.Temp))
            {
                var refCountPtrs = (int*)refCounts.GetUnsafePtr();
                for (var i = 0; i < entityComponentStore->m_Archetypes.Length; ++i)
                {
                    var archetype = entityComponentStore->m_Archetypes.Ptr[i];
                    var chunkCount = archetype->Chunks.Count;
                    for (int j = 0; j < archetype->NumSharedComponents; ++j)
                    {
                        var values = archetype->Chunks.GetSharedComponentValueArrayForType(j);
                        for (var ci = 0; ci < chunkCount; ++ci)
                            refCountPtrs[values[ci]] += 1;
                    }
                }

                var infos = SharedComponentInfoPtr;
                for (int i = 1; i < refCounts.Length; i++)
                {
                    if (refCountPtrs[i] != infos[i].RefCount)
                        return false;
                }

                return true;
            }
        }

        public NativeArray<int> MoveAllSharedComponents(ManagedComponentStore srcManagedComponents, Allocator allocator)
        {
            var remap = new NativeArray<int>(srcManagedComponents.GetSharedComponentCount(), allocator);
            remap[0] = 0;

            var srcInfos = srcManagedComponents.SharedComponentInfoPtr;
            for (int srcIndex = 1; srcIndex < remap.Length; ++srcIndex)
            {
                var srcData = srcManagedComponents.m_SharedComponentData[srcIndex];
                if (srcData == null) continue;

                var typeIndex = srcInfos[srcIndex].ComponentType;
                var hashCode = srcInfos[srcIndex].HashCode;
                var dstIndex = InsertSharedComponentAssumeNonDefaultMove(typeIndex, hashCode, srcData);

                SharedComponentInfoPtr[dstIndex].RefCount += srcInfos[srcIndex].RefCount - 1;
                SharedComponentInfoPtr[dstIndex].Version++;

                remap[srcIndex] = dstIndex;
            }

            srcManagedComponents.ResetSharedComponentData();

            return remap;
        }

        public NativeArray<int> MoveSharedComponents(ManagedComponentStore srcManagedComponents, NativeArray<ArchetypeChunk> chunks,  Allocator allocator)
        {
            var remap = new NativeArray<int>(srcManagedComponents.GetSharedComponentCount(), allocator);
            var remapPtr = (int*)remap.GetUnsafePtr();
            // Build a map of all shared component values that will be moved
            // remap will have a refcount of how many chunks reference the shared component after this loop
            for (int i = 0; i < chunks.Length; ++i)
            {
                var chunk = chunks[i].m_Chunk;
                var archetype = chunk->Archetype;
                var sharedComponentValues = chunk->SharedComponentValues;
                for (int sharedComponentIndex = 0; sharedComponentIndex < archetype->NumSharedComponents; ++sharedComponentIndex)
                    remapPtr[sharedComponentValues[sharedComponentIndex]]++;
            }        

            remap[0] = 0;

            // Move all shared components that are being referenced
            // remap will have a remap table of src SharedComponentDataIndex -> dst SharedComponentDataIndex 
            var srcInfos = srcManagedComponents.SharedComponentInfoPtr;
            for (int srcIndex = 1; srcIndex < remap.Length; ++srcIndex)
            {
                if (remapPtr[srcIndex] == 0)
                    continue;

                var srcData = srcManagedComponents.m_SharedComponentData[srcIndex];
                var typeIndex = srcInfos[srcIndex].ComponentType;
                var hashCode = srcInfos[srcIndex].HashCode;

                var dstIndex = InsertSharedComponentAssumeNonDefault(typeIndex, hashCode, srcData);

                // * remove refcount based on refcount table
                // * -1 because InsertSharedComponentAssumeNonDefault above adds 1 refcount
                int srcRefCount = remapPtr[srcIndex];
                SharedComponentInfoPtr[dstIndex].RefCount += srcRefCount - 1; 
                srcManagedComponents.RemoveReference(srcIndex, srcRefCount);
                SharedComponentInfoPtr[dstIndex].Version++;

                remapPtr[srcIndex] = dstIndex;
            }

            return remap;
        }

        public void MoveManagedObjectArrays(NativeArray<int> srcIndices, NativeArray<int> dstIndices, ManagedComponentStore srcManagedComponentStore)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (srcIndices.Length != dstIndices.Length)
                throw new ArgumentException($"The amount of source and destination indices when moving managed arrays should match! {srcIndices.Length} != {dstIndices.Length}");
#endif

            for (int i = 0; i < srcIndices.Length; ++i)
            {
                int src = srcIndices[i];
                int dst = dstIndices[i];
                m_ManagedArrays[dst] = srcManagedComponentStore.m_ManagedArrays[src];
                srcManagedComponentStore.m_ManagedArrays[src] = new ManagedArrayStorage();
            }
        }

        public void PrepareForDeserialize()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!IsEmpty())
                throw new System.ArgumentException("SharedComponentManager must be empty when deserializing a scene");
#endif

            ResetSharedComponentData();
        }

        internal void DeallocateManagedArrayStorage(int index)
        {
            Assert.IsTrue(m_ManagedArrays.Length > index);
            Assert.IsTrue(m_ManagedArrays[index].ManagedArray != null);
            m_ManagedArrays[index] = new ManagedArrayStorage();
        }

        internal void AllocateManagedArrayStorage(int index, int length)
        {
            var managedArray = new object[length];
            var managedArrayStorage = new ManagedArrayStorage {ManagedArray = managedArray};
            if (m_ManagedArrays.Length <= index)
            {
                Array.Resize(ref m_ManagedArrays, index + 1);
            }

            m_ManagedArrays[index] = managedArrayStorage;
        }

        internal void ReserveManagedArrayStorage(int count)
        {
            Array.Resize(ref m_ManagedArrays, m_ManagedArrays.Length + count);
        }

        public object GetManagedObject(Chunk* chunk, ComponentType type, int index)
        {
            var typeOfs = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, type.TypeIndex);
            if (typeOfs < 0 || chunk->Archetype->ManagedArrayOffset[typeOfs] < 0)
                throw new InvalidOperationException("Trying to get managed object for non existing component");
            return GetManagedObject(chunk, typeOfs, index);
        }

        internal object GetManagedObject(Chunk* chunk, int type, int index)
        {
            var managedStart = chunk->Archetype->ManagedArrayOffset[type] * chunk->Capacity;
            return m_ManagedArrays[chunk->ManagedArrayIndex].ManagedArray[index + managedStart];
        }

        internal object GetManagedObject(Archetype* archetype, int managedArrayIndex, int chunkCapacity, int type, int index)
        {
            var managedStart = archetype->ManagedArrayOffset[type] * chunkCapacity;
            return m_ManagedArrays[managedArrayIndex].ManagedArray[index + managedStart];
        }

        public object[] GetManagedObjectRange(Chunk* chunk, int type, out int rangeStart, out int rangeLength)
        {
            rangeStart = chunk->Archetype->ManagedArrayOffset[type] * chunk->Capacity;
            rangeLength = chunk->Count;
            return m_ManagedArrays[chunk->ManagedArrayIndex].ManagedArray;
        }

        public void SetManagedObject(Chunk* chunk, int type, int index, object val)
        {
            var managedStart = chunk->Archetype->ManagedArrayOffset[type] * chunk->Capacity;
            m_ManagedArrays[chunk->ManagedArrayIndex].ManagedArray[index + managedStart] = val;
        }

        public void SetManagedObject(Archetype* archetype, int managedArrayIndex, int chunkCapacity, int type, int index, object val)
        {
            var managedStart = archetype->ManagedArrayOffset[type] * chunkCapacity;
            m_ManagedArrays[managedArrayIndex].ManagedArray[index + managedStart] = val;
        }

        public void SetManagedObject(Chunk* chunk, ComponentType type, int index, object val)
        {
            var typeOfs = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, type.TypeIndex);
            if (typeOfs < 0 || chunk->Archetype->ManagedArrayOffset[typeOfs] < 0)
                throw new InvalidOperationException("Trying to set managed object for non existing component");
            SetManagedObject(chunk, typeOfs, index, val);
        }

        public static void CopyManagedObjects(
            ManagedComponentStore srcStore, Archetype* srcArch, int srcManagedArrayIndex, int srcChunkCapacity, int srcStartIndex,
            ManagedComponentStore dstStore, Archetype* dstArch, int dstManagedArrayIndex, int dstChunkCapacity, int dstStartIndex, int count)
        {
            var srcI = 0;
            var dstI = 0;
            while (srcI < srcArch->TypesCount && dstI < dstArch->TypesCount)
            {
                if (srcArch->Types[srcI] < dstArch->Types[dstI])
                {
                    ++srcI;
                }
                else if (srcArch->Types[srcI] > dstArch->Types[dstI])
                {
                    ++dstI;
                }
                else
                {
                    if (srcArch->IsManaged(srcI))
                    {
                        var componentType = srcArch->Types[srcI];
                        var typeInfo = TypeManager.GetTypeInfo(componentType.TypeIndex);
                        if (typeInfo.Category == TypeManager.TypeCategory.Class)
                        {
                            // If we are dealing with a Class/GameObject types just perform a shallow copy
                            for (var i = 0; i < count; ++i)
                            {
                                var obj = srcStore.GetManagedObject(srcArch, srcManagedArrayIndex, srcChunkCapacity, srcI, srcStartIndex + i);
                                dstStore.SetManagedObject(dstArch, dstManagedArrayIndex, dstChunkCapacity, dstI, dstStartIndex + i, obj);
                            }
                        }
                        else
                        {
                            for (var i = 0; i < count; ++i)
                            {
                                var obj = srcStore.GetManagedObject(srcArch, srcManagedArrayIndex, srcChunkCapacity, srcI, srcStartIndex + i);
                                dstStore.SetManagedObject(dstArch, dstManagedArrayIndex, dstChunkCapacity, dstI, dstStartIndex + i, obj);
                            }
                        }
                    }

                    ++srcI;
                    ++dstI;
                }
            }
        }

        public static void ReplicateManagedObjects(
            ManagedComponentStore srcStore, Archetype* srcArch, int srcManagedArrayIndex, int srcChunkCapacity, int srcIndex,
            ManagedComponentStore dstStore, Archetype* dstArch, int dstManagedArrayIndex, int dstChunkCapacity, int dstBaseIndex,
            int count, Entity srcEntity, Entity* dstEntities, int entityCount)
        {
            object[] companionGameObjectInstances = null;

            var srcI = 0;
            var dstI = 0;
            while (srcI < srcArch->TypesCount && dstI < dstArch->TypesCount)
            {
                if (srcArch->Types[srcI] < dstArch->Types[dstI])
                {
                    ++srcI;
                }
                else if (srcArch->Types[srcI] > dstArch->Types[dstI])
                {
                    ++dstI;
                }
                else
                {
                    if (srcArch->IsManaged(srcI))
                    {
                        var componentType = srcArch->Types[srcI];
                        var typeInfo = TypeManager.GetTypeInfo(componentType.TypeIndex);
                        var obj = srcStore.GetManagedObject(srcArch, srcManagedArrayIndex, srcChunkCapacity, srcI, srcIndex);

                        if (typeInfo.Category == TypeManager.TypeCategory.Class)
                        {
                            // If we're dealing with a class based type, we will defer the execution to InstantiateHybridComponent (if dependency injection was made), this method will
                            // - Determine if the object should be cloned (true is returned) or referenced (false is returned)
                            // - Clone the GameObject and its components (as many times as we have in 'count'), and make it a Companion Game Object by adding a CompanionLink component to it
                            // - Add the Cloned Hybrid Component to the instantiated entities (again 'count' times)
                            if (InstantiateHybridComponent == null || !InstantiateHybridComponent(obj, srcStore, dstArch, dstStore, dstManagedArrayIndex, dstChunkCapacity, srcEntity, dstEntities, entityCount, dstI, dstBaseIndex, ref companionGameObjectInstances))
                            {
                                // We end up here if we have to reference the object and not cloning it
                                for (var i = 0; i < count; ++i)
                                {
                                    dstStore.SetManagedObject(dstArch, dstManagedArrayIndex, dstChunkCapacity, dstI, dstBaseIndex + i, obj);
                                }
                            }
                        }
                        else
                        {
#if NET_DOTS
                            for (var i = 0; i < count; ++i)
                            {
                                // Until DOTS Runtime supports Properties just perform a simple shallow copy
                                dstStore.SetManagedObject(dstArch, dstManagedArrayIndex, dstChunkCapacity, dstI, dstBaseIndex + i, obj);
                            }
#else
                            if (obj == null)
                            {
                                // If we are dealing with a Class/GameObject types just perform a shallow copy
                                for (var i = 0; i < count; ++i)
                                {
                                    dstStore.SetManagedObject(dstArch, dstManagedArrayIndex, dstChunkCapacity, dstI, dstBaseIndex + i, obj);
                                }
                            }
                            else
                            {
                                // Unless we want to enforce managed components to implement an IDeepClonable interface
                                // we instead generate a binary stream of an object and then use that to instantiate our new deep copy
                                var type = TypeManager.GetType(componentType.TypeIndex);
                                var buffer = new UnsafeAppendBuffer(16, 16, Allocator.Temp);
                                var writer = new PropertiesBinaryWriter(&buffer);
                                BoxedProperties.WriteBoxedType(obj, writer);

                                for (var i = 0; i < count; ++i)
                                {
                                    var readBuffer = buffer.AsReader();
                                    var reader = new PropertiesBinaryReader(&readBuffer, writer.GetObjectTable());
                                    object newObj = BoxedProperties.ReadBoxedClass(type, reader);

                                    dstStore.SetManagedObject(dstArch, dstManagedArrayIndex, dstChunkCapacity, dstI, dstBaseIndex + i, newObj);
                                }
                                buffer.Dispose();
                            }
#endif
                        }
                    }

                    ++srcI;
                    ++dstI;
                }
            }
        }

        public void PatchEntities(ManagedComponentStore managedComponentStore, Archetype* archetype, Chunk* chunk, int entityCount,
            EntityRemapUtility.EntityRemapInfo* remapping)
        {
#if !NET_DOTS
            var managedPatches = archetype->ManagedEntityPatches;
            var managedPatchCount = archetype->ManagedEntityPatchCount;

            // Patch managed components with entity references.
            for (int p = 0; p < managedPatchCount; p++)
            {
                var componentType = managedPatches[p].Type;

                for (int i = 0; i != entityCount; i++)
                {
                    var obj = managedComponentStore.GetManagedObject(chunk, componentType, i);
                    EntityRemapUtility.PatchEntityInBoxedType(obj, remapping);
                }
            }
#endif
        }

        public void PatchEntitiesForPrefab(ManagedComponentStore managedComponentStore, Archetype* archetype, Chunk* chunk, int indexInChunk, int entityCount,
            EntityRemapUtility.SparseEntityRemapInfo* remapping, int remappingCount, Allocator allocator)
        {
#if !NET_DOTS
            var managedPatches = archetype->ManagedEntityPatches;
            var managedPatchCount = archetype->ManagedEntityPatchCount;

            // Patch managed components with entity references.
            for (int p = 0; p < managedPatchCount; p++)
            {
                var componentType = managedPatches[p].Type;

                for (int e = 0; e != entityCount; e++)
                {
                    var obj = managedComponentStore.GetManagedObject(chunk, componentType, e + indexInChunk);

                    EntityRemapUtility.PatchEntityForPrefabInBoxedType(obj, remapping + e * remappingCount, remappingCount);
                }
            }
            UnsafeUtility.Free(remapping, allocator);
#endif
        }

        public void ClearManagedObjects(Archetype* archetype, int managedArrayIndex, int chunkCapacity, int index, int count)
        {
            for (var type = 0; type < archetype->TypesCount; ++type)
            {
                if (archetype->ManagedArrayOffset[type] < 0)
                    continue;

                for (var i = 0; i < count; ++i)
                    SetManagedObject(archetype, managedArrayIndex, chunkCapacity, type, index + i, null);
            }
        }

        public void Playback(ref ManagedDeferredCommands managedDeferredCommands)
        {
            var reader = new UnsafeAppendBuffer.Reader(ref managedDeferredCommands.CommandBuffer);
            while (!reader.EndOfBuffer)
            {
                var cmd = reader.ReadNext<int>();
                switch ((ManagedDeferredCommands.Command)cmd)
                {
                    case (ManagedDeferredCommands.Command.IncrementSharedComponentVersion):
                    {
                        var sharedIndex = reader.ReadNext<int>();
                        IncrementSharedComponentVersion(sharedIndex);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.CopyManagedObjects):
                    {
                        var srcArchetype = (Archetype*)reader.ReadNext<IntPtr>();
                        var srcManagedArrayIndex = reader.ReadNext<int>();
                        var srcChunkCapacity = reader.ReadNext<int>();
                        var srcStartIndex = reader.ReadNext<int>();
                        var dstArchetype = (Archetype*)reader.ReadNext<IntPtr>();
                        var dstManagedArrayIndex = reader.ReadNext<int>();
                        var dstChunkCapacity = reader.ReadNext<int>();
                        var dstStartIndex = reader.ReadNext<int>();
                        var count = reader.ReadNext<int>();

                        CopyManagedObjects(this, srcArchetype, srcManagedArrayIndex, srcChunkCapacity, srcStartIndex,
                            this, dstArchetype, dstManagedArrayIndex, dstChunkCapacity, dstStartIndex, count);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.ReplicateManagedObjects):
                    {
                        var srcArchetype = (Archetype*)reader.ReadNext<IntPtr>();
                        var srcManagedArrayIndex = reader.ReadNext<int>();
                        var srcChunkCapacity = reader.ReadNext<int>();
                        var srcIndex = reader.ReadNext<int>();
                        var dstArchetype = (Archetype*)reader.ReadNext<IntPtr>();
                        var dstManagedArrayIndex = reader.ReadNext<int>();
                        var dstChunkCapacity = reader.ReadNext<int>();
                        var dstBaseStartIndex = reader.ReadNext<int>();
                        var count = reader.ReadNext<int>();

                        var srcEntity = reader.ReadNext<Entity>();
                        var dstEntities = (Entity*)reader.ReadNextArray<Entity>(out var entityCount);

                        ReplicateManagedObjects(this, srcArchetype, srcManagedArrayIndex, srcChunkCapacity, srcIndex,
                            this, dstArchetype, dstManagedArrayIndex, dstChunkCapacity, dstBaseStartIndex, count, srcEntity, dstEntities, entityCount);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.ClearManagedObjects):
                    {
                        var archetype = (Archetype*)reader.ReadNext<IntPtr>();
                        var managedArrayIndex = reader.ReadNext<int>();
                        var chunkCapacity = reader.ReadNext<int>();
                        var index = reader.ReadNext<int>();
                        var count = reader.ReadNext<int>();

                        ClearManagedObjects(archetype, managedArrayIndex, chunkCapacity, index, count);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.AddReference):
                    {
                        var index = reader.ReadNext<int>();
                        var numRefs = reader.ReadNext<int>();
                        AddReference(index, numRefs);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.RemoveReference):
                    {
                        var index = reader.ReadNext<int>();
                        var numRefs = reader.ReadNext<int>();
                        RemoveReference(index, numRefs);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.DeallocateManagedArrayStorage):
                    {
                        var index = reader.ReadNext<int>();
                        DeallocateManagedArrayStorage(index);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.AllocateManagedArrayStorage):
                    {
                        var index = reader.ReadNext<int>();
                        var length = reader.ReadNext<int>();
                        AllocateManagedArrayStorage(index, length);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.ReserveManagedArrayStorage):
                    {
                        var count = reader.ReadNext<int>();
                        ReserveManagedArrayStorage(count);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.MoveChunksManagedObjects):
                    {
                        var oldArchetype = (Archetype*)reader.ReadNext<IntPtr>();
                        var oldManagedArrayIndex = reader.ReadNext<int>();
                        var newArchetype = (Archetype*)reader.ReadNext<IntPtr>();
                        var newManagedArrayIndex = reader.ReadNext<int>();
                        var chunkCapacity = reader.ReadNext<int>();
                        var count = reader.ReadNext<int>();

                        CopyManagedObjects(this, oldArchetype, oldManagedArrayIndex, chunkCapacity, 0,
                            this, newArchetype, newManagedArrayIndex, chunkCapacity, 0, count);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.PatchManagedEntities):
                    {
                        var archetype = (Archetype*)reader.ReadNext<IntPtr>();
                        var chunk = (Chunk*)reader.ReadNext<IntPtr>();
                        var entityCount = reader.ReadNext<int>();
                        var remapping = (EntityRemapUtility.EntityRemapInfo*)reader.ReadNext<IntPtr>();

                        PatchEntities(this, archetype, chunk, entityCount, remapping);
                    }
                    break;

                    case (ManagedDeferredCommands.Command.PatchManagedEntitiesForPrefabs):
                    {
                        var archetype = (Archetype*)reader.ReadNext<IntPtr>();
                        var chunk = (Chunk*)reader.ReadNext<IntPtr>();
                        var indexInChunk = reader.ReadNext<int>();
                        var entityCount = reader.ReadNext<int>();
                        var remapping = (EntityRemapUtility.SparseEntityRemapInfo*)reader.ReadNext<IntPtr>();
                        var remappingCount = reader.ReadNext<int>();
                        var allocator = (Allocator)reader.ReadNext<int>();

                        PatchEntitiesForPrefab(this, archetype, chunk, indexInChunk, entityCount, remapping, remappingCount, allocator);
                    }
                    break;
                }
            }

            managedDeferredCommands.Reset();
        }
    }
}
