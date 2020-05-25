using UnityEditor;

namespace Unity.Scenes.Editor
{
    static class GUIDHelper
    {
        public static GUID UnityEditorResources = new GUID("0000000000000000d000000000000000");
        public static GUID UnityBuiltinResources = new GUID("0000000000000000e000000000000000");
        public static GUID UnityBuiltinExtraResources = new GUID("0000000000000000f000000000000000");

        public static bool IsBuiltinAsset(in GUID g) =>
            g == UnityEditorResources ||
            g == UnityBuiltinResources ||
            g == UnityBuiltinExtraResources;
    }
}
