using System;
using NUnit.Framework;
using Unity.Entities;
using Unity.Entities.Tests;

namespace MyNamespace.System
{
    public partial class EntitiesForEachSystemInNamespaceEndingInSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((in EcsTestData c) => { }).Run();
        }
    }

    public partial struct TestJob : IJobEntity
    {
        public void Execute(Entity entity) { }
    }

    public partial class JobWithCodeSystemInNamespaceEndingInSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Job.WithCode(() => { }).Run();
        }
    }

    public partial class IJobEntitySystemInNamespaceEndingInSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            new TestJob().Run();
        }
    }
}

namespace Unity.Entities.CodeGen.Tests
{
    public partial class CodeGenSystemInNamespaceTests : ECSTestsFixture
    {
        static object[] Source =
        {
            new TestCaseData(typeof(MyNamespace.System.EntitiesForEachSystemInNamespaceEndingInSystem)),
            new TestCaseData(typeof(MyNamespace.System.JobWithCodeSystemInNamespaceEndingInSystem)),
            new TestCaseData(typeof(MyNamespace.System.IJobEntitySystemInNamespaceEndingInSystem))
        };

        [TestCaseSource(nameof(Source))]
        public void CodeGenNamespaceTests_GenerateNoErrors(Type type)
        {
            Assert.DoesNotThrow(() => Create(type).Update());
        }

        World Create(Type type)
        {
            if (type.IsClass && World.GetOrCreateSystem(type) is SystemBase)
                return World;

            throw new ArgumentException($"{type} system cannot be created.");
        }
    }
}
