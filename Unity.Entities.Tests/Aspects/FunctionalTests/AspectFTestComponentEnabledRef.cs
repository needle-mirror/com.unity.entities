using NUnit.Framework;
using static Unity.Entities.SystemAPI;
namespace Unity.Entities.Tests.Aspects.FunctionalTests
{
    struct EnableableComponent : IComponentData, IEnableableComponent
    {
        public int Value;
    }

    readonly partial struct AspectWithEnableableComponentRW : IAspect
    {
        public readonly EnabledRefRW<EnableableComponent> EnableableComponent;

        public TestData Read(TestData data)
        {
            var enabled = EnableableComponent.ValueRO;
            data.Data = enabled ? AspectFunctionalTest.k_ValueRead
                                : AspectFunctionalTest.k_ValueWrite;
            ++data.OperationCount;
            return data;
        }

        public TestData Write(TestData data)
        {
            EnableableComponent.ValueRW = (data.Data != AspectFunctionalTest.k_ValueWrite);
            ++data.OperationCount;
            return data;
        }
    }

    partial class EnabledRefInAspect : AspectFunctionalTest
    {
        partial struct TestISystem : ISystem, IUseCaseTestSystem
        {
            public UseCase UseCase;
            UseCase IUseCaseTestSystem.UseCase { get => UseCase; set => UseCase = value; }
            public void OnCreate(ref SystemState state) { }
            public void OnDestroy(ref SystemState state) { }
            public void OnUpdate(ref SystemState state)
            {
                var testData = UseCase.TestData;

                if (UseCase.TestPermutation(SystemKind.ISystem, ContextKind.Foreach, AccessKind.ReadOnlyAccess))
                    foreach (AspectWithEnableableComponentRW test in SystemAPI.Query<AspectWithEnableableComponentRW>())
                        testData = test.Read(testData);

                if (UseCase.TestPermutation(SystemKind.ISystem, ContextKind.Foreach, AccessKind.ReadWriteAccess))
                    foreach (AspectWithEnableableComponentRW test in SystemAPI.Query<AspectWithEnableableComponentRW>())
                        testData = test.Write(testData);

                UseCase.TestData = testData;
            }
        }

        partial class TestSystemBase : SystemBase, IUseCaseTestSystem
        {
            public UseCase UseCase;
            UseCase IUseCaseTestSystem.UseCase { get => UseCase; set => UseCase = value; }
            protected override void OnUpdate()
            {
                var testData = UseCase.TestData;

                if (UseCase.TestPermutation(SystemKind.SystemBase, ContextKind.GetAspect, AccessKind.ReadOnlyAccess))
                    Entities.ForEach((Entity entity, in EnableableComponent comp) => testData = GetAspectRO<AspectWithEnableableComponentRW>(entity).Read(testData)).Run();

                if (UseCase.TestPermutation(SystemKind.SystemBase, ContextKind.GetAspect, AccessKind.ReadWriteAccess))
                    Entities.ForEach((Entity entity, in EnableableComponent comp) => testData = GetAspectRW<AspectWithEnableableComponentRW>(entity).Write(testData)).Run();

                UseCase.TestData = testData;
            }
        }

        [Test]
        public void UseCases([Values] SystemKind systemKind, [Values] ContextKind contextKind, [Values] AccessKind accessKind)
        {
            // create an empty entity that should not be grabbed by the aspect query.
            var entityEmtpy = m_Manager.CreateEntity();

            // create an entity that will hold all components required by the aspect
            var entity = m_Manager.CreateEntity();

            var useCase = MakeUseCase(entity, systemKind, contextKind, accessKind, expectedOperationCount: 1);
            m_Manager.AddComponent<EnableableComponent>(entity);
            m_Manager.SetComponentEnabled<EnableableComponent>(entity, true);
            TestUseCase<TestISystem, TestSystemBase>(useCase,
                    getWrittenValue: () => m_Manager.IsComponentEnabled<EnableableComponent>(entity) ? AspectFunctionalTest.k_ValueInit : AspectFunctionalTest.k_ValueWrite);
        }
    }
}
