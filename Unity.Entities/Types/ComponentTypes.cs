using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;

namespace Unity.Entities
{
    /// <summary>
    /// An immutable list of ComponentType values.
    /// </summary>
    /// <remarks>
    /// Max numbers of types is 15 (the capacity of FixedListInt64).
    ///
    /// Values in the list are sorted by their internal type index.
    ///
    /// Only the types themselves are stored, not any access modes.
    ///
    /// Cannot contain multiple ComponentType values with the same type index (safety checks in the constructors will throw an exception).
    /// </remarks>
    public unsafe struct ComponentTypes
    {
        FixedListInt64 m_sorted;

        public struct Masks
        {
            public UInt16 m_BufferMask;
            public UInt16 m_SystemStateComponentMask;
            public UInt16 m_SharedComponentMask;
            public UInt16 m_ZeroSizedMask;

            public bool IsSharedComponent(int index)
            {
                return (m_SharedComponentMask & (1 << index)) != 0;
            }

            public bool IsZeroSized(int index)
            {
                return (m_ZeroSizedMask & (1 << index)) != 0;
            }

            public int Buffers => math.countbits((UInt32)m_BufferMask);
            public int SystemStateComponents => math.countbits((UInt32)m_SystemStateComponentMask);
            public int SharedComponents => math.countbits((UInt32)m_SharedComponentMask);
            public int ZeroSizeds => math.countbits((UInt32)m_ZeroSizedMask);
        }

        public Masks m_masks;

        private void ComputeMasks()
        {
            for (var i = 0; i < m_sorted.Length; ++i)
            {
                var typeIndex = m_sorted[i];
                var mask = (UInt16)(1 << i);
                if (TypeManager.IsBuffer(typeIndex))
                    m_masks.m_BufferMask |= mask;
                if (TypeManager.IsSystemStateComponent(typeIndex))
                    m_masks.m_SystemStateComponentMask |= mask;
                if (TypeManager.IsSharedComponentType(typeIndex))
                    m_masks.m_SharedComponentMask |= mask;
                if (TypeManager.IsZeroSized(typeIndex))
                    m_masks.m_ZeroSizedMask |= mask;
            }
        }

        public int Length
        {
            get => m_sorted.Length;
        }

        public int GetTypeIndex(int index)
        {
            return m_sorted[index];
        }

        internal int ChunkComponentCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < m_sorted.Length; i++)
                {
                    if (GetComponentType(i).IsChunkComponent)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// Returns a ComponentType for the type stored at the index in the list.
        ///
        /// The returned ComponentType always has access mode ReadWrite.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public ComponentType GetComponentType(int index)
        {
            return ComponentType.FromTypeIndex(m_sorted[index]);
        }

        public ComponentTypes(ComponentType a)
        {
            m_sorted = new FixedListInt64();
            m_masks = new Masks();
            m_sorted.Add(a.TypeIndex);
            ComputeMasks();
        }

        public ComponentTypes(ComponentType a, ComponentType b)
        {
            m_sorted = new FixedListInt64();
            m_masks = new Masks();
            m_sorted.Add(a.TypeIndex);
            m_sorted.Add(b.TypeIndex);
            CheckForDuplicates();
            m_sorted.Sort();
            ComputeMasks();
        }

        public ComponentTypes(ComponentType a, ComponentType b, ComponentType c)
        {
            m_sorted = new FixedListInt64();
            m_masks = new Masks();
            m_sorted.Add(a.TypeIndex);
            m_sorted.Add(b.TypeIndex);
            m_sorted.Add(c.TypeIndex);
            m_sorted.Sort();
            CheckForDuplicates();
            ComputeMasks();
        }

        public ComponentTypes(ComponentType a, ComponentType b, ComponentType c, ComponentType d)
        {
            m_sorted = new FixedListInt64();
            m_masks = new Masks();
            m_sorted.Add(a.TypeIndex);
            m_sorted.Add(b.TypeIndex);
            m_sorted.Add(c.TypeIndex);
            m_sorted.Add(d.TypeIndex);
            m_sorted.Sort();
            CheckForDuplicates();
            ComputeMasks();
        }

        public ComponentTypes(ComponentType a, ComponentType b, ComponentType c, ComponentType d, ComponentType e)
        {
            m_sorted = new FixedListInt64();
            m_masks = new Masks();
            m_sorted.Add(a.TypeIndex);
            m_sorted.Add(b.TypeIndex);
            m_sorted.Add(c.TypeIndex);
            m_sorted.Add(d.TypeIndex);
            m_sorted.Add(e.TypeIndex);
            m_sorted.Sort();
            CheckForDuplicates();
            ComputeMasks();
        }

        public ComponentTypes(ComponentType[] componentType)
        {
            m_sorted = new FixedListInt64();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (componentType.Length > m_sorted.Capacity)
                throw new ArgumentException($"A ComponentType value cannot have more than {m_sorted.Capacity} types.");
#endif
            m_masks = new Masks();
            for (var i = 0; i < componentType.Length; ++i)
                m_sorted.Add(componentType[i].TypeIndex);
            m_sorted.Sort();
            CheckForDuplicates();
            ComputeMasks();
        }

        // Assumes m_sorted has already been sorted.
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckForDuplicates()
        {
            var prev = m_sorted[0];
            for (int i = 1; i < m_sorted.Length; i++)
            {
                var current = m_sorted[i];
                if (prev == current)
                {
                    throw new ArgumentException(
                        $"ComponentTypes cannot contain duplicate types. Remove all but one occurence of \"{GetComponentType(i).ToString()}\"");
                }
                prev = current;
            }
        }
    }
}
