
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Editor
{
    [System.Serializable]
    public class SystemInclusionList
    {
        private readonly List<Tuple<ScriptBehaviourManager, List<ComponentGroup>>> cachedMatches = new List<Tuple<ScriptBehaviourManager, List<ComponentGroup>>>();
        private bool repainted = true;

        [SerializeField] private bool showSystems;

        public void OnGUI(World world, Entity entity)
        {
            ++EditorGUI.indentLevel;
            GUILayout.BeginVertical(GUI.skin.box);
            showSystems = EditorGUILayout.Foldout(showSystems, "Used by Systems");

            if (showSystems)
            {
                if (repainted == true)
                {
                    cachedMatches.Clear();
                    WorldDebuggingTools.MatchEntityInComponentGroups(world, entity, cachedMatches);
                    repainted = false;
                }

                foreach (var pair in cachedMatches)
                {
                    var type = pair.Item1.GetType();
                    GUILayout.Label(new GUIContent(type.Name, type.AssemblyQualifiedName));
                    ++EditorGUI.indentLevel;
                    foreach (var componentGroup in pair.Item2)
                    {
                        ComponentList(componentGroup.Types, EditorGUIUtility.currentViewWidth - 30f);
                        if (GUILayout.Button("Show", GUILayout.ExpandWidth(false)))
                        {
                            EntityDebugger.SetAllSelections(world, pair.Item1 as ComponentSystemBase, componentGroup, entity);
                        }
                    }

                    --EditorGUI.indentLevel;
                }

                if (Event.current.type == EventType.Repaint)
                {
                    repainted = true;
                }
            }
            GUILayout.EndVertical();

            --EditorGUI.indentLevel;
        }

        void ComponentList(ComponentType[] types, float width)
        {
            var sortedTypes = new List<ComponentType>(types.Skip(1));
            sortedTypes.Sort(ComponentGroupListView.CompareTypes);
            var styles = new List<GUIStyle>(sortedTypes.Count);
            var names = new List<GUIContent>(sortedTypes.Count);
            var rects = new List<Rect>(sortedTypes.Count);
            var x = 0f;
            var y = 0f;
            for (var i = 0; i < sortedTypes.Count; ++i) // Skip Entity
            {
                var type = sortedTypes[i];
                var style = ComponentGroupListView.StyleForAccessMode(type.AccessModeType);
                var content = new GUIContent(type.GetManagedType().Name);
                var rect = new Rect(new Vector2(x, y), style.CalcSize(content));
                if (rect.xMax > width && x != 0f)
                {
                    rect.x = 0f;
                    rect.y += rect.height + 2f;
                }

                x = rect.xMax + 2f;
                y = rect.y;

                styles.Add(style);
                names.Add(content);
                rects.Add(rect);
            }

            var wholeRect = GUILayoutUtility.GetRect(width, rects.Last().yMax);
            if (Event.current.type == EventType.Repaint)
            {
                for (var i = 0; i < rects.Count; ++i)
                {
                    var rect = rects[i];
                    rect.position += wholeRect.position;
                    styles[i].Draw(rect, names[i], false, false, false, false);
                }
            }
        }
    }
}
