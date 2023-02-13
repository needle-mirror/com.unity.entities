using System;
using System.Collections.Generic;
#if !NET_DOTS
using System.Linq;
#endif
using Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Unity.Entities
{
    [BurstCompile]
    internal class ComponentSystemSorter
    {
        public class CircularSystemDependencyException : Exception
        {
            public CircularSystemDependencyException(IEnumerable<Type> chain)
            {
                Chain = chain;
            }

            public IEnumerable<Type> Chain { get; }

            public override string Message
            {
                get
                {
                    var lines = new List<string>
                    {
                        $"The following systems form a circular dependency cycle (check their [*Before]/[*After] attributes):"
                    };
                    foreach (var s in Chain)
                    {
                        lines.Add($"- {s.ToString()}");
                    }

                    return lines.Aggregate((str1, str2) => str1 + "\n" + str2);
                }
            }
        }

        private struct Heap
        {
            private readonly UnsafeList<TypeHeapElement> _elements;
            private int _size;
            private readonly int _capacity;
            private static readonly int BaseIndex = 1;

            public Heap(int capacity)
            {
                _capacity = capacity;
                _size = 0;
                var initialCapacity = capacity + BaseIndex;
                _elements = new UnsafeList<TypeHeapElement>(initialCapacity, Allocator.Temp);
                _elements.Length = initialCapacity;
            }

            public bool Empty => _size <= 0;

            public void Insert(TypeHeapElement e)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                if (_size >= _capacity)
                {
                    throw new InvalidOperationException($"Attempted to Insert() to a full heap.");
                }
#endif
                var i = BaseIndex + _size++;
                while (i > BaseIndex)
                {
                    var parent = i / 2;

                    if (e.CompareTo(_elements[parent]) > 0)
                    {
                        break;
                    }

                    _elements.ElementAt(i) = _elements[parent];
                    i = parent;
                }

                _elements.ElementAt(i) = e;
            }

            public TypeHeapElement Peek()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                if (Empty)
                {
                    throw new InvalidOperationException($"Attempted to Peek() an empty heap.");
                }
#endif
                return _elements[BaseIndex];
            }

            public TypeHeapElement Extract()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                if (Empty)
                {
                    throw new InvalidOperationException($"Attempted to Extract() from an empty heap.");
                }
#endif
                var top = _elements[BaseIndex];
                _elements.ElementAt(BaseIndex) = _elements[_size--];
                if (!Empty)
                {
                    Heapify(BaseIndex);
                }

                return top;
            }

            private void Heapify(int i)
            {
                // The index taken by this function is expected to be already biased by BaseIndex.
                // Thus, m_Heap[size] is a valid element (specifically, the final element in the heap)
                //Debug.Assert(i >= BaseIndex && i < (_size+BaseIndex), $"heap index {i} is out of range with size={_size}");
                var val = _elements[i];
                while (i <= _size / 2)
                {
                    var child = 2 * i;
                    if (child < _size && _elements[child + 1].CompareTo(_elements[child]) < 0)
                    {
                        child++;
                    }

                    if (val.CompareTo(_elements[child]) < 0)
                    {
                        break;
                    }

                    _elements.ElementAt(i) = _elements[child];
                    i = child;
                }

                _elements.ElementAt(i) = val;
            }
        }

        public struct TypeHeapElement : IComparable<TypeHeapElement>
        {
            private readonly int systemTypeIndex;
            private readonly long systemTypeHash;
            public int unsortedIndex;

            public TypeHeapElement(int index, int _systemTypeIndex)
            {
                unsortedIndex = index;
                systemTypeIndex = _systemTypeIndex;
                systemTypeHash = TypeManager.GetSystemTypeHash(systemTypeIndex);
            }

            public int CompareTo(TypeHeapElement other)
            {
                var cmp = systemTypeHash.CompareTo(other.systemTypeHash);
                return cmp != 0 ? cmp : unsortedIndex.CompareTo(other.unsortedIndex);
            }
        }

        private static unsafe int LookupSystemElement(int typeIndex, NativeHashMap<int, int>* lookupDictionaryPtr)
        {
            ref var mylookupDictionary = ref *lookupDictionaryPtr;

            if (mylookupDictionary.ContainsKey(typeIndex))
                return mylookupDictionary[typeIndex];
            return -1;
        }

        internal struct SystemElement 
        {
            public int TypeIndex;
            public UpdateIndex Index;
            public int OrderingBucket; // 0 = OrderFirst, 1 = none, 2 = OrderLast
            public FixedList128Bytes<int> updateBefore;
            public int nAfter;
        }
        
        [BurstCompile(CompileSynchronously = true)]
        internal static unsafe void Sort(UnsafeList<SystemElement>* elementsptr, NativeHashMap<int, int>* lookupDictionary)
        {
            var elements = *elementsptr;
            lookupDictionary->Clear();
            
            var sortedElements = new UnsafeList<SystemElement>(elements.Length,
                Allocator.Temp);
            sortedElements.Length = elements.Length;
            
            int nextOutIndex = 0;

            var readySystems = new Heap(elements.Length);
            for (int i = 0; i < elements.Length; ++i)
            {
                if (elements[i].nAfter == 0)
                {
                    readySystems.Insert(new TypeHeapElement(i, elements[i].TypeIndex));
                }
            }
            
            PopulateSystemElementLookup(lookupDictionary, elements);

            while (!readySystems.Empty)
            {
                var sysIndex = readySystems.Extract().unsortedIndex;
                var elem = elements[sysIndex];

                sortedElements[nextOutIndex++] = new SystemElement
                {
                    TypeIndex = elem.TypeIndex,
                    Index = elem.Index,
                };
                foreach (var beforeType in elem.updateBefore)
                {
                    int beforeIndex = LookupSystemElement(beforeType, lookupDictionary);
                    if (beforeIndex < 0) throw new Exception("Bug in SortSystemUpdateList(), beforeIndex < 0");
                    if (elements[beforeIndex].nAfter <= 0)
                        throw new Exception("Bug in SortSystemUpdateList(), nAfter <= 0");

                    elements.ElementAt(beforeIndex).nAfter--;
                    if (elements[beforeIndex].nAfter == 0)
                    {
                        readySystems.Insert(new TypeHeapElement(beforeIndex, elements[beforeIndex].TypeIndex));
                    }
                }
                elements.ElementAt(sysIndex).nAfter = -1; // "Remove()"
            }

            elements.CopyFrom(sortedElements);
        }

        private static unsafe void PopulateSystemElementLookup(NativeHashMap<int, int>* lookupDictionary, UnsafeList<SystemElement> elements)
        {
            //fill in for fast lookups in LookupSystemElement
            lookupDictionary->Capacity = elements.Length;
            for (int i = 0; i < elements.Length; ++i)
            {
                (*lookupDictionary)[elements[i].TypeIndex] = i;
            }
        }


        internal static unsafe void WarnAboutAnySystemAttributeBadness(int systemTypeIndex, ComponentSystemGroup group)
        {
            var systemType = TypeManager.GetSystemType(systemTypeIndex);
            var updateInGroups =
                TypeManager.GetSystemAttributes(systemType, typeof(UpdateInGroupAttribute));
            Type groupType = group.GetType();
            UpdateInGroupAttribute groupAttr;
            foreach (var attr in updateInGroups)
            {
                groupAttr = (UpdateInGroupAttribute)attr;
                groupType = groupAttr.GroupType;
                if (!TypeManager.IsSystemType(groupType))
                {
                    Debug.LogWarning($"Ignoring invalid UpdateInGroup attribute on {systemType} targeting {groupType}, because {groupType} is not a system type.");
                    continue;
                }
                
                if (!TypeManager.IsSystemAGroup(groupType))
                {
                    Debug.LogWarning($"Ignoring invalid UpdateInGroup attribute on {systemType} targeting {groupType}, because {groupType} is not a ComponentSystemGroup.");
                    continue;
                }

                if (groupType == systemType)
                {
                    Debug.LogWarning($"Ignoring invalid UpdateInGroup attribute on {systemType} because a system group cannot be updated inside itself.\n");
                    continue;
                }

                if (groupAttr.OrderFirst && groupAttr.OrderLast)
                {
                    Debug.LogWarning($"Ignoring invalid OrderFirst & OrderLast directives on UpdateInGroup attribute on {systemType} because a system cannot be ordered both first and last in a group.");
                }
            }

            foreach (var attrType in new[]
                     {
                         typeof(UpdateAfterAttribute),
                         typeof(UpdateBeforeAttribute),
                         typeof(CreateAfterAttribute),
                         typeof(CreateBeforeAttribute)
                     })
            {
                var updates = TypeManager.GetSystemAttributes(systemType, attrType);

                var field = attrType.GetProperty("SystemType");
                foreach (var attr in updates)
                {
                    var targetType = (Type)field.GetValue(attr);
                    
                    if (!TypeManager.IsSystemType(targetType))
                    {
                        Debug.LogWarning(
                            $"Ignoring invalid [{attrType}] attribute on {systemType} targeting {targetType}, because {targetType} is not a subclass of ComponentSystemBase");
                        continue;
                    }

                    if (targetType == systemType)
                    {
                        Debug.LogWarning(
                            $"Ignoring invalid [{attrType}] attribute on {systemType} because a system cannot be updated or created after or before itself.\n");
                        continue;
                    }

                    if (TypeManager.IsSystemManaged(targetType))
                    {
                        if (group.m_managedSystemsToUpdate.All(s => s.GetType() != targetType))
                        {
                            Debug.LogWarning(
                                $"Ignoring invalid [{attrType}] attribute on {systemType} targeting {targetType}.\n" +
                                $"This attribute can only order systems that are members of the same {nameof(ComponentSystemGroup)} instance.\n" +
                                $"Make sure that both systems are in the same system group with [UpdateInGroup(typeof({groupType}))],\n" +
                                $"or by manually adding both systems to the same group's update list.");
                            continue;
                        }
                    }
                    else
                    {
                        //it must be unmanaged if we get here, so look for it in the unmanaged of the group, and warn if it isn't there
                        var badUnmanagedUpdateInGroup = true;
                        for (int i = 0; i < group.m_UnmanagedSystemsToUpdate.Length; i++)
                        {
                            if (TypeManager.GetSystemType(
                                    group.World.Unmanaged.ResolveSystemState(group.m_UnmanagedSystemsToUpdate[i])->
                                        m_SystemTypeIndex) ==
                                targetType)
                            {
                                badUnmanagedUpdateInGroup = false;
                                break;
                            }
                        }

                        if (badUnmanagedUpdateInGroup)
                        {
                            Debug.LogWarning(
                                $"Ignoring invalid [{attrType}] attribute on {systemType} targeting {targetType}.\n" +
                                $"This attribute can only order systems that are members of the same {nameof(ComponentSystemGroup)} instance.\n" +
                                $"Make sure that both systems are in the same system group with [UpdateInGroup(typeof({groupType}))],\n" +
                                $"or by manually adding both systems to the same group's update list.");
                            continue;
                        }
                    }

                    var groupTypeIndex = TypeManager.GetSystemTypeIndex(groupType);
                    var thisBucket =
                        ComponentSystemGroup.ComputeSystemOrdering(systemTypeIndex, groupTypeIndex);
                    
                    var otherBucket = ComponentSystemGroup.ComputeSystemOrdering(TypeManager.GetSystemTypeIndex(targetType), groupTypeIndex);
                    if (thisBucket != otherBucket)
                    {
                        Debug.LogWarning($"Ignoring invalid [{attrType}({targetType})] attribute on {systemType} because OrderFirst/OrderLast has higher precedence.");
                        continue;
                    }
                    
                }
            }
        }

        [BurstCompile]
        internal static unsafe void FindConstraints(
            int parentTypeIndex,
            UnsafeList<SystemElement>* sysElemsPtr,
            NativeHashMap<int, int>* lookupDictionary,
            TypeManager.SystemAttributeKind afterkind,
            TypeManager.SystemAttributeKind beforekind,
            UnsafeList<int>* badSystemTypeIndices)
        {
            var sysElems = *sysElemsPtr;
            lookupDictionary->Clear();
            
            PopulateSystemElementLookup(lookupDictionary, sysElems);

            for (int i = 0; i < sysElems.Length; ++i)
            {
                var systemTypeIndex = sysElems[i].TypeIndex;

                var before = TypeManager.GetSystemAttributes(systemTypeIndex, beforekind);
                var after = TypeManager.GetSystemAttributes(systemTypeIndex, afterkind);
                foreach (var attr in before)
                {
                    bool warn = false;
                    if (CheckBeforeConstraints(parentTypeIndex, attr, systemTypeIndex, out warn))
                    {
                        if (warn)
                            badSystemTypeIndices->Add(systemTypeIndex);
                        continue;
                    }

                    int depIndex = LookupSystemElement(attr.TargetSystemTypeIndex, lookupDictionary);
                    if (depIndex < 0)
                    {
                        badSystemTypeIndices->Add(systemTypeIndex);
                        continue;
                    }

                    sysElems.ElementAt(i).updateBefore.Add(attr.TargetSystemTypeIndex);
                    sysElems.ElementAt(depIndex).nAfter++;
                }

                foreach (var attr in after)
                {
                    bool warn = false;
                    if (CheckAfterConstraints(parentTypeIndex, attr, systemTypeIndex, out warn))
                    {
                        if (warn)
                            badSystemTypeIndices->Add(systemTypeIndex);
                        continue;
                    }

                    int depIndex = LookupSystemElement(attr.TargetSystemTypeIndex, lookupDictionary);
                    if (depIndex < 0)
                    {
                        badSystemTypeIndices->Add(systemTypeIndex);

                        continue;
                    }

                    sysElems.ElementAt(depIndex).updateBefore.Add(systemTypeIndex);
                    sysElems.ElementAt(i).nAfter++;
                }
            }
        }

        private static bool CheckBeforeConstraints(int parentTypeIndex, TypeManager.SystemAttribute dep, int systemTypeIndex, out bool warn)
        {
            warn = false;
            if (dep.TargetSystemTypeIndex == systemTypeIndex)
            {
                warn = true;
                return true;
            }

            int systemBucket = ComponentSystemGroup.ComputeSystemOrdering(systemTypeIndex, parentTypeIndex);
            int depBucket = ComponentSystemGroup.ComputeSystemOrdering(dep.TargetSystemTypeIndex, parentTypeIndex);
            if (depBucket > systemBucket)
            {
                // This constraint is redundant, but harmless; it is accounted for by the bucketing order, and can be quietly ignored.
                return true;
            }
            if (depBucket < systemBucket)
            {
                warn = true;
                return true;
            }

            return false;
        }

        private static unsafe bool CheckAfterConstraints(int parentTypeIndex, TypeManager.SystemAttribute dep, int systemTypeIndex, out bool warn)
        {
            warn = false;
            if (dep.TargetSystemTypeIndex == systemTypeIndex)
            {
                warn = true;
                return true;
            }

            int systemBucket = ComponentSystemGroup.ComputeSystemOrdering(systemTypeIndex, parentTypeIndex);
            int depBucket = ComponentSystemGroup.ComputeSystemOrdering(dep.TargetSystemTypeIndex, parentTypeIndex);
            if (depBucket < systemBucket)
            {
                // This constraint is redundant, but harmless; it is accounted for by the bucketing order, and can be quietly ignored.
                return true;
            }
            if (depBucket > systemBucket)
            {
                warn = true;
                return true;
            }

            return false;
        }
    }
}
