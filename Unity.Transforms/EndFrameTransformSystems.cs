using System;
using Unity.Entities;

namespace Unity.Transforms
{
    
    
    [UnityEngine.ExecuteAlways]
    public class TransformSystemGroup : ComponentSystemGroup
    {
    }

    [Obsolete("Use ParentSystem. (UnityUpgradable) -> ParentSystem", true)]
    public struct EndFrameParentSystem
    {
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct ParentSystem : ISystem
    {
    }
    
    [Obsolete("Use CompositeScaleSystem. (UnityUpgradable) -> CompositeScaleSystem", true)]
    public struct EndFrameCompositeScaleSystem
    {
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct CompositeScaleSystem : ISystem
    {
    }
    
    [Obsolete("Use RotationEulerSystem. (UnityUpgradable) -> RotationEulerSystem", true)]
    public struct EndFrameRotationEulerSystem
    {
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct RotationEulerSystem : ISystem
    {
    }
    
    [Obsolete("Use PostRotationEulerSystem. (UnityUpgradable) -> PostRotationEulerSystem", true)]
    public struct EndFramePostRotationEulerSystem
    {
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct PostRotationEulerSystem : ISystem
    {
    }
    
    [Obsolete("Use CompositeRotationSystem. (UnityUpgradable) -> CompositeRotationSystem", true)]
    public struct EndFrameCompositeRotationSystem
    {
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(RotationEulerSystem))]
    public partial struct CompositeRotationSystem : ISystem
    {
    }
    
    [Obsolete("Use TRSToLocalToWorldSystem. (UnityUpgradable) -> TRSToLocalToWorldSystem", true)]
    public struct EndFrameTRSToLocalToWorldSystem
    {
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(CompositeRotationSystem))]
    [UpdateAfter(typeof(CompositeScaleSystem))]
    [UpdateBefore(typeof(LocalToParentSystem))]
    public partial struct TRSToLocalToWorldSystem : ISystem
    {
    }
    
    [Obsolete("Use ParentScaleInverseSystem. (UnityUpgradable) -> ParentScaleInverseSystem", true)]
    public struct EndFrameParentScaleInverseSystem
    {
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(ParentSystem))]
    [UpdateAfter(typeof(CompositeRotationSystem))]
    public partial struct ParentScaleInverseSystem : ISystem
    {
    }
    
    [Obsolete("Use TRSToLocalToParentSystem. (UnityUpgradable) -> TRSToLocalToParentSystem", true)]
    public struct EndFrameTRSToLocalToParentSystem
    {
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(CompositeRotationSystem))]
    [UpdateAfter(typeof(CompositeScaleSystem))]
    [UpdateAfter(typeof(ParentScaleInverseSystem))]
    public partial struct TRSToLocalToParentSystem : ISystem
    {
    }
    
    [Obsolete("Use LocalToParentSystem. (UnityUpgradable) -> LocalToParentSystem", true)]
    public struct EndFrameLocalToParentSystem
    {
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(TRSToLocalToParentSystem))]
    public partial struct LocalToParentSystem : ISystem
    {
    }
    
    [Obsolete("Use WorldToLocalSystem. (UnityUpgradable) -> WorldToLocalSystem", true)]
    public struct EndFrameWorldToLocalSystem
    {
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(TRSToLocalToWorldSystem))]
    [UpdateAfter(typeof(LocalToParentSystem))]
    public partial struct WorldToLocalSystem : ISystem
    {
    }
}
