using Unity.Entities;
using Unity.Mathematics;
#if !UNITY_DOTSRUNTIME
using UnityEngine.Scripting.APIUpdating;
#endif

namespace Unity.Scenes
{
#if !UNITY_DOTSRUNTIME
    [MovedFrom(true, "Unity.Entities", "Unity.Entities.Hybrid")]
#endif
    public struct SceneBoundingVolume : IComponentData
    {
        public MinMaxAABB Value;
    }
}
