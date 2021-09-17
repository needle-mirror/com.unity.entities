// For debugging purposes we can also allow selecting the legacy allow clean conversion mode.
//#define ALLOW_CLEAN_CONVERSION_MODE_FOR_DEBUG
using Unity.Entities.Conversion;
using Unity.Scenes.Editor;
using UnityEditor;

namespace Unity.Entities.Editor
{
    static class LiveConversionMenu
    {
        const string k_LiveConversionEditorMenu = "DOTS/Conversion Settings/";

        const string kLiveConversionEnabled = k_LiveConversionEditorMenu  + "Live Conversion Enabled";
        const string kAuthoringState = k_LiveConversionEditorMenu   + "Live Conversion: Authoring State in Scene View";
        const string kRuntimeState = k_LiveConversionEditorMenu + "Live Conversion: Runtime State in Scene View";


        [MenuItem(kLiveConversionEnabled, false, 0)]
        static void ToggleInEditMode()
        {
            SubSceneInspectorUtility.LiveConversionEnabled = !SubSceneInspectorUtility.LiveConversionEnabled;
        }

        [MenuItem(kLiveConversionEnabled, true)]
        static bool ValidateToggleInEditMode()
        {
            Menu.SetChecked(kLiveConversionEnabled, SubSceneInspectorUtility.LiveConversionEnabled);
            return true;
        }

        [MenuItem(kAuthoringState, false, 11)]
        static void LiveAuthoring()
            => SubSceneInspectorUtility.LiveConversionSceneViewShowRuntime = false;

        [MenuItem(kAuthoringState, true)]
        static bool ValidateLiveConvertAuthoring()
        {
            Menu.SetChecked(kAuthoringState, !SubSceneInspectorUtility.LiveConversionSceneViewShowRuntime);
            return true;
        }

        [MenuItem(kRuntimeState, false, 11)]
        static void LiveConvertGameState() => SubSceneInspectorUtility.LiveConversionSceneViewShowRuntime = true;

        [MenuItem(kRuntimeState, true)]
        static bool ValidateLiveConvertGameState()
        {
            Menu.SetChecked(kRuntimeState, SubSceneInspectorUtility.LiveConversionSceneViewShowRuntime);
            return true;
        }


        const string k_ConversionIncrementalConversionWithLogging = k_LiveConversionEditorMenu + "Incremental Conversion Logging";

        [MenuItem(k_ConversionIncrementalConversionWithLogging, true)]
        static bool ValidateIncrementalConversionWithLogging()
        {
            Menu.SetChecked(k_ConversionIncrementalConversionWithLogging, LiveConversionSettings.IsDebugLoggingEnabled);
            return true;
        }

        [MenuItem(k_ConversionIncrementalConversionWithLogging, false)]
        static void ConversionIncrementalConversionWithLogging()
        {
            LiveConversionSettings.IsDebugLoggingEnabled = !LiveConversionSettings.IsDebugLoggingEnabled;
        }

#if !ALLOW_CLEAN_CONVERSION_MODE_FOR_DEBUG
        const string k_ConversionIncrementalConversionWithDebug = k_LiveConversionEditorMenu + "Incremental Conversion Debug";

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
        const string k_ConversionAlwaysCleanConvert = k_LiveConversionEditorMenu + "Always Convert Entire Scene";
        const string k_ConversionIncrementalConversion = k_LiveConversionEditorMenu + "Incremental Conversion";
        const string k_ConversionIncrementalConversionWithDebug = k_LiveConversionEditorMenu + "Debug Incremental Conversion";

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
    }
}
