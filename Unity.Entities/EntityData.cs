using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    internal unsafe struct EntityData
    {
        public int* Version;
        public Archetype** Archetype;
        public EntityChunkData* ChunkData;
#if UNITY_EDITOR
        public NumberedWords* Name;
#endif
        
        private int m_EntitiesCapacity;
        public int FreeIndex;
        private int* m_ComponentTypeOrderVersion;
        public uint GlobalSystemVersion;
        
        public int Capacity
        {
            get { return m_EntitiesCapacity; }
            set
            {
                if (value <= m_EntitiesCapacity)
                    return;
                
                var versionBytes = (value * sizeof(int) + 63) & ~63;
                var archetypeBytes = (value * sizeof(Archetype*) + 63) & ~63;
                var chunkDataBytes = (value * sizeof(EntityChunkData) + 63) & ~63;
                var bytesToAllocate = versionBytes + archetypeBytes + chunkDataBytes;
#if UNITY_EDITOR
                var nameBytes = (value * sizeof(NumberedWords) + 63) & ~63;
                bytesToAllocate += nameBytes;
#endif

                var bytes = (byte*) UnsafeUtility.Malloc(bytesToAllocate, 64, Allocator.Persistent);

                var version = (int*) (bytes);
                var archetype = (Archetype**) (bytes + versionBytes);
                var chunkData = (EntityChunkData*) (bytes + versionBytes + archetypeBytes);
#if UNITY_EDITOR
                var name = (NumberedWords*) (bytes + versionBytes + archetypeBytes + chunkDataBytes);
#endif

                var startNdx = 0;
                if (m_EntitiesCapacity > 0)
                {
                    UnsafeUtility.MemCpy(version, Version, m_EntitiesCapacity * sizeof(int));
                    UnsafeUtility.MemCpy(archetype, Archetype, m_EntitiesCapacity * sizeof(Archetype*));
                    UnsafeUtility.MemCpy(chunkData, ChunkData, m_EntitiesCapacity * sizeof(EntityChunkData));
#if UNITY_EDITOR
                    UnsafeUtility.MemCpy(name, Name, m_EntitiesCapacity * sizeof(NumberedWords));
#endif
                    UnsafeUtility.Free(Version, Allocator.Persistent);
                    startNdx = m_EntitiesCapacity - 1;
                }

                Version = version;
                Archetype = archetype;
                ChunkData = chunkData;
#if UNITY_EDITOR
                Name = name;
#endif

                m_EntitiesCapacity = value;
                InitializeAdditionalCapacity(startNdx);
            }
        }
        
        static internal EntityData Create(int newCapacity)
        {
            EntityData entities = new EntityData();
            entities.Capacity = newCapacity;
            
            entities.GlobalSystemVersion = ChangeVersionUtility.InitialGlobalSystemVersion;
            
            const int componentTypeOrderVersionSize = sizeof(int) * TypeManager.MaximumTypesCount;
            entities.m_ComponentTypeOrderVersion = (int*) UnsafeUtility.Malloc(componentTypeOrderVersionSize,
                UnsafeUtility.AlignOf<int>(), Allocator.Persistent);
            UnsafeUtility.MemClear(entities.m_ComponentTypeOrderVersion, componentTypeOrderVersionSize);

            return entities;
        }

        internal void Dispose()
        {
            if (m_EntitiesCapacity > 0)
            {
                UnsafeUtility.Free(Version, Allocator.Persistent);

                Version = null;
                Archetype = null;
                ChunkData = null;
#if UNITY_EDITOR
                Name = null;
#endif
            
                m_EntitiesCapacity = 0;
            }

            if (m_ComponentTypeOrderVersion != null)
            {
                UnsafeUtility.Free(m_ComponentTypeOrderVersion, Allocator.Persistent);
                m_ComponentTypeOrderVersion = null;
            }
        }

        private void InitializeAdditionalCapacity(int start)
        {
            for (var i = start; i != Capacity; i++)
            {
                ChunkData[i].IndexInChunk = i + 1;
                Version[i] = 1;
                ChunkData[i].Chunk = null;
#if UNITY_EDITOR
                Name[i] = new NumberedWords();
#endif
            }

            // Last entity indexInChunk identifies that we ran out of space...
            ChunkData[Capacity - 1].IndexInChunk = -1;
        }
        
        internal void IncrementComponentTypeOrderVersion(Archetype* archetype)
        {
            // Increment type component version
            for (var t = 0; t < archetype->TypesCount; ++t)
            {
                var typeIndex = archetype->Types[t].TypeIndex;
                m_ComponentTypeOrderVersion[typeIndex & TypeManager.ClearFlagsMask]++;
            }
        }

        public int GetComponentTypeOrderVersion(int typeIndex)
        {
            return m_ComponentTypeOrderVersion[typeIndex & TypeManager.ClearFlagsMask];
        }
        
        public void IncrementGlobalSystemVersion()
        {
            ChangeVersionUtility.IncrementGlobalSystemVersion(ref GlobalSystemVersion);
        }
    }
}
