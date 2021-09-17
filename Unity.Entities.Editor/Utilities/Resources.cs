using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    static class Resources
    {
        public const string Uxml = Constants.EditorDefaultResourcesPath + "uxml/";
        public const string Uss = Constants.EditorDefaultResourcesPath + "uss/";

        const string k_ProSuffix = "_dark";
        const string k_PersonalSuffix = "_light";

        public static string SkinSuffix => EditorGUIUtility.isProSkin ? k_ProSuffix : k_PersonalSuffix;

        public static string UxmlFromName(string name)
        {
            return Uxml + name + ".uxml";
        }

        public static string UssFromName(string name)
        {
            return Uss + name + ".uss";
        }

        public static void AddCommonVariables(VisualElement rootElement)
        {
            Templates.Variables.AddStyles(rootElement);
            rootElement.AddToClassList("variables");
        }

        public static class Templates
        {
            public static readonly UITemplate Variables = new UITemplate("Common/variables");

            public static readonly UITemplate TabView = new UITemplate("Controls/TabView/tab-view");

            public static readonly UITemplate SystemSchedule = new UITemplate("SystemSchedule/system-schedule");
            public static readonly UITemplate SystemScheduleTreeViewHeader = new UITemplate("SystemSchedule/system-schedule-header");
            public static readonly UITemplate SystemScheduleItem = new UITemplate("SystemSchedule/system-schedule-item");
            public static readonly UITemplate SystemScheduleToolbar = new UITemplate("SystemSchedule/system-schedule-toolbar");
            public static readonly UITemplate DotsEditorCommon = new UITemplate("Common/dots-editor-common");
            public static readonly UITemplate CenteredMessageElement = new UITemplate("Common/centered-message-element");
            public static readonly UITemplate EntityHierarchyToolbar = new UITemplate("EntityHierarchy/entity-hierarchy-toolbar");
            public static readonly UITemplate EntityHierarchyEnableLiveConversionMessage = new UITemplate("EntityHierarchy/entity-hierarchy-enable-live-link-message");
            public static readonly UITemplate EntityHierarchyItem = new UITemplate("EntityHierarchy/entity-hierarchy-item");

            public static readonly UITemplate DebugWindow = new UITemplate("DebugWindows/debug-window");
            public static readonly UITemplate EntityDebugWindow = new UITemplate("DebugWindows/entity-debug-window");
            public static readonly UITemplate SystemsDebugWindow = new UITemplate("DebugWindows/system-debug-window");

            public static readonly UITemplate ComponentView = new UITemplate("Common/component-view");
            public static readonly UITemplate QueryView = new UITemplate("Common/query-view");
            public static readonly UITemplate SystemDependencyView = new UITemplate("Common/system-dependency-view");
            public static readonly UITemplate SystemListView = new UITemplate("Common/system-list-view");
            public static readonly UITemplate SystemQueriesView = new UITemplate("Common/system-queries-view");
            public static readonly UITemplate QueryWithEntities = new UITemplate("Common/query-with-entities");
            public static readonly UITemplate EntityView = new UITemplate("Common/entity-view");
            public static readonly UITemplate FoldoutWithActionButton = new UITemplate("Common/foldout-with-action-button");
            public static readonly UITemplate FoldoutWithoutActionButton = new UITemplate("Common/foldout-without-action-button");

            public static readonly UITemplate ComponentTypeView = new UITemplate("Components/component-type-view");

            public static class Inspector
            {
                public static readonly UITemplate EntityInspector = new UITemplate("Inspector/entity-inspector");
                public static readonly UITemplate EntityHeader = new UITemplate("Inspector/entity-header");
                public static readonly UITemplate InspectorStyle = new UITemplate("Inspector/inspector");
                public static readonly UITemplate ComponentHeader = new UITemplate("Inspector/component-header");
                public static readonly UITemplate TagComponentElement = new UITemplate("Inspector/tag-component-element");
                public static readonly UITemplate EntityField = new UITemplate("Inspector/entity-field");
                public static readonly UITemplate ComponentsTab = new UITemplate("Inspector/entity-inspector-components-tab");

                public static class RelationshipsTab
                {
                    public static readonly UITemplate Root = new UITemplate("Inspector/entity-inspector-relationships-tab");
                }
            }

            public static class ContentProvider
            {
                public static readonly UITemplate Header = new UITemplate("Content/header");
                public static readonly UITemplate EntityInfo = new UITemplate("Content/entity-info");
                public static readonly UITemplate EntityQuery = new UITemplate("Content/entity-query");
                public static readonly UITemplate EntityQueryHeader = new UITemplate("Content/entity-query-header");
                public static readonly UITemplate System = new UITemplate("Content/system");
                public static readonly UITemplate Component = new UITemplate("Content/component");

                public static readonly UITemplate ComponentsWindow = new UITemplate("Components/components-window");
                public static readonly UITemplate ComponentAttribute = new UITemplate("Content/component-attribute");
            }

            public static class Hierarchy
            {
                public static readonly UITemplate Root = new UITemplate("Hierarchy/hierarchy");
                public static readonly UITemplate Item = new UITemplate("Hierarchy/hierarchy-item");
                public static readonly UITemplate Footer = new UITemplate("Hierarchy/hierarchy-footer");
                public static readonly UITemplate Toolbar = new UITemplate("Hierarchy/hierarchy-toolbar");
                public static readonly UITemplate Loading = new UITemplate("Hierarchy/hierarchy-loading");
            }
        }
    }
}
