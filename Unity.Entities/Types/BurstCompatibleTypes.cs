using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    // This exists only to be able to make generic instances of generic methods to test burst compatibility.
    internal struct BurstCompatibleEnableableComponent : IEnableableComponent
    {
        public int UnusedField;
    }

    // This exists only to be able to make generic instances of generic methods to test burst compatibility.
    internal struct BurstCompatibleComponentData : IComponentData
    {
        public int UnusedField;
    }

    // This exists only to be able to make generic instances of generic methods to test burst compatibility.
    internal struct BurstCompatibleSharedComponentData : ISharedComponentData
    {
        public int UnusedField;
    }

    // This exists only to be able to make generic instances of generic methods to test burst compatibility.
    internal struct BurstCompatibleBufferElement : IBufferElementData
    {
        public int UnusedField;
    }

    // This exists only to be able to make generic instances of generic methods to test burst compatibility.
    [DisableGeneration]
    internal struct BurstCompatibleAspect : IAspect, IAspectCreate<BurstCompatibleAspect>
    {
        public int UnusedField;

        public void AddComponentRequirementsTo(ref UnsafeList<ComponentType> all, ref UnsafeList<ComponentType> any, ref UnsafeList<ComponentType> none,
            ref UnsafeList<ComponentType> disabled, ref UnsafeList<ComponentType> absent, bool isReadOnly)
        {
        }

        BurstCompatibleAspect IAspectCreate<BurstCompatibleAspect>.CreateAspect(Entity entity, ref SystemState system, bool isReadOnly)
        {
            throw new System.NotImplementedException();
        }
    }

    // This exists only to be able to make generic instances of generic methods to test burst compatibility.
    [DisableAutoCreation]
    partial struct BurstCompatibleSystem : ISystem
    {
        }
}
