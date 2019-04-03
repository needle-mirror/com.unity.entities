using System;
#if UNITY_EDITOR
using UnityEngine.Profiling;

#endif

namespace Unity.Entities
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class DisableAutoCreationAttribute : Attribute
    {
    }

    [Flags]
    public enum WorldSystemFilterFlags
    {
        Default                  = 1 << 0,
        GameObjectConversion     = 1 << 1,
        EntitySceneOptimizations = 1 << 2,
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class WorldSystemFilterAttribute : Attribute
    {
        public WorldSystemFilterFlags FilterFlags;

        public WorldSystemFilterAttribute(WorldSystemFilterFlags flags)
        {
            FilterFlags = flags;
        }
    }


    public abstract class ScriptBehaviourManager
    {
#if UNITY_EDITOR
        private CustomSampler m_Sampler;
#endif
        internal void CreateInstance(World world)
        {
            OnBeforeCreateManagerInternal(world);
            try
            {
                OnCreateManager();
#if UNITY_EDITOR
                var type = GetType();
                m_Sampler = CustomSampler.Create($"{world.Name} {type.FullName}");
#endif
            }
            catch
            {
                OnBeforeDestroyManagerInternal();
                OnAfterDestroyManagerInternal();
                throw;
            }
        }

        internal void DestroyInstance()
        {
            OnBeforeDestroyManagerInternal();
            OnDestroyManager();
            OnAfterDestroyManagerInternal();
        }

        protected abstract void OnBeforeCreateManagerInternal(World world);

        protected abstract void OnBeforeDestroyManagerInternal();
        protected abstract void OnAfterDestroyManagerInternal();

        /// <summary>
        ///     Called when the ScriptBehaviourManager is created.
        ///     When a new domain is loaded, OnCreate on the necessary manager will be invoked
        ///     before the ScriptBehaviour will receive its first OnCreate() call.
        /// </summary>
        protected virtual void OnCreateManager()
        {
        }

        /// <summary>
        ///     Called when the ScriptBehaviourManager is destroyed.
        ///     Before Playmode exits or scripts are reloaded OnDestroy will be called on all created ScriptBehaviourManagers.
        /// </summary>
        protected virtual void OnDestroyManager()
        {
        }

        internal abstract void InternalUpdate();

        /// <summary>
        ///     Execute the manager immediately.
        /// </summary>
        public void Update()
        {
#if UNITY_EDITOR
            m_Sampler?.Begin();
#endif
            InternalUpdate();

#if UNITY_EDITOR
            m_Sampler?.End();
#endif
        }
    }
}
