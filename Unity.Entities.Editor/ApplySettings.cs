//This script is copied from many other graphics test projects and is useful to apply settings like the color space.

using UnityEditor;

public static class SetupProject
{
    public static void EnableTransformV1()
    {
        var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, $"{defines};ENABLE_TRANSFORM_V1");
    }
}
