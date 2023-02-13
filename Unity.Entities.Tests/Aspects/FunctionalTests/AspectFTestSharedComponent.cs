using System;
using NUnit.Framework;
using static Unity.Entities.SystemAPI;

using TestComponentType = Unity.Entities.Tests.EcsTestSharedComp;
using TestFieldType = Unity.Entities.Tests.EcsTestSharedComp;
using NestedAspectType = Unity.Entities.Tests.Aspects.Types.AspectWithSharedComponent;

namespace Unity.Entities.Tests.Aspects.FunctionalTests
{
    readonly partial struct AspectAliasingSharedComponentSimple : IAspect
    {
        public readonly NestedAspectType NestedAspect;
        public readonly TestFieldType TestFieldAliased;
        public TestData Read(TestData data) => NestedAspect.Read(data);
    }
    readonly partial struct AspectAliasingSharedComponentComplex : IAspect
    {
        public readonly NestedAspectType NestedAspectAliased;

        private readonly AspectAliasingSharedComponentSimple Simple;
        public TestData Read(TestData data) => NestedAspectAliased.Read(data); // read from alias in NestedAspectAliased
    }

    readonly partial struct AspectSharedComponentOptional : IAspect
    {
        public readonly RefRO<EcsTestData> EcsTestData;
        [Optional]
        public readonly EcsTestSharedComp SharedComp;

        public static Entity ApplyTo(EntityManager entityManager, Entity entity, int initialValue, bool withOptionals)
        {
            if (!entityManager.HasComponent<EcsTestData>(entity))
                entityManager.AddComponent<EcsTestData>(entity);
            entityManager.SetComponentData(entity, new EcsTestData(initialValue));

            if (withOptionals)
            {
                if (!entityManager.HasComponent<EcsTestSharedComp>(entity))
                    entityManager.AddComponent<EcsTestSharedComp>(entity);
                entityManager.SetSharedComponent(entity, new EcsTestSharedComp(initialValue));
            }

            return entity;
        }

        public TestData ReadWithOptional(TestData data)
        {
            data.Data = SharedComp.value;
            ++data.OperationCount;
            return data;
        }
        public TestData ReadNoOptional(TestData data)
        {
            data.Data = EcsTestData.ValueRO.value;
            ++data.OperationCount;
            return data;
        }
    }

    partial class AspectAliasingSharedComponentField : AspectFunctionalTest
    {
        partial struct TestISystemComplex : ISystem, IUseCaseTestSystem
        {
            public UseCase UseCase;
            UseCase IUseCaseTestSystem.UseCase { get => UseCase; set => UseCase = value; }
            public void OnUpdate(ref SystemState state)
            {
                var testData = UseCase.TestData;

                UseCase.MarkNotSupported(SystemKind.ISystem, ContextKind.Foreach, AccessKind.ReadWriteAccess);

                if (UseCase.TestPermutation(SystemKind.ISystem, ContextKind.Foreach, AccessKind.ReadOnlyAccess))
                    foreach (AspectAliasingSharedComponentComplex test in SystemAPI.Query<AspectAliasingSharedComponentComplex>())
                        testData = test.Read(testData);

                UseCase.TestData = testData;
            }
        }

        partial struct TestISystemSimple : ISystem, IUseCaseTestSystem
        {
            public UseCase UseCase;
            UseCase IUseCaseTestSystem.UseCase { get => UseCase; set => UseCase = value; }
            public void OnUpdate(ref SystemState state)
            {
                var testData = UseCase.TestData;

                UseCase.MarkNotSupported(SystemKind.ISystem, ContextKind.Foreach, AccessKind.ReadWriteAccess);

                if (UseCase.TestPermutation(SystemKind.ISystem, ContextKind.Foreach, AccessKind.ReadOnlyAccess))
                    foreach (AspectAliasingSharedComponentSimple test in SystemAPI.Query<AspectAliasingSharedComponentSimple>())
                        testData = test.Read(testData);

                UseCase.TestData = testData;
            }
        }

        partial class TestSystemBaseComplex : SystemBase, IUseCaseTestSystem
        {
            public UseCase UseCase;
            UseCase IUseCaseTestSystem.UseCase { get => UseCase; set => UseCase = value; }
            protected override void OnUpdate()
            {
                var testData = UseCase.TestData;

                UseCase.MarkNotSupported(SystemKind.SystemBase, ContextKind.GetAspect, AccessKind.ReadWriteAccess);

                if (UseCase.TestPermutation(SystemKind.SystemBase, ContextKind.GetAspect, AccessKind.ReadOnlyAccess))
                    Entities.WithoutBurst().ForEach((Entity entity, in TestComponentType comp) => testData = GetAspectRO<AspectAliasingSharedComponentComplex>(entity).Read(testData)).Run();

                UseCase.MarkNotSupported(SystemKind.SystemBase, ContextKind.Foreach, AccessKind.ReadWriteAccess);

                if (UseCase.TestPermutation(SystemKind.SystemBase, ContextKind.Foreach, AccessKind.ReadOnlyAccess))
                    Entities.ForEach((Entity entity, in AspectAliasingSharedComponentComplex aspect) => testData = aspect.Read(testData)).Run();

                UseCase.TestData = testData;
            }
        }
        partial class TestSystemBaseSimple : SystemBase, IUseCaseTestSystem
        {
            public UseCase UseCase;
            UseCase IUseCaseTestSystem.UseCase { get => UseCase; set => UseCase = value; }
            protected override void OnUpdate()
            {
                var testData = UseCase.TestData;

                UseCase.MarkNotSupported(SystemKind.SystemBase, ContextKind.GetAspect, AccessKind.ReadWriteAccess);

                if (UseCase.TestPermutation(SystemKind.SystemBase, ContextKind.GetAspect, AccessKind.ReadOnlyAccess))
                    Entities.WithoutBurst().ForEach((Entity entity, in TestComponentType comp) => testData = GetAspectRO<AspectAliasingSharedComponentSimple>(entity).Read(testData)).Run();

                UseCase.MarkNotSupported(SystemKind.SystemBase, ContextKind.Foreach, AccessKind.ReadWriteAccess);

                if (UseCase.TestPermutation(SystemKind.SystemBase, ContextKind.Foreach, AccessKind.ReadOnlyAccess))
                    Entities.ForEach((Entity entity, in AspectAliasingSharedComponentSimple aspect) => testData = aspect.Read(testData)).Run();

                UseCase.TestData = testData;
            }
        }
        
        partial struct TestISystemOptional : ISystem, IUseCaseTestSystem
        {
            public UseCase UseCase;
            UseCase IUseCaseTestSystem.UseCase { get => UseCase; set => UseCase = value; }
            public OptionalKind Optional;
            public void OnUpdate(ref SystemState state)
            {
                var testData = UseCase.TestData;

                UseCase.MarkNotSupported(SystemKind.ISystem, ContextKind.Foreach, AccessKind.ReadWriteAccess);

                if (UseCase.TestPermutation(SystemKind.ISystem, ContextKind.Foreach, AccessKind.ReadOnlyAccess))
                    if (Optional == OptionalKind.NoOptionalComponent)
                        foreach (var test in SystemAPI.Query<AspectSharedComponentOptional>())
                            testData = test.ReadNoOptional(testData);
                    else
                        foreach (var test in SystemAPI.Query<AspectSharedComponentOptional>())
                            testData = test.ReadWithOptional(testData);

                UseCase.TestData = testData;
            }
        }

        partial class TestSystemBaseOptional : SystemBase, IUseCaseTestSystem
        {
            public UseCase UseCase;
            UseCase IUseCaseTestSystem.UseCase { get => UseCase; set => UseCase = value; }
            public OptionalKind Optional;
            protected override void OnUpdate()
            {
                var testData = UseCase.TestData;

                UseCase.MarkNotSupported(SystemKind.SystemBase, ContextKind.GetAspect, AccessKind.ReadWriteAccess);
                UseCase.MarkNotSupported(SystemKind.SystemBase, ContextKind.Foreach, AccessKind.ReadWriteAccess);
                
                if (UseCase.TestPermutation(SystemKind.SystemBase, ContextKind.GetAspect, AccessKind.ReadOnlyAccess))
                    if (Optional == OptionalKind.WithOptionalComponent)
                        Entities.WithoutBurst().ForEach((Entity entity, in EcsTestData c0, in EcsTestSharedComp c1) => testData = GetAspectRO<AspectSharedComponentOptional>(entity).ReadWithOptional(testData)).Run();
                    else
                        Entities.WithoutBurst().ForEach((Entity entity, in EcsTestData c0) => testData = GetAspectRO<AspectSharedComponentOptional>(entity).ReadNoOptional(testData)).Run();

                if (UseCase.TestPermutation(SystemKind.SystemBase, ContextKind.Foreach, AccessKind.ReadOnlyAccess))
                    if (Optional == OptionalKind.WithOptionalComponent)
                        Entities.ForEach((Entity entity, in AspectSharedComponentOptional aspect) => testData = aspect.ReadWithOptional(testData)).Run();
                    else
                        Entities.ForEach((Entity entity, in AspectSharedComponentOptional aspect) => testData = aspect.ReadNoOptional(testData)).Run();


                UseCase.TestData = testData;
            }
        }


        [Test]
        public void SimpleUseCases([Values] SystemKind systemKind, [Values] ContextKind contextKind)
        {
            // create an empty entity that should not be grabbed by the aspect query.
            var entityEmpty = m_Manager.CreateEntity();

            // create an entity that will hold all components required by the aspect
            var entity = m_Manager.CreateEntity();

            var useCase = MakeUseCase(entity, systemKind, contextKind, AccessKind.ReadOnlyAccess, expectedOperationCount: 1);
            NestedAspectType.ApplyTo(m_Manager, entity, useCase.ValueInitial);
            TestUseCase<TestISystemSimple, TestSystemBaseSimple>(useCase);
        }

        [Test]
        public void ComplexUseCases([Values] SystemKind systemKind, [Values] ContextKind contextKind)
        {
            // create an empty entity that should not be grabbed by the aspect query.
            var entityEmpty = m_Manager.CreateEntity();

            // create an entity that will hold all components required by the aspect
            var entity = m_Manager.CreateEntity();

            var useCase = MakeUseCase(entity, systemKind, contextKind, AccessKind.ReadOnlyAccess, expectedOperationCount: 1);
            NestedAspectType.ApplyTo(m_Manager, entity, useCase.ValueInitial);
            TestUseCase<TestISystemComplex, TestSystemBaseComplex>(useCase);
        }

        [Test]
        public void OptionalUseCases([Values] SystemKind systemKind, [Values] ContextKind contextKind, [Values] OptionalKind optionalKind)
        {
            // create an empty entity that should not be grabbed by the aspect query.
            var entityEmpty = m_Manager.CreateEntity();

            // create an entity that will hold all components required by the aspect
            var entity = m_Manager.CreateEntity();

            var useCase = MakeUseCase(entity, systemKind, contextKind, AccessKind.ReadOnlyAccess, expectedOperationCount: 1);
            AspectSharedComponentOptional.ApplyTo(m_Manager, entity, useCase.ValueInitial, optionalKind == OptionalKind.WithOptionalComponent);
            World.Unmanaged.GetUnsafeSystemRef<TestISystemOptional>(World.GetOrCreateSystem<TestISystemOptional>()).Optional = optionalKind;
            World.GetOrCreateSystemManaged<TestSystemBaseOptional>().Optional = optionalKind;

            TestUseCase<TestISystemOptional, TestSystemBaseOptional>(useCase);
        }
    }
}
