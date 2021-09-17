using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unity.Editor.Bridge;
using Unity.Properties.UI;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.Entities.EntitiesProfiler;
using static Unity.Entities.StructuralChangesProfiler;

namespace Unity.Entities.Editor
{
    partial class StructuralChangesProfilerModule : StructuralChangesProfilerModuleBase
    {
        static readonly string s_NoFrameDataAvailable = L10n.Tr("No frame data available. Select a frame from the charts above to see its details here.");
        static readonly string s_DisplayingFrameDataDisabled = L10n.Tr("Displaying of frame data disabled while recording. To see the data, pause recording.");
        static readonly string s_StructuralChanges = L10n.Tr("Structural Changes");
        static readonly string s_All = L10n.Tr("All");
        static readonly string s_Cost = L10n.Tr("Cost (ms)");
        static readonly string s_Count = L10n.Tr("Count");
        static readonly string s_CreateEntity = L10n.Tr("Create Entity");
        static readonly string s_DestroyEntity = L10n.Tr("Destroy Entity");
        static readonly string s_AddComponent = L10n.Tr("Add Component");
        static readonly string s_RemoveComponent = L10n.Tr("Remove Component");

        static readonly VisualElementTemplate s_WindowTemplate = PackageResources.LoadTemplate("ProfilerModules/structural-changes-profiler-window");
        static readonly VisualElementTemplate s_TreeViewItemTemplate = PackageResources.LoadTemplate("ProfilerModules/structural-changes-profiler-tree-view-item");

        long m_FrameIndex = -1;
        StructuralChangesProfilerTreeViewItemData[] m_StructuralChangesDataSource;
        readonly List<StructuralChangesProfilerTreeViewItemData> m_StructuralChangesDataFiltered = new List<StructuralChangesProfilerTreeViewItemData>();

        VisualElement m_Window;
        Label m_Message;
        VisualElement m_Content;
        SearchElement m_SearchElement;
        TreeView m_TreeView;

        public override string ProfilerCategoryName => Category.Name;

        public override string[] ProfilerCounterNames => new[]
        {
            CreateEntityCounter.Name,
            DestroyEntityCounter.Name,
            AddComponentCounter.Name,
            RemoveComponentCounter.Name
        };

        public StructuralChangesProfilerModule()
        {
            EntitiesProfiler.Initialize();
        }

        public override VisualElement CreateView(Rect area)
        {
            m_Window = s_WindowTemplate.Clone();

            var toolbar = m_Window.Q<Toolbar>("toolbar");
            m_SearchElement = toolbar.Q<SearchElement>("search");
            m_SearchElement.AddSearchDataCallback<StructuralChangesProfilerTreeViewItemData>(data =>
            {
                var result = new string[3];
                result[0] = ObjectNames.NicifyVariableName(data.Type.ToString());
                result[1] = data.WorldName;
                result[2] = data.SystemName;
                return result;
            });
            m_SearchElement.AddSearchFilterCallbackWithPopupItem<StructuralChangesProfilerTreeViewItemData, double>("cost", data => data.ElapsedNanoseconds * 1e-6, s_Cost);
            m_SearchElement.FilterPopupWidth = 250;

            var searchHandler = new SearchHandler<StructuralChangesProfilerTreeViewItemData>(m_SearchElement)
            {
                Mode = SearchHandlerType.async
            };
            searchHandler.SetSearchDataProvider(() =>
            {
                return m_StructuralChangesDataSource;
            });
            searchHandler.OnBeginSearch += query =>
            {
                m_StructuralChangesDataFiltered.Clear();
            };
            searchHandler.OnFilter += (query, filteredData) =>
            {
                m_StructuralChangesDataFiltered.AddRange(filteredData);
                Update();
            };
            // Work-around: OnFilter is not called if the search result yields no data.
            // So for the moment, we force a refresh in OnEndSearch, which means 2 refresh :(
            searchHandler.OnEndSearch += query =>
            {
                Update();
            };

            m_Message = m_Window.Q<Label>("message");
            m_Message.text = s_NoFrameDataAvailable;

            m_Content = m_Window.Q("content");
            m_Content.SetVisibility(false);

            var header = m_Content.Q("header");
            header.Q<Label>("column1").text = s_StructuralChanges;
            header.Q<Label>("column2").text = s_Cost;
            header.Q<Label>("column3").text = s_Count;

            m_TreeView = m_Content.Q<TreeView>("tree-view");
            m_TreeView.makeItem = () =>
            {
                return s_TreeViewItemTemplate.Clone();
            };
            m_TreeView.bindItem = (element, item) =>
            {
                var itemData = (StructuralChangesProfilerTreeViewItem)item;
                element.Q<Label>("column1").text = itemData.displayName;
                element.Q<Label>("column2").text = NsToMsString(itemData.totalElapsedNanoseconds);
                element.Q<Label>("column3").text = CountToString(itemData.totalCount);
            };
            m_TreeView.selectionType = SelectionType.Single;

            return m_Window;
        }

        public override void SelectedFrameIndexChanged(long index)
        {
            m_FrameIndex = index;
            if (IsRecording)
                return;

            m_StructuralChangesDataSource = GetFrames(index).SelectMany(GetTreeViewData).ToArray();
            m_SearchElement.Search();
        }

        public override void Update()
        {
            if (m_Window == null)
                return;

            if (m_FrameIndex == -1 || IsRecording)
            {
                m_Message.SetVisibility(true);
                m_Message.text = IsRecording ? s_DisplayingFrameDataDisabled : s_NoFrameDataAvailable;
                m_Content.SetVisibility(false);
                m_TreeView.rootItems = Array.Empty<ITreeViewItem>();
                return;
            }

            var itemId = 0;
            var rootItem = new StructuralChangesProfilerTreeViewItem { id = itemId++, displayName = s_All };
            var createEntityItem = new StructuralChangesProfilerTreeViewItem { id = itemId++, displayName = s_CreateEntity };
            var destroyEntityItem = new StructuralChangesProfilerTreeViewItem { id = itemId++, displayName = s_DestroyEntity };
            var addComponentItem = new StructuralChangesProfilerTreeViewItem { id = itemId++, displayName = s_AddComponent };
            var removeComponentItem = new StructuralChangesProfilerTreeViewItem { id = itemId++, displayName = s_RemoveComponent };

            foreach (var data in m_StructuralChangesDataFiltered)
            {
                var eventItem = default(StructuralChangesProfilerTreeViewItem);
                switch (data.Type)
                {
                    case StructuralChangeType.CreateEntity:
                        eventItem = createEntityItem;
                        break;
                    case StructuralChangeType.DestroyEntity:
                        eventItem = destroyEntityItem;
                        break;
                    case StructuralChangeType.AddComponent:
                        eventItem = addComponentItem;
                        break;
                    case StructuralChangeType.RemoveComponent:
                        eventItem = removeComponentItem;
                        break;
                    default:
                        throw new NotImplementedException(data.Type.ToString());
                }

                var worldItem = eventItem.children.FirstOrDefault(item => item.displayName == data.WorldName);
                if (worldItem == null)
                {
                    worldItem = new StructuralChangesProfilerTreeViewItem { id = itemId++, displayName = data.WorldName };
                    eventItem.AddChild(worldItem);
                }

                var systemItem = worldItem.children.FirstOrDefault(item => item.displayName == data.SystemName);
                if (systemItem == null)
                {
                    systemItem = new StructuralChangesProfilerTreeViewItem { id = itemId++, displayName = data.SystemName };
                    worldItem.AddChild(systemItem);
                }

                systemItem.totalElapsedNanoseconds += data.ElapsedNanoseconds;
                systemItem.totalCount++;
                worldItem.totalElapsedNanoseconds += data.ElapsedNanoseconds;
                worldItem.totalCount++;
                eventItem.totalElapsedNanoseconds += data.ElapsedNanoseconds;
                eventItem.totalCount++;
                rootItem.totalElapsedNanoseconds += data.ElapsedNanoseconds;
                rootItem.totalCount++;
            }

            if (createEntityItem.hasChildren)
                rootItem.AddChild(createEntityItem);

            if (destroyEntityItem.hasChildren)
                rootItem.AddChild(destroyEntityItem);

            if (addComponentItem.hasChildren)
                rootItem.AddChild(addComponentItem);

            if (removeComponentItem.hasChildren)
                rootItem.AddChild(removeComponentItem);

            rootItem.SortChildrenRecursive(item => item.totalElapsedNanoseconds, false);
            m_TreeView.rootItems = rootItem.hasChildren ? new[] { rootItem } : Array.Empty<StructuralChangesProfilerTreeViewItem>();

            m_Message.SetVisibility(false);
            m_Content.SetVisibility(true);
        }

        public override void Clear()
        {
            if (m_Window == null)
                return;

            m_FrameIndex = -1;
            m_StructuralChangesDataSource = null;
            m_StructuralChangesDataFiltered.Clear();
            m_Message.SetVisibility(true);
            m_Message.text = s_NoFrameDataAvailable;
            m_Content.SetVisibility(false);
            m_TreeView.rootItems = Array.Empty<ITreeViewItem>();
        }

        static IEnumerable<StructuralChangesProfilerTreeViewItemData> GetTreeViewData(RawFrameDataView frame)
        {
            var worldsData = GetSessionMetaData<WorldData>(frame, EntitiesProfiler.Guid, (int)DataTag.WorldData).Distinct().ToDictionary(x => x.SequenceNumber, x => x);
            var systemsData = GetSessionMetaData<SystemData>(frame, EntitiesProfiler.Guid, (int)DataTag.SystemData).Distinct().ToDictionary(x => x.System, x => x);
            foreach (var structuralChangeData in GetFrameMetaData<StructuralChangeData>(frame, StructuralChangesProfiler.Guid, 0))
            {
                if (worldsData.TryGetValue(structuralChangeData.WorldSequenceNumber, out var worldData))
                {
                    systemsData.TryGetValue(structuralChangeData.ExecutingSystem, out var systemData);
                    yield return new StructuralChangesProfilerTreeViewItemData(worldData, systemData, structuralChangeData);
                }
            }
        }

        static IEnumerable<T> GetSessionMetaData<T>(RawFrameDataView frame, Guid guid, int tag) where T : unmanaged
        {
            var metaDataCount = frame.GetSessionMetaDataCount(guid, tag);
            for (var metaDataIter = 0; metaDataIter < metaDataCount; ++metaDataIter)
            {
                var metaDataArray = frame.GetSessionMetaData<T>(guid, tag, metaDataIter);
                for (var i = 0; i < metaDataArray.Length; ++i)
                    yield return metaDataArray[i];
            }
        }

        static IEnumerable<T> GetFrameMetaData<T>(RawFrameDataView frame, Guid guid, int tag) where T : unmanaged
        {
            var metaDataCount = frame.GetFrameMetaDataCount(guid, tag);
            for (var metaDataIter = 0; metaDataIter < metaDataCount; ++metaDataIter)
            {
                var metaDataArray = frame.GetFrameMetaData<T>(guid, tag, metaDataIter);
                for (var i = 0; i < metaDataArray.Length; ++i)
                    yield return metaDataArray[i];
            }
        }

        static IEnumerable<RawFrameDataView> GetFrames(long index)
        {
            for (var threadIndex = 0; ; ++threadIndex)
            {
                var frame = ProfilerDriver.GetRawFrameDataView((int)index, threadIndex);
                if (!frame.valid)
                    yield break;

                if (frame.GetFrameMetaDataCount(StructuralChangesProfiler.Guid, 0) > 0)
                    yield return frame;
            }
        }

        static string NsToMsString(long nanoseconds)
        {
            if (nanoseconds >= 1e3)
                return (nanoseconds * 1e-6).ToString("F3", CultureInfo.InvariantCulture);
            else if (nanoseconds > 0)
                return "<0.001";
            return "-";
        }

        static string CountToString(int value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
