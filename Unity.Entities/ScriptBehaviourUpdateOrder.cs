using System;
using System.Collections.Generic;
#if !UNITY_DOTSRUNTIME
using UnityEngine.PlayerLoop;
using UnityEngine.LowLevel;
#endif

namespace Unity.Entities
{
    // Updating before or after a system constrains the scheduler ordering of these systems within a ComponentSystemGroup.
    // Both the before & after system must be a members of the same ComponentSystemGroup.
    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Struct, AllowMultiple = true)]
    public class UpdateBeforeAttribute : Attribute
    {
        public UpdateBeforeAttribute(Type systemType)
        {
            if (systemType == null)
                throw new ArgumentNullException(nameof(systemType));

            SystemType = systemType;
        }

        public Type SystemType { get; }
    }

    // Updating before or after a system constrains the scheduler ordering of these systems within a ComponentSystemGroup.
    // Both the before & after system must be a members of the same ComponentSystemGroup.
    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Struct, AllowMultiple = true)]
    public class UpdateAfterAttribute : Attribute
    {
        public UpdateAfterAttribute(Type systemType)
        {
            if (systemType == null)
                throw new ArgumentNullException(nameof(systemType));

            SystemType = systemType;
        }

        public Type SystemType { get; }
    }

    /// <summary>
    /// The specified Type must be a ComponentSystemGroup.
    /// Updating in a group means this system will be automatically updated by the specified ComponentSystemGroup when the group is updated.
    /// The system may order itself relative to other systems in the group with UpdateBefore and UpdateAfter. This ordering takes
    /// effect when the system group is sorted.
    ///
    /// If the optional OrderFirst parameter is set to true, this system will act as if it has an implicit [UpdateBefore] targeting all other
    /// systems in the group that do *not* have OrderFirst=true, but it may still order itself relative to other systems with OrderFirst=true.
    ///
    /// If the optional OrderLast parameter is set to true, this system will act as if it has an implicit [UpdateAfter] targeting all other
    /// systems in the group that do *not* have OrderLast=true, but it may still order itself relative to other systems with OrderLast=true.
    ///
    /// An UpdateInGroup attribute with both OrderFirst=true and OrderLast=true is invalid, and will throw an exception.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Struct)]
    public class UpdateInGroupAttribute : Attribute
    {
        public bool OrderFirst = false;
        public bool OrderLast = false;

        public UpdateInGroupAttribute(Type groupType)
        {
            if (groupType == null)
                throw new ArgumentNullException(nameof(groupType));

            GroupType = groupType;
        }

        public Type GroupType { get; }
    }

#if !UNITY_DOTSRUNTIME
    public static class ScriptBehaviourUpdateOrder
    {
        static bool AppendSystemToPlayerLoopListImpl(ComponentSystemBase system, ref PlayerLoopSystem playerLoop,
            Type playerLoopSystemType)
        {
            if (playerLoop.type == playerLoopSystemType)
            {
                var del = new DummyDelegateWrapper(system);
                int oldListLength = (playerLoop.subSystemList != null) ? playerLoop.subSystemList.Length : 0;
                var newSubsystemList = new PlayerLoopSystem[oldListLength + 1];
                for (var i = 0; i < oldListLength; ++i)
                    newSubsystemList[i] = playerLoop.subSystemList[i];
                newSubsystemList[oldListLength].type = system.GetType();
                newSubsystemList[oldListLength].updateDelegate = del.TriggerUpdate;
                playerLoop.subSystemList = newSubsystemList;
                return true;
            }
            if (playerLoop.subSystemList != null)
            {
                for(int i=0; i<playerLoop.subSystemList.Length; ++i)
                {
                    if (AppendSystemToPlayerLoopListImpl(system, ref playerLoop.subSystemList[i], playerLoopSystemType))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Add an ECS system to a specific point in the Unity player loop, so that it is updated every frame.
        /// </summary>
        /// <remarks>
        /// This function does not change the currently active player loop. If this behavior is desired, it's necessary
        /// to call PlayerLoop.SetPlayerLoop(playerLoop) after the systems have been removed.
        /// </remarks>
        /// <param name="system">The ECS system to add to the player loop.</param>
        /// <param name="playerLoop">Existing player loop to modify (e.g. PlayerLoop.GetCurrentPlayerLoop())</param>
        /// <param name="playerLoopSystemType">The Type of the PlayerLoopSystem subsystem to which the ECS system should be appended.
        /// See the UnityEngine.PlayerLoop namespace for valid values.</param>
        public static void AppendSystemToPlayerLoopList(ComponentSystemBase system, ref PlayerLoopSystem playerLoop, Type playerLoopSystemType)
        {
            if (!AppendSystemToPlayerLoopListImpl(system, ref playerLoop, playerLoopSystemType))
            {
                throw new ArgumentException(
                    $"Could not find PlayerLoopSystem with type={playerLoopSystemType}");
            }
        }

        /// <summary>
        /// Add this World's three default top-level system groups to a PlayerLoopSystem object.
        /// </summary>
        /// <remarks>
        /// This function performs the following modifications to the provided PlayerLoopSystem:
        /// - If an instance of InitializationSystemGroup exists in this World, it is appended to the
        ///   Initialization player loop phase.
        /// - If an instance of SimulationSystemGroup exists in this World, it is appended to the
        ///   Update player loop phase.
        /// - If an instance of PresentationSystemGroup exists in this World, it is appended to the
        ///   PreLateUpdate player loop phase.
        /// If instances of any or all of these system groups don't exist in this World, then no entry is added to the player
        /// loop for that system group.
        ///
        /// This function does not change the currently active player loop. If this behavior is desired, it's necessary
        /// to call PlayerLoop.SetPlayerLoop(playerLoop) after the systems have been removed.
        /// </remarks>
        /// <param name="world">The three top-level system groups from this World will be added to the provided player loop.</param>
        /// <param name="playerLoop">Existing player loop to modify (e.g.  (e.g. PlayerLoop.GetCurrentPlayerLoop())</param>
        public static void AddWorldToPlayerLoop(World world, ref PlayerLoopSystem playerLoop)
        {
            if (world == null)
                return;

            var initGroup = world.GetExistingSystem<InitializationSystemGroup>();
            if (initGroup != null)
                AppendSystemToPlayerLoopList(initGroup, ref playerLoop, typeof(Initialization));

            var simGroup = world.GetExistingSystem<SimulationSystemGroup>();
            if (simGroup != null)
                AppendSystemToPlayerLoopList(simGroup, ref playerLoop, typeof(Update));

            var presGroup = world.GetExistingSystem<PresentationSystemGroup>();
            if (presGroup != null)
                AppendSystemToPlayerLoopList(presGroup, ref playerLoop, typeof(PreLateUpdate));
        }
        /// <summary>
        /// Add this World's three default top-level system groups to the current Unity player loop.
        /// </summary>
        /// <remarks>
        /// This is a convenience wrapper around AddWorldToPlayerLoop() that retrieves the current player loop,
        /// adds a World's top-level system groups to it, and sets the modified copy as the new active player loop.
        ///
        /// Note that modifications to the active player loop do not take effect until to the next iteration through the player loop.
        /// </remarks>
        /// <param name="world">The three top-level system groups from this World will be added to the provided player loop.</param>
        public static void AddWorldToCurrentPlayerLoop(World world)
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            AddWorldToPlayerLoop(world, ref playerLoop);
            PlayerLoop.SetPlayerLoop(playerLoop);
        }

        static bool IsDelegateForWorldSystem(World world, PlayerLoopSystem pls)
        {
            if (typeof(ComponentSystemBase).IsAssignableFrom(pls.type))
            {
                var wrapper = pls.updateDelegate.Target as ScriptBehaviourUpdateOrder.DummyDelegateWrapper;
                if (wrapper.System.World == world)
                {
                    return true;
                }
            }
            return false;
        }

        static bool IsWorldInPlayerLoopSystem(World world, PlayerLoopSystem pls)
        {
            // Is *this* system a delegate for a component system associated with the current World?
            if (IsDelegateForWorldSystem(world, pls))
                return true;
            // How about anything in the subsystem list?
            if (pls.subSystemList != null)
            {
                for (int i = 0; i < pls.subSystemList.Length; ++i)
                {
                    if (IsWorldInPlayerLoopSystem(world, pls.subSystemList[i]))
                        return true;
                }
            }
            // Nope.
            return false;
        }

        /// <summary>
        /// Search the provided player loop for any systems added by this World.
        /// </summary>
        /// <remarks>
        /// Note that systems are not added to the player loop directly; they are wrapped by a DummyDelegate object that
        /// calls the system's Update() method. Any systems added to the loop using other wrapper mechanisms will not
        /// be detected by this function.
        /// </remarks>
        /// <param name="world">The function will search the provided PlayerLoopSystem for systems owned by this World.</param>
        /// <param name="playerLoop">Existing player loop to search (e.g. PlayerLoop.GetCurrentPlayerLoop())</param>
        /// <returns>True if any of this World's systems are found in the provided player loop; otherwise, false.</returns>
        public static bool IsWorldInPlayerLoop(World world, PlayerLoopSystem playerLoop)
        {
            if (world == null)
                return false;
            return IsWorldInPlayerLoopSystem(world, playerLoop);
        }
        /// <summary>
        /// Search the currently active player loop for any systems added by this World.
        /// </summary>
        /// <remarks>
        /// This is a convenience wrapper around IsWorldInPlayerLoop() that always searches the currently active player loop.
        /// </remarks>
        /// <param name="world">The function will search the currently active player loop for systems owned by this World.</param>
        public static bool IsWorldInCurrentPlayerLoop(World world)
        {
            return IsWorldInPlayerLoop(world, PlayerLoop.GetCurrentPlayerLoop());
        }


        static void RemoveWorldFromPlayerLoopSystem(World world, ref PlayerLoopSystem pls)
        {
            if (pls.subSystemList == null || pls.subSystemList.Length == 0)
                return;

            var newSubsystemList = new List<PlayerLoopSystem>(pls.subSystemList.Length);
            for (int i = 0; i < pls.subSystemList.Length; ++i)
            {
                RemoveWorldFromPlayerLoopSystem(world, ref pls.subSystemList[i]);
                if (!IsDelegateForWorldSystem(world, pls.subSystemList[i]))
                    newSubsystemList.Add(pls.subSystemList[i]);
            }

            pls.subSystemList = newSubsystemList.ToArray();
        }

        /// <summary>
        /// Remove all of this World's systems from the specified player loop.
        /// </summary>
        /// <remarks>
        /// Only the systems from this World will be removed; other player loop modifications (including systems added
        /// by other Worlds) will not be affected.
        ///
        /// This function does not change the currently active player loop. If this behavior is desired, it's necessary
        /// to call PlayerLoop.SetPlayerLoop(playerLoop) after the systems have been removed.
        /// </remarks>
        /// <param name="world">All systems in the provided player loop owned by this World will be removed from the player loop.</param>
        /// <param name="playerLoop">Existing player loop to modify (e.g. PlayerLoop.GetCurrentPlayerLoop())</param>
        public static void RemoveWorldFromPlayerLoop(World world, ref PlayerLoopSystem playerLoop)
        {
            if (world != null)
                RemoveWorldFromPlayerLoopSystem(world, ref playerLoop);
        }
        /// <summary>
        /// Remove all of this World's systems from the currently active player loop.
        /// </summary>
        /// <remarks>
        /// This is a convenience wrapper around RemoveWorldToPlayerLoop() that retrieves the current player loop,
        /// removes a World's systems from it, and sets the modified copy as the new active player loop.
        ///
        /// Note that modifications to the active player loop do not take effect until to the next iteration through the player loop.
        /// </remarks>
        /// <param name="world">All systems in the current player loop owned by this World will be removed from the player loop.</param>
        public static void RemoveWorldFromCurrentPlayerLoop(World world)
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveWorldFromPlayerLoopSystem(world, ref playerLoop);
            PlayerLoop.SetPlayerLoop(playerLoop);
        }

        /// <summary>
        /// Update the player loop with a world's root-level systems
        /// </summary>
        /// <param name="world">World with root-level systems that need insertion into the player loop. These root-level systems will be created if they don't already exist.</param>
        /// <param name="existingPlayerLoop">Optional parameter to preserve existing player loops (e.g. PlayerLoop.GetCurrentPlayerLoop())</param>
        [Obsolete("Use AddWorldToPlayerLoop() instead. (RemovedAfter 2020-10-31)")]
        public static void UpdatePlayerLoop(World world, PlayerLoopSystem? existingPlayerLoop = null)
        {
            var playerLoop = existingPlayerLoop ?? PlayerLoop.GetDefaultPlayerLoop();
            if (world != null)
            {
                // The previous implementation of this function created the top-level system groups if they didn't already
                // exist. World.AddToPlayerLoop() does not assume these systems exist, and ignores any that are missing.
                // For compatibility with the previous UpdatePlayerLoop() implementation, we need to add these groups
                // before calling World.AddToPlayerLoop().
                world.GetOrCreateSystem<InitializationSystemGroup>();
                world.GetOrCreateSystem<SimulationSystemGroup>();
                world.GetOrCreateSystem<PresentationSystemGroup>();
                AddWorldToPlayerLoop(world, ref playerLoop);
            }
            PlayerLoop.SetPlayerLoop(playerLoop);
        }

        [Obsolete("Use IsWorldInCurrentPlayerLoop() instead. (RemovedAfter 2020-10-31). (UnityUpgradeable) -> IsWorldInCurrentPlayerLoop(*)", false)]
        public static bool IsWorldInPlayerLoop(World world)
        {
            if (world == null)
                return false;

            return IsWorldInCurrentPlayerLoop(world);
        }

        // FIXME: HACK! - mono 4.6 has problems invoking virtual methods as delegates from native, so wrap the invocation in a non-virtual class
        internal class DummyDelegateWrapper
        {
            internal ComponentSystemBase System => m_System;
            private readonly ComponentSystemBase m_System;

            public DummyDelegateWrapper(ComponentSystemBase sys)
            {
                m_System = sys;
            }

            public unsafe void TriggerUpdate()
            {
                if (m_System.m_StatePtr != null)
                {
                    m_System.Update();
                }
            }
        }
    }
#endif
}
