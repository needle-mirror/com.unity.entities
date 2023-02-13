using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;

namespace Doc.CodeSamples.Tests
{
    #region aspect-example
    struct CannonBall : IComponentData
    {
        public float3 Speed;
    }

    // Aspects must be declared as a readonly partial struct
    readonly partial struct CannonBallAspect : IAspect
    {
        // An Entity field in an Aspect gives access to the Entity itself.
        // This is required for registering commands in an EntityCommandBuffer for example.
        public readonly Entity Self;

        // Aspects can contain other aspects.
        readonly TransformAspect Transform;

        // A RefRW field provides read write access to a component. If the aspect is taken as an "in"
        // parameter, the field behaves as if it was a RefRO and throws exceptions on write attempts.
        readonly RefRW<CannonBall> CannonBall;

        // Properties like this aren't mandatory. The Transform field can be public instead.
        // But they improve readability by avoiding chains of "aspect.aspect.aspect.component.value.value".
        public float3 Position
        {
            get => Transform.LocalPosition;
            set => Transform.LocalPosition = value;
        }

        public float3 Speed
        {
            get => CannonBall.ValueRO.Speed;
            set => CannonBall.ValueRW.Speed = value;
        }
    }
    #endregion

    #region aspect-iterate
    public partial struct MySystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var transform in SystemAPI.Query<TransformAspect>())
            {
                // use transform aspect here
            }
        }
    }
    #endregion
}