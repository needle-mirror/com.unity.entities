using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Unity.Entities.Editor
{
    delegate void WorldSelectionSetter(World world);

    delegate bool ShowInactiveSystemsGetter();

    class WorldPopup
    {
        public const string kNoWorldName = "\n\n\n";

        const string kPlayerLoopName = "Show Full Player Loop";
        const string kShowInactiveSystemsName = "Show Inactive Systems";

        readonly WorldDisplayNameCache m_WorldDisplayNameCache;
        GenericMenu m_CachedMenu;

        readonly WorldSelectionGetter getWorldSelection;
        readonly WorldSelectionSetter setWorldSelection;

        readonly ShowInactiveSystemsGetter getShowInactiveSystems;
        readonly GenericMenu.MenuFunction setShowInactiveSystems;

        readonly Func<bool> getShowAllWorlds;
        readonly Action<bool> setShowAllWorlds;

        readonly WorldListChangeTracker m_WorldListChangeTracker = new WorldListChangeTracker();

        public WorldPopup(WorldSelectionGetter getWorld, WorldSelectionSetter setWorld, ShowInactiveSystemsGetter getShowSystems, GenericMenu.MenuFunction setShowSystems, Func<bool> getShowAllWorlds, Action<bool> setShowAllWorlds)
        {
            getWorldSelection = getWorld;
            setWorldSelection = setWorld;

            getShowInactiveSystems = getShowSystems;
            setShowInactiveSystems = setShowSystems;

            this.getShowAllWorlds = getShowAllWorlds;
            this.setShowAllWorlds = setShowAllWorlds;

            m_WorldDisplayNameCache = new WorldDisplayNameCache(IncludeWorldInMenu);
        }

        public void OnGUI(bool showingPlayerLoop, string lastSelectedWorldName, GUIStyle style = null)
        {
            if (m_WorldListChangeTracker.HasChanged())
            {
                m_CachedMenu = null;
                m_WorldDisplayNameCache.RebuildCache();
            }

            TryRestorePreviousSelection(showingPlayerLoop, lastSelectedWorldName);

            var worldName = m_WorldDisplayNameCache.GetWorldDisplayName(getWorldSelection()) ?? kPlayerLoopName;
            if (EditorGUILayout.DropdownButton(new GUIContent(worldName), FocusType.Passive, style ?? "MiniPullDown"))
            {
                (m_CachedMenu ?? (m_CachedMenu = BuildWorldDropdown())).ShowAsContext();
            }
        }

        internal void TryRestorePreviousSelection(bool showingPlayerLoop, string lastSelectedWorldName)
        {
            if (!showingPlayerLoop && PlayerLoop.GetCurrentPlayerLoop().subSystemList != null)
            {
                if (lastSelectedWorldName == kNoWorldName)
                {
                    if (World.All.Count > 0)
                        setWorldSelection(World.All[0]);
                }
                else if (lastSelectedWorldName != getWorldSelection()?.Name)
                {
                    foreach (var world in World.All)
                    {
                        if (world.Name != lastSelectedWorldName)
                            continue;

                        setWorldSelection(world);
                        return;
                    }
                }
            }
        }

        GenericMenu BuildWorldDropdown()
        {
            var menu = new GenericMenu();
            var currentSelection = getWorldSelection();

            foreach (var world in World.All)
            {
                if (!IncludeWorldInMenu(world))
                    continue;

                var worldName = m_WorldDisplayNameCache.GetWorldDisplayName(world);
                menu.AddItem(new GUIContent(worldName), currentSelection == world, () => ChangeSelectedWorld(world));
            }

            if (menu.GetItemCount() == 0)
                menu.AddDisabledItem(new GUIContent("No Worlds"));

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Show All Worlds"), getShowAllWorlds(), ToggleShowAllWorlds);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(kPlayerLoopName), currentSelection == null, () => ChangeSelectedWorld(null));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(kShowInactiveSystemsName), getShowInactiveSystems(), setShowInactiveSystems);
            return menu;
        }

        void ToggleShowAllWorlds()
        {
            var showAllWorlds = !getShowAllWorlds();
            setShowAllWorlds(showAllWorlds);

            // Filter might have changed, we need to rebuild the display name
            // cache because some worlds can share the same name now.
            m_WorldDisplayNameCache.RebuildCache();
            m_CachedMenu = null;

            if (showAllWorlds)
                return;

            var currentSelection = getWorldSelection();
            if (currentSelection != null && (currentSelection.Flags & (WorldFlags.Streaming | WorldFlags.Shadow)) != 0)
                setWorldSelection(World.All.Count > 0 ? World.All[0] : null);
        }

        bool IncludeWorldInMenu(World w) => getShowAllWorlds() || (w.Flags & (WorldFlags.Streaming | WorldFlags.Shadow)) == 0;

        void ChangeSelectedWorld(World w)
        {
            m_CachedMenu = null;
            setWorldSelection(w);
        }
    }
}
