#if !UNITY_DOTSRUNTIME
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Unity.Scenes
{
    [MovedFrom(true, "Unity.Entities", "Unity.Entities.Hybrid")]
    public class ReferencedUnityObjects : ScriptableObject
    {
        public UnityEngine.Object[] Array;
    }
}
#endif
