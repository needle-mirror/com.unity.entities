using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Editor.Bridge;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.Entities.EntitiesProfiler;
using static Unity.Entities.MemoryProfiler;

namespace Unity.Entities.Editor
{
    partial class MemoryProfilerModule : MemoryProfilerModuleBase
    {
        static readonly string s_NoFrameDataAvailable = L10n.Tr("No frame data available. Select a frame from the charts above to see its details here.");
        static readonly string s_DisplayingFrameDataDisabled = L10n.Tr("Displaying of frame data disabled while recording. To see the data, pause recording.");

        MemoryProfilerModuleView m_View;
        long m_FrameIndex = -1;

        public override string ProfilerCategoryName => Category.Name;

        public override string[] ProfilerCounterNames => new[]
        {
            AllocatedBytesCounter.Name,
            UnusedBytesCounter.Name
        };

        public MemoryProfilerModule()
        {
            EntitiesProfiler.Initialize();
            m_View = new MemoryProfilerModuleView();
            m_View.SearchFinished = () => Update();
        }

        public override VisualElement CreateView(Rect area)
        {
            return m_View.Create();
        }

        public override void SelectedFrameIndexChanged(long index)
        {
            m_FrameIndex = index;
            if (IsRecording)
                return;

            m_View.ArchetypesDataSource = GetFrames(index).SelectMany(GetTreeViewData).ToArray();
            m_View.Search();
        }

        public override void Update()
        {
            if (m_FrameIndex == -1 || IsRecording || !m_View.HasArchetypesDataSource)
                m_View.Clear(IsRecording ? s_DisplayingFrameDataDisabled : s_NoFrameDataAvailable);
            else
                m_View.Update();
        }

        public override void Clear()
        {
            m_FrameIndex = -1;
            m_View.Clear(s_NoFrameDataAvailable);
        }

        static IEnumerable<MemoryProfilerTreeViewItemData> GetTreeViewData(RawFrameDataView frame)
        {
            var worldsData = GetSessionMetaData<WorldData>(frame, EntitiesProfiler.Guid, (int)DataTag.WorldData).Distinct().ToDictionary(x => x.SequenceNumber, x => x);
            var archetypesData = GetSessionMetaData<ArchetypeData>(frame, EntitiesProfiler.Guid, (int)DataTag.ArchetypeData).Distinct().ToDictionary(x => x.StableHash, x => x);
            foreach (var archetypeMemoryData in GetFrameMetaData<ArchetypeMemoryData>(frame, MemoryProfiler.Guid, 0))
            {
                if (worldsData.TryGetValue(archetypeMemoryData.WorldSequenceNumber, out var worldData) &&
                    archetypesData.TryGetValue(archetypeMemoryData.StableHash, out var archetypeData))
                {
                    yield return new MemoryProfilerTreeViewItemData(worldData.Name, archetypeData, archetypeMemoryData);
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

                if (frame.GetFrameMetaDataCount(MemoryProfiler.Guid, 0) > 0)
                    yield return frame;
            }
        }
    }
}
