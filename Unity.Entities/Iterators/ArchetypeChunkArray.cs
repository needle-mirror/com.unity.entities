using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    public unsafe struct ArchetypeChunk
    {
        [NativeDisableUnsafePtrRestriction] internal Chunk* m_Chunk;
        public int StartIndex;
        public int Count => m_Chunk->Count;

        public NativeArray<Entity> GetNativeArray(ArchetypeChunkEntityType archetypeChunkEntityType)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(archetypeChunkEntityType.m_Safety);
#endif
            var archetype = m_Chunk->Archetype;
            var buffer = m_Chunk->Buffer;
            var length = m_Chunk->Count;
            var startOffset = archetype->Offsets[0];
            var result =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(buffer + startOffset, length,
                    Allocator.Invalid);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, archetypeChunkEntityType.m_Safety);
#endif
            return result;
        }

        private int GetIndexInArchetype(int typeIndex)
        {
            var typeIndexInArchetype = 1;
            var archetype = m_Chunk->Archetype;

            while (archetype->Types[typeIndexInArchetype].TypeIndex != typeIndex)
            {
                ++typeIndexInArchetype;

                if (typeIndexInArchetype == archetype->TypesCount) return -1;
            }

            return typeIndexInArchetype;
        }

        public uint GetComponentVersion<T>(ArchetypeChunkComponentType<T> chunkComponentType)
            where T : struct, IComponentData
        {
            var typeIndex = chunkComponentType.m_TypeIndex;
            var typeIndexInArchetype = GetIndexInArchetype(typeIndex);
            if (typeIndexInArchetype == -1) return 0;
            return m_Chunk->ChangeVersion[typeIndexInArchetype];
        }

        public int GetSharedComponentIndex<T>(ArchetypeChunkSharedComponentType<T> chunkSharedComponentData)
            where T : struct, ISharedComponentData
        {
            var archetype = m_Chunk->Archetype;
            var typeIndex = chunkSharedComponentData.m_TypeIndex;
            var typeIndexInArchetype = GetIndexInArchetype(typeIndex);
            if (typeIndexInArchetype == -1) return -1;

            var chunkSharedComponentIndex = archetype->SharedComponentOffset[typeIndexInArchetype];
            var sharedComponentIndex = m_Chunk->SharedComponentValueArray[chunkSharedComponentIndex];
            return sharedComponentIndex;
        }

        public NativeArray<T> GetNativeArray<T>(ArchetypeChunkComponentType<T> chunkComponentType)
            where T : struct, IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(chunkComponentType.m_Safety);
#endif
            var archetype = m_Chunk->Archetype;
            var typeIndex = chunkComponentType.m_TypeIndex;
            var typeIndexInArchetype = GetIndexInArchetype(typeIndex);
            if (typeIndexInArchetype == -1)
            {
                var emptyResult =
                    NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(null, 0, Allocator.Invalid);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref emptyResult, chunkComponentType.m_Safety);
#endif
                return emptyResult;
            }

            var buffer = m_Chunk->Buffer;
            var length = m_Chunk->Count;
            var startOffset = archetype->Offsets[typeIndexInArchetype];
            var result =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(buffer + startOffset, length,
                    Allocator.Invalid);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, chunkComponentType.m_Safety);
#endif
            if (!chunkComponentType.IsReadOnly)
                m_Chunk->ChangeVersion[typeIndex] = chunkComponentType.GlobalSystemVersion;
            return result;
        }
    }

    public unsafe struct ArchetypeChunkArray
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        static internal NativeArray<ArchetypeChunk> Create(NativeList<EntityArchetype> archetypes, Allocator allocator, AtomicSafetyHandle safetyHandle)
#else
        static internal NativeArray<ArchetypeChunk> Create(NativeList<EntityArchetype> archetypes, Allocator allocator)
#endif
        {
            var archetypeCount = archetypes.Length;
            int length = 0;

            for (var i = 0; i < archetypeCount; i++)
            {
                length += archetypes[i].Archetype->ChunkCount;
            }

            if (length == 0)
            {
                return new NativeArray<ArchetypeChunk>(0, allocator);
            }

            var sourceData = (ArchetypeChunk*) UnsafeUtility.Malloc(sizeof(ArchetypeChunk) * length, 16, allocator);

            var lastChunk = (Chunk*) archetypes[0].Archetype->ChunkList.Begin;
            var lastArchetypeIndex = 0;
            var lastChunkOffset = 0;
            sourceData[0] = new ArchetypeChunk {m_Chunk = lastChunk, StartIndex = lastChunkOffset};
            var chunkCount = 1;
            for (var i = 1; i < length; i++)
            {
                lastChunkOffset += lastChunk->Count;
                lastChunk = (Chunk*) lastChunk->ChunkListNode.Next;
                if (lastChunk == (Chunk*) archetypes[lastArchetypeIndex].Archetype->ChunkList.End)
                {
                    lastArchetypeIndex++;

                    if (lastArchetypeIndex == archetypeCount)
                        break;

                    lastChunk = (Chunk*) archetypes[lastArchetypeIndex].Archetype->ChunkList.Begin;
                }

                sourceData[i] = new ArchetypeChunk {m_Chunk = lastChunk, StartIndex = lastChunkOffset};
                chunkCount++;
            }

            var arr = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<ArchetypeChunk>(sourceData, chunkCount, allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref arr,safetyHandle); 
#endif
            return arr;
        }

        static public int CalculateEntityCount(NativeArray<ArchetypeChunk> chunks)
        {
            int entityCount = 0;
            for (var i = 0; i < chunks.Length; i++)
            {
                entityCount += chunks[i].Count;
            }

            return entityCount;
        }
    }

    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct ArchetypeChunkComponentType<T>
        where T : struct, IComponentData
    {
        internal readonly int m_TypeIndex;
        internal readonly uint m_GlobalSystemVersion;
        internal readonly bool m_IsReadOnly;

        public uint GlobalSystemVersion => m_GlobalSystemVersion;
        public bool IsReadOnly => m_IsReadOnly;

#pragma warning disable 0414
        private readonly int m_Length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly int m_MinIndex;
        private readonly int m_MaxIndex;
        internal readonly AtomicSafetyHandle m_Safety;
#endif
#pragma warning restore 0414

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal ArchetypeChunkComponentType(AtomicSafetyHandle safety, bool isReadOnly, uint globalSystemVersion)
#else
        internal ArchetypeChunkComponentType(bool isReadOnly,uint globalSystemVersion)
#endif
        {
            m_Length = 1;
            m_TypeIndex = TypeManager.GetTypeIndex<T>();
            m_GlobalSystemVersion = globalSystemVersion;
            m_IsReadOnly = isReadOnly;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = 0;
            m_Safety = safety;
#endif
        }
    }

    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct ArchetypeChunkSharedComponentType<T>
        where T : struct, ISharedComponentData
    {
        internal readonly int m_TypeIndex;

#pragma warning disable 0414
        private readonly int m_Length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly int m_MinIndex;
        private readonly int m_MaxIndex;
        internal readonly AtomicSafetyHandle m_Safety;
#endif
#pragma warning restore 0414

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal ArchetypeChunkSharedComponentType(AtomicSafetyHandle safety)
#else
        internal unsafe ArchetypeChunkSharedComponentType(bool unused = false)
#endif
        {
            m_Length = 1;
            m_TypeIndex = TypeManager.GetTypeIndex<T>();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = 0;
            m_Safety = safety;
#endif
        }
    }

    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct ArchetypeChunkEntityType
    {
#pragma warning disable 0414
        private readonly int m_Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly int m_MinIndex;
        private readonly int m_MaxIndex;
        internal readonly AtomicSafetyHandle m_Safety;
#endif
#pragma warning restore 0414

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal ArchetypeChunkEntityType(AtomicSafetyHandle safety)
#else
        internal unsafe ArchetypeChunkEntityType(bool unused = false)
#endif
        {
            m_Length = 1;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = 0;
            m_Safety = safety;
#endif
        }
    }
}
