using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using Unity.Collections;

namespace Unity.Entities.Conversion
{
    static class MultiList
    {
        const int k_FirstUsageSize = 128;  // first alloc will do this many elements

        public static int CalcEnsureCapacity(int current, int needed)
        {
            if (current == 0)
                current = k_FirstUsageSize;

            while (current < needed)
                current += current / 2;

            return current;
        }

        public static bool CalcExpandCapacity(int current, ref int needed)
        {
            if (current >= needed)
                return false;

            needed = CalcEnsureCapacity(current, needed);
            return true;
        }
    }

    interface IMultiListDataImpl<T> : IDisposable
    {
        void Init();
        void Resize(int size);
        void Set(int idx, in T data);
        T Get(int idx);
    }

    struct MultiListArrayData<T> : IMultiListDataImpl<T>
    {
        public T[] Data;

        void IMultiListDataImpl<T>.Init() => Data = Array.Empty<T>();
        void IMultiListDataImpl<T>.Resize(int size) => Array.Resize(ref Data, size);
        void IMultiListDataImpl<T>.Set(int idx, in T data) => Data[idx] = data;
        T IMultiListDataImpl<T>.Get(int idx) => Data[idx];
        public void Dispose() {}
    }

    struct MultiListNativeArrayData<T> : IMultiListDataImpl<T> where T : unmanaged
    {
        public NativeArray<T> Data;

        void IMultiListDataImpl<T>.Init() { }

        void IMultiListDataImpl<T>.Resize(int size)
        {
            var newData = new NativeArray<T>(size, Allocator.Persistent);
            if (Data.IsCreated)
            {
                NativeArray<T>.Copy(Data, 0, newData, 0, Data.Length);
                Data.Dispose();
            }
            Data = newData;
        }
        void IMultiListDataImpl<T>.Set(int idx, in T data) => Data[idx] = data;
        public T Get(int idx) => Data[idx];

        public void Dispose()
        {
            if (Data.IsCreated)
                Data.Dispose();
        }
    }

    struct MultiList<T, I> : IDisposable where I : IMultiListDataImpl<T>
    {
        // `Next` in this list is used for tracking two things
        //    * sublists: Next points to next item in sublist
        //    * reuse of dealloc'd nodes: Next points to next free node
        // -1 marks the end of a free/sublist

        // `HeadIds` is a front-end index to align a set of MultiLists on a key index, while supporting
        // different sized sublists across MultiLists.

        public NativeArray<int>  HeadIds;
        public NativeArray<int>  Next;
        public int    NextFree;
        public I    Data;

        public void Init()
        {
            NextFree = -1;
            Data.Init();
            SetCapacity(16);
            SetHeadIdsCapacity(16);
        }

        public void Dispose()
        {
            if (HeadIds.IsCreated)
                HeadIds.Dispose();
            HeadIds = default;
            if (Next.IsCreated)
                Next.Dispose();
            Next = default;
            Data.Dispose();
            Data = default;
        }

        // create new sublist, return its id or throw if sublist already exists
        public void AddHead(int headIdIndex, in T data)
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (HeadIds[headIdIndex] >= 0)
                throw new ArgumentException("Already a head at this index", nameof(headIdIndex));
            #endif

            var newId = Alloc();
            HeadIds[headIdIndex] = newId;
            Next[newId] = -1;
            Data.Set(newId, in data);
        }

        // either add a head or insert at front (not tail!) of an existing list (returns id)
        public void Add(int headIdIndex, in T data)
        {
            var newId = Alloc();

            var headId = HeadIds[headIdIndex];
            if (headId < 0)
            {
                HeadIds[headIdIndex] = newId;
                Next[newId] = -1;
            }
            else
            {
                Next[newId] = Next[headId];
                Next[headId] = newId;
            }
            Data.Set(newId, in data);
        }

        public (int id, int serial) AddTail(int headIdIndex)
        {
            unsafe
            {
                int id;
                int serial = AddTail(headIdIndex, &id, 1);
                return (id, serial);
            }
        }

        // walk to end of the given list, add new entries and return the index of the first node added in the sublist.
        public unsafe int AddTail(int headIdIndex, int* outIds, int count)
        {
            var headId = HeadIds[headIdIndex];
            int currentId = headId;
            int serial = 1;
            {
                int next = Next[currentId];
                while (next > 0)
                {
                    currentId = next;
                    next = Next[currentId];
                    serial++;
                }
            }

            Alloc(outIds, count);
            for (int i = 0; i < count; i++)
            {
                Next[currentId] = outIds[i];
                currentId = outIds[i];
            }
            Next[currentId] = -1;
            return serial;
        }

        // walk to end of the given list, add new entry and return (id = node id within multilist, serial = node # within sublist)
        public (int id, int serial) AddTail(int headIdIndex, in T data)
        {
            var added = AddTail(headIdIndex);
            Data.Set(added.id, in data);
            return added;
        }

        // release an entire sublist, returning # items released
        public int ReleaseList(int headIdIndex)
        {
            var headId = HeadIds[headIdIndex];
            HeadIds[headIdIndex] = -1;

            return ReleaseSubList(headId);
        }

        // release a partial sublist (not the head), returning # items released
        public int ReleaseListKeepHead(int headIdIndex)
        {
            var headId = HeadIds[headIdIndex];
            var startId = Next[headId];
            Next[headId] = -1;

            return ReleaseSubList(startId);
        }

        int ReleaseSubList(int id)
        {
            var count = 0;
            while (id >= 0)
            {
                ++count;
                var next = Next[id];
                Release(id);
                id = next;
            }
            return count;
        }

        void Release(int id)
        {
            Next[id] = NextFree;
            NextFree = id;
        }

        [Pure]
        public MultiListEnumerator<T, I> SelectListAt(int headId) =>
            new MultiListEnumerator<T, I>(Data, Next, headId);

        [Pure]
        public MultiListEnumerator<T, I> SelectList(int headIdIndex) =>
            new MultiListEnumerator<T, I>(Data, Next, HeadIds[headIdIndex]);

        public bool TrySelectList(int headIdIndex, out MultiListEnumerator<T, I> iter)
        {
            var headId = HeadIds[headIdIndex];
            if (headId < 0)
            {
                iter = MultiListEnumerator<T, I>.Empty;
                return false;
            }

            iter = SelectListAt(headId);
            return true;
        }

        public void EnsureCapacity(int capacity)
        {
            if (Next.Length < capacity)
                SetCapacity(capacity);
        }

        static void Resize(ref NativeArray<int> data, int size)
        {
            var newData = new NativeArray<int>(size, Allocator.Persistent);
            if (data.IsCreated)
            {
                NativeArray<int>.Copy(data, 0, newData, 0, data.Length < size ? data.Length : size);
                data.Dispose();
            }
            data = newData;
        }

        public void SetHeadIdsCapacity(int newCapacity)
        {
            var oldCapacity = HeadIds.Length;
            Resize(ref HeadIds, newCapacity);

            for (var i = oldCapacity; i < newCapacity; ++i)
                HeadIds[i] = -1;
        }

        int Alloc()
        {
            if (NextFree < 0)
                SetCapacity(MultiList.CalcEnsureCapacity(Next.Length, Next.Length + 1));

            var newId = NextFree;
            NextFree = Next[newId];

            return newId;
        }

        unsafe void Alloc(int* outIds, int count)
        {
            if (count == 0)
                return;
            if (NextFree < 0 || count > 1)
                EnsureCapacity(MultiList.CalcEnsureCapacity(Next.Length, Next.Length + count));

            int next = NextFree;
            for (int i = 0; i < count; i++)
            {
                var newId = next;
                outIds[i] = newId;
                next = Next[newId];
            }

            NextFree = next;
        }

        void SetCapacity(int newCapacity)
        {
            var oldCapacity = Next.Length;

            Resize(ref Next, newCapacity);
            Data.Resize(newCapacity);

            for (var i = oldCapacity; i < newCapacity; ++i)
                Next[i] = i + 1;

            Next[newCapacity - 1] = -1;
            NextFree = oldCapacity;
        }
    }

    class MultiListEnumeratorDebugView<T> where T : unmanaged
    {
        MultiListEnumerator<T> m_Enumerator;

        public MultiListEnumeratorDebugView(MultiListEnumerator<T> enumerator)
        {
            m_Enumerator = enumerator;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items => m_Enumerator.ToArray();
    }

    struct MultiListEnumerator<T, I> : IEnumerable<T>, IEnumerator<T> where I : IMultiListDataImpl<T>
    {
        I m_Data;
        NativeArray<int> m_Next;
        int   m_StartIndex;
        int   m_CurIndex;
        bool  m_IsFirst;

        internal MultiListEnumerator(I data, NativeArray<int> next, int startIndex)
        {
            m_Data       = data;
            m_Next       = next;
            m_StartIndex = startIndex;
            m_CurIndex   = -1;
            m_IsFirst    = true;
        }

        public void Dispose() {}

        public static MultiListEnumerator<T, I> Empty => new MultiListEnumerator<T, I>(default, default, -1);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;

        public bool MoveNext()
        {
            if (m_IsFirst)
            {
                m_CurIndex = m_StartIndex;
                m_IsFirst = false;
            }
            else
                m_CurIndex = m_Next[m_CurIndex];

            return IsValid;
        }

        public void Reset()
        {
            m_CurIndex = -1;
            m_IsFirst = true;
        }

        public T Current => m_Data.Get(m_CurIndex);
        object IEnumerator.Current => Current;

        public bool IsEmpty => m_StartIndex < 0;
        public bool Any => !IsEmpty;
        public bool IsValid => m_CurIndex >= 0;

        public int Count()
        {
            var count = 0;
            for (var i = m_StartIndex; i != -1; i = m_Next[i])
                ++count;

            return count;
        }
    }

    [DebuggerTypeProxy(typeof(MultiListEnumeratorDebugView<>))]
    internal struct MultiListEnumerator<T> : IEnumerable<T>, IEnumerator<T> where T : unmanaged
    {
        MultiListEnumerator<T, MultiListNativeArrayData<T>> m_Enumerator;

        internal MultiListEnumerator(MultiListEnumerator<T, MultiListNativeArrayData<T>> enumerator)
        {
            m_Enumerator = enumerator;
        }
        internal MultiListEnumerator(MultiListNativeArrayData<T> data, NativeArray<int> next, int startIndex)
        {
            m_Enumerator = new MultiListEnumerator<T, MultiListNativeArrayData<T>>(data, next, startIndex);
        }

        public void Dispose() => m_Enumerator.Dispose();

        public static MultiListEnumerator<T> Empty => new MultiListEnumerator<T>(default, default, -1);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;

        public bool MoveNext() => m_Enumerator.MoveNext();

        public void Reset() => m_Enumerator.Reset();

        public T Current => m_Enumerator.Current;
        object IEnumerator.Current => Current;

        public bool IsEmpty => m_Enumerator.IsEmpty;
        public bool Any => !IsEmpty;
        public bool IsValid => m_Enumerator.IsValid;

        public int Count() => m_Enumerator.Count();
    }

    static class MultiListDebugUtility
    {
        public static void ValidateIntegrity<T, I>(ref MultiList<T, I> multiList) where I : IMultiListDataImpl<T>
        {
            var freeList = new List<int>();
            for (var i = multiList.NextFree; i >= 0; i = multiList.Next[i])
                freeList.Add(i);

            var allLists = SelectAllLists(multiList.HeadIds, multiList.Next);
            var enumerated = allLists.SelectMany(_ => _).Concat(freeList).ToList();

            if (enumerated.Distinct().Count() != enumerated.Count)
                throw new InvalidOperationException();
        }

        public static IEnumerable<List<int>> SelectAllLists(NativeArray<int> headIds, NativeArray<int> next)
        {
            foreach (var headId in headIds)
            {
                if (headId >= 0)
                {
                    var list = new List<int>();

                    for (var i = headId; i >= 0; i = next[i])
                        list.Add(i);

                    yield return list;
                }
            }
        }

        public static IEnumerable<List<T>> SelectAllData<T, I>(MultiList<T, I> multiList) where I : IMultiListDataImpl<T>
        {
            var data = multiList.Data;
            foreach (var list in SelectAllLists(multiList.HeadIds, multiList.Next))
                yield return new List<T>(list.Select(i => data.Get(i)));
        }
    }
}
