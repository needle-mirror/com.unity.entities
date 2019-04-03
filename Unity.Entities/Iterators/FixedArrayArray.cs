using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public unsafe struct FixedArrayArray<T> where T : struct
    {
        private ComponentChunkCache m_Cache;
        private ComponentChunkIterator m_Iterator;
        private int m_CachedFixedArrayLength;
        private readonly bool m_IsReadOnly;


        private readonly int m_Length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly int m_MinIndex;
        private readonly int m_MaxIndex;
        private readonly AtomicSafetyHandle m_Safety;
#endif
        public int Length => m_Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal FixedArrayArray(ComponentChunkIterator iterator, int length, bool isReadOnly,
            AtomicSafetyHandle safety)
#else
        internal FixedArrayArray(ComponentChunkIterator iterator, int length, bool isReadOnly)
#endif
        {
            m_Length = length;
            m_IsReadOnly = isReadOnly;
            m_Iterator = iterator;
            m_Cache = default(ComponentChunkCache);
            m_CachedFixedArrayLength = -1;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = length - 1;
            m_Safety = safety;
#endif
        }

        public NativeArray<T> this[int index]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
                if (index < m_MinIndex || index > m_MaxIndex)
                    FailOutOfRangeError(index);
                var safety = m_Safety;
#endif

                if (index < m_Cache.CachedBeginIndex || index >= m_Cache.CachedEndIndex)
                {
                    m_Iterator.UpdateCache(index, out m_Cache, !m_IsReadOnly);
                    m_CachedFixedArrayLength = m_Cache.CachedSizeOf / UnsafeUtility.SizeOf<T>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if (m_Cache.CachedSizeOf % UnsafeUtility.SizeOf<T>() != 0)
                        throw new InvalidOperationException("Fixed array size must be multiple of sizeof");
#endif
                }

                void* ptr = (byte*) m_Cache.CachedPtr + index * m_Cache.CachedSizeOf;
                var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(ptr, m_CachedFixedArrayLength,
                    Allocator.Invalid);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, safety);
#endif
                return array;
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private void FailOutOfRangeError(int index)
        {
            //@TODO: Make error message utility and share with NativeArray...
            if (index < Length && (m_MinIndex != 0 || m_MaxIndex != Length - 1))
                throw new IndexOutOfRangeException(
                    $"Index {index} is out of restricted IJobParallelFor range [{m_MinIndex}...{m_MaxIndex}] in ReadWriteBuffer.\nReadWriteBuffers are restricted to only read & write the element at the job index. You can use double buffering strategies to avoid race conditions due to reading & writing in parallel to the same elements from a job.");

            throw new IndexOutOfRangeException($"Index {index} is out of range of '{Length}' Length.");
        }
#endif
    }
}
