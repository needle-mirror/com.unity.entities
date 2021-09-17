using JetBrains.Annotations;
using Unity.Properties;
using Unity.Properties.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    [DOTSEditorPreferencesSetting(Constants.Settings.Hierarchy), UsedImplicitly]
    class HierarchySettings : ISetting
    {
        [InternalSetting] public HierarchyConfiguration Configuration = new HierarchyConfiguration();

        void ISetting.OnSettingChanged(PropertyPath path)
        {
        }

        [UsedImplicitly]
        class Inspector : Inspector<HierarchySettings>
        {
            VisualElement m_MillisecondsPerUpdate;
            VisualElement m_EntityChangeIntegrationBatchSize;
            VisualElement m_ExportImmutableBatchSize;
            
            public override VisualElement Build()
            {
                var root = new VisualElement();

                var updateModeType = new VisualElement();
                var millisecondsBetweenUpdateCycles = new VisualElement();
                var excludeUnnamedNodesForSearch = new VisualElement();

                DoDefaultGui(updateModeType, nameof(Configuration) + "." + nameof(HierarchyConfiguration.UpdateMode));
                DoDefaultGui(millisecondsBetweenUpdateCycles, nameof(Configuration) + "." + nameof(HierarchyConfiguration.MinimumMillisecondsBetweenHierarchyUpdateCycles));
                DoDefaultGui(excludeUnnamedNodesForSearch, nameof(Configuration) + "." + nameof(HierarchyConfiguration.ExcludeUnnamedNodesForSearch));
                
                root.Add(updateModeType);
                root.Add(millisecondsBetweenUpdateCycles);
                
                if (Unsupported.IsDeveloperMode())
                {
                    m_MillisecondsPerUpdate = new VisualElement();
                    m_EntityChangeIntegrationBatchSize = new VisualElement();
                    m_ExportImmutableBatchSize = new VisualElement();
                
                    // Async specific options.
                    DoDefaultGui(m_MillisecondsPerUpdate, nameof(Configuration) + "." + nameof(HierarchyConfiguration.MaximumMillisecondsPerEditorUpdate));
                    DoDefaultGui(m_EntityChangeIntegrationBatchSize, nameof(Configuration) + "." + nameof(HierarchyConfiguration.EntityChangeIntegrationBatchSize));
                    DoDefaultGui(m_ExportImmutableBatchSize, nameof(Configuration) + "." + nameof(HierarchyConfiguration.ExportImmutableBatchSize));
                    
                    updateModeType.Q<EnumField>().RegisterValueChangedCallback(evt =>
                    {
                        SetUpdateMode((Hierarchy.UpdateModeType) evt.newValue);
                    });
                
                    root.Add(m_MillisecondsPerUpdate);
                    root.Add(m_EntityChangeIntegrationBatchSize);
                    root.Add(m_ExportImmutableBatchSize);
                    
                    SetUpdateMode(Target.Configuration.UpdateMode);
                }
                
                root.Add(excludeUnnamedNodesForSearch);

                var legacy = new Button
                {
                    text = "Launch Legacy Hierarchy",
                    style = { width = 175 }
                };
                
                legacy.clickable.clicked += () =>
                {
                    EditorWindow.GetWindow<EntityHierarchyWindow>().Show();
                };
                
                root.Add(legacy);
                
                return root;
            }
            
            void SetUpdateMode(Hierarchy.UpdateModeType type)
            {
                var isAsync = type == Hierarchy.UpdateModeType.Asynchronous;
                
                m_MillisecondsPerUpdate.SetVisibility(isAsync);
                m_EntityChangeIntegrationBatchSize.SetVisibility(isAsync);
                m_ExportImmutableBatchSize.SetVisibility(isAsync);
            }
        }
    }
}