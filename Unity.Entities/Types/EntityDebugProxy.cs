using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;


namespace Unity.Entities
{
    /// <summary>
    /// EntityDebugProxy for an entity, the world isn't explicitly known, so there is some world disambiguation here.
    /// </summary>
    class EntityDebugProxy
    {
#if !NET_DOTS
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        Entity _Entity;
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        World _World;

        public EntityDebugProxy(Entity entity)
        {
            _Entity = entity;
            _World = GetWorld(entity);
        }

        // Used by Entity DebuggerDisplay
        public static string GetDebugName(int index, int version)
        {
            Entity entity = new Entity {Index = index, Version = version};
            return new DebuggerDataAccess(GetWorld(entity)).GetDebugNameWithWorld(entity);
        }

        public static World GetWorld(Entity entity)
        {
            if (!JobsUtility.IsExecutingJob)
            {
                foreach (var world in World.All)
                {
                    if (world.Unmanaged.ExecutingSystem != default)
                        return world;
                }
            }

            if (World.DefaultGameObjectInjectionWorld != null && World.DefaultGameObjectInjectionWorld.IsCreated)
                return World.DefaultGameObjectInjectionWorld;

            return null;
        }

        public EntityArchetype Archetype => new DebuggerDataAccess(_World).GetArchetype(_Entity);
        public ArchetypeChunk Chunk => new DebuggerDataAccess(_World).GetChunk(_Entity);

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public object[] Components => new DebuggerDataAccess(_World).GetComponents(_Entity);

        public World World => _World;

        public Entity_[] Worlds
        {
            get
            {
                var proxy = new List<Entity_>();
                foreach (var world in World.All)
                {
                    if (new DebuggerDataAccess(world).Exists(_Entity))
                        proxy.Add(new Entity_(world, _Entity, true));
                }
                return proxy.ToArray();
            }
        }
#endif
    }

#if !NET_DOTS
    /// <summary>
    /// Entity debug proxy when the world is explicitly known
    /// </summary>
    unsafe internal struct Entity_
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        Entity _Entity;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        DebuggerDataAccess _Access;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private bool _IncludeWorldName;

        public static Entity_[] ResolveArray(DebuggerDataAccess access, List<Entity> entities)
        {
            var resolved = new Entity_[entities.Count];
            for (int i = 0; i != resolved.Length; i++)
                resolved[i] = new Entity_(access, entities[i], false);
            return resolved;
        }

        public static Entity_ Null
        {
            get
            {
                Entity_ entity;
                entity._Entity = default;
                entity._Access = new DebuggerDataAccess((EntityComponentStore*)null);
                entity._IncludeWorldName = false;
                return entity;
            }
        }

        public Entity_(World world, Entity entity, bool includeWorldName)
            : this(new DebuggerDataAccess(world), entity, includeWorldName) { }

        public Entity_(EntityComponentStore* store, Entity entity, bool includeWorldName)
            : this(new DebuggerDataAccess(store), entity, includeWorldName) { }

        public Entity_(DebuggerDataAccess access, Entity entity, bool includeWorldName)
        {
            _Entity = entity;
            _Access = access;
            _IncludeWorldName = includeWorldName;
        }
        public override string ToString()
        {
            if (_IncludeWorldName)
                return _Access.GetDebugNameWithWorld(_Entity);
            else
                return _Access.GetDebugNameWithoutWorld(_Entity);
        }

        public EntityArchetype Archetype => _Access.GetArchetype(_Entity);
        public ArchetypeChunk Chunk => _Access.GetChunk(_Entity);

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public object[] Components => _Access.GetComponents(_Entity);
        public World World => _Access.GetWorld();
    }
#endif
}
