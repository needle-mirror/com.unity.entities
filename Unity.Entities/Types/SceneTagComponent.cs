using System;

namespace Unity.Entities
{
    [Serializable]
    public struct SceneData : IComponentData
    {
        public Hash128          SceneGUID;
        public int              SubSectionIndex;
        public int              FileSize;
        public int              SharedComponentCount;

        //public int              IsLiveLink;
    }

    [System.Serializable]
    public struct SceneSection : ISharedComponentData
    {
        public Hash128        SceneGUID;
        public int            Section;
    }
    
    public struct RequestSceneLoaded : IComponentData
    {
        //public int             Priority;
    }
}
