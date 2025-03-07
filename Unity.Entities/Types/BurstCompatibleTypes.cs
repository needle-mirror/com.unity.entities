using System;
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
#pragma warning disable CS0618 // Disable Aspects obsolete warnings
    [DisableGeneration]
    internal readonly partial struct BurstCompatibleAspect : IAspect, IAspectCreate<BurstCompatibleAspect>
    {
        public readonly int UnusedField;

        public void AddComponentRequirementsTo(ref UnsafeList<ComponentType> all)
        {
        }

        public void CompleteDependencyBeforeRO(ref SystemState state)
        {
        }

        public void CompleteDependencyBeforeRW(ref SystemState state)
        {
        }

        BurstCompatibleAspect IAspectCreate<BurstCompatibleAspect>.CreateAspect(Entity entity, ref SystemState system)
        {
            throw new System.NotImplementedException();
        }
    }
#pragma warning restore CS0618

    // This exists only to be able to make generic instances of generic methods to test burst compatibility.
    [DisableAutoCreation]
    partial struct BurstCompatibleSystem : ISystem
    {

    }
}
