using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace Unity.Entities
{
    /// <summary>
    /// Specify all traits a <see cref="World"/> can have.
    /// </summary>
    [Flags]
    public enum WorldFlags : byte
    {
        /// <summary>
        /// Default WorldFlags value.
        /// </summary>
        None       = 0,

        /// <summary>
        /// The main <see cref="World"/> for a game/application.
        /// This flag is combined with <see cref="Editor"/>, <see cref="Game"/> and <see cref="Simulation"/>.
        /// </summary>
        Live       = 1,

        /// <summary>
        /// Main <see cref="Live"/> <see cref="World"/> running in the Editor.
        /// </summary>
        Editor     = 1 << 1 | Live,

        /// <summary>
        /// Main <see cref="Live"/> <see cref="World"/> running in the Player.
        /// </summary>
        Game       = 1 << 2 | Live,

        /// <summary>
        /// Any additional <see cref="Live"/> <see cref="World"/> running in the application for background processes that
        /// queue up data for other <see cref="Live"/> <see cref="World"/> (ie. physics, AI simulation, networking, etc.).
        /// </summary>
        Simulation = 1 << 3 | Live,

        /// <summary>
        /// <see cref="World"/> on which conversion systems run to transform authoring data to runtime data.
        /// </summary>
        Conversion = 1 << 4,

        /// <summary>
        /// <see cref="World"/> in which temporary results are staged before being moved into a <see cref="Live"/> <see cref="World"/>.
        /// Typically combined with <see cref="Conversion"/> to represent an intermediate step in the full conversion process.
        /// </summary>
        Staging    = 1 << 5,

        /// <summary>
        /// <see cref="World"/> representing a previous state of another <see cref="World"/> typically to compute
        /// a diff of runtime data - for example useful for undo/redo or Live Link.
        /// </summary>
        Shadow     = 1 << 6,

        /// <summary>
        /// Dedicated <see cref="World"/> for managing incoming streamed data to the Player.
        /// </summary>
        Streaming  = 1 << 7,
    }

    /// <summary>
    /// When entering playmode or the game starts in the Player a default world is created.
    /// Sometimes you need multiple worlds to be setup when the game starts or perform some
    /// custom world initialization. This lets you override the bootstrap of game code world creation.
    /// </summary>
    public interface ICustomBootstrap
    {
        // Returns true if the bootstrap has performed initialization.
        // Returns false if default world initialization should be performed.
        bool Initialize(string defaultWorldName);
    }

    [DebuggerDisplay("{Name} - {Flags} (#{SequenceNumber})")]
    public partial class World : IDisposable
    {
        internal static readonly List<World> s_AllWorlds = new List<World>();

        public static World DefaultGameObjectInjectionWorld { get; set; }

        Dictionary<Type, ComponentSystemBase> m_SystemLookup = new Dictionary<Type, ComponentSystemBase>();
        public static NoAllocReadOnlyCollection<World> All { get; } = new NoAllocReadOnlyCollection<World>(s_AllWorlds);

        List<ComponentSystemBase> m_Systems = new List<ComponentSystemBase>();
        public NoAllocReadOnlyCollection<ComponentSystemBase> Systems { get; }

        private WorldUnmanaged m_Unmanaged;

        /// <summary>
        /// Gives access to the unmanaged data of an instance of the World. This is useful when your code needs to run
        /// with Burst.
        /// </summary>
        public WorldUnmanaged Unmanaged => m_Unmanaged;

        public WorldFlags Flags => m_Unmanaged.Flags;

        public string Name { get; }

        public override string ToString()
        {
            return Name;
        }

        public int Version => m_Unmanaged.Version;

        public EntityManager EntityManager => m_Unmanaged.EntityManager;

        public bool IsCreated => m_Systems != null;

        public ulong SequenceNumber => m_Unmanaged.SequenceNumber;

        public ref TimeData Time => ref m_Unmanaged.CurrentTime;

        public float MaximumDeltaTime
        {
            get => m_Unmanaged.MaximumDeltaTime;
            set => m_Unmanaged.MaximumDeltaTime = value;
        }

        private EntityQuery m_TimeSingletonQuery;

        public World(string name, WorldFlags flags = WorldFlags.Simulation)
        {
            m_Unmanaged.Create(this, flags);
            Systems = new NoAllocReadOnlyCollection<ComponentSystemBase>(m_Systems);

            // Debug.LogError("Create World "+ name + " - " + GetHashCode());
            Name = name;
            s_AllWorlds.Add(this);

            m_TimeSingletonQuery = EntityManager.CreateEntityQuery(ComponentType.ReadWrite<WorldTime>(),
                ComponentType.ReadWrite<WorldTimeQueue>());
        }

        public void Dispose()
        {
            if (!IsCreated)
                throw new ArgumentException("The World has already been Disposed.");
            // Debug.LogError("Dispose World "+ Name + " - " + GetHashCode());

            m_Unmanaged.EntityManager.PreDisposeCheck();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Unmanaged.DisallowGetSystem();
#endif

            DestroyAllSystemsAndLogException();
            m_Unmanaged.DestroyAllUnmanagedSystemsAndLogException();

            s_AllWorlds.Remove(this);

            m_SystemLookup.Clear();
            m_SystemLookup = null;

            if (DefaultGameObjectInjectionWorld == this)
                DefaultGameObjectInjectionWorld = null;

            m_Unmanaged.Dispose();
        }

        public static void DisposeAllWorlds()
        {
            while (s_AllWorlds.Count != 0)
            {
                s_AllWorlds[0].Dispose();
            }
        }

        // Time management

        protected Entity TimeSingleton
        {
            get
            {
                if (m_TimeSingletonQuery.IsEmptyIgnoreFilter)
                {
        #if UNITY_EDITOR
                    var entity = EntityManager.CreateEntity(typeof(WorldTime), typeof(WorldTimeQueue));
                    EntityManager.SetName(entity , "WorldTime");
        #else
                    EntityManager.CreateEntity(typeof(WorldTime), typeof(WorldTimeQueue));
        #endif
                }

                return m_TimeSingletonQuery.GetSingletonEntity();
            }
        }

        public void SetTime(TimeData newTimeData)
        {
            EntityManager.SetComponentData(TimeSingleton, new WorldTime() {Time = newTimeData});
            this.Time = newTimeData;
        }

        public void PushTime(TimeData newTimeData)
        {
            var queue = EntityManager.GetBuffer<WorldTimeQueue>(TimeSingleton);
            queue.Add(new WorldTimeQueue() { Time = this.Time });
            SetTime(newTimeData);
        }

        public void PopTime()
        {
            var queue = EntityManager.GetBuffer<WorldTimeQueue>(TimeSingleton);

            Assert.IsTrue(queue.Length > 0, "PopTime without a matching PushTime");

            var prevTime = queue[queue.Length - 1];
            queue.RemoveAt(queue.Length - 1);
            SetTime(prevTime.Time);
        }

        // Internal system management

        ComponentSystemBase CreateSystemInternal(Type type)
        {
            var system = AllocateSystemInternal(type);
            AddSystem_Add_Internal(system);
            AddSystem_OnCreate_Internal(system);
            return system;
        }

        ComponentSystemBase AllocateSystemInternal(Type type)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!m_Unmanaged.AllowGetSystem)
                throw new ArgumentException(
                    "During destruction of a system you are not allowed to create more systems.");
#endif
            return TypeManager.ConstructSystem(type);
        }

        ComponentSystemBase GetExistingSystemInternal(Type type)
        {
            ComponentSystemBase system;
            if (m_SystemLookup.TryGetValue(type, out system))
                return system;

            return null;
        }

        void AddTypeLookupInternal(Type type, ComponentSystemBase system)
        {
            while (type != typeof(ComponentSystemBase))
            {
                if (!m_SystemLookup.ContainsKey(type))
                    m_SystemLookup.Add(type, system);

                type = type.BaseType;
            }
        }

        void AddSystem_Add_Internal(ComponentSystemBase system)
        {
            m_Systems.Add(system);
            AddTypeLookupInternal(system.GetType(), system);
        }

        void AddSystem_OnCreate_Internal(ComponentSystemBase system)
        {
            try
            {
                unsafe
                {
                    var statePtr = m_Unmanaged.AllocateSystemStateForManagedSystem(this, system);
                    system.CreateInstance(this, statePtr);
                }
            }
            catch
            {
                RemoveSystemInternal(system);
                throw;
            }
            m_Unmanaged.BumpVersion();
        }

        void RemoveSystemInternal(ComponentSystemBase system)
        {
            if (!m_Systems.Remove(system))
                throw new ArgumentException($"System does not exist in the world");
            m_Unmanaged.BumpVersion();

            var type = system.GetType();
            while (type != typeof(ComponentSystemBase))
            {
                if (m_SystemLookup[type] == system)
                {
                    m_SystemLookup.Remove(type);

                    foreach (var otherSystem in m_Systems)
                        // Equivalent to otherSystem.isSubClassOf(type) but compatible with NET_DOTS
                        if (type != otherSystem.GetType() && type.IsAssignableFrom(otherSystem.GetType()))
                            AddTypeLookupInternal(otherSystem.GetType(), otherSystem);
                }

                type = type.BaseType;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckGetOrCreateSystem()
        {
            if (!IsCreated)
            {
                throw new ArgumentException("The World has already been Disposed.");
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!m_Unmanaged.AllowGetSystem)
            {
                throw new ArgumentException("You are not allowed to get or create more systems during destruction of a system.");
            }
#endif
        }

        // Public system management

        public T GetOrCreateSystem<T>() where T : ComponentSystemBase
        {
            CheckGetOrCreateSystem();

            var system = GetExistingSystemInternal(typeof(T));
            return (T)(system ?? CreateSystemInternal(typeof(T)));
        }

        public ComponentSystemBase GetOrCreateSystem(Type type)
        {
            CheckGetOrCreateSystem();

            var system = GetExistingSystemInternal(type);
            return system ?? CreateSystemInternal(type);
        }

        public T CreateSystem<T>() where T : ComponentSystemBase, new()
        {
            CheckGetOrCreateSystem();

            return (T)CreateSystemInternal(typeof(T));
        }

        public ComponentSystemBase CreateSystem(Type type)
        {
            CheckGetOrCreateSystem();

            return CreateSystemInternal(type);
        }

        public T AddSystem<T>(T system) where T : ComponentSystemBase
        {
            CheckGetOrCreateSystem();
            if (GetExistingSystemInternal(system.GetType()) != null)
                throw new Exception($"Attempting to add system '{TypeManager.GetSystemName(system.GetType())}' which has already been added to world '{Name}'");

            AddSystem_Add_Internal(system);
            AddSystem_OnCreate_Internal(system);
            return system;
        }

        public T GetExistingSystem<T>() where T : ComponentSystemBase
        {
            CheckGetOrCreateSystem();

            return (T)GetExistingSystemInternal(typeof(T));
        }

        public ComponentSystemBase GetExistingSystem(Type type)
        {
            CheckGetOrCreateSystem();

            return GetExistingSystemInternal(type);
        }

        public void DestroySystem(ComponentSystemBase system)
        {
            CheckGetOrCreateSystem();

            RemoveSystemInternal(system);
            system.DestroyInstance();
        }

        public void DestroyAllSystemsAndLogException()
        {
            if (m_Systems == null)
                return;

            // Systems are destroyed in reverse order from construction, in three phases:
            // 1. Stop all systems from running (if they weren't already stopped), to ensure OnStopRunning() is called.
            // 2. Call each system's OnDestroy() method
            // 3. Actually destroy each system
            for (int i = m_Systems.Count - 1; i >= 0; --i)
            {
                try
                {
                    m_Systems[i].OnBeforeDestroyInternal();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            for (int i = m_Systems.Count - 1; i >= 0; --i)
            {
                try
                {
                    m_Systems[i].OnDestroy_Internal();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            for (int i = m_Systems.Count - 1; i >= 0; --i)
            {
                try
                {
                    m_Systems[i].OnAfterDestroyInternal();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            m_Systems.Clear();
            m_Systems = null;
        }

        internal ComponentSystemBase[] GetOrCreateSystemsAndLogException(IEnumerable<Type> types, int typesCount)
        {
            CheckGetOrCreateSystem();

            var toInitSystems = new ComponentSystemBase[typesCount];
            // start before 0 as we increment at the top of the loop to avoid
            // special cases for the various early outs in the loop below
            var i = -1;
            foreach(var type in types)
            {
                i++;
                try
                {
                    if (GetExistingSystemInternal(type) != null)
                        continue;

                    var system = AllocateSystemInternal(type);
                    if (system == null)
                        continue;

                    toInitSystems[i] = system;
                    AddSystem_Add_Internal(system);
                }
                catch (Exception exc)
                {
                    Debug.LogException(exc);
                }
            }

            for (i = 0; i != typesCount; i++)
            {
                if (toInitSystems[i] != null)
                {
                    try
                    {
                        AddSystem_OnCreate_Internal(toInitSystems[i]);
                    }
                    catch (Exception exc)
                    {
                        Debug.LogException(exc);
                    }
                }
            }

            i = 0;
            foreach (var type in types)
            {
                toInitSystems[i] = GetExistingSystemInternal(type);
                i++;
            }

            return toInitSystems;
        }

        public ComponentSystemBase[] GetOrCreateSystemsAndLogException(Type[] types)
        {
            return GetOrCreateSystemsAndLogException(types, types.Length);
        }

        public bool QuitUpdate { get; set; }

        public void Update()
        {
            GetExistingSystem<InitializationSystemGroup>()?.Update();
            GetExistingSystem<SimulationSystemGroup>()?.Update();
            GetExistingSystem<PresentationSystemGroup>()?.Update();

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
            Assert.IsTrue(EntityManager.GetBuffer<WorldTimeQueue>(TimeSingleton).Length == 0, "PushTime without matching PopTime");
        #endif
        }

        /// <summary>
        /// Read only collection that doesn't generate garbage when used in a foreach.
        /// </summary>
        public struct NoAllocReadOnlyCollection<T> : IEnumerable<T>
        {
            readonly List<T> m_Source;

            public NoAllocReadOnlyCollection(List<T> source) => m_Source = source;

            public int Count => m_Source.Count;

            public T this[int index] => m_Source[index];

            public List<T>.Enumerator GetEnumerator() => m_Source.GetEnumerator();

            public bool Contains(T item) => m_Source.Contains(item);

            IEnumerator<T> IEnumerable<T>.GetEnumerator()
                => throw new NotSupportedException($"To avoid boxing, do not cast {nameof(NoAllocReadOnlyCollection<T>)} to IEnumerable<T>.");
            IEnumerator IEnumerable.GetEnumerator()
                => throw new NotSupportedException($"To avoid boxing, do not cast {nameof(NoAllocReadOnlyCollection<T>)} to IEnumerable.");
        }
    }

    // TODO: Make methods public once ISystemBase is ready for users
    public static class WorldExtensions
    {
        internal static SystemRef<T> AddSystem<T>(this World self) where T : struct, ISystemBase
        {
            return self.Unmanaged.CreateUnmanagedSystem<T>(self);
        }

        internal static SystemRef<T> GetExistingSystem<T>(this World self) where T : struct, ISystemBase
        {
            return self.Unmanaged.GetExistingUnmanagedSystem<T>();
        }

        internal static SystemRef<T> GetOrCreateSystem<T>(this World self) where T : struct, ISystemBase
        {
            return self.Unmanaged.GetOrCreateUnmanagedSystem<T>(self);
        }

#if !NET_DOTS && !UNITY_DOTSRUNTIME
        internal static SystemHandleUntyped GetOrCreateUnmanagedSystem(this World self, Type unmanagedType)
        {
            return self.Unmanaged.GetOrCreateUnmanagedSystem(self, unmanagedType);
        }
#endif

        internal static void DestroyUnmanagedSystem(this World self, SystemHandleUntyped sysHandle)
        {
            self.Unmanaged.DestroyUnmanagedSystem(sysHandle);
        }
    }
}
