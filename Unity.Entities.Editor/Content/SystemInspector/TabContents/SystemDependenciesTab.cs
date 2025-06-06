using JetBrains.Annotations;
using Unity.Entities.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class SystemDependenciesTab : ITabContent
    {
        public string TabName { get; } = L10n.Tr("Dependencies");
        public SystemProxy SystemProxy { get; }

        public SystemDependenciesTab(SystemProxy systemProxy) => SystemProxy = systemProxy;

        public void OnTabVisibilityChanged(bool isVisible) {}
    }

    [UsedImplicitly]
    class SystemDependenciesInspector : PropertyInspector<SystemDependenciesTab>
    {
        VisualElement m_SectionContainer;

        public override VisualElement Build()
        {
            var root = new VisualElement();
            Resources.Templates.DotsEditorCommon.AddStyles(root);
            m_SectionContainer = new VisualElement();
            root.Add(m_SectionContainer);

            m_SectionContainer.Add(BuildDependencyView());

            Update();
            return root;
        }

        VisualElement BuildDependencyView()
        {
            using var readList = PooledList<ComponentViewData>.Make();
            using var writeList = PooledList<ComponentViewData>.Make();
            Target.SystemProxy.FillListWithJobDependencyForReadingSystems(readList);
            Target.SystemProxy.FillListWithJobDependencyForWritingSystems(writeList);

            var sectionElement = new VisualElement();

            var readSection = new FoldoutWithoutActionButton
            {
                HeaderName = { text = L10n.Tr("Read Dependencies") },
                MatchingCount = { text = readList.List.Count.ToString() }
            };
            readSection.Q<Toggle>().AddToClassList(UssClasses.FoldoutWithoutActionButton.ToggleNoBorder);
            sectionElement.Add(readSection);

            var writeSection = new FoldoutWithoutActionButton
            {
                HeaderName = { text = L10n.Tr("Write Dependencies") },
                MatchingCount = { text = writeList.List.Count.ToString() }
            };
            writeSection.Q<Toggle>().AddToClassList(UssClasses.FoldoutWithoutActionButton.ToggleNoBorder);
            sectionElement.Add(writeSection);

            foreach (var comp in readList.List)
                readSection.Add(new ComponentView(comp));

            foreach (var comp in writeList.List)
                writeSection.Add(new ComponentView(comp));

            return sectionElement;
        }
    }
}
