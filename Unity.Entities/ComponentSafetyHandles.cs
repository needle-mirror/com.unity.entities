#if ENABLE_UNITY_COLLECTIONS_CHECKS
using System.Text;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace Unity.Entities
{
    unsafe struct ComponentSafetyHandles
    {
        const int              kMaxTypes = TypeManager.MaximumTypesCount;

        ComponentSafetyHandle* m_ComponentSafetyHandles;
        ushort                 m_ComponentSafetyHandlesCount;
        const int              EntityTypeIndex = 1;

        ushort*                m_TypeArrayIndices;
        const ushort           NullTypeIndex = 0xFFFF;
        // Per-component-type Static safety IDs are shared across all Worlds.
        static int* m_StaticSafetyIdsForComponentDataFromEntity;
        static int* m_StaticSafetyIdsForArchetypeChunkArrays;
        static int m_StaticSafetyIdForDynamicComponentTypeHandle = 0;
        static int m_StaticSafetyIdForEntityTypeHandle = 0;
        static byte[] m_CustomDeallocatedErrorMessageBytes = Encoding.UTF8.GetBytes("Attempted to access {5} which has been invalidated by a structural change.");
        static byte[] m_CustomDeallocatedFromJobErrorMessageBytes = Encoding.UTF8.GetBytes("Attempted to access the {5} {3} which has been invalidated by a structural change.");
        public void SetCustomErrorMessage(int staticSafetyId, AtomicSafetyErrorType errorType, byte[] messageBytes)
        {
            fixed(byte* pBytes = messageBytes)
            {
                AtomicSafetyHandle.SetCustomErrorMessage(staticSafetyId, errorType, pBytes, messageBytes.Length);
            }
        }

        int CreateStaticSafetyId(string ownerTypeName)
        {
            int staticSafetyId = 0;

            byte[] ownerNameByteArray = Encoding.UTF8.GetBytes(ownerTypeName);
            fixed(byte* ownerTypeNameBytes = ownerNameByteArray)
            {
                staticSafetyId = AtomicSafetyHandle.NewStaticSafetyId(ownerTypeNameBytes, ownerNameByteArray.Length);
            }

            SetCustomErrorMessage(staticSafetyId, AtomicSafetyErrorType.Deallocated, m_CustomDeallocatedErrorMessageBytes);
            SetCustomErrorMessage(staticSafetyId, AtomicSafetyErrorType.DeallocatedFromJob, m_CustomDeallocatedFromJobErrorMessageBytes);

            return staticSafetyId;
        }

        [BurstDiscard]
        void CreateStaticSafetyIdsForType(int typeIndex)
        {
            var typeIndexWithoutFlags = typeIndex & TypeManager.ClearFlagsMask;
            if (m_StaticSafetyIdsForComponentDataFromEntity[typeIndexWithoutFlags] == 0)
            {
                if (TypeManager.IsBuffer(typeIndex))
                {
                    m_StaticSafetyIdsForComponentDataFromEntity[typeIndexWithoutFlags] =
                        CreateStaticSafetyId(
                            "BufferFromEntity<" + TypeManager.GetTypeInfo(typeIndex).DebugTypeName + ">");
                }
                else
                {
                    m_StaticSafetyIdsForComponentDataFromEntity[typeIndexWithoutFlags] =
                        CreateStaticSafetyId(
                            "ComponentDataFromEntity<" + TypeManager.GetTypeInfo(typeIndex).DebugTypeName + ">");
                }
            }
            if (m_StaticSafetyIdsForArchetypeChunkArrays[typeIndexWithoutFlags] == 0)
            {
                if (TypeManager.IsBuffer(typeIndex))
                {
                    m_StaticSafetyIdsForArchetypeChunkArrays[typeIndexWithoutFlags] =
                        CreateStaticSafetyId(
                            "BufferTypeHandle<" + TypeManager.GetTypeInfo(typeIndex).DebugTypeName + ">");
                }
                else if (TypeManager.IsSharedComponentType(typeIndex))
                {
                    m_StaticSafetyIdsForArchetypeChunkArrays[typeIndexWithoutFlags] =
                        CreateStaticSafetyId(
                            "SharedComponentTypeHandle<" + TypeManager.GetTypeInfo(typeIndex).DebugTypeName + ">");
                }
                else
                {
                    m_StaticSafetyIdsForArchetypeChunkArrays[typeIndexWithoutFlags] =
                        CreateStaticSafetyId(
                            "ComponentTypeHandle<" + TypeManager.GetTypeInfo(typeIndex).DebugTypeName + ">");
                }
            }
        }

        [BurstDiscard]
        private void SetStaticSafetyIdForHandle_ArchetypeChunk(ref AtomicSafetyHandle handle, int typeIndex, bool dynamic)
        {
            // Configure safety handle static safety ID for ArchetypeChunk*Type by default
            int typeIndexWithoutFlags = typeIndex & TypeManager.ClearFlagsMask;
            int staticSafetyId = 0;
            if (dynamic)
                staticSafetyId = m_StaticSafetyIdForDynamicComponentTypeHandle;
            else if (typeIndex == EntityTypeIndex)
                staticSafetyId = m_StaticSafetyIdForEntityTypeHandle;
            else
                staticSafetyId = m_StaticSafetyIdsForArchetypeChunkArrays[typeIndexWithoutFlags];
            AtomicSafetyHandle.SetStaticSafetyId(ref handle, staticSafetyId);
        }

        [BurstDiscard]
        private void SetStaticSafetyIdForHandle_FromEntity(ref AtomicSafetyHandle handle, int typeIndex)
        {
            // Configure safety handle static safety ID for ArchetypeChunk*Type by default
            int typeIndexWithoutFlags = typeIndex & TypeManager.ClearFlagsMask;
            int staticSafetyId = m_StaticSafetyIdsForComponentDataFromEntity[typeIndexWithoutFlags];
            AtomicSafetyHandle.SetStaticSafetyId(ref handle, staticSafetyId);
        }

        ushort GetTypeArrayIndex(int typeIndex)
        {
            var typeIndexWithoutFlags = typeIndex & TypeManager.ClearFlagsMask;
            var arrayIndex = m_TypeArrayIndices[typeIndexWithoutFlags];
            if (arrayIndex != NullTypeIndex)
                return arrayIndex;

            arrayIndex = m_ComponentSafetyHandlesCount++;
            m_TypeArrayIndices[typeIndexWithoutFlags] = arrayIndex;
            m_ComponentSafetyHandles[arrayIndex].TypeIndex = typeIndex;

            m_ComponentSafetyHandles[arrayIndex].SafetyHandle = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetAllowSecondaryVersionWriting(m_ComponentSafetyHandles[arrayIndex].SafetyHandle, false);
            m_ComponentSafetyHandles[arrayIndex].BufferHandle = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_ComponentSafetyHandles[arrayIndex].BufferHandle, true);

            // Create static safety IDs for this type if they don't already exist.
            CreateStaticSafetyIdsForType(typeIndex);
            // Set default static safety IDs for handles
            SetStaticSafetyIdForHandle_ArchetypeChunk(ref m_ComponentSafetyHandles[arrayIndex].SafetyHandle, typeIndex, false);
            SetStaticSafetyIdForHandle_ArchetypeChunk(ref m_ComponentSafetyHandles[arrayIndex].BufferHandle, typeIndex, false);
            return arrayIndex;
        }

        void ClearAllTypeArrayIndices()
        {
            for (int i = 0; i < m_ComponentSafetyHandlesCount; ++i)
                m_TypeArrayIndices[m_ComponentSafetyHandles[i].TypeIndex & TypeManager.ClearFlagsMask] = NullTypeIndex;
            m_ComponentSafetyHandlesCount = 0;
        }

        public void OnCreate()
        {
            m_TypeArrayIndices = (ushort*)Memory.Unmanaged.Allocate(sizeof(ushort) * kMaxTypes, 16, Allocator.Persistent);
            UnsafeUtility.MemSet(m_TypeArrayIndices, 0xFF, sizeof(ushort) * kMaxTypes);

            m_ComponentSafetyHandles = (ComponentSafetyHandle*)Memory.Unmanaged.Allocate(sizeof(ComponentSafetyHandle) * kMaxTypes, 16, Allocator.Persistent);
            UnsafeUtility.MemClear(m_ComponentSafetyHandles, sizeof(ComponentSafetyHandle) * kMaxTypes);

            m_TempSafety = AtomicSafetyHandle.Create();
            m_ComponentSafetyHandlesCount = 0;

            m_InvalidateArraysMarker = new ProfilerMarker("InvalidateArrays");
            if (m_StaticSafetyIdsForComponentDataFromEntity == null)
            {
                m_StaticSafetyIdsForComponentDataFromEntity =
                    (int*)Memory.Unmanaged.Allocate(sizeof(int) * kMaxTypes, 16, Allocator.Persistent);
                UnsafeUtility.MemClear(m_StaticSafetyIdsForComponentDataFromEntity, sizeof(int) * kMaxTypes);
            }
            if (m_StaticSafetyIdsForArchetypeChunkArrays == null)
            {
                m_StaticSafetyIdsForArchetypeChunkArrays =
                    (int*)Memory.Unmanaged.Allocate(sizeof(int) * kMaxTypes, 16, Allocator.Persistent);
                UnsafeUtility.MemClear(m_StaticSafetyIdsForArchetypeChunkArrays, sizeof(int) * kMaxTypes);
            }

            m_StaticSafetyIdForDynamicComponentTypeHandle = AtomicSafetyHandle.NewStaticSafetyId<DynamicComponentTypeHandle>();
            SetCustomErrorMessage(m_StaticSafetyIdForDynamicComponentTypeHandle, AtomicSafetyErrorType.Deallocated,
                m_CustomDeallocatedErrorMessageBytes);
            SetCustomErrorMessage(m_StaticSafetyIdForDynamicComponentTypeHandle, AtomicSafetyErrorType.DeallocatedFromJob,
                m_CustomDeallocatedFromJobErrorMessageBytes);

            m_StaticSafetyIdForEntityTypeHandle = AtomicSafetyHandle.NewStaticSafetyId<EntityTypeHandle>();
            SetCustomErrorMessage(m_StaticSafetyIdForEntityTypeHandle, AtomicSafetyErrorType.Deallocated,
                m_CustomDeallocatedErrorMessageBytes);
            SetCustomErrorMessage(m_StaticSafetyIdForEntityTypeHandle, AtomicSafetyErrorType.DeallocatedFromJob,
                m_CustomDeallocatedFromJobErrorMessageBytes);
        }

        public AtomicSafetyHandle ExclusiveTransactionSafety;
        private ProfilerMarker m_InvalidateArraysMarker;

        public void CompleteAllJobsAndInvalidateArrays()
        {
            if (m_ComponentSafetyHandlesCount == 0)
                return;

            m_InvalidateArraysMarker.Begin();
            for (var i = 0; i != m_ComponentSafetyHandlesCount; i++)
            {
                AtomicSafetyHandle.CheckDeallocateAndThrow(m_ComponentSafetyHandles[i].SafetyHandle);
                AtomicSafetyHandle.CheckDeallocateAndThrow(m_ComponentSafetyHandles[i].BufferHandle);
            }

            for (var i = 0; i != m_ComponentSafetyHandlesCount; i++)
            {
                AtomicSafetyHandle.Release(m_ComponentSafetyHandles[i].SafetyHandle);
                AtomicSafetyHandle.Release(m_ComponentSafetyHandles[i].BufferHandle);
            }

            ClearAllTypeArrayIndices();
            m_InvalidateArraysMarker.End();
        }

        public void Dispose()
        {
            for (var i = 0; i < m_ComponentSafetyHandlesCount; i++)
            {
                var res0 = AtomicSafetyHandle.EnforceAllBufferJobsHaveCompletedAndRelease(m_ComponentSafetyHandles[i].SafetyHandle);
                var res1 = AtomicSafetyHandle.EnforceAllBufferJobsHaveCompletedAndRelease(m_ComponentSafetyHandles[i].BufferHandle);

                if (res0 == EnforceJobResult.DidSyncRunningJobs || res1 == EnforceJobResult.DidSyncRunningJobs)
                    Debug.LogError(
                        "Disposing EntityManager but a job is still running against the ComponentData. It appears the job has not been registered with JobComponentSystem.AddDependency.");
            }

            AtomicSafetyHandle.Release(m_TempSafety);

            Memory.Unmanaged.Free(m_TypeArrayIndices, Allocator.Persistent);
            Memory.Unmanaged.Free(m_ComponentSafetyHandles, Allocator.Persistent);
            m_ComponentSafetyHandles = null;
        }

        public void PreDisposeCheck()
        {
            for (var i = 0; i < m_ComponentSafetyHandlesCount; i++)
            {
                var res0 = AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_ComponentSafetyHandles[i].SafetyHandle);
                var res1 = AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_ComponentSafetyHandles[i].BufferHandle);
                if (res0 == EnforceJobResult.DidSyncRunningJobs || res1 == EnforceJobResult.DidSyncRunningJobs)
                    Debug.LogError(
                        "Disposing EntityManager but a job is still running against the ComponentData. It appears the job has not been registered with JobComponentSystem.AddDependency.");
            }
        }

        public void CompleteWriteDependency(int type)
        {
            var typeIndexWithoutFlags = type & TypeManager.ClearFlagsMask;
            var arrayIndex = m_TypeArrayIndices[typeIndexWithoutFlags];
            if (arrayIndex == NullTypeIndex)
                return;

            AtomicSafetyHandle.CheckReadAndThrow(m_ComponentSafetyHandles[arrayIndex].SafetyHandle);
            AtomicSafetyHandle.CheckReadAndThrow(m_ComponentSafetyHandles[arrayIndex].BufferHandle);
        }

        public void CompleteReadAndWriteDependency(int type)
        {
            var typeIndexWithoutFlags = type & TypeManager.ClearFlagsMask;
            var arrayIndex = m_TypeArrayIndices[typeIndexWithoutFlags];
            if (arrayIndex == NullTypeIndex)
                return;

            AtomicSafetyHandle.CheckWriteAndThrow(m_ComponentSafetyHandles[arrayIndex].SafetyHandle);
            AtomicSafetyHandle.CheckWriteAndThrow(m_ComponentSafetyHandles[arrayIndex].BufferHandle);
        }

        public AtomicSafetyHandle GetEntityManagerSafetyHandle()
        {
            var handle = m_ComponentSafetyHandles[GetTypeArrayIndex(EntityTypeIndex)].SafetyHandle;
            AtomicSafetyHandle.UseSecondaryVersion(ref handle);
            return handle;
        }

        public AtomicSafetyHandle GetSafetyHandleForComponentDataFromEntity(int type, bool isReadOnly)
        {
            var handle = GetSafetyHandle(type, isReadOnly);
            // Override the handle's default static safety ID
            SetStaticSafetyIdForHandle_FromEntity(ref handle, type);
            return handle;
        }

        public AtomicSafetyHandle GetBufferHandleForBufferFromEntity(int type)
        {
            Assert.IsTrue(TypeManager.IsBuffer(type));
            var handle = GetBufferSafetyHandle(type);
            // Override the handle's default static safety ID
            SetStaticSafetyIdForHandle_FromEntity(ref handle, type);
            return handle;
        }

        public AtomicSafetyHandle GetSafetyHandleForComponentTypeHandle(int type, bool isReadOnly)
        {
            // safety handles are configured with the static safety ID for ArchetypeChunk*Type by default,
            // so no further static safety ID setup is necessary in this path.
            return GetSafetyHandle(type, isReadOnly);
        }

        public AtomicSafetyHandle GetSafetyHandleForDynamicComponentTypeHandle(int type, bool isReadOnly)
        {
            var handle = GetSafetyHandle(type, isReadOnly);
            // We need to override the handle's default static safety ID to use the DynamicComponentTypeHandle version.
            SetStaticSafetyIdForHandle_ArchetypeChunk(ref handle, type, true);
            return handle;
        }

        public AtomicSafetyHandle GetSafetyHandleForBufferTypeHandle(int type, bool isReadOnly)
        {
            // safety handles are configured with the static safety ID for ArchetypeChunk*Type by default,
            // so no further static safety ID setup is necessary in this path.
            return GetSafetyHandle(type, isReadOnly);
        }

        public AtomicSafetyHandle GetBufferHandleForBufferTypeHandle(int type)
        {
            Assert.IsTrue(TypeManager.IsBuffer(type));
            // safety handles are configured with the static safety ID for ArchetypeChunk*Type by default,
            // so no further static safety ID setup is necessary in this path.
            return GetBufferSafetyHandle(type);
        }

        public AtomicSafetyHandle GetSafetyHandleForSharedComponentTypeHandle(int type)
        {
            Assert.IsTrue(TypeManager.IsSharedComponentType(type));
            var handle = GetSafetyHandle(type, false);
            // safety handles are configured with the static safety ID for ArchetypeChunk*Type by default,
            // so no further static safety ID setup is necessary in this path.
            return handle;
        }

        public AtomicSafetyHandle GetSafetyHandleForEntityTypeHandle()
        {
            var handle = GetEntityManagerSafetyHandle();
            // The EntityTypeIndex safety handle is pre-configured with a static safety ID for EntityTypeHandle,
            // so no further configuration is necessary here.
            return handle;
        }

        public AtomicSafetyHandle GetSafetyHandle(int type, bool isReadOnly)
        {
            var arrayIndex = GetTypeArrayIndex(type);
            var handle = m_ComponentSafetyHandles[arrayIndex].SafetyHandle;
            if (isReadOnly)
                AtomicSafetyHandle.UseSecondaryVersion(ref handle);
            return handle;
        }

        public AtomicSafetyHandle GetBufferSafetyHandle(int type)
        {
            Assert.IsTrue(TypeManager.IsBuffer(type));
            var arrayIndex = GetTypeArrayIndex(type);
            return m_ComponentSafetyHandles[arrayIndex].BufferHandle;
        }

        public void BeginExclusiveTransaction()
        {
            for (var i = 0; i != m_ComponentSafetyHandlesCount; i++)
            {
                AtomicSafetyHandle.CheckDeallocateAndThrow(m_ComponentSafetyHandles[i].SafetyHandle);
                AtomicSafetyHandle.CheckDeallocateAndThrow(m_ComponentSafetyHandles[i].BufferHandle);
            }

            for (var i = 0; i != m_ComponentSafetyHandlesCount; i++)
            {
                AtomicSafetyHandle.Release(m_ComponentSafetyHandles[i].SafetyHandle);
                AtomicSafetyHandle.Release(m_ComponentSafetyHandles[i].BufferHandle);
            }

            ExclusiveTransactionSafety = AtomicSafetyHandle.Create();
            ClearAllTypeArrayIndices();
        }

        public void EndExclusiveTransaction()
        {
            var res = AtomicSafetyHandle.EnforceAllBufferJobsHaveCompletedAndRelease(ExclusiveTransactionSafety);
            if (res != EnforceJobResult.AllJobsAlreadySynced)
                //@TODO: Better message
                Debug.LogError("ExclusiveEntityTransaction job has not been registered");
        }

        struct ComponentSafetyHandle
        {
            public AtomicSafetyHandle SafetyHandle;
            public AtomicSafetyHandle BufferHandle;
            public int                TypeIndex;
        }

        AtomicSafetyHandle m_TempSafety;
    }
}
#endif
