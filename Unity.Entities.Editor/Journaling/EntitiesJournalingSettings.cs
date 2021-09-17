using Unity.Properties;
using Unity.Serialization;

namespace Unity.Entities.Editor
{
    [DOTSEditorPreferencesSetting(k_SectionName)]
    class EntitiesJournalingSettings : ISetting
    {
#if !DISABLE_ENTITIES_JOURNALING
        const string k_SectionName = "Entities Journaling";
#else
        const string k_SectionName = "Entities Journaling (disabled via define)";
#endif

        [CreateProperty, DontSerialize]
        public bool Enabled
        {
#if !DISABLE_ENTITIES_JOURNALING
            get => EntitiesJournaling.Preferences.Enabled;
            set
            {
                EntitiesJournaling.Preferences.Enabled = value;
                EntitiesJournaling.Enabled = value;
            }
#else
            get => false;
            set { }
#endif
        }

        [CreateProperty, DontSerialize]
        public int TotalMemoryMB
        {
#if !DISABLE_ENTITIES_JOURNALING
            get => EntitiesJournaling.Preferences.TotalMemoryMB;
            set => EntitiesJournaling.Preferences.TotalMemoryMB = value;
#else
            get => 0;
            set { }
#endif
        }

        public void OnSettingChanged(PropertyPath path)
        {
        }
    }


#if DISABLE_ENTITIES_JOURNALING
    class EntitiesJournalingSettingsInspector : Unity.Properties.UI.Inspector<EntitiesJournalingSettings>
    {
        public override UnityEngine.UIElements.VisualElement Build()
        {
            var root = base.Build();
            root.SetEnabled(false);
            return root;
        }
    }
#endif
}
