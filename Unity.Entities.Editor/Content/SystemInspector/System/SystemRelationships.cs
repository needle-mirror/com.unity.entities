using JetBrains.Annotations;
using Unity.Properties;
using UnityEditor;

namespace Unity.Entities.Editor
{
    class SystemRelationships : ITabContent
    {
        public string TabName { get; } = L10n.Tr("Relationships");

        [CreateProperty] readonly SystemEntities m_Entities;
        [CreateProperty, UsedImplicitly] readonly SystemDependencies m_SystemDependencies;

        public void OnTabVisibilityChanged(bool isVisible)
            => m_Entities.OnTabVisibilityChanged(isVisible);

        public SystemRelationships(SystemEntities entities, SystemDependencies systemDependencies)
        {
            m_Entities = entities;
            m_SystemDependencies = systemDependencies;
        }
    }
}
