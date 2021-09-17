using System.Collections.Generic;
using System.Diagnostics;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities
{
    class EntityDebugProxy
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private Entity _Entity;

        public static string GetDebugName(int index, int version)
        {
            Entity entity = new Entity {Index = index, Version = version};
            return GetDebugName(GetWorld(entity), entity);
        }

        
        public static string GetDebugName (World world, Entity entity)
        {
            if (!entity.Equals(default) && world != null && world.IsCreated)
                return $"'{world.EntityManager.Debug.Debugger_GetName(entity)}' Entity({entity.Index}:{entity.Version}) {world}";
            return entity.ToString();
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

        public object[] Components
        {
            get
            {
                var world = GetWorld(_Entity);
                if (world != null && world.IsCreated)
                    return world.EntityManager.Debug.Debugger_GetComponents(_Entity);
                return null;
            }        
        }
        
        public EntityInWorld[] AllWorlds
        {
            get
            {
                var proxy = new List<EntityInWorld>();
                foreach (var world in World.All)
                {
                    if (world.EntityManager.Debug.Debugger_Exists(_Entity))
                        proxy.Add(new EntityInWorld(world, _Entity));
                }
                return proxy.ToArray();
            }        
        }

        public EntityDebugProxy(Entity entity)
        {
            _Entity = entity;
        }
        
        public class EntityInWorld
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            Entity _Entity;
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            World _World;

            public EntityInWorld(World world, Entity entity)
            {
                _Entity = entity;
                _World = world;
            }

            public override string ToString()
            {
                return EntityDebugProxy.GetDebugName(_World, _Entity);
            }

            public object[] Components
            {
                get
                {
                    if (_World != null && _World.IsCreated)
                        return _World.EntityManager.Debug.Debugger_GetComponents(_Entity);
                    else 
                        return null;
                }        
            }
        }
    }
}
