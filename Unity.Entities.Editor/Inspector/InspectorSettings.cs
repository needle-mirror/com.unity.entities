using JetBrains.Annotations;
using Unity.Properties;

namespace Unity.Entities.Editor
{
    [DOTSEditorPreferencesSetting(Constants.Settings.Inspector),
     InternalSetting, // Remove if not all fields are internal
     UsedImplicitly]
    class InspectorSettings : ISetting
    {
        [InternalSetting]
        public bool DisplayComponentType = false;

        void ISetting.OnSettingChanged(PropertyPath path)
        {
        }
    }
}
