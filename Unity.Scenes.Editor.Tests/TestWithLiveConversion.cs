using System;
using Unity.Entities.Conversion;
using UnityEngine;

namespace Unity.Scenes.Editor.Tests
{
    [Serializable]
    public struct TestWithLiveConversion
    {
        [SerializeField] bool _wasLiveConversionEnabled;
        [SerializeField] LiveConversionSettings.ConversionMode _previousConversionMode;

        public void Setup()
        {
            _wasLiveConversionEnabled = SubSceneInspectorUtility.LiveConversionEnabled;
            SubSceneInspectorUtility.LiveConversionEnabled = true;
            _previousConversionMode = LiveConversionSettings.Mode;
            LiveConversionSettings.TreatIncrementalConversionFailureAsError = true;
            LiveConversionSettings.EnableInternalDebugValidation = true;
            LiveConversionSettings.Mode = LiveConversionSettings.ConversionMode.IncrementalConversionWithDebug;
        }

        public void TearDown()
        {
            SubSceneInspectorUtility.LiveConversionEnabled = _wasLiveConversionEnabled;
            LiveConversionSettings.TreatIncrementalConversionFailureAsError = false;
            LiveConversionSettings.EnableInternalDebugValidation = false;
            LiveConversionSettings.Mode = _previousConversionMode;
        }


    }
}
