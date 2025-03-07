using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Assertions;
using Unity.Profiling;
using UnityEditor;
using UnityEngine.LowLevel;

namespace Unity.Entities.Editor
{
    class LocalWorldProxyUpdater : IWorldProxyUpdater
    {
        static ProfilerMarker s_UpdateTransientDataFromLocalWorldMarker =
            new ProfilerMarker(ProfilerCategory.Internal, "Update Local World Stats");

        readonly Cooldown m_Cooldown = new Cooldown(TimeSpan.FromMilliseconds(Constants.Inspector.CoolDownTime));
        readonly World m_LocalWorld;
        readonly WorldProxy m_WorldProxy;
        readonly WorldSystemListChangeTracker m_SystemListChangeTracker;
        bool m_IsActive;
        bool m_IsDirty;

        public LocalWorldProxyUpdater(World world, WorldProxy worldProxy)
        {
            m_LocalWorld = world;
            m_WorldProxy = worldProxy;
            m_SystemListChangeTracker = new WorldSystemListChangeTracker(world);
        }

        public bool IsDirty()
        {
            return m_IsDirty;
        }

        public void SetClean()
        {
            m_IsDirty = false;
        }

        public bool IsActive()
        {
            return m_IsActive;
        }

        public void EnableUpdater()
        {
            if (m_IsActive)
                return;

            EditorApplication.update += UpdateWorldProxy;
            m_IsActive = true;
            UpdateFrameData();
        }

        public void DisableUpdater()
        {
            if (!m_IsActive)
                return;

            EditorApplication.update -= UpdateWorldProxy;
            m_IsActive = false;
        }

        void UpdateWorldProxy()
        {
            if (!m_Cooldown.Update(DateTime.Now))
                return;

            if (m_LocalWorld == null || !m_LocalWorld.IsCreated || !m_WorldProxy.AllSystems.Any())
                return;

            UpdateFrameData();

            if (!m_SystemListChangeTracker.HasChanged())
                return;

            ResetWorldProxy();
            m_IsDirty = true;
        }

        public void ResetWorldProxy()
        {
            m_WorldProxy.Clear();
            PopulateWorldProxy();
            UpdateFrameData();
        }

        static void GatherRootSystemsRecursive(in World world, in PlayerLoopSystem playerLoopSystem, ref List<Type> rootSystemTypes)
        {
            if (ScriptBehaviourUpdateOrder.IsDelegateForWorldSystem(world, playerLoopSystem))
            {
                rootSystemTypes.Add(playerLoopSystem.type);
            }
            if (playerLoopSystem.subSystemList != null)
            {
                foreach (var subsystem in playerLoopSystem.subSystemList)
                {
                    GatherRootSystemsRecursive(world, subsystem, ref rootSystemTypes);
                }
            }
        }



        public unsafe void PopulateWorldProxy()
        {
            // Gather all root system types from the current player loop, limiting the search to the current World.
            var rootSystemTypes = new List<Type>();
            GatherRootSystemsRecursive(m_LocalWorld, PlayerLoop.GetCurrentPlayerLoop(), ref rootSystemTypes);

            // Register the root system types. Root system groups are pushed onto a work queue to process in a separate loop below.
            var workQueue = new Queue<GroupSystemInQueue>();
            foreach (var rootType in rootSystemTypes)
            {
                var sysHandle = m_LocalWorld.GetExistingSystem(rootType);
                if (sysHandle == default) // will happen in subset world
                    continue;
                var systemTypeIndex = TypeManager.GetSystemTypeIndex(rootType);
                if (systemTypeIndex.IsManaged)
                {
                    // managed system path
                    var sysState = m_LocalWorld.Unmanaged.ResolveSystemStateChecked(sysHandle);
                    var sysManaged = sysState->ManagedSystem;
                    m_WorldProxy.m_SystemData.Add(new ScheduledSystemData(sysManaged, -1));
                    if (systemTypeIndex.IsGroup)
                    {
                        // A root system always belong to the current world; that's where we just looked it up.
                        var systemProxy = CreateSystemProxy(m_WorldProxy, m_LocalWorld, true);
                        // Push group onto the queue to recursively process its contents below.
                        workQueue.Enqueue(new GroupSystemInQueue { group = (ComponentSystemGroup)sysManaged, index = systemProxy.SystemIndex });
                    }
                }
                else
                {
                    // unmanaged system path
                    m_WorldProxy.m_SystemData.Add(new ScheduledSystemData(sysHandle, m_LocalWorld, -1));
                    // Unmanaged systems can not currently be groups.
                    UnityEngine.Assertions.Assert.IsFalse(systemTypeIndex.IsGroup, "This code path is not expecting unmanaged system groups");
                }
            }

            // Recurse into system groups, making sure that all group children end up in sequential order in resulting list
            while (workQueue.Count > 0)
            {
                var work = workQueue.Dequeue();
                var group = work.group;
                var groupIndex = work.index;
                var firstChildIndex = m_WorldProxy.m_AllSystems.Count;

                ref var updateSystemList = ref group.m_MasterUpdateList;
                var numSystems = updateSystemList.Length;
                var removedSystemCount = 0;
                for (var i = 0; i < numSystems; i++)
                {
                    var updateIndex = updateSystemList[i];

                    ComponentSystemBase system = null;
                    ScheduledSystemData scheduledSystemData;
                    World creationWorld;

                    if (updateIndex.IsManaged)
                    {
                        system = group.ManagedSystems[updateIndex.Index];
                        if (system == null)
                        {
                            removedSystemCount++;
                            continue;
                        }

                        scheduledSystemData = new ScheduledSystemData(system, groupIndex);
                    }
                    else
                    {
                        var unmanagedSystem = group.UnmanagedSystems[updateIndex.Index];
                        if (unmanagedSystem == SystemHandle.Null)
                        {
                            removedSystemCount++;
                            continue;
                        }

                        creationWorld = FindCreationWorld(unmanagedSystem.m_WorldSeqNo);
                        unsafe
                        {
                            if (creationWorld != m_LocalWorld ||
                                creationWorld.Unmanaged.ResolveSystemState(unmanagedSystem) == null)
                            {
                                removedSystemCount++;
                                continue;
                            }
                        }

                        scheduledSystemData = new ScheduledSystemData(unmanagedSystem, creationWorld, groupIndex);
                    }

                    var systemProxy = CreateSystemProxy(m_WorldProxy, updateIndex.IsManaged? system?.World : m_LocalWorld, !updateIndex.IsManaged || system?.World == m_LocalWorld);

                    if (scheduledSystemData.Recorder != null)
                        scheduledSystemData.Recorder.enabled = true;

                    m_WorldProxy.m_SystemData.Add(scheduledSystemData);

                    if (system is ComponentSystemGroup childGroup)
                    {
                        workQueue.Enqueue(new GroupSystemInQueue { group = childGroup, index = systemProxy.SystemIndex });
                    }
                }

                // Now update the system data and group handle with child info.
                var data = m_WorldProxy.m_SystemData[groupIndex];
                data.ChildIndex = firstChildIndex;
                data.ChildCount = numSystems - removedSystemCount;
                m_WorldProxy.m_SystemData[groupIndex] = data;
            }

            // Now go back through each system and update their dependencies
            for (var i = 0; i < m_WorldProxy.m_SystemData.Count; i++)
            {
                var data = m_WorldProxy.m_SystemData[i];
                data.UpdateBeforeIndices = SystemIndicesForDependenciesFromLocalWorld<UpdateBeforeAttribute>(data, m_WorldProxy);
                data.UpdateAfterIndices = SystemIndicesForDependenciesFromLocalWorld<UpdateAfterAttribute>(data, m_WorldProxy);
                m_WorldProxy.m_SystemData[i] = data;
            }

            m_WorldProxy.m_RootSystems.AddRange(m_WorldProxy.m_AllSystems.Where(s => rootSystemTypes.Any(t => s.TypeName == t.Name)));

            m_WorldProxy.OnGraphChange();
        }

        SystemProxy CreateSystemProxy(WorldProxy worldProxy, World world, bool belongToCurrentWorld)
        {
            var thisIndex = worldProxy.m_AllSystems.Count;
            var systemProxy = new SystemProxy(worldProxy, thisIndex, world, belongToCurrentWorld);
            worldProxy.m_AllSystems.Add(systemProxy);
            return systemProxy;
        }

        struct GroupSystemInQueue
        {
            public ComponentSystemGroup group;
            public int index;
        }

        public unsafe void UpdateFrameData()
        {
            using (s_UpdateTransientDataFromLocalWorldMarker.Auto())
            {
                if (m_WorldProxy.m_AllSystems.Count != m_WorldProxy.m_FrameData.Count)
                    return;

                for (var i = 0; i < m_WorldProxy.m_AllSystems.Count; i++)
                {
                    var sys = m_WorldProxy.m_SystemData[i];
                    var state = GetStatePointer(sys);

                    SystemFrameData frameData = default;
                    if (state != null)
                    {
                        frameData.Enabled = state->Enabled;
                        frameData.IsRunning = state->ShouldRunSystem();
                        frameData.EntityCount = ComputeEntityCount(state);

                        if (sys.Recorder != null)
                        {
                            frameData.LastFrameRuntimeMilliseconds = (sys.Recorder.elapsedNanoseconds / 1e6f);
                        }
                    }

                    m_WorldProxy.m_FrameData[i] = frameData;
                }
            }
        }

        unsafe int ComputeEntityCount(SystemState* state)
        {
            var count = 0;
            var entityQueries = state->EntityQueries;
            for (var i = 0; i < entityQueries.Length; i++)
            {
                count += entityQueries[i].CalculateEntityCount();
            }

            return count;
        }

        unsafe SystemState* GetStatePointer(in ScheduledSystemData sys)
        {
            if ((sys.Category & SystemCategory.Unmanaged) == 0)
                return sys.Managed.m_StatePtr;

            var creationWorld = FindCreationWorld(sys.WorldSystemHandle.m_WorldSeqNo);
            if (creationWorld != null && creationWorld.IsCreated)
                return creationWorld.Unmanaged.ResolveSystemState(sys.WorldSystemHandle);

            return null;
        }

        int[] SystemIndicesForDependenciesFromLocalWorld<TAttribute>(in ScheduledSystemData systemData, WorldProxy worldProxy)
            where TAttribute : System.Attribute
        {
            if (systemData.ParentIndex == -1)
                return Array.Empty<int>();

            // Dependencies only matter within the same group
            var parentData = worldProxy.m_SystemData[systemData.ParentIndex];
            var baseChildIndex = parentData.ChildIndex;

            var systemType = GetLocalSystemType(systemData);
            var depTypes = SystemDependencyUtilities.GetSystemAttributes<TAttribute>(systemType);
            var slist = new List<int>();

            // TODO find a faster way to resolve this
            foreach (var depType in depTypes)
            {
                for (var ci = 0; ci < parentData.ChildCount; ci++)
                {
                    if (GetLocalSystemType(worldProxy.m_SystemData[baseChildIndex + ci]) == depType)
                    {
                        slist.Add(baseChildIndex + ci);
                        break;
                    }
                }
            }
            return slist.ToArray();
        }

        //
        // Misc
        //
        static World FindCreationWorld(uint worldSeqNo)
        {
            foreach (var world in World.All)
            {
                if (world.SequenceNumber == worldSeqNo)
                    return world;
            }

            return null;
        }

        static unsafe Type GetLocalSystemType(in ScheduledSystemData sd)
        {
            if (sd.Managed != null)
                return sd.Managed.GetType();

            Assert.IsTrue((sd.Category & SystemCategory.Unmanaged) != 0);
            var world = FindCreationWorld(sd.WorldSystemHandle.m_WorldSeqNo);
            return SystemBaseRegistry.GetStructType(world.Unmanaged.ResolveSystemState(sd.WorldSystemHandle)->UnmanagedMetaIndex);
        }
    }
}
