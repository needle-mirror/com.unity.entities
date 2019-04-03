using System;
using System.Reflection;

using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Scripting;

namespace Unity.Entities
{
    public static class ComponentGroupExtensionsForComponentArray
    {
        [Obsolete("GetComponentArray has been deprecated. Use ComponentSystem.ForEach to access managed components.")]
        public static ComponentArray<T> GetComponentArray<T>(this ComponentGroup group) where T : Component
        {
            int length = group.CalculateLength();
            ComponentChunkIterator iterator = group.GetComponentChunkIterator();
            var indexInComponentGroup = group.GetIndexInComponentGroup(TypeManager.GetTypeIndex<T>());

            iterator.IndexInComponentGroup = indexInComponentGroup;
            return new ComponentArray<T>(iterator, length, group.ArchetypeManager);
        }
        
        public static T[] ToComponentArray<T>(this ComponentGroup group) where T : Component
        {
            int length = group.CalculateLength();
            ComponentChunkIterator iterator = group.GetComponentChunkIterator();
            var indexInComponentGroup = group.GetIndexInComponentGroup(TypeManager.GetTypeIndex<T>());

            iterator.IndexInComponentGroup = indexInComponentGroup;

            var arr = new T[length];
            var cache = default(ComponentChunkCache);
            for (int i = 0; i < length; ++i)
            {
                if (i < cache.CachedBeginIndex || i >= cache.CachedEndIndex)
                    iterator.MoveToEntityIndexAndUpdateCache(i, out cache, true);
                
                arr[i] = (T)iterator.GetManagedObject(group.ArchetypeManager, cache.CachedBeginIndex, i);
            }

            return arr;
        }
    }
}

namespace Unity.Entities
{
    [Obsolete("ComponentArray has been deprecated. Use ComponentSystem.ForEach to access managed components.")]
    public struct ComponentArray<T> where T: Component
    {
        ComponentChunkIterator  m_Iterator;
        ComponentChunkCache 	m_Cache;
        readonly int                     m_Length;
        readonly ArchetypeManager		m_ArchetypeManager;

        internal ComponentArray(ComponentChunkIterator iterator, int length, ArchetypeManager typeMan)
        {
            m_Length = length;
            m_Cache = default(ComponentChunkCache);
            m_Iterator = iterator;
            m_ArchetypeManager = typeMan;
        }

        public T this[int index]
        {
            get
            {
                //@TODO: Unnecessary.. integrate into cache instead...
                if ((uint)index >= (uint)m_Length)
                    FailOutOfRangeError(index);

                if (index < m_Cache.CachedBeginIndex || index >= m_Cache.CachedEndIndex)
                    m_Iterator.MoveToEntityIndexAndUpdateCache(index, out m_Cache, true);

                return (T)m_Iterator.GetManagedObject(m_ArchetypeManager, m_Cache.CachedBeginIndex, index);
            }
        }

        public T[] ToArray()
        {
            var arr = new T[m_Length];
            var i = 0;
            while (i < m_Length)
            {
                m_Iterator.MoveToEntityIndexAndUpdateCache(i, out m_Cache, true);
                int start, length;
                var objs = m_Iterator.GetManagedObjectRange(m_ArchetypeManager, m_Cache.CachedBeginIndex, i, out start, out length);
                for (var obj = 0; obj < length; ++obj)
                    arr[i+obj] = (T)objs[start+obj];
                i += length;
            }
            return arr;
        }

        void FailOutOfRangeError(int index)
        {
            throw new IndexOutOfRangeException($"Index {index} is out of range of '{Length}' Length.");
        }

        public int Length => m_Length;
    }

    [Preserve]
    [CustomInjectionHook]
    [Obsolete("ComponentArray and injection have been deprecated. Use ComponentSystem.ForEach to access managed components.")]
    sealed class ComponentArrayInjectionHook : InjectionHook
    {
        public override Type FieldTypeOfInterest => typeof(ComponentArray<>);

        public override bool IsInterestedInField(FieldInfo fieldInfo)
        {
            return fieldInfo.FieldType.IsGenericType && fieldInfo.FieldType.GetGenericTypeDefinition() == typeof(ComponentArray<>);
        }

        public override string ValidateField(FieldInfo field, bool isReadOnly, InjectionContext injectionInfo)
        {
            if (field.FieldType != typeof(ComponentArray<>))
                return null;

            if (isReadOnly)
                return "[ReadOnly] may not be used on ComponentArray<>, it can only be used on ComponentDataArray<>";

            return null;
        }

        public override InjectionContext.Entry CreateInjectionInfoFor(FieldInfo field, bool isReadOnly)
        {
            var componentType = field.FieldType.GetGenericArguments()[0];
            var accessMode = isReadOnly ? ComponentType.AccessMode.ReadOnly : ComponentType.AccessMode.ReadWrite;
            return new InjectionContext.Entry
            {
                Hook = this,
                FieldInfo = field,
                IsReadOnly = false /* isReadOnly */,
                AccessMode = accessMode,
                IndexInComponentGroup = -1,
                FieldOffset = UnsafeUtility.GetFieldOffset(field),
                ComponentType = new ComponentType(componentType, accessMode),
                ComponentRequirements = componentType == typeof(Transform)
                    ? new[] { typeof(Transform), componentType }
                    : new []{ componentType }
            };
        }

        public override void PrepareEntry(ref InjectionContext.Entry entry, ComponentGroup entityGroup)
        {
            entry.IndexInComponentGroup = entityGroup.GetIndexInComponentGroup(entry.ComponentType.TypeIndex);
        }

        internal override unsafe void InjectEntry(InjectionContext.Entry entry, ComponentGroup entityGroup, ref ComponentChunkIterator iterator, int length, byte* groupStructPtr)
        {
            iterator.IndexInComponentGroup = entry.IndexInComponentGroup;
            var data = new ComponentArray<Component>(iterator, length, entityGroup.ArchetypeManager);
            UnsafeUtility.CopyStructureToPtr(ref data, groupStructPtr + entry.FieldOffset);
        }
    }
}
