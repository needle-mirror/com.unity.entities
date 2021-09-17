using System;
using Unity.Properties.UI;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// Context to be used when inspecting an <see cref="Entity"/>.
    /// </summary>
    class EntityInspectorContext : InspectionContext
    {
        const string k_InvalidEntityName = "{ Invalid Entity }";

        const WorldFlags ReadonlyFlags = WorldFlags.Conversion
                                         | WorldFlags.Shadow
                                         | WorldFlags.Staging
                                         | WorldFlags.Streaming;

        internal World World { get; private set; }
        internal EntityContainer EntityContainer { get; private set; }

        internal EntityManager EntityManager => EntityContainer.EntityManager;
        internal Entity Entity => EntityContainer.Entity;
        internal bool IsReadOnly => EntityContainer.IsReadOnly;

        internal void SetContext(EntitySelectionProxy proxy)
        {
            World = proxy.World;
            var isReadonly = !EditorApplication.isPlaying || IsWorldReadOnly(World);
            EntityContainer = new EntityContainer(World.EntityManager, proxy.Entity, isReadonly);
        }

        internal bool TargetExists()
        {
            return World.IsCreated && World.EntityManager.SafeExists(Entity);
        }

        internal string GetTargetName()
        {
            if (!TargetExists())
                return k_InvalidEntityName;

            var name = EntityManager.GetName(Entity);
            return string.IsNullOrEmpty(name) ? $"Entity {{{Entity.Index}:{Entity.Version}}}" : name;
        }

        internal void SetTargetName(string name)
        {
            if (!TargetExists())
                return;

            EntityManager.SetName(Entity, name);
        }

        internal UnityEngine.Object GetSourceObject()
            => this.TryGetComponentData(out EntityGuid guid) ? EditorUtility.InstanceIDToObject(guid.OriginatingId) : null;

        static bool IsWorldReadOnly(World world)
        {
            return (world.Flags & ReadonlyFlags) != WorldFlags.None;
        }
    }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
    static class EntityInspectorContextClassExtensions
    {
        public static bool TryGetComponentData<T>(this EntityInspectorContext context, out T component)
            where T : class, IComponentData
        {
            if (!context.TargetExists() || !context.EntityManager.HasComponent<T>(context.Entity))
            {
                component = default;
                return false;
            }

            component = context.EntityManager.GetComponentData<T>(context.Entity);
            return true;
        }

        public static bool TryGetChunkComponentData<T>(this EntityInspectorContext context, out T component)
            where T : class, IComponentData
        {
            if (!context.TargetExists() || !context.EntityManager.HasComponent<T>(context.Entity))
            {
                component = default;
                return false;
            }

            component = context.EntityManager.GetChunkComponentData<T>(context.Entity);
            return true;
        }
    }
#endif

    static class EntityInspectorContextStructExtensions
    {
        public static bool TryGetComponentData<T>(this EntityInspectorContext context, out T component)
            where T : struct, IComponentData
        {
            if (!context.TargetExists() || !context.EntityManager.HasComponent<T>(context.Entity))
            {
                component = default;
                return false;
            }

            component = context.EntityManager.GetComponentData<T>(context.Entity);
            return true;
        }

        public static bool TryGetChunkComponentData<T>(this EntityInspectorContext context, out T component)
            where T : struct, IComponentData
        {
            if (!context.TargetExists() || !context.EntityManager.HasComponent<T>(context.Entity))
            {
                component = default;
                return false;
            }

            component = context.EntityManager.GetChunkComponentData<T>(context.Entity);
            return true;
        }
    }
}
