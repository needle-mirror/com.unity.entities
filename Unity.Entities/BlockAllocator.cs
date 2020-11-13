#if !UNITY_WEBGL
#define USE_VIRTUAL_MEMORY
#endif
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    [BurstCompatible]
    internal unsafe struct BlockAllocator : IDisposable
    {
        BufferAllocator m_bufferAllocator;
        private UnsafeIntList m_allocations;
        int m_currentBlockIndex;
        ulong m_nextPtr;

        private const int ms_Log2BlockSize = 16;
        private const int ms_BlockSize = 1 << ms_Log2BlockSize;

        private int ms_BudgetInBytes => m_bufferAllocator.BufferCapacity << ms_Log2BlockSize;

        public BlockAllocator(AllocatorManager.AllocatorHandle handle, int budgetInBytes)
        {
            m_bufferAllocator = new BufferAllocator(budgetInBytes, ms_BlockSize, handle);
            m_nextPtr = 0;
            var blocks = (budgetInBytes + ms_BlockSize - 1) >> ms_Log2BlockSize;
            m_allocations = new UnsafeIntList(blocks, handle);

            for (int i = 0; i < blocks; ++i)
            {
                m_allocations.Add(0);
            }

            m_currentBlockIndex = -1;
        }

        public void Dispose()
        {
            m_bufferAllocator.Dispose();
            m_allocations.Dispose();
        }

        public void Free(void* pointer)
        {
            if (pointer == null)
                return;
            var blocks = m_allocations.Length; // how many blocks have we allocated?
            for (var i = blocks - 1; i >= 0; --i)
            {
                var block = (byte*)m_bufferAllocator[i]; // get a pointer to the block.
                if (pointer >= block && pointer < block + ms_BlockSize) // is the pointer we want to free in this block?
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if (m_allocations.Ptr[i] <= 0) // if that block has no allocations, we can't proceed
                        throw new ArgumentException($"Cannot free this pointer from BlockAllocator: no more allocations to free in its block.");
#endif
                    if (--m_allocations.Ptr[i] == 0) // if this was the last allocation in the block,
                    {
                        if (i == blocks - 1) // if it's the last block,
                            m_nextPtr = (ulong)m_bufferAllocator[i]; // just forget that we allocated anything from it, but keep it for later allocations
                        else
                        {
                            m_bufferAllocator.Free(i);

                            // If the current block is freed then we should reset it to ensure we
                            // allocate a new block on the next allocation.
                            if (i == m_currentBlockIndex)
                            {
                                m_currentBlockIndex = -1;
                            }
                        }
                    }
                    return;
                }
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            throw new ArgumentException($"Cannot free this pointer from BlockAllocator: can't be found in any block.");
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckAllocationTooLarge(int bytesToAllocate, int alignment)
        {
            if (bytesToAllocate > ms_BlockSize)
                throw new ArgumentException($"Cannot allocate more than {ms_BlockSize} in BlockAllocator. Requested: {bytesToAllocate}");

            // This check is to be sure that the given allocation size and alignment can even be guaranteed by the
            // allocator. Due to the fixed block sizes, there are some values of bytesToAllocate < ms_BlockSize which
            // may fail due to the alignment requirement.
            var worstCaseBytesWithAlignment = bytesToAllocate + alignment - 1;
            if (worstCaseBytesWithAlignment > ms_BlockSize)
            {
                var maxAllocationSizeForGivenAlignment = ms_BlockSize - (alignment - 1);

                throw new ArgumentException($"Cannot guarantee allocation of {bytesToAllocate} bytes. Allocation size must be <= {maxAllocationSizeForGivenAlignment} bytes to guarantee allocation.");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckExceededBudget()
        {
            if (m_bufferAllocator.IsEmpty)
                throw new ArgumentException($"Cannot exceed budget of {ms_BudgetInBytes} in BlockAllocator.");
        }

#if USE_VIRTUAL_MEMORY
        internal struct BufferAllocator : IDisposable
        {
            readonly VMRange ReservedRange;
            readonly int BufferSizeInBytes;
            readonly int MaxBufferCount;

            // Should be a bit array with fast search for zero bits.
            UnsafeIntList FreeList;

            /// <summary>
            /// Constructs an allocator.
            /// </summary>
            /// <param name="budgetInBytes">Budget of the allocator in bytes.</param>
            /// <param name="bufferSizeInBytes">Size of each buffer to be allocated in bytes.</param>
            /// <param name="handle">An AllocatorHandle to use for internal bookkeeping structures.</param>
            /// <exception cref="InvalidOperationException">Thrown if the allocator cannot reserve the address range required for the given budget.</exception>
            public BufferAllocator(int budgetInBytes, int bufferSizeInBytes, AllocatorManager.AllocatorHandle handle)
            {
                BufferSizeInBytes = bufferSizeInBytes;

                // Reserve the entire budget's worth of address space. The reserved space may be larger than the budget
                // due to page sizes.
                var pageCount = VirtualMemoryUtility.BytesToPageCount((uint)budgetInBytes, VirtualMemoryUtility.DefaultPageSizeInBytes);
                BaselibErrorState errorState;
                ReservedRange = VirtualMemoryUtility.ReserveAddressSpace(pageCount, VirtualMemoryUtility.DefaultPageSizeInBytes, out errorState);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!errorState.Success)
                {
                    throw new InvalidOperationException($"Failed to reserve address range for {budgetInBytes} bytes");
                }
#endif

                // Init a free list of blocks.
                MaxBufferCount = (int)VirtualMemoryUtility.BytesToPageCount((uint)budgetInBytes, (uint)bufferSizeInBytes);
                FreeList = new UnsafeIntList(MaxBufferCount, handle);

                for (int i = MaxBufferCount - 1; i >= 0; --i)
                {
                    FreeList.Add(i);
                }
            }

            /// <summary>
            /// Allocates an index which corresponds to a buffer.
            /// </summary>
            /// <returns>Allocated index. If allocation fails, returned index is negative.</returns>
            /// <exception cref="InvalidOperationException">Thrown when allocator is exhausted or when buffer cannot be committed.</exception>
            public int Allocate()
            {
                if (FreeList.IsEmpty)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    throw new InvalidOperationException("Cannot allocate, allocator is exhausted.");
#else
                    return -1;
#endif
                }

                int lastFreeListIndex = FreeList.Length - 1;
                int index = FreeList.Ptr[lastFreeListIndex];
                BaselibErrorState errorState;
                var range = new VMRange((IntPtr)this[index], (uint)BufferSizeInBytes, VirtualMemoryUtility.DefaultPageSizeInBytes);
                VirtualMemoryUtility.CommitMemory(range, out errorState);

                if (!errorState.Success)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    throw new InvalidOperationException($"Failed to commit address range {range}.");
#else
                    return -1;
#endif
                }

                FreeList.RemoveAtSwapBack(lastFreeListIndex);

                return index;
            }

            /// <summary>
            /// Frees the buffer represented by the given index.
            /// </summary>
            /// <param name="index">Index to buffer.</param>
            /// <exception cref="ArgumentException">Thrown when index is less than zero or when greater than or equal to BufferCapacity</exception>
            /// <exception cref="InvalidOperationException">Thrown if the buffer cannot be decommitted.</exception>
            public void Free(int index)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (index < 0 || index >= BufferCapacity)
                {
                    throw new ArgumentException($"Cannot free index {index}, it is outside the expected range [0, {BufferCapacity}).");
                }
#endif
                BaselibErrorState errorState;
                var range = new VMRange((IntPtr)this[index], (uint)BufferSizeInBytes, VirtualMemoryUtility.DefaultPageSizeInBytes);
                VirtualMemoryUtility.DecommitMemory(range, out errorState);

                if (!errorState.Success)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    throw new InvalidOperationException($"Failed to decommit address range {range}.");
#endif
                }

                FreeList.Add(index);
            }

            /// <summary>
            /// Converts an index to a pointer.
            /// </summary>
            /// <param name="index">Index to a buffer.</param>
            public void* this[int index] => (void*) (ReservedRange.ptr + (BufferSizeInBytes * index));

            /// <summary>
            /// Maximum number of buffers that can be allocated at once.
            /// </summary>
            public int BufferCapacity => MaxBufferCount;

            /// <summary>
            /// Checks if all the buffers in the allocator have been allocated.
            /// </summary>
            public bool IsEmpty => FreeList.IsEmpty;

            /// <summary>
            /// Disposes the allocator and frees the reserved address range.
            /// </summary>
            /// <exception cref="InvalidOperationException">Thrown if the reserved address range cannot be freed.</exception>
            public void Dispose()
            {
                FreeList.Dispose();

                BaselibErrorState errorState;
                VirtualMemoryUtility.FreeAddressSpace(ReservedRange, out errorState);

                if (!errorState.Success)
                {
                    throw new InvalidOperationException($"Failed to free the reserved address range {ReservedRange}.");
                }
            }
        }
#else
        internal struct BufferAllocator : IDisposable
        {
            UnsafePtrList Buffers;
            UnsafeIntList FreeList;
            readonly AllocatorManager.AllocatorHandle Handle;
            readonly int BufferSizeInBytes;

            const int kBufferAlignment = 64; //cache line size

            /// <summary>
            /// Constructs an allocator.
            /// </summary>
            /// <param name="budgetInBytes">Budget of the allocator in bytes.</param>
            /// <param name="bufferSizeInBytes">Size of each buffer to be allocated in bytes.</param>
            /// <param name="handle">An AllocatorHandle to use for buffer allocations and internal bookkeeping structures.</param>
            public BufferAllocator(int budgetInBytes, int bufferSizeInBytes, AllocatorManager.AllocatorHandle handle)
            {
                Handle = handle;
                BufferSizeInBytes = bufferSizeInBytes;
                var bufferCount = (budgetInBytes + bufferSizeInBytes - 1) / bufferSizeInBytes;
                Buffers = new UnsafePtrList(bufferCount, handle);

                for (int i = 0; i < bufferCount; ++i)
                {
                    Buffers.Add(IntPtr.Zero);
                }

                FreeList = new UnsafeIntList(bufferCount, handle);

                for (int i = bufferCount - 1; i >= 0; --i)
                {
                    FreeList.Add(i);
                }
            }

            /// <summary>
            /// Allocates an index which corresponds to a buffer.
            /// </summary>
            /// <returns>Allocated index. If allocation fails, returned index is negative.</returns>
            /// <exception cref="InvalidOperationException">Thrown when allocator is exhausted.</exception>
            public int Allocate()
            {
                if (FreeList.IsEmpty)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    throw new InvalidOperationException("Cannot allocate, allocator is exhausted.");
#else
                    return -1;
#endif
                }

                int lastFreeListIndex = FreeList.Length - 1;
                int index = FreeList.Ptr[lastFreeListIndex];
                var bufferPtr = AllocatorManager.Allocate(Handle, sizeof(byte), kBufferAlignment, BufferSizeInBytes);

                if (bufferPtr == null)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    throw new InvalidOperationException("Failed to allocate buffer.");
#else
                    return -1;
#endif
                }

                FreeList.RemoveAtSwapBack(lastFreeListIndex);
                Buffers[index] = (IntPtr)bufferPtr;

                return index;
            }

            /// <summary>
            /// Frees the buffer represented by the given index.
            /// </summary>
            /// <param name="index">Index to buffer.</param>
            /// <exception cref="ArgumentException">Thrown when index is less than zero or when greater than or equal to BufferCapacity</exception>
            public void Free(int index)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (index < 0 || index >= BufferCapacity)
                {
                    throw new ArgumentException($"Cannot free index {index}, it is outside the expected range [0, {BufferCapacity}).");
                }
#endif

                AllocatorManager.Free(Handle, (void*)Buffers[index]);
                FreeList.Add(index);
                Buffers[index] = IntPtr.Zero;
            }

            /// <summary>
            /// Converts an index to a pointer.
            /// </summary>
            /// <param name="index">Index to a buffer.</param>
            public void* this[int index] => (void*)Buffers[index];

            /// <summary>
            /// Maximum number of buffers that can be allocated at once.
            /// </summary>
            public int BufferCapacity => Buffers.Length;

            /// <summary>
            /// Checks if all the buffers in the allocator have been allocated.
            /// </summary>
            public bool IsEmpty => FreeList.IsEmpty;

            /// <summary>
            /// Disposes the allocator.
            /// </summary>
            /// <exception cref="InvalidOperationException">Thrown if the reserved address range cannot be freed.</exception>
            public void Dispose()
            {
                for (int i = 0; i < Buffers.Length; ++i)
                {
                    AllocatorManager.Free(Handle, (void*) Buffers[i]);
                }

                Buffers.Dispose();
                FreeList.Dispose();
            }
        }
#endif

        /// <summary>
        /// Allocates memory out of a block.
        /// </summary>
        /// <remarks>
        /// Not all allocation sizes and alignment combinations are valid. The maximum value bytesToAllocate can be is
        /// (ms_BlockSize - (alignment - 1)).
        /// </remarks>
        /// <param name="bytesToAllocate">Bytes to allocate.</param>
        /// <param name="alignment">Alignment in bytes for the allocation.</param>
        /// <returns>Pointer to allocation.</returns>
        public byte* Allocate(int bytesToAllocate, int alignment)
        {
            CheckAllocationTooLarge(bytesToAllocate, alignment);
            var nextAligned = CollectionHelper.Align(m_nextPtr, (ulong)alignment);
            var nextAllocationEnd = nextAligned + (ulong)bytesToAllocate;

            // If we haven't allocated a block or the next allocation end is past the end of the current block, then allocate a new block.
            if (m_currentBlockIndex < 0 || nextAllocationEnd > (ulong)m_bufferAllocator[m_currentBlockIndex] + ms_BlockSize)
            {
                CheckExceededBudget();
                // Allocate a fresh block of memory
                int index = m_bufferAllocator.Allocate();
                m_allocations.Ptr[index] = 0;
                m_currentBlockIndex = index;
                nextAligned = CollectionHelper.Align((ulong) m_bufferAllocator[m_currentBlockIndex], (ulong)alignment);
                nextAllocationEnd = nextAligned + (ulong) bytesToAllocate;
            }

            var pointer = (byte*) nextAligned;
            m_nextPtr = nextAllocationEnd;
            m_allocations.Ptr[m_currentBlockIndex]++;
            return pointer;
        }

        [BurstCompatible(GenericTypeArguments = new [] {typeof(BurstCompatibleComponentData)})]
        public T* Allocate<T>(int items = 1) where T : unmanaged
        {
            return (T*)Allocate(items * UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>());
        }

        [BurstCompatible(GenericTypeArguments = new [] {typeof(BurstCompatibleComponentData)})]
        public byte* Construct(int size, int alignment, void* src)
        {
            var res = Allocate(size, alignment);
            UnsafeUtility.MemCpy(res, src, size);
            return res;
        }

        [BurstCompatible(GenericTypeArguments = new [] {typeof(BurstCompatibleComponentData)})]
        public T* Construct<T>(T* src) where T : unmanaged
        {
            return (T*)Construct(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), src);
        }
    }
}
