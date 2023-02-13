// These types are used sometimes only by code generated by tests
#pragma warning disable 0169

using System.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;

namespace Unity.Entities.Tests
{
    public struct EcsTestDataEnableable : IComponentData, IEnableableComponent
    {
    }

    public struct EcsIntElementEnableable : IBufferElementData, IEnableableComponent
    {
        public int Value;
    }

    public struct EcsTestTag : IComponentData
    {
    }

    public struct EcsTestData : IComponentData
    {
        public int value;
    }

    public struct EcsTestData2 : IComponentData
    {
        public int value0;
        public int value1;
    }

    public struct EcsTestData3 : IComponentData
    {
        public int value0;
        public int value1;
        public int value2;
    }

    public struct EcsTestData4 : IComponentData
    {
        public int value0;
        public int value1;
        public int value2;
        public int value3;
    }

    public struct EcsTestData5 : IComponentData
    {
        public int value0;
        public int value1;
        public int value2;
        public int value3;
        public int value4;
    }

    public struct EcsIntElement : IBufferElementData
    {
        public int Value;
    }

    public struct EcsTestSharedComp : ISharedComponentData
    {
        public int value;

        public EcsTestSharedComp(int inValue)
        {
            value = inValue;
        }
    }

    public class EcsTestManagedComponent : IComponentData
    {
        public string value;
    }

    public struct EcsTestManagedSharedComp : ISharedComponentData
    {
        public string managed;
    }

    public readonly partial struct EcsTestAspect : IAspect, IAspectCreate<EcsTestAspect>
    {
        public EcsTestAspect CreateAspect(Entity entity, ref SystemState system, bool isReadOnly) =>
            throw new NotImplementedException();

        public void AddComponentRequirementsTo(ref UnsafeList<ComponentType> all, ref UnsafeList<ComponentType> any, ref UnsafeList<ComponentType> none,
                ref UnsafeList<ComponentType> disabled, ref UnsafeList<ComponentType> absent, bool isReadOnly) =>
            throw new NotImplementedException();

        public struct Lookup
        {
            public Lookup(ref global::Unity.Entities.SystemState state, bool isReadOnly) =>
                throw new NotImplementedException();
            public void Update(ref global::Unity.Entities.SystemState state) =>
                throw new NotImplementedException();
            public EcsTestAspect this[global::Unity.Entities.Entity entity] =>
                throw new NotImplementedException();
        }

        public struct ResolvedChunk
        {
            public EcsTestAspect this[int index] =>
                throw new NotImplementedException();
            public int Length;
        }

        public struct TypeHandle
        {
            public TypeHandle(ref global::Unity.Entities.SystemState state, bool isReadOnly) =>
                throw new NotImplementedException();
            public void Update(ref global::Unity.Entities.SystemState state) =>
                throw new NotImplementedException();
            public ResolvedChunk Resolve(global::Unity.Entities.ArchetypeChunk chunk) =>
                throw new NotImplementedException();
        }

        public static Enumerator Query(EntityQuery query, TypeHandle typeHandle) => default;
        public struct Enumerator : IEnumerator<EcsTestAspect>, IDisposable, IEnumerable<EcsTestAspect>
        {
            public bool MoveNext() => default;
            public void Reset() {}
            public EcsTestAspect Current => default;
            object IEnumerator.Current => Current;
            public void Dispose() => throw new NotImplementedException();
            public IEnumerator<EcsTestAspect> GetEnumerator() => throw new NotImplementedException();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public static void CompleteDependencyBeforeRO(ref SystemState state) { }
        public static void CompleteDependencyBeforeRW(ref SystemState state) { }
    }

    public struct EcsTestDataEntity : IComponentData
    {
        public int value0;
        public Entity value1;
    }

    public struct MyBlob
    {
        public BlobArray<float> myfloats;
    }

    public struct ManagedBlob
    {
        public string s;
    }

    public struct AnimationBlobData
    {
        public BlobArray<float> Keys;
    }

    public struct EntityPrefabReference
    {
        UntypedWeakReferenceId PrefabId;
    }

    public struct BoidInAnotherAssembly : IComponentData
    {
    }
}

public struct Translation : IComponentData { public float Value; }
