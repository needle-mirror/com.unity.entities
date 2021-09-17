using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using static Unity.Collections.AllocatorManager;

namespace Unity.Entities.LowLevel.Unsafe
{
    /// <summary>
    /// An unmanaged, not resizable circular buffer.
    /// </summary>
    /// <typeparam name="T">The type of elements in the circular buffer.</typeparam>
    [DebuggerDisplay("Count = {Count}, Capacity = {Capacity}, IsCreated = {IsCreated}, IsEmpty = {IsEmpty}, IsFull = {IsFull}")]
    [DebuggerTypeProxy(typeof(UnsafeCircularBufferTDebugView<>))]
    [StructLayout(LayoutKind.Sequential)]
    [BurstCompatible(GenericTypeArguments = new[] { typeof(int) })]
    internal unsafe struct UnsafeCircularBuffer<T> :
        IDisposable,
        IEnumerable<T>
        where T : unmanaged
    {
        AllocatorHandle m_Allocator;
        [NativeDisableUnsafePtrRestriction] T* m_Ptr;
        int m_Front;
        int m_Back;
        int m_Capacity;
        int m_Count;

        /// <summary>
        /// The internal buffer allocation pointer of the circular buffer.
        /// </summary>
        public T* Ptr => m_Ptr;

        /// <summary>
        /// Whether this circular buffer has been allocated (and not yet deallocated).
        /// </summary>
        /// <value>True if this list has been allocated (and not yet deallocated).</value>
        public bool IsCreated => m_Ptr != null;

        /// <summary>
        /// The capacity of the circular buffer.
        /// </summary>
        public int Capacity => m_Capacity;

        /// <summary>
        /// The element count currently in the circular buffer.
        /// </summary>
        public int Count => m_Count;

        /// <summary>
        /// Whether or not this circular buffer is empty.
        /// </summary>
        public bool IsEmpty => m_Count == 0;

        /// <summary>
        /// Whether or not this circular buffer is full (Count reached Capacity).
        /// </summary>
        public bool IsFull => m_Count == m_Capacity;

        /// <summary>
        /// Retrieve the front index value in the circular buffer.
        /// </summary>
        public int FrontIndex => m_Front;

        /// <summary>
        /// Retrieve the back index value in the circular buffer.
        /// </summary>
        public int BackIndex => m_Back;

        /// <summary>
        /// Create a new circular buffer.
        /// </summary>
        /// <param name="capacity">The total capacity.</param>
        /// <param name="allocator">The allocator.</param>
        /// <param name="options">Memory initialization options.</param>
        public UnsafeCircularBuffer(int capacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            m_Allocator = allocator;

            var sizeOf = sizeof(T);
            var alignOf = UnsafeUtility.AlignOf<T>();
            m_Ptr = (T*)m_Allocator.Allocate(sizeOf, alignOf, capacity);
            m_Front = 0;
            m_Back = 0;
            m_Capacity = capacity;
            m_Count = 0;

            if (options == NativeArrayOptions.ClearMemory && m_Ptr != null)
                UnsafeUtility.MemClear(m_Ptr, m_Capacity * sizeOf);
        }

        /// <summary>
        /// Create a new circular buffer using native array.
        /// </summary>
        /// <param name="array">The native array.</param>
        /// <param name="allocator">The allocator.</param>
        public UnsafeCircularBuffer(in NativeArray<T> array, Allocator allocator)
        {
            m_Allocator = allocator;

            var sizeOf = sizeof(T);
            var alignOf = UnsafeUtility.AlignOf<T>();
            m_Ptr = (T*)m_Allocator.Allocate(sizeOf, alignOf, array.Length);
            m_Front = 0;
            m_Back = 0;
            m_Capacity = array.Length;
            m_Count = array.Length;

            UnsafeUtility.MemCpy(m_Ptr, array.GetUnsafeReadOnlyPtr(), array.Length * sizeOf);
        }

        /// <summary>
        /// Create a new circular buffer using managed array.
        /// </summary>
        /// <param name="array">The managed array.</param>
        /// <param name="allocator">The allocator.</param>
        [NotBurstCompatible]
        public UnsafeCircularBuffer(T[] array, Allocator allocator)
        {
            m_Allocator = allocator;

            var sizeOf = sizeof(T);
            var alignOf = UnsafeUtility.AlignOf<T>();
            m_Ptr = (T*)m_Allocator.Allocate(sizeOf, alignOf, array.Length);
            m_Front = 0;
            m_Back = 0;
            m_Capacity = array.Length;
            m_Count = array.Length;

            fixed (T* ptr = array)
                UnsafeUtility.MemCpy(m_Ptr, ptr, array.Length * sizeOf);
        }

        public void Dispose()
        {
            m_Allocator.Free(m_Ptr, m_Capacity);
            m_Ptr = null;
            m_Front = 0;
            m_Back = 0;
            m_Capacity = 0;
            m_Count = 0;
        }

        /// <summary>
        /// The element at index of the circular buffer.
        /// </summary>
        /// <remarks>
        /// Throws if index is out of range.
        /// </remarks>
        /// <param name="index">The index.</param>
        /// <returns>The element returned by value.</returns>
        public T this[int index]
        {
            get => m_Ptr[GetIndex(index)];
            set => m_Ptr[GetIndex(index)] = value;
        }

        /// <summary>
        /// The element at index of the circular buffer, returned by ref.
        /// </summary>
        /// <remarks>
        /// Throws if index is out of range.
        /// </remarks>
        /// <param name="index">The index.</param>
        /// <returns>The element returned by ref.</returns>
        public ref T ElementAt(int index) => ref m_Ptr[GetIndex(index)];

        /// <summary>
        /// Retrieve the element value at the front of the circular buffer.
        /// </summary>
        /// <remarks>
        /// Throws if empty.
        /// </remarks>
        /// <returns>The element value at the front.</returns>
        public T Front()
        {
            ThrowIfEmpty();
            return m_Ptr[m_Front];
        }

        /// <summary>
        /// Retrieve the element value at the back of the circular buffer.
        /// </summary>
        /// <remarks>
        /// Throws if empty.
        /// </remarks>
        /// <returns>The element value at the back.</returns>
        public T Back()
        {
            ThrowIfEmpty();
            return m_Ptr[(m_Back == 0 ? m_Capacity : m_Back) - 1];
        }

        /// <summary>
        /// Push one element at the front of the circular buffer.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <returns>Whether or not the element could be added.</returns>
        public bool PushFront(in T element)
        {
            if (IsFull)
                return false;

            m_Front = Modulo(m_Front - 1, m_Capacity);
            m_Ptr[m_Front] = element;
            m_Count++;
            return true;
        }

        /// <summary>
        /// Push many elements at the front of the circular buffer.
        /// </summary>
        /// <param name="elements">The elements.</param>
        /// <param name="count">The element count.</param>
        /// <returns>Whether or not the elements could be added.</returns>
        public bool PushFront(T* elements, int count)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (elements == null)
                throw new ArgumentNullException(nameof(elements));
#endif
            if (count <= 0)
                return true;

            if (m_Count + count > m_Capacity)
                return false;

            var spaceAtFront = m_Front <= m_Back ? m_Front : m_Front - m_Back;
            if (count <= spaceAtFront)
            {
                UnsafeUtility.MemCpy(m_Ptr + (m_Front - count), elements, sizeof(T) * count);
            }
            else
            {
                var elementsAtEnd = count - spaceAtFront;
                UnsafeUtility.MemCpy(m_Ptr + (m_Capacity - elementsAtEnd), elements, sizeof(T) * elementsAtEnd);
                UnsafeUtility.MemCpy(m_Ptr, elements + elementsAtEnd, sizeof(T) * spaceAtFront);
            }
            m_Front = Modulo(m_Front - count, m_Capacity);
            m_Count += count;
            return true;
        }

        /// <summary>
        /// Push many elements at the front of the circular buffer.
        /// </summary>
        /// <param name="array">The elements.</param>
        /// <returns>Whether or not the elements could be added.</returns>
        public bool PushFront(in NativeArray<T> array)
        {
            return PushFront((T*)array.GetUnsafeReadOnlyPtr(), array.Length);
        }

        /// <summary>
        /// Push many elements at the front of the circular buffer.
        /// </summary>
        /// <param name="array">The elements.</param>
        /// <returns>Whether or not the elements could be added.</returns>
        [NotBurstCompatible]
        public bool PushFront(T[] array)
        {
            fixed (T* ptr = array)
                return PushFront(ptr, array.Length);
        }

        /// <summary>
        /// Push one element at the back of the circular buffer.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <returns>Whether or not the element could be added.</returns>
        public bool PushBack(in T element)
        {
            if (IsFull)
                return false;

            m_Ptr[m_Back] = element;
            m_Back = Modulo(m_Back + 1, m_Capacity);
            m_Count++;
            return true;
        }

        /// <summary>
        /// Push many elements at the back of the circular buffer.
        /// </summary>
        /// <param name="elements">The elements.</param>
        /// <param name="count">The element count.</param>
        /// <returns>Whether or not the elements could be added.</returns>
        public bool PushBack(T* elements, int count)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (elements == null)
                throw new ArgumentNullException(nameof(elements));
#endif
            if (count <= 0)
                return true;

            if (m_Count + count > m_Capacity)
                return false;

            var spaceAtBack = m_Front <= m_Back ? m_Capacity - m_Back : m_Front - m_Back;
            if (count <= spaceAtBack)
            {
                UnsafeUtility.MemCpy(m_Ptr + m_Back, elements, sizeof(T) * count);
            }
            else
            {
                UnsafeUtility.MemCpy(m_Ptr + m_Back, elements, sizeof(T) * spaceAtBack);
                UnsafeUtility.MemCpy(m_Ptr, elements + spaceAtBack, sizeof(T) * (count - spaceAtBack));
            }
            m_Back = Modulo(m_Back + count, m_Capacity);
            m_Count += count;
            return true;
        }

        /// <summary>
        /// Push many elements at the front of the circular buffer.
        /// </summary>
        /// <param name="array">The elements.</param>
        /// <returns>Whether or not the elements could be added.</returns>
        public bool PushBack(in NativeArray<T> array)
        {
            return PushBack((T*)array.GetUnsafeReadOnlyPtr(), array.Length);
        }

        /// <summary>
        /// Push many elements at the front of the circular buffer.
        /// </summary>
        /// <param name="array">The elements.</param>
        /// <returns>Whether or not the elements could be added.</returns>
        [NotBurstCompatible]
        public bool PushBack(T[] array)
        {
            fixed (T* ptr = array)
                return PushBack(ptr, array.Length);
        }

        /// <summary>
        /// Remove one element at the front of the circular buffer.
        /// </summary>
        /// <returns>Whether or not the element could be removed.</returns>
        public bool PopFront() => PopFront(1);

        /// <summary>
        /// Remove many elements at the front of the circular buffer.
        /// </summary>
        /// <param name="count">The element count.</param>
        /// <returns>Whether or not the elements could be removed.</returns>
        public bool PopFront(int count)
        {
            if (IsEmpty || m_Count < count)
                return false;

            m_Front = Modulo(m_Front + count, m_Capacity);
            m_Count -= count;
            return true;
        }

        /// <summary>
        /// Pop one element at the back of the circular buffer.
        /// </summary>
        /// <returns>Whether or not the element could be removed.</returns>
        public bool PopBack() => PopBack(1);

        /// <summary>
        /// Pop many elements at the back of the circular buffer.
        /// </summary>
        /// <param name="count"></param>
        /// <returns>Whether or not the elements could be removed.</returns>
        public bool PopBack(int count)
        {
            if (IsEmpty || m_Count < count)
                return false;

            m_Back = Modulo(m_Back - count, m_Capacity);
            m_Count -= count;
            return true;
        }

        /// <summary>
        /// Clear the circular buffer from all its elements.
        /// </summary>
        /// <remarks>
        /// Does not deallocate memory.
        /// </remarks>
        public void Clear()
        {
            m_Count = 0;
            m_Back = 0;
            m_Front = 0;
        }

        /// <summary>
        /// Convert the circular buffer to a new NativeArray.
        /// </summary>
        /// <param name="allocator">The allocator used for the new NativeArray.</param>
        /// <returns>The circular buffer elements copied into a NativeArray.</returns>
        public NativeArray<T> ToNativeArray(Allocator allocator)
        {
            var array = new NativeArray<T>(m_Count, allocator, NativeArrayOptions.UninitializedMemory);
            if (m_Count > 0)
            {
                var sizeOf = sizeof(T);
                var dest = (T*)array.GetUnsafePtr();
                if (m_Front < m_Back)
                {
                    UnsafeUtility.MemCpy(dest, m_Ptr + m_Front, sizeOf * (m_Back - m_Front));
                }
                else
                {
                    var length = m_Capacity - m_Front;
                    UnsafeUtility.MemCpy(dest, m_Ptr + m_Front, sizeOf * length);
                    UnsafeUtility.MemCpy(dest + length, m_Ptr, sizeOf * m_Back);
                }
            }
            return array;
        }

        /// <summary>
        /// Convert the circular buffer to an array.
        /// </summary>
        /// <returns>The circular buffer elements copied into an array.</returns>
        [NotBurstCompatible]
        public T[] ToArray()
        {
            using (var array = ToNativeArray(Allocator.Temp))
            {
                return array.ToArray();
            }
        }

        /// <summary>
        /// Returns an enumerator over the elements of the list.
        /// </summary>
        /// <returns>An enumerator over the elements of the list.</returns>
        public Enumerator GetEnumerator() => new Enumerator(this);

        /// <summary>
        /// This method is not implemented. Use <see cref="GetEnumerator"/> instead.
        /// </summary>
        /// <returns>Throws NotImplementedException.</returns>
        /// <exception cref="NotImplementedException">Method is not implemented.</exception>
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// This method is not implemented. Use <see cref="GetEnumerator"/> instead.
        /// </summary>
        /// <returns>Throws NotImplementedException.</returns>
        /// <exception cref="NotImplementedException">Method is not implemented.</exception>
        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// An enumerator over the elements of a circular buffer.
        /// </summary>
        /// <remarks>
        /// In an enumerator's initial state, <see cref="Current"/> is invalid.
        /// The first <see cref="MoveNext"/> call advances the enumerator to the first element of the list.
        /// </remarks>
        public struct Enumerator : IEnumerator<T>
        {
            UnsafeCircularBuffer<T> m_Buffer;
            int m_Index;

            internal Enumerator(UnsafeCircularBuffer<T> buffer)
            {
                m_Buffer = buffer;
                m_Index = -1;
            }

            /// <summary>
            /// Does nothing.
            /// </summary>
            public void Dispose() { }

            /// <summary>
            /// Advances the enumerator to the next element of the list.
            /// </summary>
            /// <remarks>
            /// The first `MoveNext` call advances the enumerator to the first element of the list. Before this call, `Current` is not valid to read.
            /// </remarks>
            /// <returns>True if `Current` is valid to read after the call.</returns>
            public bool MoveNext() => ++m_Index < m_Buffer.m_Count;

            /// <summary>
            /// Resets the enumerator to its initial state.
            /// </summary>
            public void Reset() => m_Index = -1;

            /// <summary>
            /// The current element.
            /// </summary>
            /// <value>The current element.</value>
            public T Current => m_Buffer[m_Index];

            object IEnumerator.Current => Current;
        }

        /// <summary>
        /// Convert relative index to absolute index.
        /// </summary>
        int GetIndex(int index)
        {
            ThrowIfIndexOutOfRange(index);
            return m_Front + (index < (m_Capacity - m_Front) ? index : index - m_Capacity);
        }

        /// <summary>
        /// Modulo implementation that works with negative numbers.
        /// </summary>
        int Modulo(int x, int y)
        {
            var r = x % y;
            return r < 0 ? r + y : r;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void ThrowIfEmpty()
        {
            if (IsEmpty)
                throw new InvalidOperationException("Buffer is empty.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void ThrowIfIndexOutOfRange(int index)
        {
            if (IsEmpty)
                throw new IndexOutOfRangeException($"Cannot access index {index}. Buffer is empty.");
            if (index < 0)
                throw new IndexOutOfRangeException($"Cannot access index {index}.");
            if (index >= m_Count)
                throw new IndexOutOfRangeException($"Cannot access index {index}. Buffer count is {m_Count}.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void ThrowIfCapacityExceeded(int count)
        {
            if (count > m_Capacity)
                throw new InvalidOperationException($"Count {count} exceeds capacity. Buffer capacity is {m_Capacity}.");
        }
    }

    internal sealed class UnsafeCircularBufferTDebugView<T>
        where T : unmanaged
    {
        UnsafeCircularBuffer<T> m_Buffer;

        public UnsafeCircularBufferTDebugView(UnsafeCircularBuffer<T> buffer)
        {
            m_Buffer = buffer;
        }

        public T[] Items => m_Buffer.ToArray();
    }
}
