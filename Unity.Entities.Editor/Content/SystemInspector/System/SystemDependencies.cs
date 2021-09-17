using System.Collections.Generic;
using JetBrains.Annotations;
using Unity.Properties.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class SystemDependencies
    {
        readonly World m_World;
        SystemProxy m_SystemProxy;
        readonly List<SystemDependencyViewData> m_UpdateBeforeSystemViewDataList;
        readonly List<SystemDependencyViewData> m_UpdateAfterSystemViewDataList;

        public SystemDependencies(World world, SystemProxy systemProxy)
        {
            m_World = world;
            m_SystemProxy = systemProxy;
            m_UpdateBeforeSystemViewDataList = new List<SystemDependencyViewData>();
            m_UpdateAfterSystemViewDataList = new List<SystemDependencyViewData>();
        }

        public string CurrentSystemName => m_SystemProxy.TypeName;

        public List<SystemDependencyViewData> GetUpdateBeforeSystemViewDataList()
        {
            m_UpdateBeforeSystemViewDataList.Clear();

            if (m_World == null || !m_World.IsCreated || !m_SystemProxy.Valid)
                return m_UpdateBeforeSystemViewDataList;

            foreach (var after in m_SystemProxy.UpdateAfterSet)
                m_UpdateBeforeSystemViewDataList.Add(new SystemDependencyViewData(after, after.NicifiedDisplayName));

            return m_UpdateBeforeSystemViewDataList;
        }

        public List<SystemDependencyViewData> GetUpdateAfterSystemViewDataList()
        {
            m_UpdateAfterSystemViewDataList.Clear();

            if (m_World == null || !m_World.IsCreated || !m_SystemProxy.Valid)
                return m_UpdateAfterSystemViewDataList;

            foreach (var before in m_SystemProxy.UpdateBeforeSet)
                m_UpdateAfterSystemViewDataList.Add(new SystemDependencyViewData(before, before.NicifiedDisplayName));

            return m_UpdateAfterSystemViewDataList;
        }
    }

    [UsedImplicitly]
    class SystemDependenciesInspector : Inspector<SystemDependencies>
    {
        static readonly string k_SystemDependenciesSection = L10n.Tr("Scheduling Constraints");

        public override VisualElement Build()
        {
            var updateBeforeSystemViewDataList = Target.GetUpdateBeforeSystemViewDataList();
            var updateAfterSystemViewDataList = Target.GetUpdateAfterSystemViewDataList();
            var currentSystemName = Target.CurrentSystemName;

            var sectionElement = new FoldoutWithoutActionButton
            {
                HeaderName = { text = k_SystemDependenciesSection },
                MatchingCount = { text = (updateBeforeSystemViewDataList.Count + updateAfterSystemViewDataList.Count).ToString() }
            };

            var updateBeforeSection = new FoldoutWithoutActionButton
            {
                HeaderName = { text = $"Update {currentSystemName} Before"},
                MatchingCount = { text = updateAfterSystemViewDataList.Count.ToString()}
            };

            updateBeforeSection.Q<Toggle>().AddToClassList(UssClasses.FoldoutWithoutActionButton.ToggleNoBorder);

            var updateAfterSection = new FoldoutWithoutActionButton
            {
                HeaderName = { text = $"Update {currentSystemName} After"},
                MatchingCount = { text = updateBeforeSystemViewDataList.Count.ToString()}
            };

            updateAfterSection.Q<Toggle>().AddToClassList(UssClasses.FoldoutWithoutActionButton.ToggleNoBorder);

            sectionElement.Add(updateBeforeSection);
            sectionElement.Add(updateAfterSection);

            foreach (var systemDependencyInfo in updateAfterSystemViewDataList)
            {
                updateBeforeSection.Add(new SystemDependencyView(systemDependencyInfo));
            }

            foreach (var systemDependencyInfo in updateBeforeSystemViewDataList)
            {
                updateAfterSection.Add(new SystemDependencyView(systemDependencyInfo));
            }

            return sectionElement;
        }
    }
}
