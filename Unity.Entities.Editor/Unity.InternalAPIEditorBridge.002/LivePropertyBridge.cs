using System;
using UnityEditor;
using UnityObject = UnityEngine.Object;

namespace Unity.Editor.Bridge
{
#if UNITY_2022_2_OR_NEWER
    static class LivePropertyBridge
    {
        public static void EnableLivePropertyFeatureGlobally(bool enableLivePropertyFeatureGlobally) => SerializedObject.EnableLivePropertyFeatureGlobally(enableLivePropertyFeatureGlobally);
        public static void AddLivePropertyOverride(Type type, InspectorUtility.LivePropertyOverrideCallback callback) => InspectorUtility.SetLivePropertyOverride(type, callback);
        public static void AddLivePropertyChanged(Type type, InspectorUtility.LivePropertyChangedCallback callback) => InspectorUtility.SetLivePropertyChanged(type, callback);
    }
#endif
}
