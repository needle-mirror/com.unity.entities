using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities.Internal
{
    /// <summary>
    /// This exists only for internal use and is intended to be only used by source-generated code.
    /// DO NOT USE in user code (this API will change).
    /// </summary>
    [BurstCompile]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static partial class InternalCompilerInterface
    {
        public static JobRunWithoutJobSystemDelegate BurstCompile(JobRunWithoutJobSystemDelegate d) => BurstCompiler.CompileFunctionPointer(d).Invoke;
        public static JobChunkRunWithoutJobSystemDelegate BurstCompile(JobChunkRunWithoutJobSystemDelegate d) => BurstCompiler.CompileFunctionPointer(d).Invoke;
        public static JobChunkRunWithoutJobSystemDelegateLimitEntities BurstCompile(JobChunkRunWithoutJobSystemDelegateLimitEntities d) => BurstCompiler.CompileFunctionPointer(d).Invoke;

        public delegate void JobChunkRunWithoutJobSystemDelegate(ref EntityQuery query, IntPtr jobPtr);
        public delegate void JobRunWithoutJobSystemDelegate(IntPtr jobPtr);
        public delegate void JobChunkRunWithoutJobSystemDelegateLimitEntities(ref EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength, IntPtr jobPtr);

        public static unsafe ref T UnsafeAsRef<T>(IntPtr value) where T : struct
        {
            return ref UnsafeUtility.AsRef<T>((byte*) value);
        }

        // Unsafe methods used to provide access for StructuralChanges
        public static unsafe T GetComponentData<T>(EntityManager manager, Entity entity, TypeIndex typeIndex, out T originalComponent)
            where T : unmanaged, IComponentData
        {
            var access = manager.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            UnsafeUtility.CopyPtrToStructure(ecs->GetComponentDataWithTypeRO(entity, typeIndex), out originalComponent);
            return originalComponent;
        }

        public static unsafe void WriteComponentData<T>(EntityManager manager, Entity entity, TypeIndex typeIndex, ref T lambdaComponent, ref T originalComponent)
            where T : unmanaged, IComponentData
        {
            var access = manager.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var sizeOf = UnsafeUtility.SizeOf<T>();
            // MemCmp check is necessary to ensure we only write-back the value if we changed it in the lambda (or a called function)
            if (UnsafeUtility.MemCmp(UnsafeUtility.AddressOf(ref lambdaComponent), UnsafeUtility.AddressOf(ref originalComponent), sizeOf) != 0 &&
                ecs->HasComponent(entity, typeIndex))
            {
                var ptr = ecs->GetComponentDataWithTypeRW(entity, typeIndex, ecs->GlobalSystemVersion);
                UnsafeUtility.CopyStructureToPtr(ref lambdaComponent, ptr);
            }
        }

        public static void UnsafeCreateGatherEntitiesResult(ref EntityQuery query, out InternalGatherEntitiesResult result) =>
            query.GatherEntitiesToArray(out result);

        public static void UnsafeReleaseGatheredEntities(ref EntityQuery query, ref InternalGatherEntitiesResult result) =>
            query.ReleaseGatheredEntities(ref result);

        public static unsafe Entity UnsafeGetEntityFromGatheredEntities(ref InternalGatherEntitiesResult result, int entityIndex) =>
            result.EntityBuffer[entityIndex];

        public static unsafe IntPtr UnsafeGetChunkNativeArrayReadOnlyIntPtr<T>(ArchetypeChunk chunk, ref ComponentTypeHandle<T> typeHandle) where T : unmanaged, IComponentData =>
            (IntPtr) chunk.GetRequiredComponentDataPtrRO(ref typeHandle);

        public static unsafe IntPtr UnsafeGetChunkNativeArrayIntPtr<T>(ArchetypeChunk chunk, ref ComponentTypeHandle<T> typeHandle) where T : unmanaged, IComponentData =>
            (IntPtr) chunk.GetRequiredComponentDataPtrRW(ref typeHandle);

        /// <summary>
        /// There is no need to conduct the same checks in this method as we do in `GetRequiredComponentDataPtrRO` and `GetRequiredComponentDataPtrRW` --
        /// the source-generator has already ensured that everything is correctly set up.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe IntPtr UnsafeGetChunkNativeArrayReadOnlyIntPtrWithoutChecks<T>(in ArchetypeChunk chunk, ref ComponentTypeHandle<T> typeHandle) where T : unmanaged, IComponentData =>
            (IntPtr)ChunkDataUtility.GetComponentDataWithTypeRO(
                chunk.m_Chunk,
                chunk.m_Chunk->Archetype,
                0,
                typeHandle.m_TypeIndex,
                ref typeHandle.m_LookupCache);

        /// <summary>
        /// There is no need to conduct the same checks in this method as we do in `GetRequiredComponentDataPtrRO` and `GetRequiredComponentDataPtrRW` --
        /// the source-generator has already ensured that everything is correctly set up.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe IntPtr UnsafeGetChunkNativeArrayIntPtrWithoutChecks<T>(in ArchetypeChunk chunk, ref ComponentTypeHandle<T> typeHandle) where T : unmanaged, IComponentData
        {
            byte* ptr =
                ChunkDataUtility.GetComponentDataWithTypeRW(
                    chunk.m_Chunk,
                    chunk.m_Chunk->Archetype,
                    0,
                    typeHandle.m_TypeIndex,
                    typeHandle.GlobalSystemVersion,
                    ref typeHandle.m_LookupCache);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            if (Hint.Unlikely(chunk.m_EntityComponentStore->m_RecordToJournal != 0))
                chunk.JournalAddRecordGetComponentDataRW(ref typeHandle, ptr, typeHandle.m_LookupCache.ComponentSizeOf * chunk.Count);
#endif

            return (IntPtr)ptr;
        }

// `UnsafeGetUncheckedRefRO<T>()` is called from within source-generated code for `foreach` iterations
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UncheckedRefRO<T> UnsafeGetUncheckedRefRO<T>(IntPtr ptr, int index, ref ComponentTypeHandle<T> typeHandle) where T : unmanaged, IComponentData
            => new(ptr + UnsafeUtility.SizeOf<T>() * index, typeHandle.m_Safety);
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UncheckedRefRO<T> UnsafeGetUncheckedRefRO<T>(IntPtr ptr, int index) where T : unmanaged, IComponentData
            => new(ptr + UnsafeUtility.SizeOf<T>() * index);
#endif

// `UnsafeGetUncheckedRefRW<T>()` is called from within source-generated code for `foreach` iterations
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UncheckedRefRW<T> UnsafeGetUncheckedRefRW<T>(IntPtr ptr, int index, ref ComponentTypeHandle<T> typeHandle) where T : unmanaged, IComponentData
            => new(ptr + UnsafeUtility.SizeOf<T>() * index, typeHandle.m_Safety);
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UncheckedRefRW<T> UnsafeGetUncheckedRefRW<T>(IntPtr ptr, int index) where T : unmanaged, IComponentData
            => new(ptr + UnsafeUtility.SizeOf<T>() * index);
#endif

// `GetRefRO<T>()` is called from within source-generated `IJobChunk`s
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe RefRO<T> GetRefRO<T>(IntPtr ptr, int index, ref ComponentTypeHandle<T> typeHandle) where T : unmanaged, IComponentData
        {
            return new RefRO<T>((byte*)ptr + UnsafeUtility.SizeOf<T>() * index, typeHandle.m_Safety);
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe RefRO<T> GetRefRO<T>(IntPtr ptr, int index) where T : unmanaged, IComponentData
        {
            return new RefRO<T>((byte*)ptr + UnsafeUtility.SizeOf<T>() * index);
        }
#endif

// `GetRefRW<T>()` is called from within source-generated `IJobChunk`s
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe RefRW<T> GetRefRW<T>(IntPtr ptr, int index, ref ComponentTypeHandle<T> typeHandle) where T : unmanaged, IComponentData
        {
            return new RefRW<T>((byte*)ptr + UnsafeUtility.SizeOf<T>() * index, typeHandle.m_Safety);
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe RefRW<T> GetRefRW<T>(IntPtr ptr, int index) where T : unmanaged, IComponentData
        {
            return new RefRW<T>((byte*)ptr + UnsafeUtility.SizeOf<T>() * index);
        }
#endif

        /// <summary>
        /// This type is used by source-generators to circumvent per-component safety checks when iterating through `RefRO` types in `foreach` statements,
        /// by replacing `RefRO` with `UncheckedRefRO`.
        /// </summary>
        public readonly unsafe struct UncheckedRefRO<T> where T : unmanaged, IComponentData
        {
            private readonly IntPtr _ptr;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            private readonly AtomicSafetyHandle _safety;

            public UncheckedRefRO(IntPtr ptr, AtomicSafetyHandle safety)
            {
                _ptr = ptr;
                _safety = safety;
            }
#else
            public UncheckedRefRO(IntPtr ptr) => _ptr = ptr;
#endif
            public ref readonly T ValueRO => ref *(T*)_ptr;

            public static implicit operator RefRO<T>(UncheckedRefRO<T> @unchecked)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return new RefRO<T>((T*)@unchecked._ptr, @unchecked._safety);
#else
                return new RefRO<T>((T*)@unchecked._ptr);
#endif
            }
        }

        /// <summary>
        /// This type is used by source-generators to circumvent per-component safety checks when iterating through `RefRW` types in `foreach` statements,
        /// by replacing `RefRW` with `UncheckedRefRW`.
        /// </summary>
        public readonly unsafe struct UncheckedRefRW<T> where T : unmanaged, IComponentData
        {
            private readonly IntPtr _ptr;

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
            private readonly AtomicSafetyHandle _safety;

            public UncheckedRefRW(IntPtr ptr, AtomicSafetyHandle safety)
            {
                _ptr = ptr;
                _safety = safety;
            }
    #else
            public UncheckedRefRW(IntPtr ptr) => _ptr = ptr;
    #endif

            public ref T ValueRW => ref *(T*)_ptr;
            public ref readonly T ValueRO => ref *(T*)_ptr;

            public static implicit operator RefRW<T>(UncheckedRefRW<T> @unchecked)
            {
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                return new RefRW<T>((T*)@unchecked._ptr, @unchecked._safety);
    #else
                return new RefRW<T>((T*)@unchecked._ptr);
    #endif
            }
        }

        public static unsafe IntPtr UnsafeGetChunkEntityArrayIntPtr(ArchetypeChunk chunk, EntityTypeHandle typeHandle) =>
            (IntPtr) chunk.GetEntityDataPtrRO(typeHandle);

        public static unsafe IntPtr UnsafeGetEntityArrayIntPtr(NativeArray<Entity> array) => (IntPtr) array.GetUnsafeReadOnlyPtr();

        public static unsafe T UnsafeGetCopyOfNativeArrayPtrElement<T>(IntPtr nativeArrayPtr, int elementIndex) where T : unmanaged =>
            *((T*) nativeArrayPtr + elementIndex);

        public static unsafe ref T UnsafeGetRefToNativeArrayPtrElement<T>(IntPtr nativeArrayPtr, int elementIndex) where T : unmanaged =>
            ref UnsafeUtility.AsRef<T>((T*) nativeArrayPtr + elementIndex);

        public static unsafe ref SystemState UnsafeGetSystemStateRef(IntPtr statePtr) => ref *(SystemState*) statePtr;

        public static class EntityQueryInterface
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool HasComponentsRequiredForExecuteMethodToRun(ref EntityQuery userDefinedQuery, ref Span<ComponentType> componentsUsedInExecuteMethod) =>
                userDefinedQuery.HasComponentsRequiredForExecuteMethodToRun(ref componentsUsedInExecuteMethod);
        }

        public static class JobChunkInterface
        {
            public static void RunWithoutJobsInternal<T>(ref T jobData, ref EntityQuery query)
                where T : struct, IJobChunk
            {
                JobChunkExtensions.RunByRefWithoutJobs(ref jobData, query);
            }

            public static void RunWithoutJobs<T>(ref T jobData, EntityQuery query)
                where T : struct, IJobChunk
            {
                jobData.RunByRefWithoutJobs(query);
            }

            public static void RunByRefWithoutJobs<T>(ref T jobData, EntityQuery query)
                where T : struct, IJobChunk
            {
                jobData.RunByRefWithoutJobs(query);
            }

            public static unsafe JobHandle Schedule<T>(
                T jobData,
                EntityQuery query,
                JobHandle dependsOn)
                where T : struct, IJobChunk
            {
                return JobChunkExtensions.ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, default(NativeArray<int>));
            }

            public static unsafe JobHandle ScheduleByRef<T>(
                ref T jobData,
                EntityQuery query,
                JobHandle dependsOn)
                where T : struct, IJobChunk
            {
                return JobChunkExtensions.ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, default(NativeArray<int>));
            }

            public static unsafe JobHandle ScheduleParallel<T>(
                T jobData,
                EntityQuery query,
                JobHandle dependsOn)
                where T : unmanaged, IJobChunk
            {
                return JobChunkExtensions.ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, default(NativeArray<int>));
            }

            public static unsafe JobHandle ScheduleParallelByRef<T>(
                ref T jobData,
                EntityQuery query,
                JobHandle dependsOn)
                where T : unmanaged, IJobChunk
            {
                return JobChunkExtensions.ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, default(NativeArray<int>));
            }

            public static JobHandle ScheduleParallel<T>(T jobData, EntityQuery query, JobHandle dependsOn,
                NativeArray<int> chunkBaseEntityIndices)
                where T : struct, IJobChunk
            {
                return JobChunkExtensions.ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel,
                    chunkBaseEntityIndices);
            }

            public static JobHandle ScheduleParallelByRef<T>(ref T jobData, EntityQuery query, JobHandle dependsOn,
                NativeArray<int> chunkBaseEntityIndices)
                where T : struct, IJobChunk
            {
                return JobChunkExtensions.ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel,
                    chunkBaseEntityIndices);
            }
        }

        [BurstCompile]
        public static void MergeWith(ref UnsafeList<ComponentType> mergeThese, ref UnsafeList<ComponentType> withThese)
        {
            mergeThese.AddRange(withThese);
            var componentTypeToAccessMode = new NativeHashMap<int, ComponentType.AccessMode>(initialCapacity: mergeThese.Length, Allocator.Temp);

            foreach (var componentType in mergeThese)
            {
                if (!componentTypeToAccessMode.TryGetValue(componentType.TypeIndex, out var accessMode))
                    componentTypeToAccessMode.Add(componentType.TypeIndex, componentType.AccessModeType);
                else
                {
                    if ((accessMode == ComponentType.AccessMode.Exclude && componentType.AccessModeType != ComponentType.AccessMode.Exclude)
                        || (accessMode != ComponentType.AccessMode.Exclude && componentType.AccessModeType == ComponentType.AccessMode.Exclude))
                    {
                        throw new ArgumentException("A component cannot be both excluded and included.");
                    }

                    if (componentType.AccessModeType == ComponentType.AccessMode.ReadWrite)
                        componentTypeToAccessMode[componentType.TypeIndex] = ComponentType.AccessMode.ReadWrite;
                }
            }

            mergeThese.Clear();

            foreach (var kvp in componentTypeToAccessMode)
                mergeThese.Add(new ComponentType { TypeIndex = kvp.Key, AccessModeType = kvp.Value });

            componentTypeToAccessMode.Dispose();
        }

        /// <summary>
        /// Used internally to get a single query for when you are inside a SystemBase property or generic member method.
        /// AS A USER, PLEASE DON'T USE THIS, ANYTHING BUT THIS!!
        /// </summary>
        /// <param name="system">System to attach query to.</param>
        /// <typeparam name="T">single type to query after</typeparam>
        /// <returns>An entity query for the SystemBase</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityQuery OnlyAllowedInSourceGeneratedCodeGetSingleQuery<T>(SystemBase system)
            => new EntityQueryBuilder(system.WorldUpdateAllocator).WithAllRW<T>().Build(system);

        /// <summary>
        /// Used internally by all Source Generation stubs. It throws an InvalidOperations from Source-gen not running.
        /// </summary>
        /// <returns>InvalidOperations from Source-gen not running.</returns>
        /// <exception cref="InvalidOperationException">Source-gen not run</exception>
        internal static InvalidOperationException ThrowCodeGenException() => throw new InvalidOperationException("No suitable code replacement generated, this is either due to generators failing, or lack of support in your current context");
    }
}
