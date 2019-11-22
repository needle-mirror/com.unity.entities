using Unity.Properties;

namespace Unity.Build.Common
{
    internal sealed class InternalSourceBuildConfiguration : IBuildSettingsComponent
    {
        [Property] public bool Enabled { get; set; }
    }
}
