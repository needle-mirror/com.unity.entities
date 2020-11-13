#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

namespace Unity.Entities.Conversion
{
    static class LiveConversionSettings
    {
        private const string EditorPrefsName = "com.unity.entities.conversion_mode";
        public enum ConversionMode
        {
            /// <summary>
            /// All changes trigger a clean conversion.
            /// </summary>
            AlwaysCleanConvert = 0,

            /// <summary>
            /// All changes are handled via incremental conversion, except when there are failures in which case we
            /// trigger a clean conversion.
            /// </summary>
            IncrementalConversion,

            /// <summary>
            /// Like pure incremental conversion, but also performs a clean conversion and diffs against that.
            /// </summary>
            IncrementalConversionWithDebug,
        }

        /// <summary>
        /// When set to true, a failure during incremental conversion is treated as an error. Otherwise a failure leads
        /// to a clean conversion instead. This should only be enabled for testing purposes.
        /// </summary>
        public static bool TreatIncrementalConversionFailureAsError { get; set; }

        internal static bool EnableInternalDebugValidation;
        internal static readonly List<Type> AdditionalConversionSystems = new List<Type>();

#if !UNITY_2020_2_OR_NEWER
        public static bool IsFullyIncremental => false;
        public static ConversionMode Mode => ConversionMode.AlwaysCleanConvert;
#else
        public static bool IsFullyIncremental => Mode == ConversionMode.IncrementalConversion ||
                                                 Mode == ConversionMode.IncrementalConversionWithDebug;

        public static ConversionMode Mode
        {
            get => (ConversionMode) SessionState.GetInt(EditorPrefsName, (int) ConversionMode.IncrementalConversion);
            set => SessionState.SetInt(EditorPrefsName, (int) value);
        }
#endif

    }
}
#endif
