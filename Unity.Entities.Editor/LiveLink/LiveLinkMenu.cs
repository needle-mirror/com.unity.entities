// For debugging purposes we can also allow selecting the legacy allow clean conversion mode.
//#define ALLOW_CLEAN_CONVERSION_MODE_FOR_DEBUG
using Unity.Entities.Conversion;
using Unity.Scenes.Editor;
using UnityEditor;

namespace Unity.Entities.Editor
{
    static class LiveLinkMenu
    {
        const string k_LiveLinkEditorMenu = "DOTS/Live Link Mode/";

        const string kEnableInEditMode = k_LiveLinkEditorMenu  + "Live Conversion in Edit Mode";
        const string kAuthoring = k_LiveLinkEditorMenu   + "SceneView: Editing State";
        const string kGameState = k_LiveLinkEditorMenu + "SceneView: Live Game State";


        [MenuItem(kEnableInEditMode, false, 0)]
        static void ToggleInEditMode()
        {
            SubSceneInspectorUtility.LiveLinkEnabledInEditMode = !SubSceneInspectorUtility.LiveLinkEnabledInEditMode;
        }

        [MenuItem(kEnableInEditMode, true)]
        static bool ValidateToggleInEditMode()
        {
            Menu.SetChecked(kEnableInEditMode, SubSceneInspectorUtility.LiveLinkEnabledInEditMode);
            return true;
        }

        [MenuItem(kAuthoring, false, 11)]
        static void LiveAuthoring()
            => SubSceneInspectorUtility.LiveLinkShowGameStateInSceneView = false;

        [MenuItem(kAuthoring, true)]
        static bool ValidateLiveConvertAuthoring()
        {
            Menu.SetChecked(kAuthoring, !SubSceneInspectorUtility.LiveLinkShowGameStateInSceneView);
            return true;
        }

        [MenuItem(kGameState, false, 11)]
        static void LiveConvertGameState() => SubSceneInspectorUtility.LiveLinkShowGameStateInSceneView = true;

        [MenuItem(kGameState, true)]
        static bool ValidateLiveConvertGameState()
        {
            Menu.SetChecked(kGameState, SubSceneInspectorUtility.LiveLinkShowGameStateInSceneView);
            return true;
        }


#if UNITY_2020_2_OR_NEWER
#if !ALLOW_CLEAN_CONVERSION_MODE_FOR_DEBUG
        const string k_ConversionIncrementalConversionWithDebug = k_LiveLinkEditorMenu + "Debug Incremental Conversion";

        [MenuItem(k_ConversionIncrementalConversionWithDebug, true)]
        static bool ValidateIncrementalConversionWithDebug()
        {
            Menu.SetChecked(k_ConversionIncrementalConversionWithDebug,
                LiveConversionSettings.Mode == LiveConversionSettings.ConversionMode.IncrementalConversionWithDebug);
            return true;
        }

        [MenuItem(k_ConversionIncrementalConversionWithDebug, false)]
        static void ConversionIncrementalConversionWithDebug()
        {
            var isEnabled = LiveConversionSettings.Mode == LiveConversionSettings.ConversionMode.IncrementalConversionWithDebug;
            LiveConversionSettings.Mode = isEnabled ? LiveConversionSettings.ConversionMode.IncrementalConversion : LiveConversionSettings.ConversionMode.IncrementalConversionWithDebug;
        }

#else
        const string k_ConversionAlwaysCleanConvert = k_LiveLinkEditorMenu + "Always Convert Entire Scene";
        const string k_ConversionIncrementalConversion = k_LiveLinkEditorMenu + "Incremental Conversion";
        const string k_ConversionIncrementalConversionWithDebug = k_LiveLinkEditorMenu + "Debug Incremental Conversion";

        [MenuItem(k_ConversionAlwaysCleanConvert, true)]
        static bool ValidateAlwaysCleanConvert()
        {
            Menu.SetChecked(k_ConversionAlwaysCleanConvert,
                LiveConversionSettings.Mode == LiveConversionSettings.ConversionMode.AlwaysCleanConvert);
            return true;
        }

        [MenuItem(k_ConversionAlwaysCleanConvert, false)]
        static void ConversionAlwaysCleanConvert()
        {
            LiveConversionSettings.Mode = LiveConversionSettings.ConversionMode.AlwaysCleanConvert;
        }

        [MenuItem(k_ConversionIncrementalConversion, true)]
        static bool ValidateConversionIncrementalConversion()
        {
            Menu.SetChecked(k_ConversionIncrementalConversion,
                LiveConversionSettings.Mode == LiveConversionSettings.ConversionMode.IncrementalConversion);
            return true;
        }

        [MenuItem(k_ConversionIncrementalConversion, false)]
        static void ConversionIncrementalConversion()
        {
            LiveConversionSettings.Mode = LiveConversionSettings.ConversionMode.IncrementalConversion;
        }

        [MenuItem(k_ConversionIncrementalConversionWithDebug, true)]
        static bool ValidateIncrementalConversionWithDebug()
        {
            Menu.SetChecked(k_ConversionIncrementalConversionWithDebug,
                LiveConversionSettings.Mode == LiveConversionSettings.ConversionMode.IncrementalConversionWithDebug);
            return true;
        }

        [MenuItem(k_ConversionIncrementalConversionWithDebug, false)]
        static void ConversionIncrementalConversionWithDebug()
        {
            LiveConversionSettings.Mode = LiveConversionSettings.ConversionMode.IncrementalConversionWithDebug;
        }
#endif
#endif
    }
}
