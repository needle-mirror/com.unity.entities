using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities
{
    [BurstCompile]
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

        public static void UnsafeCreateGatherEntitiesResult(ref EntityQuery query, out EntityQuery.GatherEntitiesResult result) =>
            query.GatherEntitiesToArray(out result);

        public static void UnsafeReleaseGatheredEntities(ref EntityQuery query, ref EntityQuery.GatherEntitiesResult result) =>
            query.ReleaseGatheredEntities(ref result);

        public static unsafe Entity UnsafeGetEntityFromGatheredEntities(ref EntityQuery.GatherEntitiesResult result, int entityIndex) =>
            result.EntityBuffer[entityIndex];

        public static unsafe IntPtr UnsafeGetChunkNativeArrayReadOnlyIntPtr<T>(ArchetypeChunk chunk, ref ComponentTypeHandle<T> typeHandle) where T : unmanaged, IComponentData =>
            (IntPtr) chunk.GetRequiredComponentDataPtrRO(ref typeHandle);

        public static unsafe IntPtr UnsafeGetChunkNativeArrayIntPtr<T>(ArchetypeChunk chunk, ref ComponentTypeHandle<T> typeHandle) where T : unmanaged, IComponentData =>
            (IntPtr) chunk.GetRequiredComponentDataPtrRW(ref typeHandle);

        public static unsafe IntPtr UnsafeGetChunkEntityArrayIntPtr(ArchetypeChunk chunk, EntityTypeHandle typeHandle) =>
            (IntPtr) chunk.GetEntityDataPtrRO(typeHandle);

        public static unsafe IntPtr UnsafeGetEntityArrayIntPtr(NativeArray<Entity> array) => (IntPtr) array.GetUnsafeReadOnlyPtr();

        public static unsafe T UnsafeGetCopyOfNativeArrayPtrElement<T>(IntPtr nativeArrayPtr, int elementIndex) where T : unmanaged =>
            *((T*) nativeArrayPtr + elementIndex);

        public static unsafe ref T UnsafeGetRefToNativeArrayPtrElement<T>(IntPtr nativeArrayPtr, int elementIndex) where T : unmanaged =>
            ref UnsafeUtility.AsRef<T>((T*) nativeArrayPtr + elementIndex);

        public static unsafe ref SystemState UnsafeGetSystemStateRef(IntPtr statePtr) => ref *(SystemState*) statePtr;

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

        /// <summary>
        /// Used internally by all Source Generation stubs. It throws an InvalidOperations from Source-gen not running.
        /// </summary>
        /// <returns>InvalidOperations from Source-gen not running.</returns>
        /// <exception cref="InvalidOperationException">Source-gen not run</exception>
        internal static InvalidOperationException ThrowCodeGenException() => throw new InvalidOperationException("No suitable code replacement generated, this is either due to generators failing, or lack of support in your current context");
    }
}
