using UnityEditor;
using UnityEngine;

namespace Unity.Editor.Bridge
{
    static class EditorWindowBridge
    {
        public static void ClearPersistentViewData(EditorWindow window) => window.ClearPersistentViewData();

        public static T[] GetEditorWindowInstances<T>() where T : EditorWindow => Resources.FindObjectsOfTypeAll<T>();

        public static DockArea GetDockArea(this EditorWindow @this)
        {
            if (@this.m_Parent is UnityEditor.DockArea dockArea)
                return new DockArea(dockArea);

            return null;
        }

        internal class DockArea
        {
            readonly UnityEditor.DockArea m_DockArea;

            public DockArea(UnityEditor.DockArea dockArea)
            {
                m_DockArea = dockArea;
            }

            public void AddTab(EditorWindow windowToDock)
                => m_DockArea.AddTab(windowToDock);

            public bool IsValid => m_DockArea;
        }
    }
}
