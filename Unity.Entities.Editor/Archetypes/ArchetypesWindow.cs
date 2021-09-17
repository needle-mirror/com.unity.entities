using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using static Unity.Entities.Editor.MemoryProfilerModule;

namespace Unity.Entities.Editor
{
    partial class ArchetypesWindow : DOTSEditorWindow
    {
        static readonly string s_WindowName = L10n.Tr("Archetypes");
        static readonly string s_NoDataAvailable = L10n.Tr("No archetype data available.");

        [MenuItem(Constants.MenuItems.ArchetypesWindow, false, Constants.MenuItems.WindowPriority)]
        static void OpenWindow() => GetWindow<ArchetypesWindow>();

        ArchetypesMemoryDataRecorder m_Recorder;
        NativeList<ulong> m_ArchetypesStableHash;
        MemoryProfilerModuleView m_View;

        void OnEnable()
        {
            titleContent = new GUIContent(s_WindowName, EditorIcons.Archetype);
            minSize = Constants.MinWindowSize;

            m_Recorder = new ArchetypesMemoryDataRecorder();
            m_ArchetypesStableHash = new NativeList<ulong>(64, Allocator.Persistent);
            m_View = new MemoryProfilerModuleView();
            m_View.SearchFinished = () =>
            {
                if (!m_View.HasArchetypesDataSource)
                    m_View.Clear(s_NoDataAvailable);
                else
                    m_View.Update();
            };

            var viewElement = m_View.Create();
            m_View.Clear(s_NoDataAvailable);
            rootVisualElement.Add(viewElement);
        }

        void OnDisable()
        {
            m_ArchetypesStableHash.Dispose();
            m_Recorder.Dispose();
        }

        protected override void OnUpdate()
        {
            m_Recorder.Record();
            if (!SequenceEqual(m_ArchetypesStableHash, m_Recorder.ArchetypesStableHash))
            {
                m_View.ArchetypesDataSource = GetTreeViewData().ToArray();
                m_View.Search();
                m_ArchetypesStableHash.CopyFrom(m_Recorder.ArchetypesStableHash);
            }
        }

        protected override void OnWorldSelected(World world)
        {
        }

        IEnumerable<MemoryProfilerTreeViewItemData> GetTreeViewData()
        {
            var worldsData = m_Recorder.WorldsData.Distinct().ToDictionary(x => x.SequenceNumber, x => x);
            var archetypesData = m_Recorder.ArchetypesData.Distinct().ToDictionary(x => x.StableHash, x => x);
            foreach (var archetypeMemoryData in m_Recorder.ArchetypesMemoryData)
            {
                if (worldsData.TryGetValue(archetypeMemoryData.WorldSequenceNumber, out var worldData) &&
                    archetypesData.TryGetValue(archetypeMemoryData.StableHash, out var archetypeData))
                {
                    yield return new MemoryProfilerTreeViewItemData(worldData.Name, archetypeData, archetypeMemoryData);
                }
            }
        }

        static unsafe bool SequenceEqual<T>(NativeArray<T> array1, NativeArray<T> array2)
            where T : unmanaged
        {
            return array1.Length == array2.Length ? UnsafeUtility.MemCmp(array1.GetUnsafeReadOnlyPtr(), array2.GetUnsafeReadOnlyPtr(), sizeof(T) * array1.Length) == 0 : false;
        }
    }
}
