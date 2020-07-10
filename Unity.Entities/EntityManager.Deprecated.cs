using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Unity.Entities
{
    public unsafe partial struct EntityManager
    {
        // Workaround for EntityManager.IsCreated while we wait for deprecation
        internal static class DeprecatedRegistry
        {
            static private readonly object s_Lock = new object();

            struct Cell
            {
                public World World;
                public uint WorldId;
            }

            static private readonly Dictionary<IntPtr, Cell> s_Mapping = new Dictionary<IntPtr, Cell>();

            public static bool IsAlive(EntityManager manager)
            {
                var key = new IntPtr(manager.m_EntityDataAccess);
                lock (s_Lock)
                {
                    if (!s_Mapping.TryGetValue(key, out var cell))
                        return false;

                    return cell.World.IsCreated && cell.World.m_WorldId == cell.WorldId;
                }
            }

            public static void Register(EntityManager manager, World w)
            {
                lock (s_Lock)
                {
                    s_Mapping[new IntPtr(manager.m_EntityDataAccess)] = new Cell { World = w, WorldId = w.m_WorldId };
                }
            }

            public static void Unregister(EntityManager manager)
            {
                lock (s_Lock)
                {
                    s_Mapping.Remove(new IntPtr(manager.m_EntityDataAccess));
                }
            }
        }

        /// <summary>
        /// Reports whether the EntityManager has been initialized yet.
        /// </summary>
        /// <value>True, if the EntityManager's OnCreateManager() function has finished.</value>
        [Obsolete("Use World.IsCreated. EntityManager lifetimes are tied to the world they came from. (RemoveAfter 2020-08-01)")]
        public bool IsCreated => DeprecatedRegistry.IsAlive(this);

        // Temporarily allow conversion from null reference to allow existing packages to compile.
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("EntityManager is a struct. Please use `default` instead of `null`. (RemovedAfter 2020-07-01)")]
        public static implicit operator EntityManager(EntityManagerNullShim? shim) => default(EntityManager);

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("This is slow. Use The EntityDataAccess directly in new code. (RemovedAfter 2020-08-04)")]
        internal EntityComponentStore* EntityComponentStore => GetCheckedEntityDataAccess()->EntityComponentStore;

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("This is slow. Use The EntityDataAccess directly in new code. (RemovedAfter 2020-08-04)")]
        internal ComponentSafetyHandles* SafetyHandles => &GetCheckedEntityDataAccess()->DependencyManager->Safety;
        #endif

    }
}
