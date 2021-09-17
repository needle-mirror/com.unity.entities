using Unity.Entities;
using Unity.Jobs;

[assembly: DisableAutoCreation]

namespace Unity.Entities.CodeGen.Tests
{
    public class TestSystemBase : SystemBase
    {
        protected override void OnUpdate() {}
    }
}
