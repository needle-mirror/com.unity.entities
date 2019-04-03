using Unity.Entities;

namespace Unity.Transforms
{
    public struct MoveForward : ISharedComponentData { }

    public class MoveForwardComponent : SharedComponentDataWrapper<MoveForward> { } 
}
