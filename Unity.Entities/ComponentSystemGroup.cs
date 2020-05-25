using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
#if !NET_DOTS
using System.Linq;
#endif

namespace Unity.Entities
{
    internal struct UpdateIndex
    {
        private ushort Data;

        public bool IsManaged => (Data & 0x8000) != 0;
        public int Index => Data & 0x7fff;

        public UpdateIndex(int index, bool managed)
        {
            Data = (ushort) index;
            Data |= (ushort)((managed ? 1 : 0) << 15);
        }
    }

    public unsafe abstract class ComponentSystemGroup : ComponentSystem
    {
        private bool m_systemSortDirty = false;

        /// <summary>
        /// Controls if the legacy sort ordering is used.
        /// </summary>
        public bool UseLegacySortOrder { get; set; } = true;

        internal bool Created { get; private set; } = false;

        protected List<ComponentSystemBase> m_systemsToUpdate = new List<ComponentSystemBase>();
        internal List<ComponentSystemBase> m_systemsToRemove = new List<ComponentSystemBase>();

        internal UnsafeList<UpdateIndex> m_MasterUpdateList;
        internal UnsafeList<SystemRefUntyped> m_UnmanagedSystemsToUpdate;
        internal UnsafeList<SystemRefUntyped> m_UnmanagedSystemsToRemove;

        public virtual IEnumerable<ComponentSystemBase> Systems => m_systemsToUpdate;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_UnmanagedSystemsToUpdate = new UnsafeList<SystemRefUntyped>(0, Allocator.Persistent);
            m_UnmanagedSystemsToRemove = new UnsafeList<SystemRefUntyped>(0, Allocator.Persistent);
            m_MasterUpdateList = new UnsafeList<UpdateIndex>(0, Allocator.Persistent);
            Created = true;
        }

        protected override void OnDestroy()
        {
            m_MasterUpdateList.Dispose();
            m_UnmanagedSystemsToRemove.Dispose();
            m_UnmanagedSystemsToUpdate.Dispose();
            base.OnDestroy();
            Created = false;
        }

        private void CheckCreated()
        {
            if (!Created)
                throw new InvalidOperationException($"Group of type {GetType()} has not been created, either the derived class forgot to call base.OnCreate(), or it has been destroyed");
        }

        public void AddSystemToUpdateList(ComponentSystemBase sys)
        {
            CheckCreated();

            if (sys != null)
            {
                if (this == sys)
                    throw new ArgumentException($"Can't add {TypeManager.GetSystemName(GetType())} to its own update list");

                // Check for duplicate Systems. Also see issue #1792
                if (m_systemsToUpdate.IndexOf(sys) >= 0)
                    return;

                m_systemsToUpdate.Add(sys);
                m_systemSortDirty = true;
            }
        }

        private int UnmanagedSystemIndex(SystemRefUntyped sysRef)
        {
            int len = m_UnmanagedSystemsToUpdate.Length;
            var ptr = m_UnmanagedSystemsToUpdate.Ptr;
            for (int i = 0; i < len; ++i)
            {
                if (ptr[len] == sysRef)
                {
                    return i;
                }
            }
            return -1;
        }

        internal void AddUnmanagedSystemToUpdateList(SystemRefUntyped sysRef)
        {
            CheckCreated();

            if (-1 != UnmanagedSystemIndex(sysRef))
                return;

            if (UseLegacySortOrder)
                throw new InvalidOperationException("ISystemBase systems are not compatible with legacy sort order. Set UseLegacySortOrder to false to use ISystemBase systems.");

            m_UnmanagedSystemsToUpdate.Add(sysRef);
            m_systemSortDirty = true;
        }

        public void RemoveSystemFromUpdateList(ComponentSystemBase sys)
        {
            CheckCreated();

            m_systemSortDirty = true;
            m_systemsToRemove.Add(sys);
        }

        internal void RemoveUnmanagedSystemFromUpdateList(SystemRefUntyped sys)
        {
            CheckCreated();

            m_systemSortDirty = true;
            m_UnmanagedSystemsToRemove.Add(sys);
        }

        [Obsolete("Use SortSystems(). (RemovedAfter 2020-07-30)")]
        public virtual void SortSystemUpdateList()
        {
            CheckCreated();

            if (!UseLegacySortOrder)
                throw new InvalidOperationException("UseLegacySortOrder must be true to use the SortSystemUpdateList() legacy API");

            if (!m_systemSortDirty)
                return;

            m_systemSortDirty = false;

            RemovePending();

            foreach (var sys in m_systemsToUpdate)
            {
                if (TypeManager.IsSystemAGroup(sys.GetType()))
                {
                    RecurseUpdate((ComponentSystemGroup) sys);
                }
            }

            var elems = new List<ComponentSystemSorter.SystemElement>(m_systemsToUpdate.Count);

            for (int i = 0; i < m_systemsToUpdate.Count; ++i)
            {
                elems.Add(new ComponentSystemSorter.SystemElement { Index = new UpdateIndex(i, true), Type = m_systemsToUpdate[i].GetType() });
            }

            ComponentSystemSorter.Sort(elems, this.GetType());

            var oldSystems = m_systemsToUpdate;
            m_systemsToUpdate = new List<ComponentSystemBase>(oldSystems.Count);
            m_MasterUpdateList.Clear();
            for (int i = 0; i < elems.Count; ++i)
            {
                var index = elems[i].Index;
                m_systemsToUpdate.Add(oldSystems[index.Index]);
                m_MasterUpdateList.Add(new UpdateIndex(i, true));
            }
        }

        private void RemovePending()
        {
            if (m_systemsToRemove.Count > 0)
            {
                foreach (var sys in m_systemsToRemove)
                {
                    m_systemsToUpdate.Remove(sys);
                }

                m_systemsToRemove.Clear();
            }
        }

        private static void RecurseUpdate(ComponentSystemGroup group)
        {
            if (group.UseLegacySortOrder)
            {
#pragma warning disable 618
                group.SortSystemUpdateList();
#pragma warning restore 618
            }
            else
            {
                group.SortSystemUpdateList2();
            }
        }

        private void SortSystemUpdateList2()
        {
            if (!m_systemSortDirty)
                return;

            if (UseLegacySortOrder)
                throw new InvalidOperationException("UseLegacySortOrder must be false to use the updated sorting API");

            RemovePending();


            // Build three lists of systems
            var elems = new List<ComponentSystemSorter.SystemElement>[3]
            {
                new List<ComponentSystemSorter.SystemElement>(4),
                new List<ComponentSystemSorter.SystemElement>(m_systemsToUpdate.Count + m_UnmanagedSystemsToUpdate.Length),
                new List<ComponentSystemSorter.SystemElement>(4),
            };

            var ourType = GetType();
            for (int i = 0; i < m_systemsToUpdate.Count; ++i)
            {
                var system = m_systemsToUpdate[i];
                var sysType = system.GetType();

                // Take order first/last ordering into account
                int ordering = ComputeSystemOrdering(sysType, ourType);
                elems[ordering].Add(new ComponentSystemSorter.SystemElement { Index = new UpdateIndex(i, true), Type = sysType });
            }

            for (int i = 0; i < m_UnmanagedSystemsToUpdate.Length; ++i)
            {
                var sysType = World.GetTypeOfUnmanagedSystem(m_UnmanagedSystemsToUpdate[i]);
                int ordering = ComputeSystemOrdering(sysType, ourType);
                elems[ordering].Add(new ComponentSystemSorter.SystemElement {Index = new UpdateIndex(i, false), Type = sysType});
            }

            // Perform the sort for each bucket.
            for (int i = 0; i < 3; ++i)
            {
                if (elems[i].Count > 0)
                {
                    ComponentSystemSorter.Sort(elems[i], ourType);
                }
            }

            // Because people can freely look at the list of managed systems, we need to put that part of list in order.
            var oldSystems = m_systemsToUpdate;
            m_systemsToUpdate = new List<ComponentSystemBase>(oldSystems.Count);
            for (int i = 0; i < 3; ++i)
            {
                foreach (var e in elems[i])
                {
                    var index = e.Index;
                    if (index.IsManaged)
                    {
                        m_systemsToUpdate.Add(oldSystems[index.Index]);
                    }
                }
            }

            // Commit results to master update list
            m_MasterUpdateList.Clear();
            m_MasterUpdateList.SetCapacity(elems[0].Count + elems[1].Count + elems[2].Count);

            // Append buckets in order, but replace managed indicies with incrementinging indices
            // into the newly sorted m_systemsToUpdate list
            int managedIndex = 0;
            for (int i = 0; i < 3; ++i)
            {
                foreach (var e in elems[i])
                {
                    if (e.Index.IsManaged)
                    {
                        m_MasterUpdateList.Add(new UpdateIndex(managedIndex++, true));
                    }
                    else
                    {
                        m_MasterUpdateList.Add(e.Index);
                    }
                }
            }

            m_systemSortDirty = false;

            foreach (var sys in m_systemsToUpdate)
            {
                if (TypeManager.IsSystemAGroup(sys.GetType()))
                {
                    RecurseUpdate((ComponentSystemGroup) sys);
                }
            }
        }

        private static int ComputeSystemOrdering(Type sysType, Type ourType)
        {
            foreach (var uga in TypeManager.GetSystemAttributes(sysType, typeof(UpdateInGroupAttribute)))
            {
                var updateInGroupAttribute = (UpdateInGroupAttribute) uga;

                if (updateInGroupAttribute.GroupType.IsAssignableFrom(ourType))
                {
                    if (updateInGroupAttribute.OrderFirst)
                    {
                        return 0;
                    }

                    if (updateInGroupAttribute.OrderLast)
                    {
                        return 2;
                    }
                }
            }

            return 1;
        }

        /// <summary>
        /// Update the component system's sort order.
        /// </summary>
        public void SortSystems()
        {
            CheckCreated();

            RecurseUpdate(this);
        }

#if UNITY_DOTSPLAYER
        public void RecursiveLogToConsole()
        {
            foreach (var sys in m_systemsToUpdate)
            {
                if (sys is ComponentSystemGroup)
                {
                    (sys as ComponentSystemGroup).RecursiveLogToConsole();
                }

                var name = TypeManager.GetSystemName(sys.GetType());
                Debug.Log(name);
            }
        }

#endif

        protected override void OnStopRunning()
        {
        }

        internal override void OnStopRunningInternal()
        {
            OnStopRunning();

            foreach (var sys in m_systemsToUpdate)
            {
                if (sys == null)
                    continue;

                if (sys.m_StatePtr == null)
                    continue;

                if (!sys.m_StatePtr->m_PreviouslyEnabled)
                    continue;

                sys.m_StatePtr->m_PreviouslyEnabled = false;
                sys.OnStopRunningInternal();
            }

            for (int i = 0; i < m_UnmanagedSystemsToUpdate.Length; ++i)
            {
                var sys = World.ResolveSystemState(m_UnmanagedSystemsToUpdate[i]);

                if (sys == null || !sys->m_PreviouslyEnabled)
                    continue;

                sys->m_PreviouslyEnabled = false;

                // Optional callback here
            }
        }

        /// <summary>
        /// An optional callback.  If set, this group's systems will be updated in a loop while
        /// this callback returns true.  This can be used to implement custom processing before/after
        /// update (first call should return true, second should return false), or to run a group's
        /// systems multiple times (return true more than once).
        ///
        /// The group is passed as the first parameter.
        /// </summary>
        public Func<ComponentSystemGroup, bool> UpdateCallback;

        protected override void OnUpdate()
        {
            CheckCreated();

            if (UpdateCallback == null)
            {
                UpdateAllSystems();
            }
            else
            {
                while (UpdateCallback(this))
                {
                    UpdateAllSystems();
                }
            }
        }

        void UpdateAllSystems()
        {
            if (m_systemSortDirty)
                SortSystems();

            // Update all unmanaged and managed systems together, in the correct sort order.
            // The master update list contains indices for both managed and unmanaged systems.
            // Negative values indicate an index in the unmanaged system list.
            // Positive values indicate an index in the managed system list.
            for (int i = 0; i < m_MasterUpdateList.Length; ++i)
            {
                try
                {
                    var index = m_MasterUpdateList[i];

                    if (!index.IsManaged)
                    {
                        // Update unmanaged (burstable) code.
                        SystemState* sys = World.ResolveSystemState(m_UnmanagedSystemsToUpdate[index.Index]);
                        if (sys != null)
                        {
                            if (SystemBase.UnmanagedUpdate(sys, out var details))
                            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                                var metaIndex = sys->m_UnmanagedMetaIndex;
                                var systemDebugName = SystemBaseRegistry.GetDebugName(metaIndex);
                                var errorString = details.FormatToString(systemDebugName);
                                Debug.LogError(errorString);
#endif
                            }
                        }
                    }
                    else
                    {
                        // Update managed code.
                        var sys = m_systemsToUpdate[index.Index];
                        sys.Update();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
#if UNITY_DOTSPLAYER
                    // When in a DOTS Runtime build, throw this upstream -- continuing after silently eating an exception
                    // is not what you'll want, except maybe once we have LiveLink.  If you're looking at this code
                    // because your LiveLink dots runtime build is exiting when you don't want it to, feel free
                    // to remove this block, or guard it with something to indicate the player is not for live link.
                    throw;
#endif
                }

                if (World.QuitUpdate)
                    break;
            }
        }
    }

    public static class ComponentSystemGroupExtensions
    {
        internal static void AddSystemToUpdateList(this ComponentSystemGroup self, SystemRefUntyped sysRef)
        {
            self.AddUnmanagedSystemToUpdateList(sysRef);
        }

        internal static void RemoveSystemFromUpdateList(this ComponentSystemGroup self, SystemRefUntyped sysRef)
        {
            self.RemoveUnmanagedSystemFromUpdateList(sysRef);
        }
    }
}
