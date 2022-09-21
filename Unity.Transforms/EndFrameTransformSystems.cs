using System;
using Unity.Entities;

namespace Unity.Transforms
{
    /// <summary>
    /// A system group containing systems that process entity transformation data. 
    /// </summary>
    /// <remarks>
    /// This group includes systems that update any entity transformation hierarchies, compute up-to-date <see cref="LocalToWorldTransform"/> values
    /// for all entities not in world-space, or compute <see cref="LocalToWorld"/> matrices.
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    public class TransformSystemGroup : ComponentSystemGroup
    {
    }

#if !ENABLE_TRANSFORM_V1
#else
    /// <inheritdoc cref="ParentSystem"/>
    [Obsolete("Use ParentSystem. (UnityUpgradable) -> ParentSystem", true)]
    public struct EndFrameParentSystem
    {
    }
#endif

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct ParentSystem : ISystem
    {
    }

#if !ENABLE_TRANSFORM_V1
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    public partial struct TransformToMatrixSystem : ISystem
    {
    }
#else
    /// <inheritdoc cref="CompositeScaleSystem"/>
    [Obsolete("Use CompositeScaleSystem. (UnityUpgradable) -> CompositeScaleSystem", true)]
    public struct EndFrameCompositeScaleSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct CompositeScaleSystem : ISystem
    {
    }

    /// <inheritdoc cref="RotationEulerSystem"/>
    [Obsolete("Use RotationEulerSystem. (UnityUpgradable) -> RotationEulerSystem", true)]
    public struct EndFrameRotationEulerSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct RotationEulerSystem : ISystem
    {
    }

    /// <inheritdoc cref="PostRotationEulerSystem"/>
    [Obsolete("Use PostRotationEulerSystem. (UnityUpgradable) -> PostRotationEulerSystem", true)]
    public struct EndFramePostRotationEulerSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct PostRotationEulerSystem : ISystem
    {
    }

    /// <inheritdoc cref="CompositeRotationSystem"/>
    [Obsolete("Use CompositeRotationSystem. (UnityUpgradable) -> CompositeRotationSystem", true)]
    public struct EndFrameCompositeRotationSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(RotationEulerSystem))]
    public partial struct CompositeRotationSystem : ISystem
    {
    }

    /// <inheritdoc cref="TRSToLocalToWorldSystem"/>
    [Obsolete("Use TRSToLocalToWorldSystem. (UnityUpgradable) -> TRSToLocalToWorldSystem", true)]
    public struct EndFrameTRSToLocalToWorldSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(CompositeRotationSystem))]
    [UpdateAfter(typeof(CompositeScaleSystem))]
    [UpdateBefore(typeof(LocalToParentSystem))]
    public partial struct TRSToLocalToWorldSystem : ISystem
    {
    }

    /// <inheritdoc cref="ParentScaleInverseSystem"/>
    [Obsolete("Use ParentScaleInverseSystem. (UnityUpgradable) -> ParentScaleInverseSystem", true)]
    public struct EndFrameParentScaleInverseSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(ParentSystem))]
    [UpdateAfter(typeof(CompositeRotationSystem))]
    public partial struct ParentScaleInverseSystem : ISystem
    {
    }

    /// <inheritdoc cref="TRSToLocalToParentSystem"/>
    [Obsolete("Use TRSToLocalToParentSystem. (UnityUpgradable) -> TRSToLocalToParentSystem", true)]
    public struct EndFrameTRSToLocalToParentSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(CompositeRotationSystem))]
    [UpdateAfter(typeof(CompositeScaleSystem))]
    [UpdateAfter(typeof(ParentScaleInverseSystem))]
    public partial struct TRSToLocalToParentSystem : ISystem
    {
    }

    /// <inheritdoc cref="LocalToParentSystem"/>
    [Obsolete("Use LocalToParentSystem. (UnityUpgradable) -> LocalToParentSystem", true)]
    public struct EndFrameLocalToParentSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(TRSToLocalToParentSystem))]
    public partial struct LocalToParentSystem : ISystem
    {
    }

    /// <inheritdoc cref="WorldToLocalSystem"/>
    [Obsolete("Use WorldToLocalSystem. (UnityUpgradable) -> WorldToLocalSystem", true)]
    public struct EndFrameWorldToLocalSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(TRSToLocalToWorldSystem))]
    [UpdateAfter(typeof(LocalToParentSystem))]
    public partial struct WorldToLocalSystem : ISystem
    {
    }
#endif
}
