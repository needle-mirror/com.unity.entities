using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Mathematics;

namespace Unity.Entities
{
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

    internal unsafe partial struct WorldUnmanagedImpl
    {
        private StateAllocator _stateMemory;
        private UnsafeList<ushort> _pendingDestroys;
        private UnsafeMultiHashMap<long, ushort> _unmanagedSlotByTypeHash;
        internal readonly ulong SequenceNumber;
        public WorldFlags Flags;
        public TimeData CurrentTime;
        public SystemHandleUntyped ExecutingSystem;
        /// <summary>
        /// The maximum DeltaTime that will be applied to a World in a single call to Update().
        /// If the actual elapsed time since the previous frame exceeds this value, it will be clamped.
        /// This helps maintain a minimum frame rate after a large frame time spike, by spreading out the recovery over
        /// multiple frames.
        /// The value is expressed in seconds. The default value is 1/3rd seconds. Recommended values are 1/10th and 1/3rd seconds.
        /// </summary>
        public float MaximumDeltaTime;

        public int Version { get; private set; }
        internal void BumpVersion() => Version++;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public bool AllowGetSystem { get; private set; }
        internal void DisallowGetSystem() => AllowGetSystem = false;
#endif

        internal WorldUnmanagedImpl(ulong sequenceNumber, WorldFlags flags)
        {
            CurrentTime = default;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AllowGetSystem = true;
#endif
            SequenceNumber = sequenceNumber;
            MaximumDeltaTime = 1.0f / 3.0f;
            Flags = flags;
            _unmanagedSlotByTypeHash = new UnsafeMultiHashMap<long, ushort>(32, Allocator.Persistent);
            _pendingDestroys = new UnsafeList<ushort>(32, Allocator.Persistent);
            _stateMemory = default;
            _stateMemory.Init();
            Version = 0;
            ExecutingSystem = default;
        }

        internal void Dispose()
        {
            _unmanagedSlotByTypeHash.Dispose();
            _pendingDestroys.Dispose();
            _stateMemory.Dispose();
        }

        private void InvalidateSlot(ushort handle)
        {
            var block = _stateMemory.GetBlock(handle, out var subIndex);
            StateAllocator.IncVersion(ref block->Version[subIndex]);
            ++Version;
        }

        private void FreeSlot(ushort handle, SystemState* statePtr)
        {
            var systemPtr = statePtr->m_SystemPtr;
            // If the system ptr is not null, this is an unmanaged system and we need to actually destroy it.
            if (systemPtr != null)
            {
                try
                {
                    SystemBaseRegistry.CallOnStopRunning(statePtr);
                    SystemBaseRegistry.CallOnDestroy(statePtr);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            statePtr->Dispose();
            _stateMemory.Free(handle);
        }

        private void FreeSlot(ushort handle)
        {
            var statePtr = _stateMemory.ResolveNoCheck(handle);
            FreeSlot(handle, statePtr);
        }

        [NotBurstCompatible]
        internal void DestroyAllUnmanagedSystemsAndLogException()
        {
            for (int i = 0; i < 64; ++i)
            {
                var blockPtr = _stateMemory.m_Level1 + i;

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

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void FailBecauseSystemDoesNotExist()
        {
            throw new InvalidOperationException("System does not exist");
        }

        private SystemHandleUntyped GetExistingUnmanagedSystem(long typeHash)
        {
            if (_unmanagedSlotByTypeHash.TryGetFirstValue(typeHash, out ushort handle, out _))
            {
                var block = _stateMemory.GetBlock(handle, out var subIndex);
                return new SystemHandleUntyped(handle, block->Version[subIndex], (uint) SequenceNumber);
            }
            FailBecauseSystemDoesNotExist();
            return default;
        }

        internal SystemRef<T> GetExistingUnmanagedSystem<T>() where T : struct, ISystemBase
        {
            if (_unmanagedSlotByTypeHash.TryGetFirstValue(BurstRuntime.GetHashCode64<T>(), out ushort handle, out _))
            {
                var block = _stateMemory.GetBlock(handle, out var subIndex);
                var sysHandle = new SystemHandle<T>(handle, block->Version[subIndex], (uint) SequenceNumber);
                void* ptr = (void*)(IntPtr)block->SystemPointer[subIndex];
                return new SystemRef<T>(ptr, sysHandle);
            }
            FailBecauseSystemDoesNotExist();
            return default;
        }

        internal void DestroyPendingSystems()
        {
            int len = _pendingDestroys.Length;

            for (var i = 0; i < len; ++i)
            {
                FreeSlot(_pendingDestroys[i]);
            }

            _pendingDestroys.Clear();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckSysHandleVersion(SystemHandleUntyped sysHandle)
        {
            if (sysHandle.m_Version == 0)
                throw new ArgumentException("sysHandle is invalid (default constructed)");
        }

        private void InvalidateSystemHandle(SystemHandleUntyped sysHandle)
        {
            CheckSysHandleVersion(sysHandle);
            long typeHash = _stateMemory.GetTypeHashNoCheck(sysHandle.m_Handle);

            // TODO: Find other systems of same type in creation order, restore type lookup. Needed?
            _unmanagedSlotByTypeHash.Remove(typeHash, sysHandle.m_Handle);

            // Invalidate the slot so handles no longer resolve, but don't free the storage yet
            // as there could be live references in the parent scope. We can only free after the
            // calling system has updated
            InvalidateSlot(sysHandle.m_Handle);
        }

        internal void DestroyManagedSystem(SystemState* state)
        {
            InvalidateSystemHandle(state->m_Handle);
            FreeSlot(state->m_Handle.m_Handle, state);
        }

        internal void DestroyUnmanagedSystem(SystemHandleUntyped sysHandle)
        {
            InvalidateSystemHandle(sysHandle);
            _pendingDestroys.Add(sysHandle.m_Handle);
        }

        private ushort AllocSlot(World self, int structSize, long typeHash, out SystemState* statePtr, out void* systemPtr, out ushort version)
        {
            var metaIndex = SystemBaseRegistry.GetSystemTypeMetaIndex(typeHash);
            systemPtr = Memory.Unmanaged.Allocate(structSize, 16, Allocator.Persistent);
            UnsafeUtility.MemClear(systemPtr, structSize);

            statePtr = _stateMemory.Alloc(out var handle, out version, systemPtr, typeHash);

            UnsafeUtility.MemClear(statePtr, sizeof(SystemState));
            var safeHandle = new SystemHandleUntyped(handle, version, (uint) SequenceNumber);
            statePtr->InitUnmanaged(self, safeHandle, metaIndex, systemPtr);

            ++Version;

            return handle;
        }

        internal SystemState* AllocateSystemStateForManagedSystem(World self, ComponentSystemBase system)
        {
            var type = system.GetType();
            long typeHash = BurstRuntime.GetHashCode64(type);
            SystemState* statePtr = _stateMemory.Alloc(out var handle, out var version, null, typeHash);
            var safeHandle = new SystemHandleUntyped(handle, version, (uint) SequenceNumber);
            statePtr->InitManaged(self, safeHandle, type, system);
            _unmanagedSlotByTypeHash.Add(typeHash, handle);
            ++Version;
            return statePtr;
        }

        internal SystemRef<T> CreateUnmanagedSystem<T>(World self) where T : struct, ISystemBase
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!UnsafeUtility.IsUnmanaged<T>())
            {
                throw new ArgumentException($"type {typeof(T)} cannot contain managed systems");
            }
#endif

            long typeHash = BurstRuntime.GetHashCode64<T>();
            void* systemPtr = null;
            ushort handle = AllocSlot(self, UnsafeUtility.SizeOf<T>(), typeHash, out var statePtr, out systemPtr, out var version);

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

            _unmanagedSlotByTypeHash.Add(typeHash, handle);

            return new SystemRef<T>(systemPtr, new SystemHandle<T>(handle, version, (uint)SequenceNumber));
        }

#if !NET_DOTS && !UNITY_DOTSRUNTIME
        private SystemHandleUntyped CreateUnmanagedSystem(World self, Type t, long typeHash)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!UnsafeUtility.IsUnmanaged(t))
            {
                throw new ArgumentException($"type {t} cannot contain managed systems");
            }
#endif
            void* systemPtr = null;
            ushort handle = AllocSlot(self, UnsafeUtility.SizeOf(t), typeHash, out var statePtr, out systemPtr, out var version);

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

            _unmanagedSlotByTypeHash.Add(typeHash, handle);

            return new SystemHandleUntyped(handle, version, (uint)SequenceNumber);
        }
#endif

        internal SystemRef<T> GetOrCreateUnmanagedSystem<T>(World self) where T : struct, ISystemBase
        {
            if (_unmanagedSlotByTypeHash.ContainsKey(BurstRuntime.GetHashCode64<T>()))
            {
                return GetExistingUnmanagedSystem<T>();
            }
            else
            {
                return CreateUnmanagedSystem<T>(self);
            }
        }

#if !NET_DOTS && !UNITY_DOTSRUNTIME
        internal SystemHandleUntyped GetOrCreateUnmanagedSystem(World self, Type t)
        {
            long hash = BurstRuntime.GetHashCode64(t);
            if (_unmanagedSlotByTypeHash.ContainsKey(hash))
            {
                return GetExistingUnmanagedSystem(hash);
            }
            else
            {
                return CreateUnmanagedSystem(self, t, hash);
            }
        }

        internal SystemHandleUntyped CreateUnmanagedSystem(World self, Type t)
        {
            long hash = BurstRuntime.GetHashCode64(t);
            return CreateUnmanagedSystem(self, t, hash);
        }
#endif

        internal SystemState* ResolveSystemState(SystemHandleUntyped id)
        {
            // Nothing can resolve while we're shutting down.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!AllowGetSystem)
                return null;
#endif
            // System ID is for a different world.
            if (id.m_WorldSeqNo != (uint)SequenceNumber)
                return null;

            ushort handle = id.m_Handle;
            ushort version = id.m_Version;

            // System ID is out of bounds.
            if (handle >= 64 * 64)
                return null;

            return _stateMemory.Resolve(handle, version);
        }

        internal bool IsSystemValid(SystemHandleUntyped id) => ResolveSystemState(id) != null;
    }

    [BurstCompatible]
    public unsafe struct WorldUnmanaged
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
#endif
#if UNITY_2020_2_OR_NEWER
        private WorldUnmanagedImpl* m_Impl;
#else
        private void* m_Impl;
#endif
        private EntityManager m_EntityManager;

        /// <summary>
        /// Returns the EntityManager associated with this instance of the world.
        /// </summary>
        public EntityManager EntityManager => m_EntityManager;

        internal SystemHandleUntyped ExecutingSystem
        {
            get => GetImpl().ExecutingSystem;
            set => GetImpl().ExecutingSystem = value;
        }
        public ref TimeData CurrentTime => ref GetImpl().CurrentTime;

        public WorldFlags Flags => GetImpl().Flags;

        public float MaximumDeltaTime
        {
            get => GetImpl().MaximumDeltaTime;
            set => GetImpl().MaximumDeltaTime = value;
        }

        public ulong SequenceNumber => GetImpl().SequenceNumber;
        public int Version => GetImpl().Version;
        internal void BumpVersion() => GetImpl().BumpVersion();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal bool AllowGetSystem => GetImpl().AllowGetSystem;
        internal void DisallowGetSystem() => GetImpl().DisallowGetSystem();
#endif

        internal static readonly SharedStatic<ulong> ms_NextSequenceNumber = SharedStatic<ulong>.GetOrCreate<World>();

        [NotBurstCompatible]
        internal void Create(World world, WorldFlags flags)
        {
            var ptr = Memory.Unmanaged.Allocate(UnsafeUtility.SizeOf<WorldUnmanagedImpl>(), 16, Allocator.Persistent);
#if UNITY_2020_2_OR_NEWER
            m_Impl = (WorldUnmanagedImpl*)ptr;
#else
            m_Impl = ptr;
#endif
            UnsafeUtility.AsRef<WorldUnmanagedImpl>(m_Impl) = new WorldUnmanagedImpl(ms_NextSequenceNumber.Data++, flags);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif
            // The EntityManager itself is only a handle to a data access and already performs safety checks, so it is
            // OK to keep it on this handle itself instead of in the actual implementation.
            m_EntityManager = default;
            m_EntityManager.Initialize(world);
        }

        [NotBurstCompatible]
        internal void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
            AtomicSafetyHandle.Release(m_Safety);
#endif
            m_EntityManager.DestroyInstance();
            m_EntityManager = default;
            UnsafeUtility.AsRef<WorldUnmanagedImpl>(m_Impl).Dispose();
            Memory.Unmanaged.Free(m_Impl, Allocator.Persistent);
            m_Impl = null;
        }

        private ref WorldUnmanagedImpl GetImpl()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckExistsAndThrow(m_Safety);
#endif
            return ref UnsafeUtility.AsRef<WorldUnmanagedImpl>(m_Impl);
        }

        [NotBurstCompatible]
        internal SystemState* AllocateSystemStateForManagedSystem(World self, ComponentSystemBase system) =>
            GetImpl().AllocateSystemStateForManagedSystem(self, system);

        [NotBurstCompatible]
        internal void DestroyAllUnmanagedSystemsAndLogException() =>
            GetImpl().DestroyAllUnmanagedSystemsAndLogException();

        [NotBurstCompatible]
        internal void DestroyPendingSystems() =>
            GetImpl().DestroyPendingSystems();

        [NotBurstCompatible]
        internal SystemRef<T> CreateUnmanagedSystem<T>(World self) where T : struct, ISystemBase =>
            GetImpl().CreateUnmanagedSystem<T>(self);

        [NotBurstCompatible]
        internal SystemRef<T> GetOrCreateUnmanagedSystem<T>(World self) where T : struct, ISystemBase =>
            GetImpl().GetOrCreateUnmanagedSystem<T>(self);

#if !NET_DOTS && !UNITY_DOTSRUNTIME
        [NotBurstCompatible]
        internal SystemHandleUntyped GetOrCreateUnmanagedSystem(World self, Type unmanagedType) =>
            GetImpl().GetOrCreateUnmanagedSystem(self, unmanagedType);

        [NotBurstCompatible]
        internal SystemHandleUntyped CreateUnmanagedSystem(World self, Type unmanagedType) =>
            GetImpl().CreateUnmanagedSystem(self, unmanagedType);
#endif

        [NotBurstCompatible]
        internal void DestroyUnmanagedSystem(SystemHandleUntyped sysHandle) =>
            GetImpl().DestroyUnmanagedSystem(sysHandle);

        [NotBurstCompatible]
        internal void DestroyManagedSystem(SystemState* state) =>
            GetImpl().DestroyManagedSystem(state);

        internal SystemState* ResolveSystemState(SystemHandleUntyped id) => GetImpl().ResolveSystemState(id);

        [NotBurstCompatible]
        internal Type GetTypeOfSystem(SystemHandleUntyped systemHandleUntyped)
        {
            SystemState* s = ResolveSystemState(systemHandleUntyped);
            if (s != null)
            {
                if (s->m_ManagedSystem.IsAllocated)
                {
                    return s->m_ManagedSystem.Target.GetType();
                }
                return SystemBaseRegistry.GetStructType(s->UnmanagedMetaIndex);
            }

            return null;
        }

        // TODO: Make public when ISystemBase is exposed
        [BurstCompatible(GenericTypeArguments = new[]{typeof(BurstCompatibleSystem)})]
        internal SystemRef<T> GetExistingUnmanagedSystem<T>() where T : struct, ISystemBase =>
            GetImpl().GetExistingUnmanagedSystem<T>();

        // TODO: Make public when ISystemBase is exposed
        internal bool IsSystemValid(SystemHandleUntyped id) => GetImpl().IsSystemValid(id);

        // TODO: Make public when ISystemBase is exposed
        [BurstCompatible(GenericTypeArguments = new[]{typeof(BurstCompatibleSystem)})]
        internal ref T ResolveSystem<T>(SystemHandle<T> systemHandle) where T : struct, ISystemBase
        {
            var ptr = ResolveSystemState(systemHandle);
            CheckSystemReference(ptr);
            return ref UnsafeUtility.AsRef<T>(ptr->m_SystemPtr);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckSystemReference(SystemState* ptr)
        {
            if (ptr == null)
                throw new InvalidOperationException("System reference is not valid");
        }
    }
}
