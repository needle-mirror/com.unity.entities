using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{

    [BurstCompile]
    public struct WorldAllocator : AllocatorManager.IAllocator
    {
        AllocatorManager.AllocatorHandle m_handle;
        AllocatorManager.AllocatorHandle m_backingAllocatorHandle;
        int m_blockCount;
        UnsafeAtomicCounter32 m_blockCounter;

        public bool IsInitialized()
        {
            return m_blockCount != 0;
        }

        public unsafe void Initialize(AllocatorManager.AllocatorHandle backingAllocatorHandle)
        {
            m_blockCount = 1;
            fixed (int* count = &m_blockCount) m_blockCounter = new UnsafeAtomicCounter32(count);
            m_backingAllocatorHandle = backingAllocatorHandle;
        }

        /// <summary>
        /// Dispose the allocator. This must be called to free the memory blocks that were allocated from the system.
        /// </summary>
        public void Dispose()
        {
            if (m_blockCount != 0)
            {
                m_handle.Rewind();
                CheckCounter();
                m_blockCount = 0;
            }
        }

        /// <summary>
        /// The allocator function. It can allocate, deallocate, or reallocate.
        /// </summary>
        public AllocatorManager.TryFunction Function => Try;

        /// <summary>
        /// Invoke the allocator function.
        /// </summary>
        /// <param name="block">The block to allocate, deallocate, or reallocate. See <see cref="AllocatorManager.Try"/></param>
        /// <returns>0 if successful. Otherwise, returns the error code from the allocator function.</returns>
        public int Try(ref AllocatorManager.Block block)
        {
            unsafe
            {
                if (block.Range.Pointer == IntPtr.Zero)
                {
                    if (block.Bytes == 0)
                    {
                        return 0;
                    }

                    var ptr = (byte*)Memory.Unmanaged.Allocate(block.Bytes, block.Alignment, m_backingAllocatorHandle);
                    block.Range.Pointer = (IntPtr)ptr;
                    block.AllocatedItems = block.Range.Items;

                    //Debug.Log($"Alloc {m_handle.Index} / #{m_blockCount}: {block.Range.Pointer}, {block.AllocatedBytes}");
                    m_blockCounter.Add(1);

                    return 0;
                }

                if (block.Range.Items == 0)
                {
                    m_blockCounter.Sub(1);
                    //Debug.Log($"Free  {m_handle.Index} / #{m_blockCount}: {block.Range.Pointer}, {block.AllocatedBytes}");

                    Memory.Unmanaged.Free((void*)block.Range.Pointer, m_backingAllocatorHandle);

                    block.Range.Pointer = IntPtr.Zero;
                    block.AllocatedItems = 0;

                    return 0;
                }

                return -1;
            }
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(AllocatorManager.TryFunction))]
        internal static int Try(IntPtr state, ref AllocatorManager.Block block)
        {
            unsafe { return ((WorldAllocator*)state)->Try(ref block); }
        }

        /// <summary>
        /// This allocator.
        /// </summary>
        /// <value>This allocator.</value>
        public AllocatorManager.AllocatorHandle Handle { get { return m_handle; } set { m_handle = value; } }

        /// <summary>
        /// Cast the Allocator index into Allocator
        /// </summary>
        public Allocator ToAllocator { get { return m_handle.ToAllocator; } }

        /// <summary>
        /// Check whether an allocator is a custom allocator
        /// </summary>
        public bool IsCustomAllocator { get { return m_handle.IsCustomAllocator; } }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckCounter()
        {
            if (m_blockCount != 1)
            {
                throw new Exception($"WorldAllocator {m_handle.Index} has memory blocks that are not deallocated. Memory leak of {m_blockCount-1} blocks.");
            }
        }
    }
}
