using UnityEditor;

namespace Unity.Entities.Editor
{
    static class EntitiesJournalingMenuItem
    {
#if !DISABLE_ENTITIES_JOURNALING
        const string k_Name = "DOTS/Entities Journaling/Enable Entities Journaling";
#else
        const string k_Name = "DOTS/Entities Journaling/Enable Entities Journaling (disabled via define)";
#endif

        [MenuItem(k_Name)]
        static void ToggleEntitiesJournaling()
        {
#if !DISABLE_ENTITIES_JOURNALING
            var enabled = !EntitiesJournaling.Preferences.Enabled;
            EntitiesJournaling.Preferences.Enabled = enabled;
            EntitiesJournaling.Enabled = enabled;
#endif
        }

        [MenuItem(k_Name, true)]
        static bool ValidateToggleEntitiesJournaling()
        {
#if !DISABLE_ENTITIES_JOURNALING
            Menu.SetChecked(k_Name, EntitiesJournaling.Preferences.Enabled);
            return true;
#else
            Menu.SetChecked(k_Name, false);
            return false;
#endif
        }
    }
}
