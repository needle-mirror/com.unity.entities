using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;

namespace Unity.Entities
{
    /// <summary> Obsolete. Use <see cref="ComponentTypeSet"/> instead.</summary>
    [Obsolete("ComponentTypes has been renamed to ComponentTypeSet. (UnityUpgradable) -> ComponentTypeSet", true)]
    public struct ComponentTypes
    {
    }

    /// <summary>
    /// An immutable set of <see cref="ComponentType"/> values.
    /// </summary>
    /// <remarks>
    /// Max numbers of types is 15 (the capacity of <see cref="FixedList64Bytes{T}"/> of 32-bit integers).
    ///
    /// Values in the list are sorted by their internal type index.
    ///
    /// Only the types themselves are stored, not any access modes.
    ///
    /// Cannot contain multiple ComponentType values with the same type index (safety checks in the constructors will throw an exception).
    /// </remarks>
    [GenerateTestsForBurstCompatibility]
    [DebuggerTypeProxy(typeof(ComponentTypeSetDebugView))]
    public unsafe struct ComponentTypeSet
    {
        FixedList64Bytes<TypeIndex> m_sorted;
        internal struct Masks
        {
            public UInt16 m_BufferMask;
            public UInt16 m_CleanupComponentMask;
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
            public int CleanupComponents => math.countbits((UInt32)m_CleanupComponentMask);
            public int SharedComponents => math.countbits((UInt32)m_SharedComponentMask);
            public int ZeroSizeds => math.countbits((UInt32)m_ZeroSizedMask);
        }

        internal Masks m_masks;

        private void ComputeMasks()
        {
            for (var i = 0; i < m_sorted.Length; ++i)
            {
                var typeIndex = m_sorted[i];
                var mask = (UInt16)(1 << i);
                if (typeIndex.IsBuffer)
                    m_masks.m_BufferMask |= mask;
                if (typeIndex.IsCleanupComponent)
                    m_masks.m_CleanupComponentMask |= mask;
                if (typeIndex.IsSharedComponentType)
                    m_masks.m_SharedComponentMask |= mask;
                if (typeIndex.IsZeroSized)
                    m_masks.m_ZeroSizedMask |= mask;
            }
        }

        /// <summary>
        /// The component type count
        /// </summary>
        public int Length
        {
            get => m_sorted.Length;
        }

        /// <summary>
        /// Get the type index of a component type within this list
        /// </summary>
        /// <param name="index">The index of the component within the types stored in this list</param>
        /// <returns>The type index of the component type at the specified index</returns>
        public TypeIndex GetTypeIndex(int index)
        {
            return m_sorted[index];
        }

        internal TypeIndex* Types => (TypeIndex*)m_sorted.Buffer;

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
        /// Gets a ComponentType for the type stored at the index in the list.
        /// </summary>
        /// <param name="index">The index in the list.</param>
        /// <returns>Returns the ComponentType for the type stored at the index in the list.
        /// The returned ComponentType always has access mode ReadWrite.</returns>
        public ComponentType GetComponentType(int index)
        {
            return ComponentType.FromTypeIndex(m_sorted[index]);
        }

        /// <summary>
        /// Create an instance with one component type
        /// </summary>
        /// <param name="a">A component type</param>
        public ComponentTypeSet(ComponentType a)
        {
            m_sorted = new FixedList64Bytes<TypeIndex>();
            m_masks = new Masks();
            m_sorted.Add(a.TypeIndex);
            ComputeMasks();
        }

        /// <summary>
        /// Create an instance with two component types
        /// </summary>
        /// <param name="a">A component type</param>
        /// <param name="b">A component type</param>
        public ComponentTypeSet(ComponentType a, ComponentType b)
        {
            m_sorted = new FixedList64Bytes<TypeIndex>();
            m_masks = new Masks();
            m_sorted.Add(a.TypeIndex);
            m_sorted.Add(b.TypeIndex);
            CheckForDuplicates();
            m_sorted.Sort();
            ComputeMasks();
        }

        /// <summary>
        /// Create an instance with three component types
        /// </summary>
        /// <param name="a">A component type</param>
        /// <param name="b">A component type</param>
        /// <param name="c">A component type</param>
        public ComponentTypeSet(ComponentType a, ComponentType b, ComponentType c)
        {
            m_sorted = new FixedList64Bytes<TypeIndex>();
            m_masks = new Masks();
            m_sorted.Add(a.TypeIndex);
            m_sorted.Add(b.TypeIndex);
            m_sorted.Add(c.TypeIndex);
            m_sorted.Sort();
            CheckForDuplicates();
            ComputeMasks();
        }

        /// <summary>
        /// Create an instance with five component types
        /// </summary>
        /// <param name="a">A component type</param>
        /// <param name="b">A component type</param>
        /// <param name="c">A component type</param>
        /// <param name="d">A component type</param>
        public ComponentTypeSet(ComponentType a, ComponentType b, ComponentType c, ComponentType d)
        {
            m_sorted = new FixedList64Bytes<TypeIndex>();
            m_masks = new Masks();
            m_sorted.Add(a.TypeIndex);
            m_sorted.Add(b.TypeIndex);
            m_sorted.Add(c.TypeIndex);
            m_sorted.Add(d.TypeIndex);
            m_sorted.Sort();
            CheckForDuplicates();
            ComputeMasks();
        }

        /// <summary>
        /// Create an instance with five component types
        /// </summary>
        /// <param name="a">A component type</param>
        /// <param name="b">A component type</param>
        /// <param name="c">A component type</param>
        /// <param name="d">A component type</param>
        /// <param name="e">A component type</param>
        public ComponentTypeSet(ComponentType a, ComponentType b, ComponentType c, ComponentType d, ComponentType e)
        {
            m_sorted = new FixedList64Bytes<TypeIndex>();
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

        /// <summary>
        /// Create an instance from a list of component types
        /// </summary>
        /// <param name="types">The list of component types</param>
        /// <exception cref="ArgumentException">Thrown if the length of <paramref name="types"/> exceeds the maximum ComponentTypes capacity (15).</exception>
        [ExcludeFromBurstCompatTesting("Takes managed array")]
        public ComponentTypeSet(ComponentType[] types)
        {
            m_sorted = new FixedList64Bytes<TypeIndex>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (types.Length > m_sorted.Capacity)
                throw new ArgumentException($"A ComponentTypes value cannot have more than {m_sorted.Capacity} types.");
#endif
            m_masks = new Masks();
            for (var i = 0; i < types.Length; ++i)
                m_sorted.Add(types[i].TypeIndex);
            m_sorted.Sort();
            CheckForDuplicates();
            ComputeMasks();
        }

        /// <summary>
        /// Create an instance from a list of component types
        /// </summary>
        /// <param name="types">The list of component types</param>
        /// <exception cref="ArgumentException">Thrown if the length of <paramref name="types"/> exceeds the maximum ComponentTypes capacity (15).</exception>
        public ComponentTypeSet(in FixedList128Bytes<ComponentType> types)
        {
            m_sorted = new FixedList64Bytes<TypeIndex>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (types.Length > m_sorted.Capacity)
                throw new ArgumentException($"A ComponentTypes value cannot have more than {m_sorted.Capacity} types.");
#endif
            m_masks = new Masks();
            for (var i = 0; i < types.Length; ++i)
                m_sorted.Add(types[i].TypeIndex);
            m_sorted.Sort();
            CheckForDuplicates();
            ComputeMasks();
        }

        // Assumes m_sorted has already been sorted.
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private void CheckForDuplicates()
        {
            var prev = m_sorted[0];
            for (int i = 1; i < m_sorted.Length; i++)
            {
                var current = m_sorted[i];
                if (prev == current)
                {
                    throw new ArgumentException(
                        $"ComponentTypes cannot contain duplicate types. Remove all but one occurrence of \"{GetComponentType(i).ToString()}\"");
                }
                prev = current;
            }
        }
    }
}
