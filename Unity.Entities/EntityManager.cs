using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

[assembly: InternalsVisibleTo("Unity.Entities.Hybrid")]
[assembly: InternalsVisibleTo("Unity.Tiny.Core")]
[assembly: InternalsVisibleTo("Unity.DOTS.Editor")]

namespace Unity.Entities
{
    // Exists to allow `EntityManager mgr = null` to compile, as it required by existing packages (Physics)
    [EditorBrowsable(EditorBrowsableState.Never)]
    public struct EntityManagerNullShim
    {
    }

    /// <summary>
    /// The EntityManager manages entities and components in a World.
    /// </summary>
    /// <remarks>
    /// The EntityManager provides an API to create, read, update, and destroy entities.
    ///
    /// A <see cref="World"/> has one EntityManager, which manages all the entities for that World.
    ///
    /// Many EntityManager operations result in *structural changes* that change the layout of entities in memory.
    /// Before it can perform such operations, the EntityManager must wait for all running Jobs to complete, an event
    /// called a *sync point*. A sync point both blocks the main thread and prevents the application from taking
    /// advantage of all available cores as the running Jobs wind down.
    ///
    /// Although you cannot prevent sync points entirely, you should avoid them as much as possible. To this end, the ECS
    /// framework provides the <see cref="EntityCommandBuffer"/>, which allows you to queue structural changes so that
    /// they all occur at one time in the frame.
    /// </remarks>
    [Preserve]
    [NativeContainer]
    [DebuggerTypeProxy(typeof(EntityManagerDebugView))]
    [BurstCompatible]
    public unsafe partial struct EntityManager : IEquatable<EntityManager>
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;

#if UNITY_2020_1_OR_NEWER
        private static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<EntityManager>();
        [BurstDiscard]
        private static void CreateStaticSafetyId()
        {
            s_staticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<EntityManager>();
        }
#endif
        private bool m_IsInExclusiveTransaction;
#endif

        [NativeDisableUnsafePtrRestriction]
        private EntityDataAccess* m_EntityDataAccess;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void AssertIsExclusiveTransaction()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_IsInExclusiveTransaction == !m_EntityDataAccess->IsInExclusiveTransaction)
            {
                if (m_IsInExclusiveTransaction)
                    throw new InvalidOperationException("EntityManager cannot be used from this context because it is part of an exclusive transaction that has already ended.");
                throw new InvalidOperationException("EntityManager cannot be used from this context because it is not part of the exclusive transaction that is currently active.");
            }
#endif
        }

        internal EntityDataAccess* GetCheckedEntityDataAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            AssertIsExclusiveTransaction();
            return m_EntityDataAccess;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal bool IsInsideForEach => GetCheckedEntityDataAccess()->m_InsideForEach != 0;

        internal struct InsideForEach : IDisposable
        {
            EntityManager m_Manager;
            int m_InsideForEachSafety;

            public InsideForEach(EntityManager manager)
            {
                m_Manager = manager;
                EntityDataAccess* g = manager.GetCheckedEntityDataAccess();
                m_InsideForEachSafety = g->m_InsideForEach++;
            }

            public void Dispose()
            {
                EntityDataAccess* g = m_Manager.GetCheckedEntityDataAccess();
                int newValue = --g->m_InsideForEach;
                if (m_InsideForEachSafety != newValue)
                {
                    throw new InvalidOperationException("for each unbalanced");
                }
            }
        }
#endif

        // Attribute to indicate an EntityManager method makes structural changes.
        // Do not remove form EntityManager and please apply to all appropriate methods.
        [AttributeUsage(AttributeTargets.Method)]
        private class StructuralChangeMethodAttribute : Attribute
        {
        }

        /// <summary>
        /// The <see cref="World"/> of this EntityManager.
        /// </summary>
        /// <value>A World has one EntityManager and an EntityManager manages the entities of one World.</value>
        [NotBurstCompatible]
        public World World => GetCheckedEntityDataAccess()->ManagedEntityDataAccess.m_World;

        /// <summary>
        /// The latest entity generational version.
        /// </summary>
        /// <value>This is the version number that is assigned to a new entity. See <see cref="Entity.Version"/>.</value>
        public int Version => GetCheckedEntityDataAccess()->EntityComponentStore->EntityOrderVersion;

        /// <summary>
        /// A counter that increments after every system update.
        /// </summary>
        /// <remarks>
        /// The ECS framework uses the GlobalSystemVersion to track changes in a conservative, efficient fashion.
        /// Changes are recorded per component per chunk.
        /// </remarks>
        /// <seealso cref="ArchetypeChunk.DidChange"/>
        /// <seealso cref="ChangedFilterAttribute"/>
        public uint GlobalSystemVersion => GetCheckedEntityDataAccess()->EntityComponentStore->GlobalSystemVersion;

        /// <summary>
        /// The capacity of the internal entities array.
        /// </summary>
        /// <value>The number of entities the array can hold before it must be resized.</value>
        /// <remarks>
        /// The entities array automatically resizes itself when the entity count approaches the capacity.
        /// You should rarely need to set this value directly.
        ///
        /// **Important:** when you set this value (or when the array automatically resizes), the EntityManager
        /// first ensures that all Jobs finish. This can prevent the Job scheduler from utilizing available CPU
        /// cores and threads, resulting in a temporary performance drop.
        /// </remarks>
        public int EntityCapacity => GetCheckedEntityDataAccess()->EntityComponentStore->EntitiesCapacity;

        /// <summary>
        /// A EntityQuery instance that matches all components.
        /// </summary>
        public EntityQuery UniversalQuery => GetCheckedEntityDataAccess()->m_UniversalQuery;

        /// <summary>
        /// An object providing debugging information and operations.
        /// </summary>
        [NotBurstCompatible]
        public EntityManagerDebug Debug
        {
            get
            {
                var guts = GetCheckedEntityDataAccess()->ManagedEntityDataAccess;
                if (guts.m_Debug == null)
                    guts.m_Debug = new EntityManagerDebug(this);
                return guts.m_Debug;
            }
        }

        /// <summary>
        /// The total reserved address space for all Chunks in all Worlds.
        /// </summary>
        public static ulong TotalChunkAddressSpaceInBytes
        {
            get => Entities.EntityComponentStore.TotalChunkAddressSpaceInBytes;
            set => Entities.EntityComponentStore.TotalChunkAddressSpaceInBytes = value;
        }

        [NotBurstCompatible]
        internal void Initialize(World world)
        {
            TypeManager.Initialize();
            StructuralChange.Initialize();
            EntityCommandBuffer.Initialize();
            ECBInterop.Initialize();
            ChunkIterationUtility.Initialize();

            // Pick any recorded types that have come in after a domain reload.
            EarlyInitHelpers.FlushEarlyInits();
            SystemBaseRegistry.InitializePendingTypes();

            CreateJobReflectionData();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();

#if UNITY_2020_1_OR_NEWER
            if (s_staticSafetyId.Data == 0)
            {
                CreateStaticSafetyId();
            }
            AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, s_staticSafetyId.Data);
#endif

            m_IsInExclusiveTransaction = false;
#endif
            m_EntityDataAccess = (EntityDataAccess*)UnsafeUtility.Malloc(sizeof(EntityDataAccess), 16, Allocator.Persistent);
            UnsafeUtility.MemClear(m_EntityDataAccess, sizeof(EntityDataAccess));
            EntityDataAccess.Initialize(m_EntityDataAccess, world);
        }

        private void CreateJobReflectionData()
        {
            // Until we have reliable IL postprocessing or code generation we will have to resort to making these initialization calls manually.
            IJobBurstSchedulableExtensions.JobStruct<EntityComponentStore.EntityBatchFromEntityChunkDataShared>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<EntityComponentStore.SortEntityInChunk>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<EntityComponentStore.GetOrCreateDestroyedEntitiesJob>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<EntityDataAccess.DestroyChunks>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<ChunkPatchEntities>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<MoveChunksJob>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<MoveAllChunksJob>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<GatherAllManagedComponentIndicesJob>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<GatherManagedComponentIndicesInChunkJob>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<MoveFilteredChunksBetweenArchetypexJob>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<GatherChunksAndOffsetsJob>.Initialize();
            IJobBurstSchedulableExtensions.JobStruct<GatherChunksAndOffsetsWithFilteringJob>.Initialize();

            IJobParallelForExtensionsBurstSchedulable.ParallelForJobStructBurstSchedulable<EntityComponentStore.GatherEntityInChunkForEntities>.Initialize();
            IJobParallelForExtensionsBurstSchedulable.ParallelForJobStructBurstSchedulable<RemapChunksFilteredJob>.Initialize();
            IJobParallelForExtensionsBurstSchedulable.ParallelForJobStructBurstSchedulable<RemapAllChunksJob>.Initialize();
            IJobParallelForExtensionsBurstSchedulable.ParallelForJobStructBurstSchedulable<RemapAllArchetypesJob>.Initialize();
            IJobParallelForExtensionsBurstSchedulable.ParallelForJobStructBurstSchedulable<GatherChunks>.Initialize();
            IJobParallelForExtensionsBurstSchedulable.ParallelForJobStructBurstSchedulable<GatherChunksWithFiltering>.Initialize();
            IJobParallelForExtensionsBurstSchedulable.ParallelForJobStructBurstSchedulable<JoinChunksJob>.Initialize();
        }

        internal void PreDisposeCheck()
        {
            EndExclusiveEntityTransaction();
            GetCheckedEntityDataAccess()->DependencyManager->PreDisposeCheck();
        }

        [NotBurstCompatible]
        internal void DestroyInstance()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            PreDisposeCheck();

            GetCheckedEntityDataAccess()->Dispose();
            UnsafeUtility.Free(m_EntityDataAccess, Allocator.Persistent);
            m_EntityDataAccess = null;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
            m_Safety = default;
#endif
        }

        internal static EntityManager CreateEntityManagerInUninitializedState()
        {
            return new EntityManager();
        }

        public bool Equals(EntityManager other)
        {
            return m_EntityDataAccess == other.m_EntityDataAccess;
        }

        [NotBurstCompatible]
        public override bool Equals(object obj)
        {
            return obj is EntityManager other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((int)(long)m_EntityDataAccess);
        }

        public static bool operator==(EntityManager lhs, EntityManager rhs)
        {
            return lhs.m_EntityDataAccess == rhs.m_EntityDataAccess;
        }

        public static bool operator!=(EntityManager lhs, EntityManager rhs)
        {
            return lhs.m_EntityDataAccess != rhs.m_EntityDataAccess;
        }
    }
}
