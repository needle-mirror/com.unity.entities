using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Entities
{
    public class CircularSystemDependencyException : Exception
    {
        public CircularSystemDependencyException(IEnumerable<ComponentSystemBase> chain)
        {
            Chain = chain;
#if UNITY_CSHARP_TINY
            var lines = new List<string>();
            Console.WriteLine($"The following systems form a circular dependency cycle (check their [UpdateBefore]/[UpdateAfter] attributes):");
            foreach (var s in Chain)
            {
                Console.WriteLine($"- {s.GetType().ToString()}");
            }
#endif
        }

        public IEnumerable<ComponentSystemBase> Chain { get; }

#if !UNITY_CSHARP_TINY
        public override string Message
        {
            get
            {
                var lines = new List<string>();
                lines.Add($"The following systems form a circular dependency cycle (check their [UpdateBefore]/[UpdateAfter] attributes):");
                foreach (var s in Chain)
                {
                    lines.Add($"- {s.GetType().ToString()}");
                }
                return lines.Aggregate((str1, str2) => str1 + "\n" + str2);
            }
        }
#endif
    }

    public abstract class ComponentSystemGroup : ComponentSystem
    {
        protected List<ComponentSystemBase> m_systemsToUpdate = new List<ComponentSystemBase>();

        public virtual IEnumerable<ComponentSystemBase> Systems => m_systemsToUpdate;

        public void AddSystemToUpdateList(ComponentSystemBase sys)
        {
            if (sys != null)
            {
                m_systemsToUpdate.Add(sys);
            }
        }

        class Heap<T>
            where T : IComparable<T>
        {
            private T[] _elements;
            private int _size;
            private int _capacity;
            private static readonly int BaseIndex = 1;
            public Heap(int capacity) {
                _capacity = capacity;
                _size = 0;
                _elements = new T[capacity + BaseIndex];
            }
            public bool Empty { get { return _size <= 0; } }
            public void Insert(T e) {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (_size >= _capacity)
                {
                    throw new InvalidOperationException($"Attempted to Insert() to a full heap.");
                }
#endif
                int i = BaseIndex + _size++;
                while (i > BaseIndex) {
                    int parent = i / 2;

                    if (e.CompareTo(_elements[parent]) > 0) {
                        break;
                    }
                    _elements[i] = _elements[parent];
                    i = parent;
                }
                _elements[i] = e;
            }
            public T Peek() {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Empty)
                {
                    throw new InvalidOperationException($"Attempted to Peek() an empty heap.");
                }
#endif
                return _elements[BaseIndex];
            }
            public T Extract() {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Empty)
                {
                    throw new InvalidOperationException($"Attempted to Extract() from an empty heap.");
                }
#endif
                T top = _elements[BaseIndex];
                _elements[BaseIndex] = _elements[_size--];
                if (!Empty) {
                    Heapify(BaseIndex);
                }
                return top;
            }
            private void Heapify(int i) {
                // The index taken by this function is expected to be already biased by BaseIndex.
                // Thus, m_Heap[size] is a valid element (specifically, the final element in the heap)
                //Debug.Assert(i >= BaseIndex && i < (_size+BaseIndex), $"heap index {i} is out of range with size={_size}");
                T val = _elements[i];
                while (i <= _size / 2) {
                    int child = 2 * i;
                    if (child < _size && _elements[child + 1].CompareTo(_elements[child]) < 0) {
                        child++;
                    }
                    if (val.CompareTo(_elements[child]) < 0) {
                        break;
                    }
                    _elements[i] = _elements[child];
                    i = child;
                }
                _elements[i] = val;
            }
        }

        struct SysAndDep
        {
            public ComponentSystemBase system;
            public List<Type> updateBefore;
            public List<Type> updateAfter;
        }
        public struct TypeHeapElement : IComparable<TypeHeapElement>
        {
            private int typeHash;
            public Type type;

            public TypeHeapElement(Type t)
            {
                type = t;
                typeHash = t.GetHashCode();
            }
            public int CompareTo(TypeHeapElement other)
            {
                if (typeHash < other.typeHash)
                    return -1;
                else if (typeHash > other.typeHash)
                    return 1;
                return 0;
            }
        }

        public virtual void SortSystemUpdateList()
        {
#if !UNITY_CSHARP_TINY
            // Populate dictionary mapping systemType to system-and-before/after-types
            // TODO: Can't use Dictionary in Tiny.
            var lookup = new Dictionary<Type, SysAndDep>();
            foreach (var sys in m_systemsToUpdate)
            {
                if (sys.GetType().IsSubclassOf(typeof(ComponentSystemGroup)))
                {
                    (sys as ComponentSystemGroup).SortSystemUpdateList();
                }
                lookup[sys.GetType()] = new SysAndDep
                {
                    system = sys,
                    updateBefore = new List<Type>(),
                    updateAfter = new List<Type>(),
                };
            }
            foreach (var sys in m_systemsToUpdate)
            {
                var depsLists = lookup[sys.GetType()];
                var before = sys.GetType().GetCustomAttributes(typeof(UpdateBeforeAttribute), true);
                var after = sys.GetType().GetCustomAttributes(typeof(UpdateAfterAttribute), true);
                foreach (var attr in before)
                {
                    var dep = attr as UpdateBeforeAttribute;
                    if (!lookup.ContainsKey(dep.SystemType))
                    {
                        Debug.LogWarning("Ignoring invalid [UpdateBefore] dependency for " + sys.GetType() + ": " + dep.SystemType + " must be a member of the same ComponentSystemGroup.");
                        continue;
                    }

                    depsLists.updateBefore.Add(dep.SystemType);
                    lookup[dep.SystemType].updateAfter.Add(sys.GetType());
                }
                foreach (var attr in after)
                {
                    var dep = attr as UpdateAfterAttribute;
                    if (!lookup.ContainsKey(dep.SystemType))
                    {
                        Debug.LogWarning("Ignoring invalid [UpdateAfter] dependency for " + sys.GetType() + ": " + dep.SystemType + " must be a member of the same ComponentSystemGroup.");
                        continue;
                    }
                    lookup[dep.SystemType].updateBefore.Add(sys.GetType());
                    depsLists.updateAfter.Add(dep.SystemType);
                }
            }

            // Clear the systems list and rebuild it in sorted order from the lookup table
            var readySystems = new Heap<TypeHeapElement>(m_systemsToUpdate.Count);
            m_systemsToUpdate.Clear();
            foreach (var sd in lookup.Values)
            {
                if (sd.updateAfter.Count == 0)
                {
                    readySystems.Insert(new TypeHeapElement(sd.system.GetType()));
                }
            }
            while (!readySystems.Empty)
            {
                var sysType = readySystems.Extract().type;
                var sd = lookup[sysType];
                lookup.Remove(sysType);
                m_systemsToUpdate.Add(sd.system);
                foreach (var beforeType in sd.updateBefore)
                {
                    if (lookup[beforeType].updateAfter.Remove(sysType))
                    {
                        if (lookup[beforeType].updateAfter.Count == 0)
                        {
                            readySystems.Insert(new TypeHeapElement(beforeType));
                        }
                    }
                }
            }

            if (lookup.Count > 0)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var visitedSystems = new List<ComponentSystemBase>();
                var start = lookup.First().Value;
                var current = start;
                while (true)
                {
                    visitedSystems.Add(current.system);
                    current = lookup[current.updateAfter.First()];
                    var indexOf = visitedSystems.IndexOf(current.system);
                    if (indexOf != -1)
                    {
                        throw new CircularSystemDependencyException(visitedSystems.Skip(indexOf).Reverse());
                    }
                }
#else
                foreach (var sd in lookup)
                {
                    sd.Value.updateBefore.Clear();
                    sd.Value.updateAfter.Clear();
                }
#endif
            }
#endif
        }

        protected override void OnUpdate()
        {
            foreach (var sys in m_systemsToUpdate)
            {
                sys.Update();
            }
        }
    }
}
