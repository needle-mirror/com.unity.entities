using Unity.Entities;

namespace Unity.Entities.Tests.InAnotherAssembly
{
    public struct EcsTestDataInAnotherAssembly : IComponentData
    {
        public int value;
    }

    public struct EcsTestData2InAnotherAssembly : IComponentData
    {
        public int value0;
    }

    public partial struct SimplestCaseJobInAnotherAssembly : IJobEntity
    {
        void Execute(ref EcsTestDataInAnotherAssembly e1, in EcsTestData2InAnotherAssembly e2) => e1.value += e2.value0;
    }
}
