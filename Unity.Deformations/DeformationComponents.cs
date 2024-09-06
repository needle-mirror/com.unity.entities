using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Deformations
{
    /// <summary>
    /// Float buffer that contains weight values that determine how much a 
    /// corresponding blend shape is applied to the mesh.
    /// </summary>
    /// <remarks>
    /// This data structure is used for mesh deformations. 
    /// </remarks>
    public struct BlendShapeWeight : IBufferElementData
    {
        /// <summary>
        /// The weight value of the blend shape. The range is from `0.0f` to `100.0f`, where `0.0f` is 0% and `100.0f` is 100%.
        /// </summary>
        public float Value;
    }

    /// <summary>
    ///  Matrix buffer that contains the skinned transformations of bones in 
    ///  relation to the bind pose.
    /// </summary>
    /// <remarks>
    ///  This data structure is used for mesh deformations.
    /// </remarks>
    public struct SkinMatrix : IBufferElementData
    {
        /// <summary>
        /// The matrix buffer of the skinned transformations.
        /// </summary>
        public float3x4 Value;
    }
}
