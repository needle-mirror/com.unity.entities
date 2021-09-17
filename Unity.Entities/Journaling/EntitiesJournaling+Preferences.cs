#if !UNITY_DOTSRUNTIME
using UnityEngine;
#endif

namespace Unity.Entities
{
    partial class EntitiesJournaling
    {
        internal static class Preferences
        {
            const bool k_EnabledDefault = false;
            const int k_TotalMemoryMBDefault = 64;

#if !UNITY_DOTSRUNTIME
            const string k_EnabledKey = nameof(EntitiesJournaling) + "." + nameof(Enabled);
            const string k_TotalMemoryMBKey = nameof(EntitiesJournaling) + nameof(TotalMemoryMB);

            public static bool Enabled
            {
                get => PlayerPrefs.GetInt(k_EnabledKey, k_EnabledDefault ? 1 : 0) == 1;
                set => PlayerPrefs.SetInt(k_EnabledKey, value ? 1 : 0);
            }

            public static int TotalMemoryMB
            {
                get => PlayerPrefs.GetInt(k_TotalMemoryMBKey, k_TotalMemoryMBDefault);
                set => PlayerPrefs.SetInt(k_TotalMemoryMBKey, value);
            }
#else
            public static bool Enabled { get; set; } = k_EnabledDefault;
            public static int TotalMemoryMB { get; set; } = k_TotalMemoryMBDefault;
#endif
        }
    }
}
