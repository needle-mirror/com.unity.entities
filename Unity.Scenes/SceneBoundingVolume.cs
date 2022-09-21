using Unity.Entities;
using Unity.Mathematics;
#if !UNITY_DOTSRUNTIME
using UnityEngine.Scripting.APIUpdating;
#endif

namespace Unity.Scenes
{
    /// <summary>
    /// Component that represents a Scene or a Scene Section bounding volume
    /// </summary>
#if !UNITY_DOTSRUNTIME
    [MovedFrom(true, "Unity.Entities", "Unity.Entities.Hybrid")]
#endif
    public struct SceneBoundingVolume : IComponentData
    {
        /// <summary>
        /// Bounding volume
        /// </summary>
        public MinMaxAABB Value;
    }
}
