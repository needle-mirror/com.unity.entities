#if UNITY_DOTSRUNTIME
namespace Unity.Scenes
{
    /// DOTS representation of a Scene mirroring UnityEngine's Scene representation
    internal struct Scene
    {
        public bool isLoaded;
        public bool IsValid() { return false; }
    }
}
#endif
