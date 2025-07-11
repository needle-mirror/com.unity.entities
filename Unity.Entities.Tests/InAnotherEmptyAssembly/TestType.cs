using Unity.Entities.Tests.InAnotherAssembly;
using Unity.Entities;

[assembly: DisableAutoCreation]

[assembly: RegisterGenericSystemType(typeof(MyGenericISystem_FromAnotherAssembly<Unity.Entities.Tests.InAnotherEmptyAssembly.TestType>))]
[assembly: RegisterGenericComponentType(typeof(MySentinelGenericComponent_FromAnotherAssembly<Unity.Entities.Tests.InAnotherEmptyAssembly.TestType>))]

namespace Unity.Entities.Tests.InAnotherEmptyAssembly
{
    public struct TestType {
        public int value;
    }
}
