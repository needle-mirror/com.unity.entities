using Unity.Entities;

namespace Unity.Rendering
{
    public struct MeshLODGroupComponent : IComponentData
    {
        public int activeLod;
        public float size;
        public float biasMinusOne;
        public float limit0;
        public float limit1;
        public float limit2;
    }
    public struct MeshLODComponent : IComponentData
    {
        public Entity group;
        public int lod;
        public int isInactive;
    }

    public struct MeshLODInactive : IComponentData
    {
    }
}
