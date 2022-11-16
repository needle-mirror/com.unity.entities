using System.Collections;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Burst.Intrinsics;
using Unity.Core;
using Unity.Entities.CodeGeneratedJobForEach;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities
{
    public class EntitiesMock { }

    public struct Entity : IQueryTypeParameter { }

    public struct EntityQueryBuilder
    {
        public EntityQueryBuilder(Allocator allocator)
        {
        }

        public void Dispose()
        {
        }

        public void Reset()
        {
        }

        public EntityQueryBuilder WithAll<T>() => this;
        public EntityQueryBuilder WithAny<T>() => this;
        public EntityQueryBuilder WithNone<T>() => this;
        public EntityQueryBuilder WithOptions() => this;
        public EntityQueryBuilder AddAdditionalQuery() => this;
        public EntityQuery Build(ref SystemState systemState) => default;
    }

    unsafe public struct BlobBuilder : IDisposable
    {
        public BlobBuilder(Allocator allocator, int chunkSize = 65536) {}
        public BlobBuilderArray<T> Construct<T>(ref BlobArray<T> blobArray, params T[] data) where T : struct => default;
        public ref T ConstructRoot<T>() where T : struct => throw new NotImplementedException();
        public ref T SetPointer<T>(ref BlobPtr<T> ptr, ref T obj) where T : struct => throw new NotImplementedException();
        public BlobBuilderArray<T> Allocate<T>(ref BlobArray<T> ptr, int length) where T : struct => default;
        public void Dispose() {}
        public BlobAssetReference<T> CreateBlobAssetReference<T>(Allocator allocator) where T : unmanaged => default;
    }

    public unsafe struct BlobAssetReference<T> where T : unmanaged
    {
        public ref T Value => throw new NotImplementedException();
    }

    public class MayOnlyLiveInBlobStorageAttribute : Attribute {}

    public unsafe ref struct BlobBuilderArray<T> where T : struct
    {
        public ref T this[int index] => throw new NotImplementedException();
    }
    [MayOnlyLiveInBlobStorage] public unsafe struct BlobArray<T> where T : struct {}
    [MayOnlyLiveInBlobStorage] public unsafe struct BlobPtr<T> where T : struct {}

    public interface IJobChunk
    {
        void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask);
    }

    public interface IJobEntity{}

    public readonly struct RefRW<T> : IQueryTypeParameter where T : struct, IComponentData
    {
        public unsafe RefRW(NativeArray<T> componentDatas, int index){}
    }

    public readonly struct RefRO<T> : IQueryTypeParameter where T : struct, IComponentData
    {
        public unsafe RefRO(NativeArray<T> componentDatas, int index){}
    }

    public readonly struct SafeBitRef
    {

    }
    public readonly struct EnabledRefRW<T> : IQueryTypeParameter where T : struct, IEnableableComponent
    {
        public unsafe EnabledRefRW(SafeBitRef ptr, int* ptrChunkDisable) { }
    }

    public readonly struct EnabledRefRO<T> : IQueryTypeParameter where T : struct, IEnableableComponent
    {
        public unsafe EnabledRefRO(SafeBitRef ptr) { }
    }

    public unsafe partial struct EntityCommandBuffer : IDisposable
    {
        public void Dispose() => throw new NotImplementedException();

        unsafe public struct ParallelWriter { }

        public ParallelWriter AsParallelWriter() => default;

        public void Playback(EntityManager mgr) { }
        [SupportedInEntitiesForEach]
        public void AddComponent<T>(Entity e) where T : unmanaged, IComponentData { }
        [SupportedInEntitiesForEach]
        public void RemoveComponentForEntityQuery<T>(EntityQuery entityQuery) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal class SupportedInEntitiesForEach : Attribute
    {
    }

    public static unsafe class EntityCommandBufferManagedComponentExtensions
    {
        [SupportedInEntitiesForEach]
        public static void AddComponent<T>(this EntityCommandBuffer ecb, Entity e, T component) where T : class { }
        [SupportedInEntitiesForEach]
        public static void AddComponent<T>(this EntityCommandBuffer ecb, Entity e) where T : class { }
    }

    public struct ComponentTypeHandle<T>
    {
        public unsafe void Update(ref SystemState state) {}
        public unsafe void Update(SystemBase system) {}
    }
    public struct EnabledMask
    {

        public unsafe EnabledRefRW<T> GetComponentEnabledRefRW<T>(int index)
            where T : unmanaged, IComponentData, IEnableableComponent => default;
        public EnabledRefRO<T> GetComponentEnabledRefRO<T>(int index)
            where T : unmanaged, IComponentData, IEnableableComponent => default;

        public unsafe EnabledRefRW<T> GetOptionalComponentEnabledRefRW<T>(int index)
            where T : unmanaged, IComponentData, IEnableableComponent => default;

        public EnabledRefRO<T> GetOptionalComponentEnabledRefRO<T>(int index)
            where T : unmanaged, IComponentData, IEnableableComponent => default;

    }
    public unsafe struct ArchetypeChunk : IEquatable<ArchetypeChunk>
    {
        public int ChunkEntityCount => default;
        public int Count => default;
        public bool Equals(ArchetypeChunk other) => true;
        public override bool Equals(object obj) => true;
        public override int GetHashCode() => default;
        public NativeArray<T> GetNativeArray<T>(ref ComponentTypeHandle<T> chunkComponentTypeHandle) where T : unmanaged, IComponentData => default;

        public EnabledMask GetEnabledMask<T>(ref ComponentTypeHandle<T> chunkComponentTypeHandle)
            where T : unmanaged, IComponentData, IEnableableComponent
            => default;
    }

    public enum ScheduleGranularity
    {
        Chunk = 0,
        Entity = 1,
    }

    public struct EntityQueryMask
    {
        public bool MatchesIgnoreFilter(Entity entity) => false;
    }

    public unsafe struct ComponentLookup<T> where T : struct, IComponentData
    {
        public void Update(ref SystemState systemState){}
        public RefRW<T> GetRefRW(Entity entity, bool isReadOnly = false) => default;
        public RefRO<T> GetRefRO(Entity entity) => default;
        public RefRO<T> GetRefROOptional(Entity entity) => default;
        public RefRW<T> GetRefRWOptional(Entity entity, bool isReadOnly) => default;

        public EnabledRefRW<T2> GetEnabledRefRW<T2>(Entity entity, bool isReadOnly)
            where T2 : unmanaged, IComponentData, IEnableableComponent => default;
        public EnabledRefRW<T2> GetEnabledRefRWOptional<T2>(Entity entity, bool isReadOnly)
            where T2 : unmanaged, IComponentData, IEnableableComponent => default;

        public EnabledRefRO<T2> GetEnabledRefRO<T2>(Entity entity)
            where T2 : unmanaged, IComponentData, IEnableableComponent => default;

        public EnabledRefRO<T2> GetEnabledRefROOptional<T2>(Entity entity)
            where T2 : unmanaged, IComponentData, IEnableableComponent => default;
        public T this[Entity e] => default;
    }

    public unsafe struct BufferLookup<T> where T : unmanaged, IBufferElementData
    {
        public void Update(ref SystemState systemState){}
        public T this[Entity e] => default;
    }

    public unsafe struct EntityStorageInfoLookup
    {
        public void Update(SystemBase system) { }
        public void Update(ref SystemState systemState) { }
        public bool Exists(Entity entity) => default;
    }

    public abstract unsafe partial class ComponentSystemBase
    {
        protected internal virtual void OnCreateForCompiler() {}
        protected internal EntityQuery GetEntityQuery(params ComponentType[] componentTypes) => default;
        protected EntityQuery GetEntityQuery(NativeArray<ComponentType> componentTypes) => default;
        protected internal EntityQuery GetEntityQuery(params EntityQueryDesc[] queryDesc) => default;
        public T GetSingleton<T>() where T : unmanaged, IComponentData => default;
        protected JobHandle Dependency { get; set; }
        protected ComponentTypeHandle<T> GetComponentTypeHandle<T>(bool isReadOnly) => default;
        public ComponentLookup<T> GetComponentLookup<T>(bool isReadOnly = false)
            where T : unmanaged, IComponentData => default;
        public EntityManager EntityManager => throw default;
    }

    public abstract unsafe class SystemBase : ComponentSystemBase
    {
        protected abstract void OnUpdate();
        protected internal ref SystemState CheckedStateRef => throw new Exception();
        protected new JobHandle Dependency { get; set; }
        protected internal ForEachLambdaJobDescription Entities => new ForEachLambdaJobDescription();
        protected internal LambdaSingleJobDescription Job => new LambdaSingleJobDescription();
        protected internal T GetComponent<T>(Entity entity) where T : unmanaged, IComponentData => default;
        protected internal void SetComponent<T>(Entity entity, T component) where T : unmanaged, IComponentData{}

        public DynamicBuffer<T> GetBuffer<T>(Entity entity, bool isReadOnly = false) where T : unmanaged, IBufferElementData
            => default;

        public new ComponentLookup<T> GetComponentLookup<T>(bool isReadOnly = false)
            where T : unmanaged, IComponentData => default;

        public ref readonly TimeData Time => throw default;
        public World World => default;
    }

    public class World
    {
        public T GetOrCreateSystemManaged<T>() where T : ComponentSystemBase => default;
    }

    public abstract unsafe partial class EntityCommandBufferSystem : SystemBase
    {
        public EntityCommandBuffer CreateCommandBuffer() => default;
        public void AddJobHandleForProducer(JobHandle producerJob)
        {
        }
    }

    public interface ISystem
    {
        void OnUpdate(ref SystemState state);
    }

    public class LambdaSingleJobDescription
    {
        public LambdaSingleJobDescription WithCode(Action action) => this;
        public JobHandle Schedule() => default;
        public JobHandle ScheduleParallel() => default;
    }

    public interface IComponentData : IQueryTypeParameter { }

    public interface ISharedComponentData {}
    public interface IEnableableComponent { }

    public delegate void ForEachDelegate<T1,T2>(ref T1 t1, ref T2 t2);

    public interface IBufferElementData { }

    public struct DynamicBuffer<T> where T : unmanaged { }

    public struct SystemState
    {
        public EntityQuery GetEntityQuery(ComponentType a) => default;
        public EntityQuery GetEntityQuery(params EntityQueryDesc[] queryDesc) => default;
        public ComponentTypeHandle<T> GetComponentTypeHandle<T>(bool isReadOnly = false) where T : struct, IComponentData => default;

        public ComponentLookup<T> GetComponentLookup<T>(bool isReadOnly = false)
            where T : struct, IComponentData => default;

        public void CompleteDependency(){}

        public EntityTypeHandle GetEntityTypeHandle() => default;

        public EntityManager EntityManager => default;
    }

    public struct ComponentType
    {
        public static unsafe ComponentType[] Combine(params ComponentType[][] componentTypes) => default;
        public static ComponentType ReadWrite<T>() => default;
        public static ComponentType ReadOnly<T>() => default;
    }

    public struct EntityQuery
    {
        public EntityQueryMask GetEntityQueryMask() => default;
        public EntityQueryMask GetEntityQueryMask(EntityQuery query) => default;
        public RefRW<T> GetSingletonRW<T>() where T : unmanaged, IComponentData => throw new Exception();
        public T GetSingleton<T>() where T : unmanaged, IComponentData => default;
    }

    public struct SystemAPIQueryBuilder
    {
        public SystemAPIQueryBuilder WithAll<T1>() => default;
        public SystemAPIQueryBuilder WithAny<T1>() => default;
        public SystemAPIQueryBuilder WithNone<T1>() => default;
        public SystemAPIQueryBuilder WithOptions(EntityQueryOptions options) => default;
        public EntityQuery Build() => default;
    }

    public static class SystemAPI
    {
        // Query builder
        public static SystemAPIQueryBuilder QueryBuilder() => default;

        // Query
        public static QueryEnumerable<T> Query<T>()
            where T : struct, IQueryTypeParameter
            => default;

        public static QueryEnumerable<global::System.ValueTuple<T1, T2>> Query<T1, T2>()
            where T1 : struct, IQueryTypeParameter
            where T2 : struct, IQueryTypeParameter
            => default;

        public static QueryEnumerable<global::System.ValueTuple<T1, T2>> Query<T1, T2, T3>()
            where T1 : struct, IQueryTypeParameter
            where T2 : struct, IQueryTypeParameter
            where T3 : struct, IQueryTypeParameter
            => default;

        // Time
        public static ref readonly TimeData Time => throw new Exception();

        // Components
        public static ComponentLookup<T> GetComponentLookup<T>(bool isReadOnly = false) where T : unmanaged, IComponentData => default;
        public static T GetComponent<T>(Entity entity) where T : struct, IComponentData => default;
        public static void SetComponent<T>(Entity entity, T component) where T : struct, IComponentData {}
        public static bool HasComponent<T>(Entity entity) where T : struct, IComponentData => default;

        // Buffer
        public static BufferLookup<T> GetBufferLookup<T>(bool isReadOnly = false) where T : unmanaged, IBufferElementData => default;
        public static bool HasBuffer<T>(Entity entity) where T : unmanaged, IBufferElementData => default;
        public static DynamicBuffer<T> GetBuffer<T>(Entity entity) where T : unmanaged, IBufferElementData => default;

        // StorageInfo
        public static EntityStorageInfoLookup GetEntityStorageInfoLookup() => default;
        public static bool Exists(Entity entity) => default;

        // Singletons
        public static RefRW<T> GetSingletonRW<T>() where T : unmanaged, IComponentData => throw new Exception();

        // Aspects
        public static T GetAspectRW<T>(Entity entity) where T : struct, IAspect => default;
        public static T GetAspectRO<T>(Entity entity) where T : struct, IAspect => default;

        public static class ManagedAPI
        {
            public static T GetComponent<T>(Entity entity) where T : class => default;
            public static T GetSingleton<T>() where T : class => throw new Exception();
        }
    }

    public struct QueryEnumerable<T> : IEnumerable<T> where T : struct
    {
        public QueryEnumerable<T> WithAll<TComponent>() => throw ThrowCodeGenException();
        public QueryEnumerable<T> WithAny<TComponent>() => throw ThrowCodeGenException();

        public QueryEnumerable<T> WithNone<TComponent>() => throw ThrowCodeGenException();
        public QueryEnumerable<T> WithSharedComponentFilter<T1>() => throw ThrowCodeGenException();
        public QueryEnumerable<T> WithSharedComponentFilter<T1, T2>() => throw ThrowCodeGenException();
        public QueryEnumerable<T> WithChangeFilter<T1>() => throw ThrowCodeGenException();
        public QueryEnumerable<T> WithChangeFilter<T1, T2>() => throw ThrowCodeGenException();
        public QueryEnumerable<T> WithFilter(NativeArray<Entity> entities) => throw ThrowCodeGenException();
        public QueryEnumerable<T> WithOptions(EntityQueryOptions options) => throw ThrowCodeGenException();

        public T Current => throw ThrowCodeGenException();
        public bool MoveNext() => false;
        public void Reset() {}
        public void Dispose() {}
        public IEnumerator<T> GetEnumerator() => throw ThrowCodeGenException();
        IEnumerator IEnumerable.GetEnumerator() => throw ThrowCodeGenException();

        static InvalidOperationException ThrowCodeGenException() => throw new InvalidOperationException();

    }

    public interface IQueryTypeParameter {}
    public interface IAspect : IQueryTypeParameter {}

    public interface IAspectCreate<T> : IQueryTypeParameter where T : IAspect
    {
        T CreateAspect(Entity entity, ref SystemState system, bool isReadOnly);
    }

    public interface ISystemCompilerGenerated
    {
        void OnCreateForCompiler(ref SystemState state);
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class DOTSCompilerPatchedMethodAttribute : Attribute
    {
        public DOTSCompilerPatchedMethodAttribute(string targetMethodName) { }
    }

    public sealed class EntityIndexInQuery : Attribute {}

    [Flags]
    public enum EntityQueryOptions
    {
        Default = 0,
        IncludePrefab = 1,
        IncludeDisabled = 2,
        FilterWriteGroup = 4,
        IgnoreEnabledBits = 8,
        IncludeSystems = 16,
    }
    public unsafe struct EntityQueryEnumerator : IDisposable
    {
        // hot state
        public int         IndexInChunk;
        public int         EntityCount;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public EntityQueryEnumerator(EntityQuery query)
        {
            EntityCount = 1;
            IndexInChunk = 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Dispose() {}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNextHotLoop() => false;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool MoveNextColdLoop(out ArchetypeChunk chunk) => true;
    }

    public class EntityQueryDesc : IEquatable<EntityQueryDesc>
    {
        public ComponentType[] Any = Array.Empty<ComponentType>();
        public ComponentType[] None = Array.Empty<ComponentType>();
        public ComponentType[] All = Array.Empty<ComponentType>();
        public EntityQueryOptions Options = EntityQueryOptions.Default;

        public bool Equals(EntityQueryDesc other) => true;
        public override bool Equals(object obj) => true;
        public override int GetHashCode() => default;
    }

    public static class JobChunkExtensions
    {
        public static unsafe JobHandle ScheduleParallel<T>(this T jobData, EntityQuery query, JobHandle dependsOn)
            where T : struct, IJobChunk => default;
    }

    public static partial class InternalCompilerInterface
    {
        public static JobRunWithoutJobSystemDelegate BurstCompile(JobRunWithoutJobSystemDelegate d) => default;
        public static JobChunkRunWithoutJobSystemDelegate BurstCompile(JobChunkRunWithoutJobSystemDelegate d) => default;
        public static JobChunkRunWithoutJobSystemDelegateLimitEntities BurstCompile(JobChunkRunWithoutJobSystemDelegateLimitEntities d) => default;
        public delegate void JobChunkRunWithoutJobSystemDelegate(ref EntityQuery query, IntPtr jobPtr);
        public delegate void JobRunWithoutJobSystemDelegate(IntPtr jobPtr);
        public delegate void JobChunkRunWithoutJobSystemDelegateLimitEntities(ref EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength, IntPtr jobPtr);
        public static unsafe ref T UnsafeAsRef<T>(IntPtr value) where T : struct => throw new Exception();
        public static unsafe T GetComponentData<T>(EntityManager manager, Entity entity, int typeIndex, out T originalComponent) where T : struct, IComponentData => throw new Exception();
        public static unsafe void WriteComponentData<T>(EntityManager manager, Entity entity, int typeIndex, ref T lambdaComponent, ref T originalComponent) where T : struct, IComponentData {}
        public static unsafe IntPtr UnsafeGetChunkNativeArrayReadOnlyIntPtr<T>(ArchetypeChunk chunk, ref ComponentTypeHandle<T> typeHandle) where T : struct, IComponentData => default;
        public static unsafe IntPtr UnsafeGetChunkNativeArrayIntPtr<T>(ArchetypeChunk chunk, ref ComponentTypeHandle<T> typeHandle) where T : struct, IComponentData => default;
        public static unsafe IntPtr UnsafeGetChunkEntityArrayIntPtr(ArchetypeChunk chunk, EntityTypeHandle typeHandle) => default;
        public static unsafe IntPtr UnsafeGetEntityArrayIntPtr(NativeArray<Entity> array) => default;
        public static unsafe T UnsafeGetCopyOfNativeArrayPtrElement<T>(IntPtr nativeArrayPtr, int elementIndex) where T : unmanaged => *((T*) nativeArrayPtr + elementIndex);
        public static unsafe ref T UnsafeGetRefToNativeArrayPtrElement<T>(IntPtr nativeArrayPtr, int elementIndex) where T : unmanaged => throw new Exception();
        public static unsafe IntPtr UnsafeGetSystemStateIntPtr(ref SystemState state) => default;
        public static unsafe ref SystemState UnsafeGetSystemStateRef(IntPtr statePtr) => ref *(SystemState*) statePtr;
        public static class JobChunkInterface
        {
            public static void RunWithoutJobsInternal<T>(ref T jobData, ref EntityQuery query) where T : struct, IJobChunk {}
            public static void RunWithoutJobs<T>(ref T jobData, EntityQuery query) where T : struct, IJobChunk {}
            public static void RunByRefWithoutJobs<T>(ref T jobData, EntityQuery query) where T : struct, IJobChunk {}
            public static JobHandle Schedule<T>(T jobData, EntityQuery query, JobHandle dependsOn) where T : struct, IJobChunk => default;
            public static JobHandle ScheduleParallel<T>(T jobData, EntityQuery query, JobHandle dependsOn) where T : struct, IJobChunk => default;
            public static JobHandle Schedule<T>(T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray, JobHandle dependsOn) where T : struct, IJobChunk => default;
#pragma warning disable 618
            public static JobHandle ScheduleParallel<T>(T jobData, EntityQuery query, ScheduleGranularity scheduleGranularity, NativeArray<Entity> limitToEntityArray, JobHandle dependsOn) where T : struct, IJobChunk => default;
#pragma warning restore 618
            public static void RunWithoutJobs<T>(ref T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray) where T : struct, IJobChunk {}
            public static void RunByRefWithoutJobs<T>(ref T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray) where T : struct, IJobChunk {}
            public static unsafe void RunWithoutJobsInternal<T>(ref T jobData, ref EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength) where T : struct, IJobChunk {}
            public static unsafe void RunWithoutJobsInternal<T>(ref T jobData, ref EntityQuery query, Entity* limitToEntityArray, int limitToEntityArrayLength) where T : struct, IJobChunk {}
        }

        public interface IIsFullyUnmanaged {}
    }

    public struct EntityTypeHandle
    {
        public void Update(SystemBase system) {}
        public void Update(ref SystemState state) {}
    }

    public struct EntityManager
    {
        private class StructuralChangeMethodAttribute : Attribute { }

        public int EntityOrderVersion => throw default;

        [StructuralChangeMethod]
        public bool RemoveComponent<T>(Entity entity) => throw default;

        [StructuralChangeMethod]
        public Entity CreateEntity() => throw default;

        [StructuralChangeMethod]
        public Entity CreateEntity(params ComponentType[] types) => default;

        public void DestroyEntity(EntityQuery entityQuery)
        {
        }

        public EntityQuery CreateEntityQuery(params ComponentType[] requiredComponents) => default;
        public void CompleteDependencyBeforeRW<T>(){}
        public void CompleteDependencyBeforeRO<T>(){}
    }

    public static class EnabledBitUtility
    {
        public static bool GetNextRange(ref v128 mask, ref int beginIndex, ref int endIndex) => default;
    }

    namespace Serialization
    {
        public struct UntypedWeakReferenceId {}
    }
}

namespace Unity.Collections
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public sealed class ReadOnlyAttribute : Attribute
    {
    }

    public enum Allocator
    {
        Invalid = 0,
        None = 1,
        Temp = 2,
        TempJob = 3,
        Persistent = 4,
        AudioKernel = 5,
        FirstUserIndex = 64, // 0x00000040
    }
    public struct NativeArray<T> : IDisposable, IEnumerable<T>, IEnumerable, IEquatable<NativeArray<T>>
    {
        public unsafe ReadOnly AsReadOnly() => default;
        public void Dispose(){}
        public IEnumerator<T> GetEnumerator() => default;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public bool Equals(NativeArray<T> other) => default;
        public int Length => default;

        public unsafe T this[int index] => default;

        public struct ReadOnly : IEnumerable<T>, IEnumerable
        {
            public int Length => default;
            public unsafe T this[int index] => default;
            public IEnumerator<T> GetEnumerator() => throw new NotImplementedException();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

namespace Unity.Jobs
{
    public struct JobHandle : IEquatable<JobHandle>
    {
        public bool Equals(JobHandle other) => default;

        public override bool Equals(object obj) => obj is JobHandle other && Equals(other);

        public override int GetHashCode() => throw new NotImplementedException();
    }

    namespace LowLevel.Unsafe
    {
        public sealed class JobProducerTypeAttribute : Attribute
        {
            public JobProducerTypeAttribute(Type producerType) { }
        }
    }

    public interface IJob{}

    public static class IJobExtensions
    {
        public static JobHandle Schedule<T>(this T job, JobHandle dependency) where T : struct, IJob => default;
    }
}

namespace Unity.Mathematics
{
    public static partial class math
    {
        public static int countbits(ulong x) => default;
        public static int min(int x, int y) { return x < y ? x : y; }
    }
}

namespace UnityEngine
{
    public class Object{}
    public class Component:Object{}
    public class Transform : Component{}
}

namespace Unity.Core
{
    public readonly struct TimeData
    {
        public readonly double ElapsedTime;
        public readonly float DeltaTime;
    }
}
