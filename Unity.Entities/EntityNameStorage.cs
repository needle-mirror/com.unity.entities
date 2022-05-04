using System;
using System.Diagnostics;
using System.Text;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Unity.Entities
{
    sealed class EntityNameStorageDebugView
    {
        EntityNameStorage m_nameStorage;

        public EntityNameStorageDebugView(EntityNameStorage nameStorage)
        {
            m_nameStorage = nameStorage;
        }

        public FixedString128Bytes[] Table
        {
            get
            {
                var table = new FixedString128Bytes[EntityNameStorage.Entries];
                for (var i = 0; i < EntityNameStorage.Entries; ++i)
                    EntityNameStorage.GetFixedString(i, ref table[i]);
                return table;
            }
        }
    }

    /// <summary>
    ///
    /// </summary>
    [DebuggerTypeProxy(typeof(EntityNameStorageDebugView))]
    [BurstCompatible]
    internal struct EntityNameStorage
    {
        internal struct Entry
        {
            public int offset;
            public int length;
        }

        internal struct State
        {
            public byte initialized;
            internal byte hasLoggedError;
            public UnsafeList<byte> buffer; // all the UTF-8 encoded bytes in one place
            public UnsafeList<Entry> entry; // one offset for each text in "buffer"
            public UnsafeParallelMultiHashMap<int, int> hash; // from string hash to table entry
            public int chars; // bytes in buffer allocated so far
            public int entries; // number of strings allocated so far
            public FixedString512Bytes kMaxEntriesMsg;
        }
        internal static readonly SharedStatic<State> s_State = SharedStatic<State>.GetOrCreate<EntityNameStorage>();


        internal const int kMaxEntries = 16 << 10;
        internal const int kMaxChars = kMaxEntries * 64;
        internal const int kErrorExceedMaxEntryCapacity = -1;



        public const int kEntityNameMaxLengthBytes = FixedString64Bytes.utf8MaxLengthInBytes;

        /// <summary>
        ///
        /// </summary>
        public static int Entries => s_State.Data.entries;

        public static void Initialize()
        {
            if (s_State.Data.initialized != 0)
                return;

            s_State.Data.buffer = new UnsafeList<byte>(kMaxChars, Allocator.Persistent);
            s_State.Data.buffer.Length = s_State.Data.buffer.Capacity;
            s_State.Data.entry = new UnsafeList<Entry>(kMaxEntries, Allocator.Persistent);
            s_State.Data.entry.Length = s_State.Data.entry.Capacity;
            s_State.Data.hash = new UnsafeParallelMultiHashMap<int, int>(kMaxEntries, Allocator.Persistent);
            Clear();
            s_State.Data.initialized = 1;
            s_State.Data.hasLoggedError = 0;
            s_State.Data.kMaxEntriesMsg = "Max unique Entity Name capacity exceeded. If you require more storage, edit EntityNameStorage.cs and change the value of kMaxEntries to pre-allocate more space.";


        }

        /// <summary>
        ///
        /// </summary>
        public static void Shutdown()
        {
            if (s_State.Data.initialized == 0)
                return;

            s_State.Data.buffer.Dispose();
            s_State.Data.entry.Dispose();
            s_State.Data.hash.Dispose();
            s_State.Data.initialized = 0;
            s_State.Data.hasLoggedError = 0;
        }

        public static void Clear()
        {
            s_State.Data.chars = 0;
            s_State.Data.entries = 0;
            s_State.Data.hash.Clear();
            var temp = new FixedString64Bytes();
            GetOrCreateIndex(ref temp); // make sure that Index=0 means empty string
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        [BurstCompatible(GenericTypeArguments = new [] { typeof(FixedString64Bytes) })]
        public static unsafe void GetFixedString<T>(int index, ref T temp)
        where T : IUTF8Bytes, INativeList<byte>
        {
            Assert.IsTrue(index < s_State.Data.entries);
            var e = s_State.Data.entry[index];
            Assert.IsTrue(e.length <= kEntityNameMaxLengthBytes);
            temp.Length = math.min(e.length, temp.Capacity);
            UnsafeUtility.MemCpy(temp.GetUnsafePtr(), s_State.Data.buffer.Ptr + e.offset, temp.Length);
        }

        [BurstCompatible(GenericTypeArguments = new [] { typeof(FixedString64Bytes) })]
        public static int GetIndexFromHashAndFixedString<T>(int h, ref T temp)
        where T : IUTF8Bytes, INativeList<byte>
        {
            Assert.IsTrue(temp.Length <= kEntityNameMaxLengthBytes);
            int itemIndex;
            NativeParallelMultiHashMapIterator<int> iter;
            if (s_State.Data.hash.TryGetFirstValue(h, out itemIndex, out iter))
            {
                do
                {
                    var e = s_State.Data.entry[itemIndex];
                    Assert.IsTrue(e.length <= kEntityNameMaxLengthBytes);
                    if (e.length == temp.Length)
                    {
                        int matches;
                        for (matches = 0; matches < e.length; ++matches)
                            if (temp[matches] != s_State.Data.buffer[e.offset + matches])
                                break;
                        if (matches == temp.Length)
                            return itemIndex;
                    }
                } while (s_State.Data.hash.TryGetNextValue(out itemIndex, ref iter));
            }
            return -1;
        }

        [BurstCompatible(GenericTypeArguments = new [] { typeof(FixedString64Bytes) })]
        public static bool Contains<T>(ref T value)
        where T : IUTF8Bytes, INativeList<byte>
        {
            int h = value.GetHashCode();
            return GetIndexFromHashAndFixedString(h, ref value) != -1;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [NotBurstCompatible]
        public static unsafe bool Contains(string value)
        {
            FixedString64Bytes temp = value;
            return Contains(ref temp);
        }

        [BurstCompatible(GenericTypeArguments = new [] { typeof(FixedString64Bytes) })]
        public static int GetOrCreateIndex<T>(ref T value)
        where T : IUTF8Bytes, INativeList<byte>
        {
            int h = value.GetHashCode();
            var itemIndex = GetIndexFromHashAndFixedString(h, ref value);

            if (itemIndex != kErrorExceedMaxEntryCapacity)
                return itemIndex;

            //return an invalid value when table entry is full
            if (s_State.Data.entries >= kMaxEntries)
                return kErrorExceedMaxEntryCapacity;

            Assert.IsTrue(s_State.Data.chars + value.Length <= kMaxChars);
            var o = s_State.Data.chars;
            var l = (ushort)value.Length;
            for (var i = 0; i < l; ++i)
                s_State.Data.buffer[s_State.Data.chars++] = value[i];
            s_State.Data.entry[s_State.Data.entries] = new Entry { offset = o, length = l };
            s_State.Data.hash.Add(h, s_State.Data.entries);
            return s_State.Data.entries++;
        }
    }


    /// <summary>
    ///
    /// </summary>
    /// <remarks>
    /// An "EntityName" is an integer that refers to 4,096 or fewer chars of UTF-16 text in a global storage blob.
    /// Each should refer to *at most* about one printed page of text.
    ///
    /// If you need more text, consider using one EntityName struct for each printed page's worth.
    ///
    /// Each EntityName instance that you create is stored in a single, internally-managed EntityNameStorage object,
    /// which can hold up to 16,384 EntityName entries. Once added, the entries in EntityNameStorage cannot be modified
    /// or removed.
    /// </remarks>
    [BurstCompatible]
    internal struct EntityName
    {
        internal int Index;

        [BurstCompatible(GenericTypeArguments = new [] { typeof(FixedString64Bytes) })]
        public void ToFixedString<T>(ref T value)
        where T : IUTF8Bytes, INativeList<byte>
        {
            EntityNameStorage.GetFixedString(Index, ref value);
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        [NotBurstCompatible]
        public override string ToString()
        {
            FixedString64Bytes temp = default;
            ToFixedString(ref temp);
            return temp.ToString();
        }

        [BurstCompatible(GenericTypeArguments = new [] { typeof(FixedString64Bytes) })]
        public void SetFixedString<T>(ref T value)
        where T : IUTF8Bytes, INativeList<byte>
        {
            int tryIndex = EntityNameStorage.GetOrCreateIndex(ref value);

            if(tryIndex >= 0)
                Index = tryIndex;
            else if(EntityNameStorage.s_State.Data.hasLoggedError == 0)
            {
                UnityEngine.Debug.LogError(EntityNameStorage.s_State.Data.kMaxEntriesMsg);
                EntityNameStorage.s_State.Data.hasLoggedError++;
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="value"></param>
        [NotBurstCompatible]
        public unsafe void SetString(string value)
        {

            FixedString64Bytes temp = new FixedString64Bytes();
            fixed (char* chars = value)
            {
                UTF8ArrayUnsafeUtility.Copy(temp.GetUnsafePtr(), out var utf8Len,
                    EntityNameStorage.kEntityNameMaxLengthBytes,chars, value.Length);

                temp.Length = utf8Len;
            }


            SetFixedString(ref temp);
        }
    }
}
