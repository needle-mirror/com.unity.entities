using System;
using System.Diagnostics;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Profiling;

namespace Unity.Entities
{
    /// <summary>
    /// The ComponentDependencyManager maintains JobHandles for each type with any jobs that read or write those component types.
    /// ComponentSafetyHandles which is embedded maintains a safety handle for each component type registered in the TypeManager.
    /// Safety and job handles are only maintained for components that can be modified by jobs:
    /// That means only dynamic buffer components and component data that are not tag components will have valid
    /// safety and job handles. For those components the safety handle represents ReadOnly or ReadWrite access to those
    /// components as well as their change versions.
    /// The Entity type is a special case: It can not be modified by jobs and its safety handle is used to represent the
    /// entire EntityManager state. Any job reading from any part of the EntityManager must contain either a safety handle
    /// for the Entity type OR a safety handle for any other component type.
    /// Job component systems that have no other type dependencies have their JobHandles registered on the Entity type
    /// to ensure that they are completed by CompleteAllJobsAndInvalidateArrays
    /// </summary>
#if !ENABLE_SIMPLE_SYSTEM_DEPENDENCIES
    unsafe partial struct ComponentDependencyManager
    {
        struct DependencyHandle
        {
            public JobHandle WriteFence;
            public int       NumReadFences;
            public TypeIndex TypeIndex;
        }

        const int              kMaxReadJobHandles = 17;
        const int              kMaxTypes = TypeManager.MaximumTypesCount;

        JobHandle*             m_JobDependencyCombineBuffer;
        int                    m_JobDependencyCombineBufferCount;

        // Indexed by TypeIndex
        ushort*                m_TypeArrayIndices;
        DependencyHandle*      m_DependencyHandles;
        ushort                 m_DependencyHandlesCount;
        JobHandle*             m_ReadJobFences;

        TypeIndex              EntityTypeIndex;
        const ushort           NullTypeIndex = 0xFFFF;

        JobHandle              m_ExclusiveTransactionDependency;
        byte                   _IsInTransaction;

        private ProfilerMarker m_Marker;
        private WorldUnmanaged m_World;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public ComponentSafetyHandles Safety;
#endif
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
        public ForEachDisallowStructuralChangeSupport ForEachStructuralChange;
#endif

        ushort GetTypeArrayIndex(TypeIndex typeIndex)
        {
            var arrayIndex = m_TypeArrayIndices[typeIndex.Index];
            if (arrayIndex != NullTypeIndex)
                return arrayIndex;

            Assert.IsFalse(TypeManager.IsZeroSized(typeIndex) && !TypeManager.IsEnableable(typeIndex));
            arrayIndex = m_DependencyHandlesCount++;
            m_TypeArrayIndices[typeIndex.Index] = arrayIndex;
            m_DependencyHandles[arrayIndex].TypeIndex = typeIndex;
            m_DependencyHandles[arrayIndex].NumReadFences = 0;
            m_DependencyHandles[arrayIndex].WriteFence = new JobHandle();

            return arrayIndex;
        }

        void ClearDependencies()
        {
            for (int i = 0; i < m_DependencyHandlesCount; ++i)
                m_TypeArrayIndices[m_DependencyHandles[i].TypeIndex.Index] = NullTypeIndex;
            m_DependencyHandlesCount = 0;
        }

        public void OnCreate(WorldUnmanaged world)
        {
            m_World = world;
            m_TypeArrayIndices = (ushort*)Memory.Unmanaged.Allocate(sizeof(ushort) * kMaxTypes, 16, Allocator.Persistent);
            UnsafeUtility.MemSet(m_TypeArrayIndices, 0xFF, sizeof(ushort) * kMaxTypes);

            m_ReadJobFences = (JobHandle*)Memory.Unmanaged.Allocate(sizeof(JobHandle) * kMaxReadJobHandles * kMaxTypes, 16, Allocator.Persistent);
            UnsafeUtility.MemClear(m_ReadJobFences, sizeof(JobHandle) * kMaxReadJobHandles * kMaxTypes);

            EntityTypeIndex = TypeManager.GetTypeIndex<Entity>();

            m_DependencyHandles = (DependencyHandle*)Memory.Unmanaged.Allocate(sizeof(DependencyHandle) * kMaxTypes, 16, Allocator.Persistent);
            UnsafeUtility.MemClear(m_DependencyHandles, sizeof(DependencyHandle) * kMaxTypes);

            m_JobDependencyCombineBufferCount = 4 * 1024;
            m_JobDependencyCombineBuffer = (JobHandle*)Memory.Unmanaged.Allocate(sizeof(DependencyHandle) * m_JobDependencyCombineBufferCount, 16, Allocator.Persistent);

            m_DependencyHandlesCount = 0;
            _IsInTransaction = 0;
            m_ExclusiveTransactionDependency = default;

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            ForEachStructuralChange.Init();
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.OnCreate();
#endif
            m_Marker = new ProfilerMarker("CompleteAllJobs");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void AssertCompleteSyncPoint()
        {
            if (JobsUtility.IsExecutingJob)
            {
                throw new InvalidOperationException(
                    "Jobs accessing the entity manager must issue a complete sync point");
            }
        }

        public void CompleteAllJobs()
        {
            var executingSystem = m_World.ExecutingSystem;
            if (executingSystem != default)
            {
                var systemState = m_World.ResolveSystemState(executingSystem);
                if (systemState != null)
                    systemState->m_JobHandle.Complete();
            }

            if (m_DependencyHandlesCount != 0)
            {
                AssertCompleteSyncPoint();
                m_Marker.Begin();
                for (int t = 0; t < m_DependencyHandlesCount; ++t)
                {
                    m_DependencyHandles[t].WriteFence.Complete();

                    var readFencesCount = m_DependencyHandles[t].NumReadFences;
                    var readFences = m_ReadJobFences + t * kMaxReadJobHandles;
                    for (var r = 0; r != readFencesCount; r++)
                        readFences[r].Complete();
                    m_DependencyHandles[t].NumReadFences = 0;
                }
                ClearDependencies();
                m_Marker.End();
            }
        }

        public void CompleteAllJobsAndInvalidateArrays()
        {
            CompleteAllJobs();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.CompleteAllJobsAndInvalidateArrays();
#endif
        }

        public void CompleteAllJobsAndCheckDeallocateAndThrow()
        {
            CompleteAllJobs();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.CheckAllJobsCanDeallocate();
#endif
        }

        public void Dispose()
        {
            for (var i = 0; i < m_DependencyHandlesCount; i++)
                m_DependencyHandles[i].WriteFence.Complete();

            for (var i = 0; i < m_DependencyHandlesCount * kMaxReadJobHandles; i++)
                m_ReadJobFences[i].Complete();

            Memory.Unmanaged.Free(m_JobDependencyCombineBuffer, Allocator.Persistent);

            Memory.Unmanaged.Free(m_TypeArrayIndices, Allocator.Persistent);
            Memory.Unmanaged.Free(m_DependencyHandles, Allocator.Persistent);
            m_DependencyHandles = null;

            Memory.Unmanaged.Free(m_ReadJobFences, Allocator.Persistent);
            m_ReadJobFences = null;

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            ForEachStructuralChange.Dispose();
#endif
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.Dispose();
#endif
        }

        public void PreDisposeCheck()
        {
            for (var i = 0; i < m_DependencyHandlesCount; i++)
                m_DependencyHandles[i].WriteFence.Complete();

            for (var i = 0; i < m_DependencyHandlesCount * kMaxReadJobHandles; i++)
                m_ReadJobFences[i].Complete();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.PreDisposeCheck();
#endif
        }

        public void CompleteDependenciesNoChecks(TypeIndex* readerTypes, int readerTypesCount, TypeIndex* writerTypes, int writerTypesCount)
        {
            for (var i = 0; i != writerTypesCount; i++)
                CompleteReadAndWriteDependencyNoChecks(writerTypes[i]);

            for (var i = 0; i != readerTypesCount; i++)
                CompleteWriteDependencyNoChecks(readerTypes[i]);
        }

        public bool HasReaderOrWriterDependency(TypeIndex typeIndex, JobHandle dependency)
        {
            var typeArrayIndex = m_TypeArrayIndices[typeIndex.Index];
            if (typeArrayIndex == NullTypeIndex)
                return false;

            var writer = m_DependencyHandles[typeArrayIndex].WriteFence;
            if (JobHandle.CheckFenceIsDependencyOrDidSyncFence(dependency, writer))
                return true;

            var count = m_DependencyHandles[typeArrayIndex].NumReadFences;
            for (var r = 0; r < count; r++)
            {
                var reader = m_ReadJobFences[typeArrayIndex * kMaxReadJobHandles + r];
                if (JobHandle.CheckFenceIsDependencyOrDidSyncFence(dependency, reader))
                    return true;
            }

            return false;
        }

        public JobHandle GetDependency(TypeIndex* readerTypes, int readerTypesCount, TypeIndex* writerTypes, int writerTypesCount)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (readerTypesCount * kMaxReadJobHandles + writerTypesCount > m_JobDependencyCombineBufferCount)
                throw new ArgumentException("Too many readers & writers in GetDependency");
#endif

            var count = 0;
            for (var i = 0; i != readerTypesCount; i++)
            {
                var typeArrayIndex = m_TypeArrayIndices[readerTypes[i].Index];
                if (typeArrayIndex != NullTypeIndex)
                    m_JobDependencyCombineBuffer[count++] = m_DependencyHandles[typeArrayIndex].WriteFence;
            }

            for (var i = 0; i != writerTypesCount; i++)
            {
                var typeArrayIndex = m_TypeArrayIndices[writerTypes[i].Index];
                if (typeArrayIndex == NullTypeIndex)
                    continue;

                m_JobDependencyCombineBuffer[count++] = m_DependencyHandles[typeArrayIndex].WriteFence;

                var numReadFences = m_DependencyHandles[typeArrayIndex].NumReadFences;
                for (var j = 0; j != numReadFences; j++)
                    m_JobDependencyCombineBuffer[count++] = m_ReadJobFences[typeArrayIndex * kMaxReadJobHandles + j];
            }

            return JobHandleUnsafeUtility.CombineDependencies(m_JobDependencyCombineBuffer, count);
        }

        public JobHandle AddDependency(TypeIndex* readerTypes, int readerTypesCount, TypeIndex* writerTypes, int writerTypesCount,
            JobHandle dependency)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            JobHandle* combinedDependencies = null;
            var combinedDependenciesCount = 0;
#endif
            if (readerTypesCount == 0 && writerTypesCount == 0)
            {
                ushort entityTypeArrayIndex = GetTypeArrayIndex(EntityTypeIndex);
                // if no dependency types are provided add read dependency to the Entity type
                // to ensure these jobs are still synced by CompleteAllJobsAndInvalidateArrays
                m_ReadJobFences[entityTypeArrayIndex * kMaxReadJobHandles +
                                m_DependencyHandles[entityTypeArrayIndex].NumReadFences] = dependency;
                m_DependencyHandles[entityTypeArrayIndex].NumReadFences++;

                if (m_DependencyHandles[entityTypeArrayIndex].NumReadFences == kMaxReadJobHandles)
                {
                    //@TODO: Check dynamically if the job debugger is enabled?
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    return CombineReadDependencies(entityTypeArrayIndex);
#else
                    CombineReadDependencies(entityTypeArrayIndex);
#endif
                }
                return dependency;
            }

            for (var i = 0; i != writerTypesCount; i++)
            {
                m_DependencyHandles[GetTypeArrayIndex(writerTypes[i])].WriteFence = dependency;
            }


            for (var i = 0; i != readerTypesCount; i++)
            {
                var reader = GetTypeArrayIndex(readerTypes[i]);
                m_ReadJobFences[reader * kMaxReadJobHandles + m_DependencyHandles[reader].NumReadFences] =
                    dependency;
                m_DependencyHandles[reader].NumReadFences++;

                if (m_DependencyHandles[reader].NumReadFences == kMaxReadJobHandles)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    var combined = CombineReadDependencies(reader);
                    if (combinedDependencies == null)
                    {
                        JobHandle* temp = stackalloc JobHandle[readerTypesCount];
                        combinedDependencies = temp;
                    }

                    combinedDependencies[combinedDependenciesCount++] = combined;
#else
                    CombineReadDependencies(reader);
#endif
                }
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (combinedDependencies != null)
                return JobHandleUnsafeUtility.CombineDependencies(combinedDependencies, combinedDependenciesCount);
            return dependency;
#else
            return dependency;
#endif
        }

        internal void CompleteWriteDependencyNoChecks(TypeIndex typeIndex)
        {
            var arrayIndex = m_TypeArrayIndices[typeIndex.Index];
            if (arrayIndex != NullTypeIndex)
                m_DependencyHandles[arrayIndex].WriteFence.Complete();
        }

        internal void CompleteReadAndWriteDependencyNoChecks(TypeIndex typeIndex)
        {
            var arrayIndex = m_TypeArrayIndices[typeIndex.Index];
            if (arrayIndex != NullTypeIndex)
            {
                for (var i = 0; i < m_DependencyHandles[arrayIndex].NumReadFences; ++i)
                    m_ReadJobFences[arrayIndex * kMaxReadJobHandles + i].Complete();
                m_DependencyHandles[arrayIndex].NumReadFences = 0;

                m_DependencyHandles[arrayIndex].WriteFence.Complete();
            }
        }

        public void CompleteWriteDependency(TypeIndex type)
        {
            CompleteWriteDependencyNoChecks(type);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.CompleteWriteDependency(type);
#endif
        }

        public void CompleteReadAndWriteDependency(TypeIndex type)
        {
            CompleteReadAndWriteDependencyNoChecks(type);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (TypeManager.GetTypeInfo(type).Category == TypeManager.TypeCategory.EntityData)
                throw new InvalidOperationException($"Can't complete a write-dependency for type Unity.Entities.Entity");
            Safety.CompleteReadAndWriteDependency(type);
#endif
        }

        JobHandle CombineReadDependencies(ushort typeArrayIndex)
        {
            var combined = JobHandleUnsafeUtility.CombineDependencies(
                m_ReadJobFences + typeArrayIndex * kMaxReadJobHandles, m_DependencyHandles[typeArrayIndex].NumReadFences);

            m_ReadJobFences[typeArrayIndex * kMaxReadJobHandles] = combined;
            m_DependencyHandles[typeArrayIndex].NumReadFences = 1;

            return combined;
        }

        JobHandle GetAllDependencies()
        {
            var jobHandles = new NativeArray<JobHandle>(m_DependencyHandlesCount * (kMaxReadJobHandles + 1), Allocator.Temp);

            var count = 0;
            for (var i = 0; i != m_DependencyHandlesCount; i++)
            {
                jobHandles[count++] = m_DependencyHandles[i].WriteFence;

                var numReadFences = m_DependencyHandles[i].NumReadFences;
                for (var j = 0; j != numReadFences; j++)
                    jobHandles[count++] = m_ReadJobFences[i * kMaxReadJobHandles + j];
            }

            var combined = JobHandle.CombineDependencies(jobHandles);
            jobHandles.Dispose();

            return combined;
        }
    }
#else
    unsafe partial struct ComponentDependencyManager
    {
        JobHandle              m_Dependency;
        JobHandle              m_ExclusiveTransactionDependency;
        byte                   _IsInTransaction;
        WorldUnmanaged         m_World;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public ComponentSafetyHandles Safety;
#endif
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
        public ForEachDisallowStructuralChangeSupport ForEachStructuralChange;
#endif

        public void OnCreate(WorldUnmanaged world)
        {
            m_Dependency = default;
            _IsInTransaction = 0;
            m_World = world;
            m_ExclusiveTransactionDependency = default;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            ForEachStructuralChange.Init();
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.OnCreate();
#endif
        }

        void CompleteAllJobs()
        {
            var executingSystem = m_World.ExecutingSystem;
            if (executingSystem != default)
            {
                var systemState = m_World.ResolveSystemState(executingSystem);
                if (systemState != null)
                    systemState->m_JobHandle.Complete();
            }

            m_Dependency.Complete();
        }

        public void CompleteAllJobsAndInvalidateArrays()
        {
            CompleteAllJobs();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.CompleteAllJobsAndInvalidateArrays();
#endif
        }

        public void CompleteAllJobsAndCheckDeallocateAndThrow()
        {
            CompleteAllJobs();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.CheckAllJobsCanDeallocate();
#endif
        }

        public void Dispose()
        {
            m_Dependency.Complete();

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            ForEachStructuralChange.Dispose();
#endif
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.Dispose();
#endif
        }

        public void PreDisposeCheck()
        {
            m_Dependency.Complete();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.PreDisposeCheck();
#endif
        }

        public void CompleteDependenciesNoChecks(TypeIndex* readerTypes, int readerTypesCount, TypeIndex* writerTypes, int writerTypesCount)
        {
            m_Dependency.Complete();
        }

        public bool HasReaderOrWriterDependency(TypeIndex type, JobHandle dependency)
        {
            return JobHandle.CheckFenceIsDependencyOrDidSyncFence(dependency, m_Dependency);
        }

        public JobHandle GetDependency(TypeIndex* readerTypes, int readerTypesCount, TypeIndex* writerTypes, int writerTypesCount)
        {
            return m_Dependency;
        }

        public JobHandle AddDependency(TypeIndex* readerTypes, int readerTypesCount, TypeIndex* writerTypes, int writerTypesCount, JobHandle dependency)
        {
            m_Dependency = dependency;
            return dependency;
        }

        public void CompleteWriteDependency(TypeIndex type)
        {
            m_Dependency.Complete();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.CompleteWriteDependency(type);
#endif
        }

        public void CompleteReadAndWriteDependency(TypeIndex type)
        {
            m_Dependency.Complete();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.CompleteReadAndWriteDependency(type);
#endif
        }

        internal void CompleteWriteDependencyNoChecks(TypeIndex type)
        {
            m_Dependency.Complete();
        }

        internal void CompleteReadAndWriteDependencyNoChecks(TypeIndex type)
        {
            m_Dependency.Complete();
        }


        void ClearDependencies()
        {
            m_Dependency = default;
        }

        JobHandle GetAllDependencies() => m_Dependency;
    }
#endif

    // Shared code of the above two different implementation
    partial struct ComponentDependencyManager
    {
        internal bool IsInTransaction
        {
            get { return _IsInTransaction != 0; }
            set { _IsInTransaction = (byte) (value ? 1u : 0u); }
        }

        public JobHandle ExclusiveTransactionDependency
        {
            get { return m_ExclusiveTransactionDependency; }
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                if (_IsInTransaction == 0)
                    throw new InvalidOperationException(
                        "EntityManager.ExclusiveEntityTransactionDependency can only be used after EntityManager.BeginExclusiveEntityTransaction has been called.");
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!JobHandle.CheckFenceIsDependencyOrDidSyncFence(m_ExclusiveTransactionDependency, value))
                    throw new InvalidOperationException(
                        message: "EntityManager.ExclusiveEntityTransactionDependency must depend on the Entity Transaction job. \n" + 
"Correct usage looks like EntityManager.ExclusiveEntityTransactionDependency = \n" +
"yourJob.Schedule(EntityManager.ExclusiveEntityTransactionDependency) or \n" +
"EntityManager.ExclusiveEntityTransactionDependency = \n" +
"yourJob.Schedule(JobHandle.CombineDependencies(EntityManager.ExclusiveEntityTransactionDependency, someOtherJobHandle) \n"+
                "This is so that you don't lose the pre-existing job dependency when adding your own to the chain.");
#endif
                m_ExclusiveTransactionDependency = value;
            }
        }

        public void PreEndExclusiveTransaction()
        {
            if (_IsInTransaction == 1)
            {
                m_ExclusiveTransactionDependency.Complete();
            }
        }

        public void EndExclusiveTransaction()
        {
            if (_IsInTransaction == 0)
                return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.EndExclusiveTransaction();
#endif
            _IsInTransaction = 0;
        }

        public void BeginExclusiveTransaction()
        {
            if (_IsInTransaction == 1)
                return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Safety.BeginExclusiveTransaction();
#endif

            _IsInTransaction = 1;
            m_ExclusiveTransactionDependency = GetAllDependencies();
            ClearDependencies();
        }
    }

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
    internal unsafe struct ForEachDisallowStructuralChangeSupport
    {
        const int         kMaxNestedForEachDisallowStructuralChange = 32;

        internal int      Depth;

        EntityQueryMask*  _forEachQueryMasks;

        public void Init()
        {
            Depth = 0;
            _forEachQueryMasks = (EntityQueryMask*)Memory.Unmanaged.Allocate(sizeof(EntityQueryMask) * kMaxNestedForEachDisallowStructuralChange, 16, Allocator.Persistent);
        }

        internal void BeginIsInForEach(EntityQueryImpl* query)
        {
            CheckIsWithinMaxAllowedNestedForEach(Depth);
            if (query->_QueryData->HasEnableableComponents != 0)
                _forEachQueryMasks[Depth++] = new EntityQueryMask();
            else
                _forEachQueryMasks[Depth++] = query->GetEntityQueryMask();
        }

        void CheckIsWithinMaxAllowedNestedForEach(int value)
        {
            if (value >= kMaxNestedForEachDisallowStructuralChange)
                throw new InvalidOperationException(
                    $"The maximum allowed number of nested foreach with structural changes is {kMaxNestedForEachDisallowStructuralChange}.");
        }

        internal void SetIsInForEachDisallowStructuralChangeCounter(int counter)
        {
            CheckIsWithinMaxAllowedNestedForEach(counter);
            if (counter > Depth)
            {
                ClearEntityQueryCacheFrom(Depth);
            }

            Depth = counter;
        }

        void ClearEntityQueryCacheFrom(int index)
        {
            UnsafeUtility.MemClear(
                _forEachQueryMasks + index * sizeof(EntityQueryMask),
                sizeof(EntityQueryMask) * (kMaxNestedForEachDisallowStructuralChange - Depth));
        }

        internal void EndIsInForEach()
        {
            Depth--;
        }

        internal bool IsAdditiveStructuralChangePossible(Archetype* archetype)
        {
            for (var i = 0; i < Depth; i++)
            {
                if (_forEachQueryMasks[i].IsCreated() && _forEachQueryMasks[i].Matches(archetype))
                    return false;
            }

            return true;
        }

        public void Dispose()
        {
            Memory.Unmanaged.Free(_forEachQueryMasks, Allocator.Persistent);
        }
    }
#endif
}
