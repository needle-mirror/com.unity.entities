using System;
using System.Collections.Generic;
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
    public struct SystemHandleUntyped : IEquatable<SystemHandleUntyped>, IComparable<SystemHandleUntyped>
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
    public struct SystemHandle<T> where T : unmanaged, ISystem
    {
        internal SystemHandleUntyped MHandle;

        internal SystemHandle(ushort slot, ushort version, uint worldSeqNo)
        {
            MHandle = new SystemHandleUntyped(slot, version, worldSeqNo);
        }

        public SystemHandleUntyped UntypedHandle => MHandle;

        public static implicit operator SystemHandleUntyped(SystemHandle<T> self) => self.MHandle;
    }

    /// <summary>
    /// Allows access by reference to the struct instance backing a system
    /// </summary>
    /// <typeparam name="T">The system struct type</typeparam>
    public unsafe ref struct SystemRef<T> where T : unmanaged, ISystem
    {
        private void* m_Pointer;
        private SystemHandle<T> m_Handle;

        internal SystemRef(void* p, SystemHandle<T> handle)
        {
            m_Pointer = p;
            m_Handle = handle;
        }
        
        public void Update(WorldUnmanaged w)
        {
            ComponentSystemGroup.UpdateSystem(ref w, m_Handle.UntypedHandle);
            
        }

        /// <summary>
        /// Return a reference to the system struct
        /// </summary>
        public ref T Struct => ref UnsafeUtility.AsRef<T>(m_Pointer);

        /// <summary>
        /// Return a handle that can be stored and resolved against the World in the future to get back to the same struct
        /// </summary>
        public SystemHandle<T> Handle => m_Handle;

        public static implicit operator SystemHandle<T>(SystemRef<T> self) => self.m_Handle;
        public static implicit operator SystemHandleUntyped(SystemRef<T> self) => self.m_Handle.MHandle;
    }

    internal unsafe partial struct WorldUnmanagedImpl
    {
        internal AllocatorManager.AllocatorHandle m_AllocatorHandle;
        private StateAllocator _stateMemory;
        private UnsafeList<ushort> _pendingDestroys;
        private UnsafeParallelMultiHashMap<long, ushort> _unmanagedSlotByTypeHash;
        internal readonly ulong SequenceNumber;
        public WorldFlags Flags;
        public TimeData CurrentTime;
        public SystemHandleUntyped ExecutingSystem;
        public RewindableAllocator* UpdateAllocator;
        internal AllocatorHelper<RewindableAllocator> UpdateAllocatorHelper0;
        internal AllocatorHelper<RewindableAllocator> UpdateAllocatorHelper1;

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

        internal WorldUnmanagedImpl(ulong sequenceNumber, WorldFlags flags, AllocatorManager.AllocatorHandle allocatorHandle)
        {
            m_AllocatorHandle = allocatorHandle;
            CurrentTime = default;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AllowGetSystem = true;
#endif
            SequenceNumber = sequenceNumber;
            MaximumDeltaTime = 1.0f / 3.0f;
            Flags = flags;
            _unmanagedSlotByTypeHash = new UnsafeParallelMultiHashMap<long, ushort>(32, m_AllocatorHandle);
            _pendingDestroys = new UnsafeList<ushort>(32, m_AllocatorHandle);
            _stateMemory = default;
            _stateMemory.Init();
            Version = 0;
            ExecutingSystem = default;

            UpdateAllocatorHelper0 = new AllocatorHelper<RewindableAllocator>(m_AllocatorHandle);
            UpdateAllocatorHelper0.Allocator.Initialize(128 * 1024);
            UpdateAllocatorHelper1 = new AllocatorHelper<RewindableAllocator>(m_AllocatorHandle);
            UpdateAllocatorHelper1.Allocator.Initialize(128 * 1024);

            UpdateAllocator = (RewindableAllocator*)UnsafeUtility.AddressOf<RewindableAllocator>(ref UpdateAllocatorHelper0.Allocator);
        }

        internal void Dispose()
        {
            UpdateAllocatorHelper0.Allocator.Dispose();
            UpdateAllocatorHelper1.Allocator.Dispose();

            UpdateAllocatorHelper0.Dispose();
            UpdateAllocatorHelper1.Dispose();

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
                    if (statePtr->PreviouslyEnabled)
                    {
                        SystemBaseRegistry.CallOnStopRunning(statePtr);
                    }
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

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
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

        internal SystemRef<T> GetExistingUnmanagedSystem<T>() where T : unmanaged, ISystem
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

        internal SystemHandleUntyped GetExistingUnmanagedSystem(Type t)
        {
            var hash = BurstRuntime.GetHashCode64(t);
            if (_unmanagedSlotByTypeHash.ContainsKey(hash))
            {
                return GetExistingUnmanagedSystem(hash);
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

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
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

        internal SystemRef<T> CreateUnmanagedSystem<T>(World self, bool callOnCreate) where T : unmanaged, ISystem
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (!UnsafeUtility.IsUnmanaged<T>())
            {
                throw new ArgumentException($"type {typeof(T)} cannot contain managed systems");
            }
#endif

            long typeHash = BurstRuntime.GetHashCode64<T>();
            void* systemPtr = null;
            ushort handle = AllocSlot(self, UnsafeUtility.SizeOf<T>(), typeHash, out var statePtr, out systemPtr, out var version);

            if (callOnCreate)
            {
                CallSystemOnCreate(handle, statePtr);
                CallSystemOnCreateForCompiler(handle, statePtr);
            }

            _unmanagedSlotByTypeHash.Add(typeHash, handle);

            return new SystemRef<T>(systemPtr, new SystemHandle<T>(handle, version, (uint)SequenceNumber));
        }

        private SystemHandleUntyped CreateUnmanagedSystem(World self, Type t, long typeHash, bool callOnCreate)
        {
            void* systemPtr = null;
            ushort handle = AllocSlot(self, TypeManager.GetSystemTypeSize(t), typeHash, out var statePtr, out systemPtr, out var version);

            if (callOnCreate)
            {
                CallSystemOnCreate(handle, statePtr);
                CallSystemOnCreateForCompiler(handle, statePtr);
            }

            _unmanagedSlotByTypeHash.Add(typeHash, handle);

            return new SystemHandleUntyped(handle, version, (uint)SequenceNumber);
        }

        private void CallSystemOnCreate(ushort handle, SystemState* statePtr)
        {
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
        }

        void CallSystemOnCreateForCompiler(ushort handle, SystemState* statePtr)
        {
            try
            {
                SystemBaseRegistry.CallOnCreateForCompiler(statePtr);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                FreeSlot(handle);
                throw;
            }
        }

        internal SystemRef<T> GetOrCreateUnmanagedSystem<T>(World self, bool callOnCreate = true) where T : unmanaged, ISystem
        {
            if (_unmanagedSlotByTypeHash.ContainsKey(BurstRuntime.GetHashCode64<T>()))
            {
                return GetExistingUnmanagedSystem<T>();
            }
            else
            {
                return CreateUnmanagedSystem<T>(self, callOnCreate);
            }
        }

        internal SystemHandleUntyped GetOrCreateUnmanagedSystem(World self, Type t, bool callOnCreate = true)
        {
            long hash = TypeManager.GetSystemTypeHash(t);
            if (_unmanagedSlotByTypeHash.ContainsKey(hash))
            {
                return GetExistingUnmanagedSystem(hash);
            }
            else
            {
                return CreateUnmanagedSystem(self, t, hash, callOnCreate);
            }
        }

        internal SystemHandleUntyped CreateUnmanagedSystem(World self, Type t, bool callOnCreate)
        {
            long hash = TypeManager.GetSystemTypeHash(t);
            return CreateUnmanagedSystem(self, t, hash, callOnCreate);
        }

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

        internal void ResetUpdateAllocator()
        {
            var UpdateAllocator0 = (RewindableAllocator*)UnsafeUtility.AddressOf<RewindableAllocator>(ref UpdateAllocatorHelper0.Allocator);
            var UpdateAllocator1 = (RewindableAllocator*)UnsafeUtility.AddressOf<RewindableAllocator>(ref UpdateAllocatorHelper1.Allocator);
            UpdateAllocator = (UpdateAllocator == UpdateAllocator0) ? UpdateAllocator1 : UpdateAllocator0;
            UpdateAllocator->Rewind();
        }

        internal NativeArray<IntPtr> GetAllUnmanagedSystemStates(Allocator a)
        {
            int totalCount = 0;
            for (int i = 0; i < 64; ++i)
            {
                var blockPtr = _stateMemory.m_Level1 + i;
                totalCount += math.countbits(~blockPtr->FreeBits);
            }

            var outputIndex = 0;
            var result = new NativeArray<IntPtr>(totalCount, a);

            for (int i = 0; i < 64; ++i)
            {
                var blockPtr = _stateMemory.m_Level1 + i;

                var allocBits = ~blockPtr->FreeBits;

                while (allocBits != 0)
                {
                    int bit = math.tzcnt(allocBits);

                    result[outputIndex++] = (IntPtr) _stateMemory.GetState((ushort)(i * 64 + bit));

                    allocBits &= ~(1ul << bit);
                }
            }

            return result;
        }
    }

    [BurstCompatible]
    public unsafe struct WorldUnmanaged
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
#endif
        internal AllocatorManager.AllocatorHandle m_AllocatorHandle;

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
        [BurstCompatible(RequiredUnityDefine = "ENABLE_UNITY_COLLECTIONS_CHECKS", CompileTarget = BurstCompatibleAttribute.BurstCompatibleCompileTarget.Editor)]
        internal bool AllowGetSystem => GetImpl().AllowGetSystem;

        [BurstCompatible(RequiredUnityDefine = "ENABLE_UNITY_COLLECTIONS_CHECKS", CompileTarget = BurstCompatibleAttribute.BurstCompatibleCompileTarget.Editor)]
        internal void DisallowGetSystem() => GetImpl().DisallowGetSystem();
#endif

        internal static readonly SharedStatic<ulong> ms_NextSequenceNumber = SharedStatic<ulong>.GetOrCreate<World>();

        AllocatorHelper<WorldAllocator> m_WorldAllocatorHelper;

        [NotBurstCompatible]
        internal void Create(World world, WorldFlags flags, AllocatorManager.AllocatorHandle backingAllocatorHandle)
        {
            m_WorldAllocatorHelper = new AllocatorHelper<WorldAllocator>(backingAllocatorHandle);
            m_WorldAllocatorHelper.Allocator.Initialize(backingAllocatorHandle);
            m_AllocatorHandle = m_WorldAllocatorHelper.Allocator.Handle;

            var ptr = m_AllocatorHandle.Allocate(sizeof(WorldUnmanagedImpl), 16, 1);
#if UNITY_2020_2_OR_NEWER
            m_Impl = (WorldUnmanagedImpl*)ptr;
#else
            m_Impl = ptr;
#endif
            UnsafeUtility.AsRef<WorldUnmanagedImpl>(m_Impl) = new WorldUnmanagedImpl(++ms_NextSequenceNumber.Data, flags, m_AllocatorHandle);

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
            m_AllocatorHandle.Free(m_Impl, sizeof(WorldUnmanagedImpl), 16, 1);
            m_Impl = null;

            m_WorldAllocatorHelper.Allocator.Dispose();
            m_WorldAllocatorHelper.Dispose();

        }

        private ref WorldUnmanagedImpl GetImpl()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckExistsAndThrow(m_Safety);
#endif
            return ref UnsafeUtility.AsRef<WorldUnmanagedImpl>(m_Impl);
        }

        public ref RewindableAllocator UpdateAllocator => ref UnsafeUtility.AsRef<RewindableAllocator>(GetImpl().UpdateAllocator);

        internal void ResetUpdateAllocator()
        {
            GetImpl().ResetUpdateAllocator();
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
        internal SystemRef<T> CreateUnmanagedSystem<T>(World self, bool callOnCreate) where T : unmanaged, ISystem =>
            GetImpl().CreateUnmanagedSystem<T>(self, callOnCreate);

        [NotBurstCompatible]
        internal SystemRef<T> GetOrCreateUnmanagedSystem<T>(World self) where T : unmanaged, ISystem =>
            GetImpl().GetOrCreateUnmanagedSystem<T>(self);

        [NotBurstCompatible]
        internal SystemHandleUntyped GetOrCreateUnmanagedSystem(World self, Type unmanagedType) =>
            GetImpl().GetOrCreateUnmanagedSystem(self, unmanagedType);

        [NotBurstCompatible]
        internal SystemHandleUntyped CreateUnmanagedSystem(World self, Type unmanagedType, bool callOnCreate) =>
            GetImpl().CreateUnmanagedSystem(self, unmanagedType, callOnCreate);

        [NotBurstCompatible]
        internal void DestroyUnmanagedSystem(SystemHandleUntyped sysHandle) =>
            GetImpl().DestroyUnmanagedSystem(sysHandle);

        [NotBurstCompatible]
        internal void DestroyManagedSystem(SystemState* state) =>
            GetImpl().DestroyManagedSystem(state);

        public SystemState* ResolveSystemState(SystemHandleUntyped id) => GetImpl().ResolveSystemState(id);

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

        [BurstCompatible(GenericTypeArguments = new[]{typeof(BurstCompatibleSystem)})]
        public SystemRef<T> GetExistingUnmanagedSystem<T>() where T : unmanaged, ISystem =>
            GetImpl().GetExistingUnmanagedSystem<T>();

        [NotBurstCompatible]
        public SystemHandleUntyped GetExistingUnmanagedSystem(Type unmanagedType) =>
            GetImpl().GetExistingUnmanagedSystem(unmanagedType);

        public bool IsSystemValid(SystemHandleUntyped id) => GetImpl().IsSystemValid(id);

        [BurstCompatible(GenericTypeArguments = new[]{typeof(BurstCompatibleSystem)})]
        public ref T ResolveSystem<T>(SystemHandle<T> systemHandle) where T : unmanaged, ISystem
        {
            var ptr = ResolveSystemState(systemHandle);
            CheckSystemReference(ptr);
            return ref UnsafeUtility.AsRef<T>(ptr->m_SystemPtr);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void CheckSystemReference(SystemState* ptr)
        {
            if (ptr == null)
                throw new InvalidOperationException("System reference is not valid");
        }

        internal NativeArray<IntPtr> GetAllUnmanagedSystemStates(Allocator a)
        {
            return GetImpl().GetAllUnmanagedSystemStates(a);
        }

        [NotBurstCompatible]
        internal NativeArray<SystemHandleUntyped> GetOrCreateUnmanagedSystems(World world, IList<Type> unmanagedTypes)
        {
            int count = unmanagedTypes.Count;
            var result = new NativeArray<SystemHandleUntyped>(count, Allocator.Temp);

            ref var impl = ref GetImpl();

            for (int i = 0; i < count; ++i)
            {
                result[i] = impl.GetOrCreateUnmanagedSystem(world, unmanagedTypes[i], false);
            }

            for (int i = 0; i < count; ++i)
            {
                var systemState = ResolveSystemState(result[i]);
                SystemBaseRegistry.CallOnCreate(systemState);
                SystemBaseRegistry.CallOnCreateForCompiler(systemState);
            }

            return result;
        }
    }
}
