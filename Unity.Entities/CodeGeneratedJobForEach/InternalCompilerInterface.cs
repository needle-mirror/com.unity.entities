using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    [BurstCompile]
    public static partial class InternalCompilerInterface
    {
        public static JobRunWithoutJobSystemDelegate BurstCompile(JobRunWithoutJobSystemDelegate d) => BurstCompiler.CompileFunctionPointer(d).Invoke;
        public static JobEntityBatchRunWithoutJobSystemDelegate BurstCompile(JobEntityBatchRunWithoutJobSystemDelegate d) => BurstCompiler.CompileFunctionPointer(d).Invoke;
        public static JobEntityBatchRunWithoutJobSystemDelegateLimitEntities BurstCompile(JobEntityBatchRunWithoutJobSystemDelegateLimitEntities d) => BurstCompiler.CompileFunctionPointer(d).Invoke;

        public delegate void JobEntityBatchRunWithoutJobSystemDelegate(ref ArchetypeChunkIterator iterator, IntPtr jobPtr);
        public delegate void JobRunWithoutJobSystemDelegate(IntPtr jobPtr);
        public delegate void JobEntityBatchRunWithoutJobSystemDelegateLimitEntities(ref EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength, IntPtr jobPtr);

        public static unsafe ref T UnsafeAsRef<T>(IntPtr value) where T : struct
        {
            return ref UnsafeUtility.AsRef<T>((byte*) value);
        }

        // Unsafe methods used to provide access for StructuralChanges
        public static unsafe T GetComponentData<T>(EntityManager manager, Entity entity, int typeIndex, out T originalComponent)
            where T : struct, IComponentData
        {
            var access = manager.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            UnsafeUtility.CopyPtrToStructure(ecs->GetComponentDataWithTypeRO(entity, typeIndex), out originalComponent);
            return originalComponent;
        }

        public static unsafe void WriteComponentData<T>(EntityManager manager, Entity entity, int typeIndex, ref T lambdaComponent, ref T originalComponent)
            where T : struct, IComponentData
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
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
                EntitiesJournaling.RecordSetComponentData(manager.World.Unmanaged, default, &entity, 1, &typeIndex, 1, ptr, sizeOf);
#endif
            }
        }

        public static void UnsafeCreateGatherEntitiesResult(ref EntityQuery query, out EntityQuery.GatherEntitiesResult result) =>
            query.GatherEntitiesToArray(out result);

        public static void UnsafeReleaseGatheredEntities(ref EntityQuery query, ref EntityQuery.GatherEntitiesResult result) =>
            query.ReleaseGatheredEntities(ref result);

        public static unsafe Entity UnsafeGetEntityFromGatheredEntities(ref EntityQuery.GatherEntitiesResult result, int entityIndex) =>
            result.EntityBuffer[entityIndex];

        public static unsafe IntPtr UnsafeGetChunkNativeArrayReadOnlyIntPtr<T>(ArchetypeChunk chunk, ComponentTypeHandle<T> typeHandle) where T : struct, IComponentData =>
            (IntPtr) chunk.GetComponentDataPtrRO(ref typeHandle);

        public static unsafe IntPtr UnsafeGetChunkNativeArrayIntPtr<T>(ArchetypeChunk chunk, ComponentTypeHandle<T> typeHandle) where T : struct, IComponentData =>
            (IntPtr) chunk.GetComponentDataPtrRW(ref typeHandle);

        public static unsafe IntPtr UnsafeGetChunkEntityArrayIntPtr(ArchetypeChunk chunk, EntityTypeHandle typeHandle) =>
            (IntPtr) chunk.GetNativeArray(typeHandle).GetUnsafeReadOnlyPtr();

        public static unsafe IntPtr UnsafeGetEntityArrayIntPtr(NativeArray<Entity> array) => (IntPtr) array.GetUnsafeReadOnlyPtr();

        public static unsafe T UnsafeGetCopyOfNativeArrayPtrElement<T>(IntPtr nativeArrayPtr, int elementIndex) where T : unmanaged =>
            *((T*) nativeArrayPtr + elementIndex);

        public static unsafe ref T UnsafeGetRefToNativeArrayPtrElement<T>(IntPtr nativeArrayPtr, int elementIndex) where T : unmanaged =>
            ref UnsafeUtility.AsRef<T>((T*) nativeArrayPtr + elementIndex);
    }
}
