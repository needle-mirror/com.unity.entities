using System;
using Unity.Entities.Conversion;
using UnityEngine;

namespace Unity.Scenes.Editor.Tests
{
    [Serializable]
    struct TestWithLiveConversion
    {
        [SerializeField] bool _wasLiveLinkEnabled;
        [SerializeField] LiveConversionSettings.ConversionMode _previousConversionMode;

        public void Setup()
        {
            _wasLiveLinkEnabled = SubSceneInspectorUtility.LiveLinkEnabledInEditMode;
            SubSceneInspectorUtility.LiveLinkEnabledInEditMode = true;
#if UNITY_2020_2_OR_NEWER
            _previousConversionMode = LiveConversionSettings.Mode;
            LiveConversionSettings.TreatIncrementalConversionFailureAsError = true;
            LiveConversionSettings.EnableInternalDebugValidation = true;
            LiveConversionSettings.Mode = LiveConversionSettings.ConversionMode.IncrementalConversionWithDebug;
#endif
        }

        public void TearDown()
        {
            SubSceneInspectorUtility.LiveLinkEnabledInEditMode = _wasLiveLinkEnabled;
#if UNITY_2020_2_OR_NEWER
            LiveConversionSettings.TreatIncrementalConversionFailureAsError = false;
            LiveConversionSettings.EnableInternalDebugValidation = false;
            LiveConversionSettings.Mode = _previousConversionMode;
#endif
        }


    }
}
