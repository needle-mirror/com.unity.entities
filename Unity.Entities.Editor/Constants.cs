using UnityEngine;

namespace Unity.Entities.Editor
{
    static class Constants
    {
        public const string PackageName = "com.unity.entities";
        public const string PackagePath = "Packages/" + PackageName;

        public const string EditorDefaultResourcesPath = PackagePath + "/Editor Default Resources/";

        public static readonly Vector2 MinWindowSize = new Vector2(200, 200); // Matches SceneHierarchy's min size

        public static class Conversion
        {
            public const string SelectedComponentSessionKey = "Conversion.Selected.{0}.{1}";
            public const string ShowAdditionalEntitySessionKey = "Conversion.ShowAdditional.{0}";
            public const string SelectedAdditionalEntitySessionKey = "Conversion.Additional.{0}";
        }

        public static class MenuItems
        {
            public const int WindowPriority = 3006;
            public const string SystemScheduleWindow = "Window/DOTS/Systems";
            public const string EntityHierarchyWindow = "Window/DOTS/Entities";
            public const string HierarchyWindow = "Window/DOTS/Hierarchy";
            public const string ComponentsWindow = "Window/DOTS/Components";
            public const string ArchetypesWindow = "Window/DOTS/Archetypes";
        }

        public static class ListView
        {
            public const int ItemHeight = 16;
        }

        public static class Settings
        {
            public const string Inspector = "Entity Inspector Settings";
            public const string Hierarchy = "Entity Hierarchy Settings";
            public const string SystemsWindow = "Systems Window Settings";
            public const string Advanced = "Advanced Settings";
        }

        public static class SystemSchedule
        {
            public const string k_ComponentToken = "c:";
            public const string k_SystemDependencyToken = "sd:";
            public const string k_ScriptType = " t:Script";
            public const int k_ShowMinimumQueryCount = 2;
            public const string k_Dash = "-";
        }
        
        public static class Hierarchy
        {
            public const string ComponentToken = "c";
            public const string ComponentTokenCaseInsensitive = "cC";
            public const string IndexToken = "i";
        }

        public static class EntityHierarchy
        {
            public const string FullViewName = "FullView";
            public const string SearchViewName = "SearchView";

            public const string ComponentToken = "c";
            public const string ComponentTokenCaseInsensitive = "cC";

            // Sensible initial capacity values for our numerous collections.
            // Aiming to minimize the amount of times we grow the backing collections, while being memory mindful.
            public static class InitialCapacity
            {
                public const int EntityNode = 1024;
                public const int SceneNode = 64;
                public const int CustomNode = 64;

                public const int AllNodes = EntityNode + SceneNode + CustomNode;

                public const int Children = 512;
            }
        }

        public static class Inspector
        {
            public const int MaxVisibleSystemCount = 50;
            public const double CoolDownTime = 300;
        }

        public static class State
        {
            public const string ViewDataKeyPrefix = "dots-editor__";
        }
    }
}
