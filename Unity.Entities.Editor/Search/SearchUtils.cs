using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using Unity.Editor.Bridge;
using UnityEditor.UI;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    internal class SearchItemWrapper<T> : UnityEngine.ScriptableObject
    {
        public SearchItem item;
        public T objItem;
    }

    [QueryListBlock("World", "world", "w", "=")]
    class QueryWorldBlock : QueryListBlock
    {
        public QueryWorldBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            var category = flags.HasFlag(SearchPropositionFlags.NoCategory) ? null : this.category;
            foreach (var t in World.All)
            {
                var name = t.Name.Contains(" ") ? $"\"{t.Name}\"" : t.Name;
                yield return new SearchProposition(category, t.Name, replacement: name, help: $"Entity World: {t.Name}", type: GetType(), data: name);
            }
        }
    }

    [QueryListBlock("Component", "c", "c", "=")]
    class QueryComponentTypeBlock : QueryListBlock
    {
        public QueryComponentTypeBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            var category = flags.HasFlag(SearchPropositionFlags.NoCategory) ? null : this.category;
            foreach (var cn in SearchUtils.componentNames)
            {
                var name = cn.Contains(" ") ? $"\"{cn}\"" : cn;
                yield return new SearchProposition(category, $"all={cn}", replacement: name, help: $"Component type: {cn}", type: GetType(), data: name);
            }
        }
    }

    [QueryListBlock("Component (All)", "All", "all", "=")]
    class QueryAllComponentTypeBlock : QueryComponentTypeBlock
    {
        public QueryAllComponentTypeBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }
    }

    [QueryListBlock("Component (None)", "None", "-c", "=")]
    class QueryNotAllComponentTypeBlock : QueryListBlock
    {
        public QueryNotAllComponentTypeBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            var category = flags.HasFlag(SearchPropositionFlags.NoCategory) ? null : this.category;
            foreach (var cn in SearchUtils.componentNames)
            {
                var name = cn.Contains(" ") ? $"\"{cn}\"" : cn;
                yield return new SearchProposition(category, $"none={cn}", replacement: name, help: $"No Component of type: {cn}", type: GetType(), data: name);
            }
        }
    }

    [QueryListBlock("Component (None)", "None", "none", "=")]
    class QueryNoComponentTypeBlock : QueryNotAllComponentTypeBlock
    {
        public QueryNoComponentTypeBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }
    }

    [QueryListBlock("Component (Any)", "Any", "any", "=")]
    class QueryAnyComponentTypeBlock : QueryListBlock
    {
        public QueryAnyComponentTypeBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            var category = flags.HasFlag(SearchPropositionFlags.NoCategory) ? null : this.category;
            foreach (var cn in SearchUtils.componentNames)
            {
                var name = cn.Contains(" ") ? $"\"{cn}\"" : cn;
                yield return new SearchProposition(category, $"any={cn}", replacement: name, help: $"Any Component of type: {cn}", type: GetType(), data: name);
            }
        }
    }

    [QueryListBlock("Kind", "Kind", "k", "=")]
    class QueryNodeKindBlock : QueryListBlock
    {
        public QueryNodeKindBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            return SearchUtils.GetEnumPropositions<NodeKind>(flags, this, "Kind:", new[] { NodeKind.Entity, NodeKind.GameObject, NodeKind.Scene, NodeKind.SubScene });
        }
    }

    [QueryListBlock("Prefab Type", "Prefab Type", "prefabtype", "=")]
    class QueryPrefabTypeBlock : QueryListBlock
    {
        public QueryPrefabTypeBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            return SearchUtils.GetEnumPropositions<Editor.Hierarchy.HierarchyPrefabType>(flags, this, "Prefab Type:");
        }
    }

    [QueryListBlock("System Dependencies", "System Dependencies", "sd", "=")]
    class QuerySystemDependenciesBlock : QueryListBlock
    {
        public QuerySystemDependenciesBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            var category = flags.HasFlag(SearchPropositionFlags.NoCategory) ? null : this.category;
            foreach (var sys in SystemSearchProvider.systems)
            {
                yield return new SearchProposition(category, $"{id}{op}{sys.name}", replacement: sys.name, help: $"{category} {sys.name}", type: GetType(), data: sys.name);
            }
        }
    }

    [QueryListBlock("System", "System", "s", ":")]
    class QuerySystemBlock : QuerySystemDependenciesBlock
    {
        public QuerySystemBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }
    }

    [QueryListBlock("Origin System", "Origin System", "os", ":")]
    class QueryOriginSystemBlock : QuerySystemDependenciesBlock
    {
        public QueryOriginSystemBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }
    }

    [QueryListBlock("Executing System", "Executing System", "es", ":")]
    class QueryExecutingSystemBlock : QuerySystemDependenciesBlock
    {
        public QueryExecutingSystemBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }
    }


    [QueryListBlock("Category", "category", "category", ":")]
    class QueryComponentCategoryBlock : QueryListBlock
    {
        public QueryComponentCategoryBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            return SearchUtils.GetEnumPropositions<ComponentsWindow.DebugTypeCategory>(flags, this, "Category:");
        }
    }

    [QueryListBlock("Record Type", "rt", "rt", "=")]
    class QueryRecordTypeBlock : QueryListBlock
    {
        public QueryRecordTypeBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
            : base(source, id, value, attr)
        {
        }

        public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags)
        {
            return SearchUtils.GetEnumPropositions<EntitiesJournaling.RecordType>(flags, this, "Record Type:");
        }
    }

    static class SearchUtils
    {
        public static Texture2D componentIcon = PackageResources.LoadIcon("Components/Component.png");
        public static Texture2D chunkComponentIcon = PackageResources.LoadIcon("Components/Chunk Component.png");
        public static Texture2D bufferComponentIcon = PackageResources.LoadIcon("Components/Buffer Component.png");
        public static Texture2D sharedComponentIcon = PackageResources.LoadIcon("Components/Shared Component.png");
        public static Texture2D managedComponentIcon = PackageResources.LoadIcon("Components/Managed Component.png");
        public static Texture2D tagComponentIcon = PackageResources.LoadIcon("Components/Tag Component.png");
        public static Texture2D search = PackageResources.LoadIcon("Search/Search.png");
        public static Texture2D gotoIcon = PackageResources.LoadIcon("Go To/Go to.png");

        public static Texture2D sceneAssetIcon = SearchBridge.LoadIcon("SceneAsset Icon");
        public static Texture2D entityIcon = PackageResources.LoadIcon("Entity/Entity.png");
        public static Texture2D entityPrefabIcon = PackageResources.LoadIcon("Entity/EntityPrefab.png");
        public static Texture2D gameObjectIcon = SearchBridge.LoadIcon("GameObject Icon");
        public static Texture2D prefabIcon = SearchBridge.LoadIcon("Prefab Icon");

        public static class Styles
        {
            public static readonly GUIStyle iconButton = "IconButton";
        }

        static string[] s_ComponentNames;
        public static IEnumerable<string> componentNames
        {
            get
            {
                if (s_ComponentNames == null)
                {
                    s_ComponentNames = TypeManager.GetAllTypes().Where(t => t.Type != null).Select(t => t.Type.Name).ToArray();
                }
                return s_ComponentNames;
            }
        }

        public static Texture2D GetComponentIcon(TypeIndex typeIndex)
        {
            if (TypeManager.IsChunkComponent(typeIndex))
                return chunkComponentIcon;
            else if (TypeManager.IsBuffer(typeIndex))
                return bufferComponentIcon;
            else if (TypeManager.IsSharedComponentType(typeIndex))
                return sharedComponentIcon;
            else if (TypeManager.IsManagedComponent(typeIndex))
                return managedComponentIcon;
            else if (TypeManager.IsZeroSized(typeIndex))
                return tagComponentIcon;
            else
                return componentIcon;
        }

        const string k_EditorWorld = "Editor World";
        const string k_DefaultWorld = "Default World";
        static string GetDefaultWorldName()
        {
            return EditorApplication.isPlaying ? k_DefaultWorld : k_EditorWorld;
        }

        public static World FindWorld(string worldName = null)
        {
            if (worldName != null)
            {
                worldName = worldName.ToLowerInvariant();
                if (worldName == "default")
                    worldName = k_DefaultWorld;
                else if (worldName == "editor")
                    worldName = k_EditorWorld;
            }
            worldName = worldName ?? GetDefaultWorldName();
            foreach (var w in World.All)
                if (w.Name.Equals(worldName, System.StringComparison.InvariantCultureIgnoreCase))
                    return w;
            return null;
        }

        internal static IEnumerable<SearchProposition> GetEnumPropositions<TEnum>(SearchPropositionFlags flags, QueryListBlock b, string helpTemplate) where TEnum : System.Enum
        {
            return GetEnumPropositions(flags, b, helpTemplate, (TEnum[])Enum.GetValues(typeof(TEnum)));
        }

        internal static IEnumerable<SearchProposition> GetEnumPropositions<TEnum>(SearchPropositionFlags flags, QueryListBlock b, string helpTemplate, IEnumerable<TEnum> values) where TEnum : System.Enum
        {
            var category = flags.HasFlag(SearchPropositionFlags.NoCategory) ? null : b.category;
            foreach (var obj in values)
            {
                var e = obj;
                yield return new SearchProposition(category: category, label: e.ToString(), help: $"{helpTemplate} {e}",
                        data: e, priority: 0, icon: null, type: b.GetType(), color: SearchBridge.GetBackgroundColor(b));
            }
        }

        public static void GetQueryParts(IQueryNode n, List<IFilterNode> filters, List<IQueryNode> toggles, List<ISearchNode> searches)
        {
            if (n == null)
                return;
            if (n.type == QueryNodeType.Toggle)
            {
                toggles.Add(n);
            }
            else if (n is IFilterNode filterNode)
            {
                filters.Add(filterNode);
            }
            else if (n is ISearchNode searchNode)
            {
                searches.Add(searchNode);
            }

            if (n.children != null)
            {
                foreach (var child in n.children)
                {
                    GetQueryParts(child, filters, toggles, searches);
                }
            }
        }

        public static T ParseEnum<T>(string value)
        {
            var values = Enum.GetValues(typeof(T));
            var names = Enum.GetNames(typeof(T));
            for (var i = 0; i < names.Length; ++i)
                if (names[i].Equals(value, StringComparison.InvariantCultureIgnoreCase))
                    return (T)values.GetValue(i);
            return (T)values.GetValue(0);
        }

        public static string AddOrReplaceFilterInQuery(string originalQuery, string filter, string op, object value)
        {
            var newFilter = $"{filter}{op}{value}";
            if (originalQuery.Contains(newFilter))
                return originalQuery;

            var regexStr = $"\\b{filter}{op}(\\S*)";
            var regex = new Regex(regexStr);
            
            var newQuery = regex.Replace(originalQuery, newFilter);
            var m = regex.Match(originalQuery);
            if (newQuery == originalQuery)
            {
                if (newQuery.Length == 0)
                    newQuery = newFilter;
                else if (newQuery[newQuery.Length - 1] == ':')
                    newQuery = $"{newQuery}{newFilter}";
                else
                    newQuery = $"{newQuery} {newFilter}";
            }
            return newQuery;
        }

        public static string DefaultFetchLabel(SearchItem item, SearchContext context)
        {
            return item.label ?? item.id ?? string.Empty;
        }

        public static string DefaultFetchDescription(SearchItem item, SearchContext context)
        {
            var desc = item.description ?? string.Empty;
            if (item.options.HasFlag(SearchItemOptions.Compacted) && item?.provider?.fetchLabel != null)
            {
                return $"{item.provider.fetchLabel(item, context)} - {desc}";
            }
            return desc;
        }

        public static Texture2D DefaultFetchThumbnail(SearchItem item, SearchContext context)
        {
            return item.thumbnail;
        }

        public static Button CreateJumpButton(Action onButtonClick)
        {
            var jump = new Button();
            SetupJumpButton(jump, onButtonClick);
            return jump;
        }

        public static void SetupJumpButton(Button jump, Action onButtonClick)
        {
            jump.style.backgroundImage = SearchBridge.LoadIcon("SearchJump Icon");
            jump.AddToClassList(UssClasses.DotsEditorCommon.SearchIcon);
            jump.clicked += onButtonClick;
        }
    }

}
