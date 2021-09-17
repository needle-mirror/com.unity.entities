using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
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

        override public string ToString()
        {
            return IsManaged ? "Managed: Index " + Index : "UnManaged: Index " + Index;
        }
    }

    [DebuggerTypeProxy(typeof(ComponentSystemGroupDebugView))]
    public unsafe abstract class ComponentSystemGroup : ComponentSystem
    {
        private bool m_systemSortDirty = false;

        // If true (the default), calling SortSystems() will sort the system update list, respecting the constraints
        // imposed by [UpdateBefore] and [UpdateAfter] attributes. SortSystems() is called automatically during
        // DefaultWorldInitialization, as well as at the beginning of ComponentSystemGroup.OnUpdate(), but may also be
        // called manually.
        //
        // If false, calls to SortSystems() on this system group will have no effect on update order of systems in this
        // group (though SortSystems() will still be called recursively on any child system groups). The group's systems
        // will update in the order of the most recent sort operation, with any newly-added systems updating in
        // insertion order at the end of the list.
        //
        // Setting this value to false is not recommended unless you know exactly what you're doing, and you have full
        // control over the systems which will be updated in this group.
        private bool m_EnableSystemSorting = true;
        public bool EnableSystemSorting
        {
            get => m_EnableSystemSorting;
            protected set
            {
                if (value && !m_EnableSystemSorting)
                    m_systemSortDirty = true; // force a sort after re-enabling sorting
                m_EnableSystemSorting = value;
            }
        }

        public bool Created { get; private set; } = false;

        internal List<ComponentSystemBase> m_systemsToUpdate = new List<ComponentSystemBase>();
        internal List<ComponentSystemBase> m_systemsToRemove = new List<ComponentSystemBase>();

        internal UnsafeList<UpdateIndex> m_MasterUpdateList;
        internal UnsafeList<SystemHandleUntyped> m_UnmanagedSystemsToUpdate;
        internal UnsafeList<SystemHandleUntyped> m_UnmanagedSystemsToRemove;

        internal delegate bool UnmanagedUpdateSignature(IntPtr pSystemState, out SystemDependencySafetyUtility.SafetyErrorDetails errorDetails);
        static UnmanagedUpdateSignature s_UnmanagedUpdateFn = BurstCompiler.CompileFunctionPointer<UnmanagedUpdateSignature>(SystemBase.UnmanagedUpdate).Invoke;

        public virtual IReadOnlyList<ComponentSystemBase> Systems => m_systemsToUpdate;
        internal UnsafeList<SystemHandleUntyped> UnmanagedSystems => m_UnmanagedSystemsToUpdate;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_UnmanagedSystemsToUpdate = new UnsafeList<SystemHandleUntyped>(0, Allocator.Persistent);
            m_UnmanagedSystemsToRemove = new UnsafeList<SystemHandleUntyped>(0, Allocator.Persistent);
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
                {
                    if (m_systemsToRemove.Contains(sys))
                    {
                        m_systemsToRemove.Remove(sys);
                    }
                    return;
                }

                m_MasterUpdateList.Add(new UpdateIndex(m_systemsToUpdate.Count, true));
                m_systemsToUpdate.Add(sys);
                m_systemSortDirty = true;
            }
        }

        private int UnmanagedSystemIndex(SystemHandleUntyped sysHandle)
        {
            int len = m_UnmanagedSystemsToUpdate.Length;
            var ptr = m_UnmanagedSystemsToUpdate.Ptr;
            for (int i = 0; i < len; ++i)
            {
                if (ptr[i] == sysHandle)
                {
                    return i;
                }
            }
            return -1;
        }

        public void AddUnmanagedSystemToUpdateList(SystemHandleUntyped sysHandle)
        {
            CheckCreated();

            if (-1 != UnmanagedSystemIndex(sysHandle))
            {
                int index = m_UnmanagedSystemsToRemove.IndexOf(sysHandle);
                if (-1 != index)
                    m_UnmanagedSystemsToRemove.RemoveAt(index);
                return;
            }

            m_MasterUpdateList.Add(new UpdateIndex(m_UnmanagedSystemsToUpdate.Length, false));
            m_UnmanagedSystemsToUpdate.Add(sysHandle);
            m_systemSortDirty = true;
        }

        public void RemoveSystemFromUpdateList(ComponentSystemBase sys)
        {
            CheckCreated();

            if (m_systemsToUpdate.Contains(sys) && !m_systemsToRemove.Contains(sys))
            {
                m_systemSortDirty = true;
                m_systemsToRemove.Add(sys);
            }
        }

        internal void RemoveUnmanagedSystemFromUpdateList(SystemHandleUntyped sys)
        {
            CheckCreated();

            if (m_UnmanagedSystemsToUpdate.Contains(sys) && !m_UnmanagedSystemsToRemove.Contains(sys))
            {
                m_systemSortDirty = true;
                m_UnmanagedSystemsToRemove.Add(sys);
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

            for(int i=0; i<m_UnmanagedSystemsToRemove.Length; ++i)
            {
                var sysHandle = m_UnmanagedSystemsToRemove[i];
                m_UnmanagedSystemsToUpdate.RemoveAt(m_UnmanagedSystemsToUpdate.IndexOf(sysHandle));
            }

            m_UnmanagedSystemsToRemove.Clear();
        }


        private void RemoveSystemsFromUnsortedUpdateList()
        {
            if (m_systemsToRemove.Count <= 0 && m_UnmanagedSystemsToRemove.Length <= 0)
                return;

            var world = World.Unmanaged;
            int largestID = 0;

            //determine the size of the lookup table used for looking up system information; whether a system is due to be removed
            //and/or the new update index of the system
            foreach (var managedSystem in m_systemsToUpdate)
            {
                largestID = math.max(largestID, managedSystem.CheckedState()->m_SystemID);
            }
            foreach (var unmanagedSystem in m_UnmanagedSystemsToUpdate)
            {
                largestID = math.max(largestID, world.ResolveSystemState(unmanagedSystem)->m_SystemID);
            }

            var newListIndices = new NativeArray<int>(largestID + 1, Allocator.Temp);
            var systemIsRemoved = new NativeArray<byte>(largestID + 1, Allocator.Temp,NativeArrayOptions.ClearMemory);

            //update removed system lookup table
            foreach (var managedSystem in m_systemsToRemove)
            {
                systemIsRemoved[managedSystem.CheckedState()->m_SystemID] = 1;
            }

            foreach (var unmanagedSystem in m_UnmanagedSystemsToRemove)
            {
                systemIsRemoved[world.ResolveSystemState(unmanagedSystem)->m_SystemID] = 1;
            }

            var newManagedUpdateList = new List<ComponentSystemBase>(m_systemsToUpdate.Count);
            var newUnmanagedUpdateList = new UnsafeList<SystemHandleUntyped>(m_UnmanagedSystemsToUpdate.Length,Allocator.Persistent);

            //use removed lookup table to determine which systems will be in the new update
            foreach (var managedSystem in m_systemsToUpdate)
            {
                var systemID = managedSystem.CheckedState()->m_SystemID;
                if (systemIsRemoved[systemID] == 0)
                {
                    //the new update index will be based on the position in the systems list
                    newListIndices[systemID] = newManagedUpdateList.Count;
                    newManagedUpdateList.Add(managedSystem);
                }
            }

            foreach (var unmanagedSystem in m_UnmanagedSystemsToUpdate)
            {
                var systemID = world.ResolveSystemState(unmanagedSystem)->m_SystemID;
                if (systemIsRemoved[systemID] == 0)
                {
                    newListIndices[systemID] = newUnmanagedUpdateList.Length;
                    newUnmanagedUpdateList.Add(unmanagedSystem);
                }
            }

            var newMasterUpdateList = new UnsafeList<UpdateIndex>(newManagedUpdateList.Count + newUnmanagedUpdateList.Length,Allocator.Persistent);

            foreach (var updateIndex in m_MasterUpdateList)
            {
                if (updateIndex.IsManaged)
                {
                    var system = m_systemsToUpdate[updateIndex.Index];
                    var systemID = system.CheckedState()->m_SystemID;
                    //use the two lookup tables to determine if and where the new master update list entries go
                    if (systemIsRemoved[systemID] == 0)
                    {
                        newMasterUpdateList.Add(new UpdateIndex(newListIndices[systemID], true));
                    }
                }
                else
                {
                    var system = m_UnmanagedSystemsToUpdate[updateIndex.Index];
                    var systemID = world.ResolveSystemState(system)->m_SystemID;
                    if (systemIsRemoved[systemID] == 0)
                    {
                        newMasterUpdateList.Add(new UpdateIndex(newListIndices[systemID], false));
                    }
                }
            }

            newListIndices.Dispose();
            systemIsRemoved.Dispose();

            m_systemsToUpdate = newManagedUpdateList;
            m_systemsToRemove.Clear();

            m_UnmanagedSystemsToUpdate.Dispose();
            m_UnmanagedSystemsToUpdate = newUnmanagedUpdateList;
            m_UnmanagedSystemsToRemove.Clear();

            m_MasterUpdateList.Dispose();
            m_MasterUpdateList = newMasterUpdateList;
        }

        private void RecurseUpdate()
        {
            if (!EnableSystemSorting)
            {
                RemoveSystemsFromUnsortedUpdateList();
            }
            else if (m_systemSortDirty)
            {
                GenerateMasterUpdateList();
            }
            m_systemSortDirty = false;

            foreach (var sys in m_systemsToUpdate)
            {
                if (TypeManager.IsSystemAGroup(sys.GetType()))
                {
                    var childGroup = sys as ComponentSystemGroup;
                    childGroup.RecurseUpdate();
                }
            }
        }

        private void GenerateMasterUpdateList()
        {
            RemovePending();

            var groupType = GetType();
            var allElems = new ComponentSystemSorter.SystemElement[m_systemsToUpdate.Count + m_UnmanagedSystemsToUpdate.Length];
            var systemsPerBucket = new int[3];
            for (int i = 0; i < m_systemsToUpdate.Count; ++i)
            {
                var system = m_systemsToUpdate[i];
                var sysType = system.GetType();
                int orderingBucket = ComputeSystemOrdering(sysType, groupType);
                allElems[i] = new ComponentSystemSorter.SystemElement
                {
                    Type = sysType,
                    Index = new UpdateIndex(i, true),
                    OrderingBucket = orderingBucket,
                    updateBefore = new List<Type>(),
                    nAfter = 0,
                };
                systemsPerBucket[orderingBucket]++;
            }
            for (int i = 0; i < m_UnmanagedSystemsToUpdate.Length; ++i)
            {
                var sysType = World.Unmanaged.GetTypeOfSystem(m_UnmanagedSystemsToUpdate[i]);
                int orderingBucket = ComputeSystemOrdering(sysType, groupType);
                allElems[m_systemsToUpdate.Count + i] = new ComponentSystemSorter.SystemElement
                {
                    Type = sysType,
                    Index = new UpdateIndex(i, false),
                    OrderingBucket = orderingBucket,
                    updateBefore = new List<Type>(),
                    nAfter = 0,
                };
                systemsPerBucket[orderingBucket]++;
            }

            // Find & validate constraints between systems in the group
            ComponentSystemSorter.FindConstraints(groupType, allElems);

            // Build three lists of systems
            var elemBuckets = new []
            {
                new ComponentSystemSorter.SystemElement[systemsPerBucket[0]],
                new ComponentSystemSorter.SystemElement[systemsPerBucket[1]],
                new ComponentSystemSorter.SystemElement[systemsPerBucket[2]],
            };
            var nextBucketIndex = new int[3];

            for(int i=0; i<allElems.Length; ++i)
            {
                int bucket = allElems[i].OrderingBucket;
                int index = nextBucketIndex[bucket]++;
                elemBuckets[bucket][index] = allElems[i];
            }
            // Perform the sort for each bucket.
            for (int i = 0; i < 3; ++i)
            {
                if (elemBuckets[i].Length > 0)
                {
                    ComponentSystemSorter.Sort(elemBuckets[i]);
                }
            }

            // Because people can freely look at the list of managed systems, we need to put that part of list in order.
            var oldSystems = m_systemsToUpdate;
            m_systemsToUpdate = new List<ComponentSystemBase>(oldSystems.Count);
            for (int i = 0; i < 3; ++i)
            {
                foreach (var e in elemBuckets[i])
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
            m_MasterUpdateList.SetCapacity(allElems.Length);

            // Append buckets in order, but replace managed indices with incrementing indices
            // into the newly sorted m_systemsToUpdate list
            int managedIndex = 0;
            for (int i = 0; i < 3; ++i)
            {
                foreach (var e in elemBuckets[i])
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
        }

        internal static int ComputeSystemOrdering(Type sysType, Type ourType)
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

            RecurseUpdate();
        }

#if UNITY_DOTSRUNTIME
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

                if (!sys.m_StatePtr->PreviouslyEnabled)
                    continue;

                sys.m_StatePtr->PreviouslyEnabled = false;
                sys.OnStopRunningInternal();
            }

            for (int i = 0; i < m_UnmanagedSystemsToUpdate.Length; ++i)
            {
                var sys = World.Unmanaged.ResolveSystemState(m_UnmanagedSystemsToUpdate[i]);

                if (sys == null || !sys->PreviouslyEnabled)
                    continue;

                sys->PreviouslyEnabled = false;

                // Optional callback here
            }
        }

        [Obsolete("This property has been renamed to RateManager. (RemovedAfter DOTS 1.0)")]
        public IRateManager FixedRateManager
        {
            get => RateManager;
            set => RateManager = value;
        }

        public IRateManager RateManager { get; set; }

        protected override void OnUpdate()
        {
            CheckCreated();

            if (RateManager == null)
            {
                UpdateAllSystems();
            }
            else
            {
                while (RateManager.ShouldGroupUpdate(this))
                {
                    UpdateAllSystems();
                }
            }
        }

        public static void UpdateSystem(ref WorldUnmanaged world, SystemHandleUntyped sh)
        {
            SystemState* sys = world.ResolveSystemState(sh);

            if (sys != null)
            {
                bool updateError = false;
                updateError = s_UnmanagedUpdateFn((IntPtr) sys, out var details);

                if (updateError)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    var errorString = details.FormatToString();
                    Debug.LogError(errorString);
#endif
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
            var world = World.Unmanaged;
            var previouslyExecutingSystem = world.ExecutingSystem;
            // Cache the update list length before updating; any new systems added mid-loop will change the length and
            // should not be processed until the subsequent group update, to give SortSystems() a chance to run.
            int updateListLength = m_MasterUpdateList.Length;
            for (int i = 0; i < updateListLength; ++i)
            {
                try
                {
                    var index = m_MasterUpdateList[i];

                    if (!index.IsManaged)
                    {
                        // Update unmanaged (burstable) code.
                        var handle = m_UnmanagedSystemsToUpdate[index.Index];
                        world.ExecutingSystem = handle;
                        UpdateSystem(ref world, m_UnmanagedSystemsToUpdate[index.Index]);
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
#if UNITY_DOTSRUNTIME
                    // When in a DOTS Runtime build, throw this upstream -- continuing after silently eating an exception
                    // is not what you'll want, except maybe once we have LiveLink.  If you're looking at this code
                    // because your LiveLink dots runtime build is exiting when you don't want it to, feel free
                    // to remove this block, or guard it with something to indicate the player is not for live link.
                    throw;
#endif
                }
                finally
                {
                    world.ExecutingSystem = previouslyExecutingSystem;
                }

                if (World.QuitUpdate)
                    break;
            }

            World.Unmanaged.DestroyPendingSystems();
        }
    }

    public static class ComponentSystemGroupExtensions
    {
        internal static void AddSystemToUpdateList(this ComponentSystemGroup self, SystemHandleUntyped sysHandle)
        {
            self.AddUnmanagedSystemToUpdateList(sysHandle);
        }

        internal static void RemoveSystemFromUpdateList(this ComponentSystemGroup self, SystemHandleUntyped sysHandle)
        {
            self.RemoveUnmanagedSystemFromUpdateList(sysHandle);
        }
    }
}
