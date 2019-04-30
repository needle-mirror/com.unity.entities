namespace Unity.Entities
{
    internal unsafe struct EntityChunkData
    {
        public Chunk* Chunk;
        public int IndexInChunk;
    }
}
