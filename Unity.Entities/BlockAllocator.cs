using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    internal unsafe struct BlockAllocator : IDisposable
    {
        private byte* m_FirstBlock;
        private byte* m_LastBlock;
        private int m_LastBlockUsedSize;

        private const int ms_BlockSize = 64 * 1024;
        private const int ms_BlockAlignment = 64; //cache line size

        private int totalSizeInBytes;

        public void Dispose()
        {
            while (m_FirstBlock != null)
            {
                var nextBlock = ((byte**)m_FirstBlock)[0];
                UnsafeUtility.Free(m_FirstBlock, Allocator.Persistent);
                m_FirstBlock = nextBlock;
            }

            m_LastBlock = null;
        }

        public byte* Allocate(int bytesToAllocate, int alignment)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (bytesToAllocate > ms_BlockSize)
                throw new ArgumentException($"Cannot allocate more than {ms_BlockSize} in BlockAllocator. Requested: {bytesToAllocate}");
#endif
            var alignedBlockSize = (m_LastBlockUsedSize + alignment - 1) & ~(alignment - 1);
            if (m_LastBlock == null || bytesToAllocate > ms_BlockSize - alignedBlockSize)
            {
                // Allocate a fresh block of memory
                var block = (byte*)UnsafeUtility.Malloc(ms_BlockSize, ms_BlockAlignment, Allocator.Persistent);
                ((byte**)block)[0] = null;

                // Ran out of space in current block, so grow the allocator.
                if (m_LastBlock != null)
                    ((byte**)m_LastBlock)[0] = block;
                // First allocation for this allocator.
                else
                    m_FirstBlock = block;

                m_LastBlock = block;
                m_LastBlockUsedSize = sizeof(byte*);
                alignedBlockSize = (m_LastBlockUsedSize + alignment - 1) & ~(alignment - 1);
            }

            var ptr = m_LastBlock + alignedBlockSize;
            m_LastBlockUsedSize = alignedBlockSize + bytesToAllocate;
            totalSizeInBytes += m_LastBlockUsedSize;
            return ptr;
        }

        public byte* Construct(int size, int alignment, void* src)
        {
            var res = Allocate(size, alignment);
            UnsafeUtility.MemCpy(res, src, size);
            return res;
        }
    }
}
