using System;
using Unity.Mathematics;

namespace Unity.Entities
{
    [Serializable]
    public struct SceneSectionData : IComponentData
    {
        public Hash128          SceneGUID;
        public int              SubSectionIndex;
        public int              FileSize;
        public int              ObjectReferenceCount;
        public MinMaxAABB       BoundingVolume;

        [Obsolete("SharedComponentCount from the deprecated SceneData API is obsolete and will be (RemovedAfter 2020-01-22)", false)]
        public int SharedComponentCount
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }
    }

    public struct SceneReference : IComponentData, IEquatable<SceneReference>
    {
        public Hash128 SceneGUID;

        public bool Equals(SceneReference other)
        {
            return SceneGUID.Equals(other.SceneGUID);
        }
        public override int GetHashCode()
        {
            return SceneGUID.GetHashCode();
        }
    }

    [System.Serializable]
    public struct SceneSection : ISharedComponentData, IEquatable<SceneSection>
    {
        public Hash128        SceneGUID;
        public int            Section;

        public bool Equals(SceneSection other)
        {
            return SceneGUID.Equals(other.SceneGUID) && Section == other.Section;
        }

        public override int GetHashCode()
        {
            return (SceneGUID.GetHashCode() * 397) ^ Section;
        }
    }

    [Flags]
    public enum SceneLoadFlags
    {
        /// <summary>
        /// Prevents adding a RequestSceneLoaded to the SubScene section entities when it gets created
        /// </summary>
        DisableAutoLoad = 1,
        /// <summary>
        /// Wait for the SubScene to be fully converted (only relevant for Editor and LiveLink)
        /// </summary>
        BlockOnImport = 2,
        /// <summary>
        /// Disable asynchronous streaming, SubScene section will be fully loaded during the next update of the streaming system
        /// </summary>
        BlockOnStreamIn = 4,
    }
    
    public struct RequestSceneLoaded : IComponentData
    {
        public SceneLoadFlags LoadFlags;
    }
}
