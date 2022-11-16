using System;
using Unity.Entities;

namespace Unity.Transforms
{
    /// <summary>
    /// A system group containing systems that process entity transformation data.
    /// </summary>
    /// <remarks>
    /// This group includes systems that update any entity transformation hierarchies, compute up-to-date <see cref="WorldTransform"/> values
    /// for all entities and compute <see cref="LocalToWorld"/> matrices.
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    public class TransformSystemGroup : ComponentSystemGroup
    {
    }

#if !ENABLE_TRANSFORM_V1
#else
    /// <summary> Obsolete. Use <see cref="ParentSystem"/> instead.</summary>
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
#else
    /// <summary> Obsolete. Use <see cref="CompositeScaleSystem"/> instead.</summary>
    [Obsolete("Use CompositeScaleSystem. (UnityUpgradable) -> CompositeScaleSystem", true)]
    public struct EndFrameCompositeScaleSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct CompositeScaleSystem : ISystem
    {
    }

    /// <summary> Obsolete. Use <see cref="RotationEulerSystem"/> instead.</summary>
    [Obsolete("Use RotationEulerSystem. (UnityUpgradable) -> RotationEulerSystem", true)]
    public struct EndFrameRotationEulerSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct RotationEulerSystem : ISystem
    {
    }

    /// <summary> Obsolete. Use <see cref="PostRotationEulerSystem"/> instead.</summary>
    [Obsolete("Use PostRotationEulerSystem. (UnityUpgradable) -> PostRotationEulerSystem", true)]
    public struct EndFramePostRotationEulerSystem
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct PostRotationEulerSystem : ISystem
    {
    }

    /// <summary> Obsolete. Use <see cref="CompositeRotationSystem"/> instead.</summary>
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

    /// <summary> Obsolete. Use <see cref="TRSToLocalToWorldSystem"/> instead.</summary>
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

    /// <summary> Obsolete. Use <see cref="ParentScaleInverseSystem"/> instead.</summary>
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

    /// <summary> Obsolete. Use <see cref="TRSToLocalToParentSystem"/> instead.</summary>
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

    /// <summary> Obsolete. Use <see cref="LocalToParentSystem"/> instead.</summary>
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

    /// <summary> Obsolete. Use <see cref="WorldToLocalSystem"/> instead.</summary>
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
