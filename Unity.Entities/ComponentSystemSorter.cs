using System;
using System.Collections.Generic;
#if !NET_DOTS
using System.Linq;
#endif
using Unity;
using Unity.Entities;

namespace Unity.Entities
{
    public class ComponentSystemSorter
    {
        public class CircularSystemDependencyException : Exception
        {
            public CircularSystemDependencyException(IEnumerable<Type> chain)
            {
                Chain = chain;
#if NET_DOTS
                var lines = new List<string>();
                Console.WriteLine($"The following systems form a circular dependency cycle (check their [UpdateBefore]/[UpdateAfter] attributes):");
                foreach (var s in Chain)
                {
                    string name = TypeManager.GetSystemName(s);
                    Console.WriteLine(name);
                }
#endif
            }

            public IEnumerable<Type> Chain { get; }

#if !NET_DOTS
            public override string Message
            {
                get
                {
                    var lines = new List<string>
                    {
                        $"The following systems form a circular dependency cycle (check their [UpdateBefore]/[UpdateAfter] attributes):"
                    };
                    foreach (var s in Chain)
                    {
                        lines.Add($"- {s.ToString()}");
                    }

                    return lines.Aggregate((str1, str2) => str1 + "\n" + str2);
                }
            }
#endif
        }

        private class Heap
        {
            private readonly TypeHeapElement[] _elements;
            private int _size;
            private readonly int _capacity;
            private static readonly int BaseIndex = 1;

            public Heap(int capacity)
            {
                _capacity = capacity;
                _size = 0;
                _elements = new TypeHeapElement[capacity + BaseIndex];
            }

            public bool Empty => _size <= 0;

            public void Insert(TypeHeapElement e)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
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

                    _elements[i] = _elements[parent];
                    i = parent;
                }

                _elements[i] = e;
            }

            public TypeHeapElement Peek()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Empty)
                {
                    throw new InvalidOperationException($"Attempted to Peek() an empty heap.");
                }
#endif
                return _elements[BaseIndex];
            }

            public TypeHeapElement Extract()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Empty)
                {
                    throw new InvalidOperationException($"Attempted to Extract() from an empty heap.");
                }
#endif
                var top = _elements[BaseIndex];
                _elements[BaseIndex] = _elements[_size--];
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

                    _elements[i] = _elements[child];
                    i = child;
                }

                _elements[i] = val;
            }
        }

        private struct SysAndDep
        {
            public Type type;
            public UpdateIndex externalIndex;
            public List<Type> updateBefore;
            public int nAfter;
        }

        public struct TypeHeapElement : IComparable<TypeHeapElement>
        {
            private readonly string typeName;
            public int unsortedIndex;

            public TypeHeapElement(int index, Type t)
            {
                unsortedIndex = index;
                typeName = TypeManager.GetSystemName(t);
            }

            public int CompareTo(TypeHeapElement other)
            {
                // Workaround for missing string.CompareTo() in HPC#. This is not a fully compatible substitute,
                // but should be suitable for comparing system names.
                if (typeName.Length < other.typeName.Length)
                    return -1;
                if (typeName.Length > other.typeName.Length)
                    return 1;
                for (int i = 0; i < typeName.Length; ++i)
                {
                    if (typeName[i] < other.typeName[i])
                        return -1;
                    if (typeName[i] > other.typeName[i])
                        return 1;
                }
                return 0;
            }
        }

        // Tiny doesn't have a data structure that can take Type as a key.
        // For now, this gives Tiny a linear search. Would like to do better.
#if !NET_DOTS
        private static Dictionary<Type, int> lookupDictionary = null;

        private static int LookupSysAndDep(Type t, SysAndDep[] array)
        {
            if (lookupDictionary == null)
            {
                lookupDictionary = new Dictionary<Type, int>();
                for (int i = 0; i < array.Length; ++i)
                {
                    lookupDictionary[array[i].type] = i;
                }
            }

            if (lookupDictionary.ContainsKey(t))
                return lookupDictionary[t];
            return -1;
        }

#else
        private static int LookupSysAndDep(Type t, SysAndDep[] array)
        {
            for (int i = 0; i < array.Length; ++i)
            {
                if (array[i].type == t)
                {
                    return i;
                }
            }
            return -1;
        }

#endif

        private static void WarningForBeforeCheck(Type sysType, Type depType)
        {
            Debug.LogWarning(
                $"Ignoring redundant [UpdateBefore] attribute on {sysType} because {depType} is already restricted to be last.\n"
                + $"Set the target parameter of [UpdateBefore] to a different system class in the same {nameof(ComponentSystemGroup)} as {sysType}.");
        }

        private static void WarningForAfterCheck(Type sysType, Type depType)
        {
            Debug.LogWarning(
                $"Ignoring redundant [UpdateAfter] attribute on {sysType} because {depType} is already restricted to be first.\n"
                + $"Set the target parameter of [UpdateAfter] to a different system class in the same {nameof(ComponentSystemGroup)} as {sysType}.");
        }

        internal struct SystemElement
        {
            public Type Type;
            public UpdateIndex Index;
        }

        internal static void Sort(List<SystemElement> elements, Type parentType)
        {
#if !NET_DOTS
            lookupDictionary = null;
#endif
            // Populate dictionary mapping systemType to system-and-before/after-types.
            // This is clunky - it is easier to understand, and cleaner code, to
            // just use a Dictionary<Type, SysAndDep>. However, Tiny doesn't currently have
            // the ability to use Type as a key to a NativeHash, so we're stuck until that gets addressed.
            //
            // Likewise, this is important shared code. It can be done cleaner with 2 versions, but then...
            // 2 sets of bugs and slightly different behavior will creep in.
            //
            var sysAndDep = new SysAndDep[elements.Count];

            for (int i = 0; i < elements.Count; ++i)
            {
                var elem = elements[i];

                sysAndDep[i] = new SysAndDep
                {
                    type = elem.Type,
                    externalIndex = elem.Index,
                    updateBefore = new List<Type>(),
                    nAfter = 0,
                };
            }

            FindConstraints(parentType, sysAndDep);

            // Clear the systems list and rebuild it in sorted order from the lookup table
            elements.Clear();

            var readySystems = new Heap(sysAndDep.Length);
            for (int i = 0; i < sysAndDep.Length; ++i)
            {
                if (sysAndDep[i].nAfter == 0)
                {
                    readySystems.Insert(new TypeHeapElement(i, sysAndDep[i].type));
                }
            }

            while (!readySystems.Empty)
            {
                var sysIndex = readySystems.Extract().unsortedIndex;
                var sd = sysAndDep[sysIndex];

                sysAndDep[sysIndex] = default; // "Remove()"
                elements.Add(new SystemElement {Type = sd.type, Index = sd.externalIndex});
                foreach (var beforeType in sd.updateBefore)
                {
                    int beforeIndex = LookupSysAndDep(beforeType, sysAndDep);
                    if (beforeIndex < 0) throw new Exception("Bug in SortSystemUpdateList(), beforeIndex < 0");
                    if (sysAndDep[beforeIndex].nAfter <= 0)
                        throw new Exception("Bug in SortSystemUpdateList(), nAfter <= 0");

                    sysAndDep[beforeIndex].nAfter--;
                    if (sysAndDep[beforeIndex].nAfter == 0)
                    {
                        readySystems.Insert(new TypeHeapElement(beforeIndex, sysAndDep[beforeIndex].type));
                    }
                }
            }

            for (int i = 0; i < sysAndDep.Length; ++i)
            {
                if (sysAndDep[i].type != null)
                {
                    // Since no System in the circular dependency would have ever been added
                    // to the heap, we should have values for everything in sysAndDep. Check,
                    // just in case.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    var visitedSystems = new List<Type>();
                    var startIndex = i;
                    var currentIndex = i;
                    while (true)
                    {
                        if (sysAndDep[currentIndex].type != null)
                            visitedSystems.Add(sysAndDep[currentIndex].type);

                        currentIndex = LookupSysAndDep(sysAndDep[currentIndex].updateBefore[0], sysAndDep);
                        if (currentIndex < 0 || currentIndex == startIndex || sysAndDep[currentIndex].type == null)
                        {
                            throw new CircularSystemDependencyException(visitedSystems);
                        }
                    }
#else
                    sysAndDep[i] = default;
#endif
                }
            }
        }

        private static void FindConstraints(Type parentType, SysAndDep[] sysAndDep)
        {
            for (int i = 0; i < sysAndDep.Length; ++i)
            {
                var systemType = sysAndDep[i].type;

                var before = TypeManager.GetSystemAttributes(systemType, typeof(UpdateBeforeAttribute));
                var after = TypeManager.GetSystemAttributes(systemType, typeof(UpdateAfterAttribute));
                foreach (var attr in before)
                {
                    var dep = attr as UpdateBeforeAttribute;

                    if (CheckBeforeConstraints(parentType, dep, systemType))
                        continue;

                    int depIndex = LookupSysAndDep(dep.SystemType, sysAndDep);
                    if (depIndex < 0)
                    {
                        Debug.LogWarning(
                            $"Ignoring invalid [UpdateBefore] attribute on {systemType} because {dep.SystemType} belongs to a different {nameof(ComponentSystemGroup)}.\n"
                            + $"This attribute can only order systems that are children of the same {nameof(ComponentSystemGroup)}.\n"
                            + $"Make sure that both systems are in the same parent group with [UpdateInGroup(typeof({parentType})].\n"
                            + $"You can also change the relative order of groups when appropriate, by using [UpdateBefore] and [UpdateAfter] attributes at the group level.");
                        continue;
                    }

                    sysAndDep[i].updateBefore.Add(dep.SystemType);
                    sysAndDep[depIndex].nAfter++;
                }

                foreach (var attr in after)
                {
                    var dep = attr as UpdateAfterAttribute;

                    if (CheckAfterConstraints(parentType, dep, systemType))
                        continue;

                    int depIndex = LookupSysAndDep(dep.SystemType, sysAndDep);
                    if (depIndex < 0)
                    {
                        Debug.LogWarning(
                            $"Ignoring invalid [UpdateAfter] attribute on {systemType} because {dep.SystemType} belongs to a different {nameof(ComponentSystemGroup)}.\n"
                            + $"This attribute can only order systems that are children of the same {nameof(ComponentSystemGroup)}.\n"
                            + $"Make sure that both systems are in the same parent group with [UpdateInGroup(typeof({parentType})].\n"
                            + $"You can also change the relative order of groups when appropriate, by using [UpdateBefore] and [UpdateAfter] attributes at the group level.");
                        continue;
                    }

                    sysAndDep[depIndex].updateBefore.Add(systemType);
                    sysAndDep[i].nAfter++;
                }
            }
        }

        private static bool CheckBeforeConstraints(Type parentType, UpdateBeforeAttribute dep, Type systemType)
        {
            if (!typeof(ComponentSystemBase).IsAssignableFrom(dep.SystemType))
            {
                Debug.LogWarning(
                    $"Ignoring invalid [UpdateBefore] attribute on {systemType} because {dep.SystemType} is not a subclass of {nameof(ComponentSystemBase)}.\n"
                    + $"Set the target parameter of [UpdateBefore] to a system class in the same {nameof(ComponentSystemGroup)} as {systemType}.");
                return true;
            }

            if (dep.SystemType == systemType)
            {
                Debug.LogWarning(
                    $"Ignoring invalid [UpdateBefore] attribute on {systemType} because a system cannot be updated before itself.\n"
                    + $"Set the target parameter of [UpdateBefore] to a different system class in the same {nameof(ComponentSystemGroup)} as {systemType}.");
                return true;
            }

            return false;
        }

        private static bool CheckAfterConstraints(Type parentType, UpdateAfterAttribute dep, Type systemType)
        {
            if (!typeof(ComponentSystemBase).IsAssignableFrom(dep.SystemType))
            {
                Debug.LogWarning(
                    $"Ignoring invalid [UpdateAfter] attribute on {systemType} because {dep.SystemType} is not a subclass of {nameof(ComponentSystemBase)}.\n"
                    + $"Set the target parameter of [UpdateAfter] to a system class in the same {nameof(ComponentSystemGroup)} as {systemType}.");
                return true;
            }

            if (dep.SystemType == systemType)
            {
                Debug.LogWarning(
                    $"Ignoring invalid [UpdateAfter] attribute on {systemType} because a system cannot be updated after itself.\n"
                    + $"Set the target parameter of [UpdateAfter] to a different system class in the same {nameof(ComponentSystemGroup)} as {systemType}.");
                return true;
            }

            return false;
        }
    }
}
