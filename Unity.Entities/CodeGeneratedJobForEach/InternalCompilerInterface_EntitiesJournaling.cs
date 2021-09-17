#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
using System;

namespace Unity.Entities
{
    partial class InternalCompilerInterface
    {
        public static unsafe void EntitiesJournaling_RecordSetComponentData<T>(ulong worldSequenceNumber, in SystemHandleUntyped executingSystem, ArchetypeChunk chunk, ComponentTypeHandle<T> typeHandle, IntPtr nativeArrayPtr)
            where T : struct, IComponentData
        {
            EntitiesJournaling.RecordSetComponentData(worldSequenceNumber, in executingSystem, default, &chunk, 1, &typeHandle.m_TypeIndex, 1, (void*)nativeArrayPtr, typeHandle.m_SizeInChunk * chunk.ChunkEntityCount);
        }

        public static unsafe void EntitiesJournaling_RecordSetComponentData<T>(ulong worldSequenceNumber, in SystemHandleUntyped executingSystem, Entity entity, ComponentTypeHandle<T> typeHandle, IntPtr nativeArrayPtr)
            where T : struct, IComponentData
        {
            EntitiesJournaling.RecordSetComponentData(worldSequenceNumber, in executingSystem, default, &entity, 1, &typeHandle.m_TypeIndex, 1, (void*)nativeArrayPtr, typeHandle.m_SizeInChunk);
        }

        public static unsafe void EntitiesJournaling_RecordSetComponentObject<T>(ulong worldSequenceNumber, in SystemHandleUntyped executingSystem, ArchetypeChunk chunk, ComponentTypeHandle<T> typeHandle)
        {
            EntitiesJournaling.RecordSetComponentObject(worldSequenceNumber, in executingSystem, default, &chunk, 1, &typeHandle.m_TypeIndex, 1);
        }

        public static unsafe void EntitiesJournaling_RecordSetComponentObject<T>(ulong worldSequenceNumber, in SystemHandleUntyped executingSystem, Entity entity, int typeIndex)
        {
            EntitiesJournaling.RecordSetComponentObject(worldSequenceNumber, in executingSystem, default, &entity, 1, &typeIndex, 1);
        }

        public static unsafe void EntitiesJournaling_RecordSetSharedComponentData<T>(ulong worldSequenceNumber, in SystemHandleUntyped executingSystem, ArchetypeChunk chunk, SharedComponentTypeHandle<T> typeHandle)
            where T : struct, ISharedComponentData
        {
            EntitiesJournaling.RecordSetSharedComponentData(worldSequenceNumber, in executingSystem, default, &chunk, 1, &typeHandle.m_TypeIndex, 1);
        }

        public static unsafe void EntitiesJournaling_RecordSetSharedComponentData<T>(ulong worldSequenceNumber, in SystemHandleUntyped executingSystem, Entity entity, int typeIndex)
            where T : struct, ISharedComponentData
        {
            EntitiesJournaling.RecordSetSharedComponentData(worldSequenceNumber, in executingSystem, default, &entity, 1, &typeIndex, 1);
        }

        public static unsafe void EntitiesJournaling_RecordSetBuffer<T>(ulong worldSequenceNumber, in SystemHandleUntyped executingSystem, ArchetypeChunk chunk, BufferTypeHandle<T> typeHandle)
            where T : struct, IBufferElementData
        {
            EntitiesJournaling.RecordSetBuffer(worldSequenceNumber, in executingSystem, default, &chunk, 1, &typeHandle.m_TypeIndex, 1);
        }

        public static unsafe void EntitiesJournaling_RecordSetBuffer<T>(ulong worldSequenceNumber, in SystemHandleUntyped executingSystem, Entity entity, int typeIndex)
            where T : struct, IBufferElementData
        {
            EntitiesJournaling.RecordSetBuffer(worldSequenceNumber, in executingSystem, default, &entity, 1, &typeIndex, 1);
        }
    }
}
#endif
