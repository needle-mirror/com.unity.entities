using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities.Tests.ForEachCodegen
{
    [TestFixture]
    public unsafe partial class ForEachISystemTests : ECSTestsFixture
    {
        Entity TestEntity;
        Entity DisabledEntity;

        [SetUp]
        public void SetUp()
        {
            World.GetOrCreateSystem<MyTestSystem>();

            var myArch = m_Manager.CreateArchetype(
                ComponentType.ReadWrite<EcsTestData>(),
                ComponentType.ReadWrite<EcsTestData2>(),
                ComponentType.ReadWrite<EcsTestSharedComp>(),
                ComponentType.ReadWrite<EcsTestSharedComp2>(),
                ComponentType.ReadWrite<EcsIntElement>(),
                ComponentType.ReadWrite<EcsTestTag>());

            TestEntity = m_Manager.CreateEntity(myArch);
            m_Manager.SetComponentData(TestEntity, new EcsTestData() { value = 3});
            m_Manager.SetComponentData(TestEntity, new EcsTestData2() { value0 = 4});
            var buffer = m_Manager.GetBuffer<EcsIntElement>(TestEntity);
            buffer.Add(new EcsIntElement() {Value = 18});
            buffer.Add(new EcsIntElement() {Value = 19});
            m_Manager.SetSharedComponentData(TestEntity, new EcsTestSharedComp() { value = 5 });
            m_Manager.SetSharedComponentData(TestEntity, new EcsTestSharedComp2() { value0 = 11, value1 = 13 });

            DisabledEntity = m_Manager.CreateEntity(typeof(Disabled), typeof(EcsTestData3));
        }

        SystemRef<MyTestSystem> GetTestSystemRef() => World.GetExistingSystem<MyTestSystem>();
        ref SystemState GetSystemStateRef(SystemRef<MyTestSystem> testSystemRef) =>
            ref UnsafeUtility.AsRef<SystemState>(World.Unmanaged.ResolveSystemState(testSystemRef));

        void AssertNothingChanged() => Assert.AreEqual(3, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);


        [Test]
        public void SimplestCase()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            testSystemRef.Struct.SimplestCase(ref systemStateRef);
            Assert.AreEqual(7, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithTagComponent()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            testSystemRef.Struct.WithTagComponent(ref systemStateRef);
            Assert.AreEqual(5, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithNone()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            testSystemRef.Struct.WithNone(ref systemStateRef);
            AssertNothingChanged();
        }

        [Test]
        public void WithAny_DoesntExecute_OnEntityWithoutThatComponent()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            testSystemRef.Struct.WithAny_DoesntExecute_OnEntityWithoutThatComponent(ref systemStateRef);
            AssertNothingChanged();
        }

        [Test]
        public void WithAllSharedComponent()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            testSystemRef.Struct.WithAllSharedComponentData(ref systemStateRef);
            Assert.AreEqual(4, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void StoresEntityQueryInField()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            var entityCountFromQuery = testSystemRef.Struct.m_StoredQuery.CalculateEntityCount();
            var entityCountFromJob = testSystemRef.Struct.StoresEntityQueryInField(ref systemStateRef);

            Assert.AreEqual(entityCountFromQuery, entityCountFromJob);
        }

        [Test]
        public void WithEntityQueryOption_DisabledEntity()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            var entity = testSystemRef.Struct.WithEntityQueryOption_DisabledEntity(ref systemStateRef);
            Assert.AreEqual(DisabledEntity, entity);
        }

        [Test]
        public void AddToDynamicBuffer()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            testSystemRef.Struct.AddToDynamicBuffer(ref systemStateRef);
            var buffer = m_Manager.GetBuffer<EcsIntElement>(TestEntity);
            Assert.AreEqual(3, buffer.Length);
            CollectionAssert.AreEqual(new[] {18, 19, 4}, buffer.Reinterpret<int>().AsNativeArray());
        }

        [Test]
        public void ModifyDynamicBuffer()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            testSystemRef.Struct.ModifyDynamicBuffer(ref systemStateRef);
            var buffer = m_Manager.GetBuffer<EcsIntElement>(TestEntity);
            CollectionAssert.AreEqual(new[] {18 * 2, 19 * 2}, buffer.Reinterpret<int>().AsNativeArray());
        }

        [Test]
        public void IterateExistingDynamicBufferReadOnly()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            testSystemRef.Struct.IterateExistingDynamicBufferReadOnly(ref systemStateRef);
            Assert.AreEqual(18 + 19, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void DisposeNativeArray_DisposesAtEnd()
        {
            var testSystemRef = GetTestSystemRef();
            ref var systemStateRef = ref GetSystemStateRef(testSystemRef);

            var testArray = new NativeArray<int>(100, Allocator.Temp);
            var isCreated = false;

            isCreated = testSystemRef.Struct.DisposeNativeArray(ref systemStateRef, testArray);
            Assert.IsFalse(isCreated);
        }
    }

    partial struct MyTestSystem : ISystem
    {
        public EntityQuery m_StoredQuery;

        public void OnCreate(ref SystemState state) {}
        public void OnDestroy(ref SystemState state) {}
        public void OnUpdate(ref SystemState state) {}

        public void SimplestCase(ref SystemState systemState)
        {
            systemState.Entities.ForEach((ref EcsTestData e1, in EcsTestData2 e2) => { e1.value += e2.value0; }).Schedule();
            systemState.Dependency.Complete();
        }

        public void WithTagComponent(ref SystemState systemState)
        {
            systemState.Entities.ForEach((ref EcsTestData e1, ref EcsTestTag e2) => { e1.value = 5; }).Schedule();
            systemState.Dependency.Complete();
        }

        public void WithNone(ref SystemState systemState)
        {
            var one = 1;
            systemState.Entities
                .WithNone<EcsTestData2>()
                .ForEach((ref EcsTestData e1) => { e1.value += one; })
                .Schedule();
            systemState.Dependency.Complete();
        }

        public void WithAny_DoesntExecute_OnEntityWithoutThatComponent(ref SystemState systemState)
        {
            var one = 1;
            systemState.Entities
                .WithAny<EcsTestData3>()
                .ForEach((ref EcsTestData e1) => { e1.value += one; })
                .Schedule();
            systemState.Dependency.Complete();
        }

        public void WithAllSharedComponentData(ref SystemState systemState)
        {
            var one = 1;
            systemState.Entities
                .WithAll<EcsTestSharedComp>()
                .ForEach((ref EcsTestData e1) => { e1.value += one; })
                .Schedule();
            systemState.Dependency.Complete();
        }

        public int StoresEntityQueryInField(ref SystemState systemState)
        {
            var count = 0;
            systemState.Entities
                .WithStoreEntityQueryInField(ref m_StoredQuery)
                .ForEach((ref EcsTestData e1) => { count++; })
                .Run();

            return count;
        }

        public Entity WithEntityQueryOption_DisabledEntity(ref SystemState systemState)
        {
            Entity disabledEntity = default;
            systemState.Entities
                .WithEntityQueryOptions(EntityQueryOptions.IncludeDisabled)
                .ForEach((Entity entity, in EcsTestData3 data3) => { disabledEntity = entity; })
                .Run();
            systemState.Dependency.Complete();
            return disabledEntity;
        }

        public void AddToDynamicBuffer(ref SystemState systemState)
        {
            systemState.Entities
                .ForEach((ref EcsTestData e1, ref DynamicBuffer<EcsIntElement> buf) =>
                {
                    buf.Add(4);
                })
                .Schedule();
            systemState.Dependency.Complete();
        }

        public void ModifyDynamicBuffer(ref SystemState systemState)
        {
            systemState.Entities
                .ForEach((ref EcsTestData e1, ref DynamicBuffer<EcsIntElement> buf) =>
                {
                    for (int i = 0; i < buf.Length; ++i) buf[i] = buf[i].Value * 2;
                })
                .Schedule();
            systemState.Dependency.Complete();
        }

        static int SumOfBufferElements(DynamicBuffer<EcsIntElement> buf)
        {
            var total = 0;
            for (var i = 0; i != buf.Length; i++)
                total += buf[i].Value;
            return total;
        }

        public void IterateExistingDynamicBufferReadOnly(ref SystemState systemState)
        {
            systemState.Entities
                .ForEach((ref EcsTestData e1, in DynamicBuffer<EcsIntElement> buf) =>
                {
                    e1.value = SumOfBufferElements(buf);
                })
                .Schedule();
            systemState.Dependency.Complete();
        }

        public bool DisposeNativeArray(ref SystemState systemState, NativeArray<int> testArray)
        {
            systemState.Entities
                .WithReadOnly(testArray)
                .WithDisposeOnCompletion(testArray)
                .ForEach((Entity entity) =>
                {
                    var length = testArray.Length;
                })
                .Run();

            return testArray.IsCreated;
        }
    }
}
