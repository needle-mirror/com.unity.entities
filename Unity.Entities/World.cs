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

    /// <summary>
    /// An identifier representing an unmanaged system struct instance in a particular world.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemHandleUntyped : IEquatable<SystemHandleUntyped>, IComparable<SystemHandleUntyped>
    {
        internal ushort m_Handle;
        internal ushort m_Version;
        internal uint m_WorldSeqNo;

        private ulong ToUlong()
        {
            return ((ulong)m_WorldSeqNo << 32) | ((ulong)m_Handle << 16) | (ulong)m_Version;
        }

        internal SystemHandleUntyped(ushort handle, ushort version, uint worldSeqNo)
        {
            m_Handle = handle;
            m_Version = version;
            m_WorldSeqNo = worldSeqNo;
        }

        public int CompareTo(SystemHandleUntyped other)
        {
            ulong a = ToUlong();
            ulong b = other.ToUlong();
            if (a < b)
                return -1;
            else if (a > b)
                return 1;
            return 0;
        }

        public override bool Equals(object obj)
        {
            if (obj is SystemHandleUntyped foo)
                return Equals(foo);
            return false;
        }

        public bool Equals(SystemHandleUntyped other)
        {
            return ToUlong() == other.ToUlong();
        }

        public override int GetHashCode()
        {
            int hashCode = -116238775;
            hashCode = hashCode * -1521134295 + m_Handle.GetHashCode();
            hashCode = hashCode * -1521134295 + m_Version.GetHashCode();
            hashCode = hashCode * -1521134295 + m_WorldSeqNo.GetHashCode();
            return hashCode;
        }

        public static bool operator==(SystemHandleUntyped a, SystemHandleUntyped b)
        {
            return a.Equals(b);
        }

        public static bool operator!=(SystemHandleUntyped a, SystemHandleUntyped b)
        {
            return !a.Equals(b);
        }
    }

    /// <summary>
    /// An identifier representing an unmanaged system struct instance in a particular world.
    /// </summary>
    internal struct SystemHandle<T> where T : struct, ISystemBase
    {
        internal SystemHandleUntyped MHandle;

        internal SystemHandle(ushort slot, ushort version, uint worldSeqNo)
        {
            MHandle = new SystemHandleUntyped(slot, version, worldSeqNo);
        }

        public static implicit operator SystemHandleUntyped(SystemHandle<T> self) => self.MHandle;
    }

    internal struct WorldUnmanagedImpl
    {
        public TimeData CurrentTime;
    }

    public unsafe struct WorldUnmanaged
    {
        private WorldUnmanagedImpl* m_Impl;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
#endif
        public ref TimeData CurrentTime => ref GetImpl()->CurrentTime;

        internal void Create()
        {
            m_Impl = (WorldUnmanagedImpl*) UnsafeUtility.Malloc(sizeof(WorldUnmanagedImpl), 16, Allocator.Persistent);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif
        }

        internal void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
            AtomicSafetyHandle.Release (m_Safety);
#endif
            UnsafeUtility.Free(m_Impl, Allocator.Persistent);
            m_Impl = null;
        }

        private WorldUnmanagedImpl* GetImpl()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckExistsAndThrow(m_Safety);
#endif
            return m_Impl;
        }
    }

    [DebuggerDisplay("{Name} - {Flags} (#{SequenceNumber})")]
    public unsafe partial class World : IDisposable
    {
        internal static readonly List<World> s_AllWorlds = new List<World>();

        public static World DefaultGameObjectInjectionWorld { get; set; }

    #if UNITY_DOTSRUNTIME
        [Obsolete("use World.All instead. (RemovedAfter 2020-06-02)")]
        public static World[] AllWorlds => s_AllWorlds.ToArray();
    #else
        [Obsolete("use World.All instead. (RemovedAfter 2020-06-02)")]
        public static System.Collections.ObjectModel.ReadOnlyCollection<World> AllWorlds => new System.Collections.ObjectModel.ReadOnlyCollection<World>(s_AllWorlds);
    #endif
        Dictionary<Type, ComponentSystemBase> m_SystemLookup = new Dictionary<Type, ComponentSystemBase>();
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
        bool m_AllowGetSystem = true;
    #endif
        public static NoAllocReadOnlyCollection<World> All { get; } = new NoAllocReadOnlyCollection<World>(s_AllWorlds);

        // Manages a hierarchical bit set of 64 x 64 bits for that many system states.
        // The intent is to be able to do two trailing zero counts to find a free slot,
        // without maintaining a free list
        internal struct StateAllocator
        {
            internal ulong m_FreeBits;
            internal StateAllocLevel1* m_Level1;

            public void Init()
            {
                m_FreeBits = ~0ul;

                int allocSize = sizeof(StateAllocLevel1) * 64;
                var l1 = m_Level1 = (StateAllocLevel1*)UnsafeUtility.Malloc(allocSize, 16, Allocator.Persistent);
                UnsafeUtility.MemClear(l1, allocSize);

                for (int i = 0; i < 64; ++i)
                {
                    l1[i].FreeBits = ~0ul;
                }
            }

            public void Dispose()
            {
                var l1 = m_Level1;

                for (int i = 0; i < 64; ++i)
                {
                    if (l1[i].States != null)
                    {
                        UnsafeUtility.Free(l1[i].States, Allocator.Persistent);
                    }
                }

                UnsafeUtility.Free(l1, Allocator.Persistent);
                m_Level1 = null;

                this = default;
            }

            public SystemState* Resolve(ushort handle, ushort version)
            {
                int index = handle >> 6;
                int subIndex = handle & 63;

                ref var leaf = ref m_Level1[index];
                return leaf.Version[subIndex] == version ? leaf.States + subIndex : null;
            }

            public SystemState* ResolveNoCheck(ushort handle)
            {
                int index = handle >> 6;
                int subIndex = handle & 63;
                return m_Level1[index].States + subIndex;
            }

            public long GetTypeHashNoCheck(ushort handle)
            {
                int index = handle >> 6;
                int subIndex = handle & 63;
                return m_Level1[index].TypeHash[subIndex];
            }

            public SystemState* Alloc(out ushort outHandle, out ushort outVersion, void* systemPtr, long typeHash)
            {
                CheckFull();

                int index = math.tzcnt(m_FreeBits);

                ref var leaf = ref *(m_Level1 + index);

                int subIndex = math.tzcnt(leaf.FreeBits);

                CheckSubIndex(subIndex);

                if (leaf.States == null)
                {
                    leaf.States = (SystemState*)UnsafeUtility.Malloc(64 * sizeof(SystemState), 16, Allocator.Persistent);
                }

                leaf.FreeBits &= ~(1ul << subIndex);

                // Branch-free clear of our bit in parent cascade if we're empty
                ulong empty = leaf.FreeBits == 0 ? 1ul : 0ul;
                m_FreeBits &= ~(empty << index);

                var resultPtr = leaf.States + subIndex;

                UnsafeUtility.MemClear(resultPtr, sizeof(SystemState));

                outHandle = (ushort)((index << 6) + subIndex);

                IncVersion(ref leaf.Version[subIndex]);
                outVersion = leaf.Version[subIndex];
                leaf.TypeHash[subIndex] = typeHash;
                leaf.SystemPointer[subIndex] = (ulong)(IntPtr)systemPtr;

                return resultPtr;
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void CheckFull()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (0 == m_FreeBits)
                    throw new InvalidOperationException("out of system state slots; maximum is 4,096");
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void CheckSubIndex(int subIndex)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if ((uint)subIndex > 63)
                    throw new InvalidOperationException("data structure corrupted");
#endif
            }

            public void Free(ushort handle)
            {
                int index = handle >> 6;
                int subIndex = handle & 63;

                CheckIndex(index);

                m_FreeBits |= 1ul << index;

                ref var leaf = ref *(m_Level1 + index);

                CheckIsAllocated(ref leaf, subIndex);

                leaf.FreeBits |= (1ul << subIndex);
                IncVersion(ref leaf.Version[subIndex]);
                leaf.SystemPointer[subIndex] = 0;
                leaf.TypeHash[subIndex] = 0;
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void CheckIsAllocated(ref StateAllocLevel1 leaf, int subIndex)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if ((leaf.FreeBits & (1ul << subIndex)) != 0)
                {
                    throw new InvalidOperationException("slot is not allocated");
                }
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void CheckIndex(int index)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (index > 63)
                    throw new ArgumentException("bad index");
#endif
            }

            public static void IncVersion(ref ushort v)
            {
                uint m = v;
                m += 1;
                m = (m >> 16) | m; // Fold overflow bit down to make 0xffff wrap to 0x0001, avoiding zero which is reserved for "unused"
                v = (ushort)m;
            }

            public SystemState* GetState(ushort handle)
            {
                return GetBlock(handle, out var subIndex)->States + subIndex;
            }

            public StateAllocLevel1* GetBlock(ushort handle, out ushort subIndex)
            {
                ushort index = (ushort)(handle >> 6);
                subIndex = (ushort)(handle & 63);
                return m_Level1 + index;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct StateAllocLevel1
        {
            public ulong FreeBits;
            public SystemState* States;
            public fixed ushort Version[64];
            public fixed long TypeHash[64];
            public fixed ulong SystemPointer[64];
        }

        private StateAllocator m_StateMemory;
        private UnsafeList<ushort> m_PendingUnmanagedDestroys;
        UnsafeMultiHashMap<long, ushort> m_UnmanagedSlotByTypeHash;

        List<ComponentSystemBase> m_Systems = new List<ComponentSystemBase>();
        public NoAllocReadOnlyCollection<ComponentSystemBase> Systems { get; }

        EntityManager m_EntityManager;
        readonly ulong m_SequenceNumber;

        static int ms_SystemIDAllocator = 0;

        private WorldUnmanaged m_Unmanaged;

        public WorldUnmanaged Unmanaged => m_Unmanaged;

        internal static readonly SharedStatic<ulong> ms_NextSequenceNumber = SharedStatic<ulong>.GetOrCreate<World>();

        public readonly WorldFlags Flags;

        public string Name { get; }

        public override string ToString()
        {
            return Name;
        }

        public int Version { get; private set; }

        public EntityManager EntityManager => m_EntityManager;

        public bool IsCreated => m_Systems != null;

        public ulong SequenceNumber => m_SequenceNumber;

        public ref TimeData Time => ref m_Unmanaged.CurrentTime;

        private EntityQuery m_TimeSingletonQuery;

        public World(string name) : this(name, WorldFlags.Simulation)
        {}

        private static uint s_WorldId = 0xfacefeed;
        internal readonly uint m_WorldId = s_WorldId++;

        internal World(string name, WorldFlags flags)
        {
            m_Unmanaged.Create();

            Systems = new NoAllocReadOnlyCollection<ComponentSystemBase>(m_Systems);

            m_SequenceNumber = ms_NextSequenceNumber.Data++;

            // Debug.LogError("Create World "+ name + " - " + GetHashCode());
            Name = name;
            Flags = flags;
            s_AllWorlds.Add(this);

            m_EntityManager = default;
            m_EntityManager.Initialize(this);
            EntityManager.DeprecatedRegistry.Register(m_EntityManager, this);
            m_TimeSingletonQuery = EntityManager.CreateEntityQuery(ComponentType.ReadWrite<WorldTime>(),
                ComponentType.ReadWrite<WorldTimeQueue>());

            m_UnmanagedSlotByTypeHash = new UnsafeMultiHashMap<long, ushort>(32, Allocator.Persistent);
            m_PendingUnmanagedDestroys = new UnsafeList<ushort>(32, Allocator.Persistent);

            m_StateMemory = default;
            m_StateMemory.Init();
        }

        public void Dispose()
        {
            if (!IsCreated)
                throw new ArgumentException("The World has already been Disposed.");
            // Debug.LogError("Dispose World "+ Name + " - " + GetHashCode());

            m_EntityManager.PreDisposeCheck();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_AllowGetSystem = false;
#endif
            EntityManager.DeprecatedRegistry.Unregister(m_EntityManager);

            DestroyAllSystemsAndLogException();
            DestroyAllUnmanagedSystemsAndLogException();

            // Destroy EntityManager last
            m_EntityManager.DestroyInstance();
            m_EntityManager = default;

            s_AllWorlds.Remove(this);

            m_SystemLookup.Clear();
            m_SystemLookup = null;

            if (DefaultGameObjectInjectionWorld == this)
                DefaultGameObjectInjectionWorld = null;

            m_UnmanagedSlotByTypeHash.Dispose();
            m_PendingUnmanagedDestroys.Dispose();

            m_StateMemory.Dispose();
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
            if (!m_AllowGetSystem)
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
                system.CreateInstance(this);
            }
            catch
            {
                RemoveSystemInternal(system);
                throw;
            }
            ++Version;
        }

        void RemoveSystemInternal(ComponentSystemBase system)
        {
            if (!m_Systems.Remove(system))
                throw new ArgumentException($"System does not exist in the world");
            ++Version;

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
            if (!m_AllowGetSystem)
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

        internal void DestroyAllUnmanagedSystemsAndLogException()
        {
            for (int i = 0; i < 64; ++i)
            {
                var blockPtr = m_StateMemory.m_Level1 + i;

                var allocBits = ~blockPtr->FreeBits;

                while (allocBits != 0)
                {
                    int bit = math.tzcnt(allocBits);

                    FreeSlot((ushort)(i * 64 + bit));

                    allocBits &= ~(1ul << bit);
                }

                Assert.IsTrue(blockPtr->FreeBits == ~0ul);
            }
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

        internal static int AllocateSystemID()
        {
            return ++ms_SystemIDAllocator;
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

        //-----------------------------------------------------------------------------
        // Unmanaged stuff

        internal SystemState* ResolveSystemState(SystemHandleUntyped id)
        {
            // Nothing can resolve while we're shutting down.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!m_AllowGetSystem)
                return null;
#endif
            // System ID is for a different world.
            if (id.m_WorldSeqNo != (uint)m_SequenceNumber)
                return null;

            ushort handle = id.m_Handle;
            ushort version = id.m_Version;

            // System ID is out of bounds.
            if (handle >= 64 * 64)
                return null;

            return m_StateMemory.Resolve(handle, version);
        }

        // TODO: Make public when ISystemBase is exposed
        internal bool IsSystemValid(SystemHandleUntyped id)
        {
            return ResolveSystemState(id) != null;
        }

        internal SystemHandleUntyped InternalGetExistingUnmanagedSystem(long typeHash)
        {
            if (m_UnmanagedSlotByTypeHash.TryGetFirstValue(typeHash, out ushort handle, out _))
            {
                var block = m_StateMemory.GetBlock(handle, out var subIndex);
                return new SystemHandleUntyped(handle, block->Version[subIndex], (uint) m_SequenceNumber);
            }

            throw new InvalidOperationException("system does not exist");
        }

        internal SystemRef<T> InternalGetExistingUnmanagedSystem<T>() where T : struct, ISystemBase
        {
            if (m_UnmanagedSlotByTypeHash.TryGetFirstValue(BurstRuntime.GetHashCode64<T>(), out ushort handle, out _))
            {
                var block = m_StateMemory.GetBlock(handle, out var subIndex);
                var sysHandle = new SystemHandle<T>(handle, block->Version[subIndex], (uint) m_SequenceNumber);
                void* ptr = (void*)(IntPtr)block->SystemPointer[subIndex];
                return new SystemRef<T>(ptr, sysHandle);
            }

            throw new InvalidOperationException("system does not exist");
        }

        // TODO: Make public when ISystemBase is exposed
        internal ref T ResolveSystem<T>(SystemHandle<T> systemHandle) where T : struct, ISystemBase
        {
            var ptr = ResolveSystemState(systemHandle);
            if (ptr == null)
                throw new InvalidOperationException("System reference is not valid");
            return ref UnsafeUtility.AsRef<T>(ptr->m_SystemPtr);
        }

        private ushort AllocSlot(int structSize, long typeHash, out SystemState* statePtr, out void* systemPtr, out ushort version)
        {
            var metaIndex = SystemBaseRegistry.GetSystemTypeMetaIndex(typeHash);
            systemPtr = UnsafeUtility.Malloc(structSize, 16, Allocator.Persistent);
            UnsafeUtility.MemClear(systemPtr, structSize);

            statePtr = m_StateMemory.Alloc(out var handle, out version, systemPtr, typeHash);

            UnsafeUtility.MemClear(statePtr, sizeof(SystemState));
            statePtr->InitUnmanaged(this, metaIndex, systemPtr);

            ++Version;

            return handle;
        }

        private void FreeSlot(ushort handle)
        {
            var statePtr = m_StateMemory.ResolveNoCheck(handle);
            var systemPtr = statePtr->m_SystemPtr;

            if (systemPtr != null)
            {
                try
                {
                    SystemBaseRegistry.CallOnDestroy(statePtr);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            statePtr->Dispose();

            m_StateMemory.Free(handle);
        }

        private void InvalidateSlot(ushort handle)
        {
            var block = m_StateMemory.GetBlock(handle, out var subIndex);
            StateAllocator.IncVersion(ref block->Version[subIndex]);
            ++Version;
        }

        internal SystemRef<T> InternalCreateUnmanagedSystem<T>() where T : struct, ISystemBase
        {
            long typeHash = BurstRuntime.GetHashCode64<T>();
            void* systemPtr = null;
            ushort handle = AllocSlot(UnsafeUtility.SizeOf<T>(), typeHash, out var statePtr, out systemPtr, out var version);

            try
            {
                SystemBaseRegistry.CallOnCreate(statePtr);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                FreeSlot(handle);
                throw;
            }

            m_UnmanagedSlotByTypeHash.Add(typeHash, handle);

            return new SystemRef<T>(systemPtr, new SystemHandle<T>(handle, version, (uint)m_SequenceNumber));
        }

#if !UNITY_DOTSRUNTIME
        internal SystemHandleUntyped InternalCreateUnmanagedSystem(Type t, long typeHash)
        {
            void* systemPtr = null;
            ushort handle = AllocSlot(UnsafeUtility.SizeOf(t), typeHash, out var statePtr, out systemPtr, out var version);

            try
            {
                SystemBaseRegistry.CallOnCreate(statePtr);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                FreeSlot(handle);
                throw;
            }

            m_UnmanagedSlotByTypeHash.Add(typeHash, handle);

            return new SystemHandleUntyped(handle, version, (uint)m_SequenceNumber);
        }
#endif

        internal void InternalDestroyUnmanagedSystem(SystemHandleUntyped sysHandle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (sysHandle.m_Version == 0)
                throw new ArgumentException("sysHandle is invalid (default constructed)");
#endif

            long typeHash = m_StateMemory.GetTypeHashNoCheck(sysHandle.m_Handle);

            // TODO: Find other systems of same type in creation order, restore type lookup. Needed?
            m_UnmanagedSlotByTypeHash.Remove(typeHash, sysHandle.m_Handle);

            // Invalidate the slot so handles no longer resolve, but don't free the storage yet
            // as there could be live references in the parent scope. We can only free after the
            // calling system has updated
            InvalidateSlot(sysHandle.m_Handle);

            m_PendingUnmanagedDestroys.Add(sysHandle.m_Handle);
        }

        internal void InternalDestroyPendingUnmanagedSystems()
        {
            int len = m_PendingUnmanagedDestroys.Length;

            for (var i = 0; i < len; ++i)
            {
                FreeSlot(m_PendingUnmanagedDestroys[i]);
            }

            m_PendingUnmanagedDestroys.Clear();
        }

        internal SystemRef<T> GetOrCreateUnmanagedSystem<T>() where T : struct, ISystemBase
        {
            if (m_UnmanagedSlotByTypeHash.ContainsKey(BurstRuntime.GetHashCode64<T>()))
            {
                return InternalGetExistingUnmanagedSystem<T>();
            }
            else
            {
                return InternalCreateUnmanagedSystem<T>();
            }
        }

#if !UNITY_DOTSRUNTIME
        internal SystemHandleUntyped GetOrCreateUnmanagedSystem(Type t)
        {
            long hash = BurstRuntime.GetHashCode64(t);
            if (m_UnmanagedSlotByTypeHash.ContainsKey(hash))
            {
                return InternalGetExistingUnmanagedSystem(hash);
            }
            else
            {
                return InternalCreateUnmanagedSystem(t, hash);
            }
        }

        internal SystemHandleUntyped CreateUnmanagedSystem(Type t)
        {
            long hash = BurstRuntime.GetHashCode64(t);
            return InternalCreateUnmanagedSystem(t, hash);
        }
#endif

        internal Type GetTypeOfUnmanagedSystem(SystemHandleUntyped systemHandleUntyped)
        {
            SystemState* s = ResolveSystemState(systemHandleUntyped);

            if (s != null)
            {
                return SystemBaseRegistry.GetStructType(s->UnmanagedMetaIndex);
            }

            return null;
        }
    }

    /// <summary>
    /// Allows access by reference to the struct instance backing a system
    /// </summary>
    /// <typeparam name="T">The system struct type</typeparam>
    internal unsafe ref struct SystemRef<T> where T : struct, ISystemBase
    {
        private void* m_Pointer;
        private SystemHandle<T> m_Handle;

        internal SystemRef(void* p, SystemHandle<T> handle)
        {
            m_Pointer = p;
            m_Handle = handle;
        }

        /// <summary>
        /// Return a reference to the system struct
        /// </summary>
        public ref T Struct => ref UnsafeUtility.AsRef<T>(m_Pointer);

        /// <summary>
        /// Return a handle that can be stored and resolved against the World in the future to get back to the same struct
        /// </summary>
        public SystemHandle<T> Handle => m_Handle;
    }

    // TODO: Make methods public once ISystemBase is ready for users
    public unsafe static class WorldExtensions
    {
        internal static SystemRef<T> AddSystem<T>(this World self) where T : struct, ISystemBase
        {
            return self.InternalCreateUnmanagedSystem<T>();
        }

        internal static SystemRef<T> GetExistingSystem<T>(this World self) where T : struct, ISystemBase
        {
            return self.InternalGetExistingUnmanagedSystem<T>();
        }

        internal static SystemRef<T> GetOrCreateSystem<T>(this World self) where T : struct, ISystemBase
        {
            return self.GetOrCreateUnmanagedSystem<T>();
        }

#if !UNITY_DOTSRUNTIME
        internal static SystemHandleUntyped GetOrCreateUnmanagedSystem(this World self, Type unmanagedType)
        {
            return self.GetOrCreateUnmanagedSystem(unmanagedType);
        }
#endif

        internal static void DestroySystem(this World self, SystemHandleUntyped sysHandle)
        {
            self.InternalDestroyUnmanagedSystem(sysHandle);
        }
    }
}
