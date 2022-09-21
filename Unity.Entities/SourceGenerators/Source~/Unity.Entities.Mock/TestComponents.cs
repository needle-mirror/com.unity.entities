using System.ComponentModel;
using Unity.Entities;

namespace Unity.Entities.Tests
{
    public struct EcsTestDataEnableable : IComponentData, IEnableableComponent
    {
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

    readonly partial struct EcsTestAspect : IAspect
    {
        public EcsTestAspect CreateAspect(Entity entity, ref SystemState system, bool isReadOnly) =>
            throw new NotImplementedException();
    }

    public struct EcsTestDataEntity : IComponentData
    {
        public int value0;
        public Entity value1;
    }
}

public struct Translation : IComponentData { public float Value; }
