using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Properties;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using UnityEditor.Searcher;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Build
{
    [CustomEditor(typeof(BuildPipelineScriptedImporter))]
    sealed class BuildPipelineScriptedImporterEditor : ScriptedImporterEditor
    {
        ReorderableList m_BuildStepsList;
        bool m_IsModified;
        TextField m_RunStepTextInput;
        Label m_CustomInspectorHeader;

        public override bool showImportedObject { get; } = false;

        public override void OnEnable()
        {
            BuildPipeline.AssetChanged += OnBuildPipelineImported;
            Refresh();
            base.OnEnable();
        }

        void OnBuildPipelineImported(BuildPipeline pipeline)
        {
            Refresh();
        }

        public override void OnDisable()
        {
            BuildPipeline.AssetChanged -= OnBuildPipelineImported;
            base.OnDisable();
        }

        protected override void OnHeaderGUI()
        {
            // Intentional
            //base.OnHeaderGUI();
        }

        public override bool HasModified()
        {
            return m_IsModified;
        }

        protected override void Apply()
        {
            Save();
            m_IsModified = false;
            base.Apply();
            Restore();
        }

        protected override void ResetValues()
        {
            Restore();
            m_IsModified = false;
            base.ResetValues();
        }

        void Save()
        {
            var pipeline = assetTarget as BuildPipeline;
            var importer = target as BuildPipelineScriptedImporter;
            if (null == pipeline || null == importer)
            {
                return;
            }

            pipeline.SerializeToPath(importer.assetPath);
        }

        void Restore()
        {
            var pipeline = assetTarget as BuildPipeline;
            var importer = target as BuildPipelineScriptedImporter;
            if (null == pipeline || null == importer)
            {
                return;
            }

            BuildPipeline.DeserializeFromPath(pipeline, importer.assetPath);
            SetRunStepValue(pipeline);
            Refresh();
        }

        void Refresh()
        {
            var pipeline = assetTarget as BuildPipeline;
            var importer = target as BuildPipelineScriptedImporter;
            if (null == pipeline || null == importer)
            {
                return;
            }

            if (m_CustomInspectorHeader != null)
                SetCustomInspectorHeader();

            m_BuildStepsList = new ReorderableList(pipeline.BuildSteps, typeof(IBuildStep), true, true, true, true);
            m_BuildStepsList.headerHeight = 3;
            m_BuildStepsList.onAddDropdownCallback = AddDropdownCallbackDelegate;
            m_BuildStepsList.drawElementCallback = ElementCallbackDelegate;
            m_BuildStepsList.drawHeaderCallback = HeaderCallbackDelegate;
            m_BuildStepsList.onReorderCallback = ReorderCallbackDelegate;
            m_BuildStepsList.onRemoveCallback = RemoveCallbackDelegate;
            m_BuildStepsList.drawFooterCallback = FooterCallbackDelegate;
            m_BuildStepsList.drawNoneElementCallback = DrawNoneElementCallback;
        }

        static string GetDisplayName(Type t)
        {
            var attr = t.GetCustomAttribute<BuildStepAttribute>();
            var name = (attr == null || string.IsNullOrEmpty(attr.description)) ? t.Name : attr.description;
            var cat = (attr == null || string.IsNullOrEmpty(attr.category)) ? string.Empty : attr.category;
            if (string.IsNullOrEmpty(cat))
                return name;
            return $"{cat}/{name}";
        }

        static bool IsShown(Type t)
        {
            var flags = t.GetCustomAttribute<BuildStepAttribute>()?.flags;
            return (flags & BuildStepAttribute.Flags.Hidden) != BuildStepAttribute.Flags.Hidden;
        }

        bool AddStep(SearcherItem item)
        {
            var pipeline = assetTarget as BuildPipeline;
            var importer = target as BuildPipelineScriptedImporter;
            if (null == pipeline || null == importer)
            {
                return false;
            }

            if (item is TypeSearcherItem typeItem)
            {
                if (TypeConstruction.TryConstruct<IBuildStep>(typeItem.Type, out var step))
                {
                    pipeline.BuildSteps.Add(step);
                    m_IsModified = true;
                    return true;
                }
            }
            return false;
        }

        static bool FilterSearch(Type t, string searchString)
        {
            if (t == null && !string.IsNullOrEmpty(searchString))
                return false;
            return GetDisplayName(t).ToLower().Contains(searchString.ToLower());
        }

        bool OnFooter(Rect r)
        {
            if (!GUI.Button(r, new GUIContent("Browse...")))
                return true;

            var selPath = EditorUtility.OpenFilePanel("Select Build Pipeline Step Asset", "Assets", "asset");
            if (string.IsNullOrEmpty(selPath))
                return true;

            var assetsPath = Application.dataPath;
            if (!selPath.StartsWith(assetsPath))
            {
                Debug.LogErrorFormat("Assets are required to be in the Assets folder.");
                return false;
            }

            var relPath = "Assets" + selPath.Substring(assetsPath.Length);
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relPath);
            if (obj == null)
            {
                Debug.LogErrorFormat("Unable to load asset at path {0}.", selPath);
                return false;
            }
            var step = obj as IBuildStep;
            if (step == null)
            {
                Debug.LogErrorFormat("Asset at path {0} is not a valid IBuildStep.", selPath);
                return false;
            }

            var pipeline = assetTarget as BuildPipeline;
            var importer = target as BuildPipelineScriptedImporter;
            if (null == pipeline || null == importer)
            {
                return false;
            }

            if (step == pipeline as IBuildStep)
            {
                Debug.LogErrorFormat("IBuildStep at path {0} cannot be added to itself.", selPath);
                return false;
            }

            pipeline.BuildSteps.Add(step);
            m_IsModified = true;
            return false;
        }

        void AddDropdownCallbackDelegate(Rect buttonRect, ReorderableList list)
        {
            var databases = new[]
            {
                TypeSearcherDatabase.GetBuildStepsDatabase(
                    new HashSet<Type>(BuildStep.GetAvailableTypes(type => !IsShown(type))), GetDisplayName),
            };

            var searcher = new Searcher(
                databases,
                new AddTypeSearcherAdapter("Add Build Step"));

            var editorWindow = EditorWindow.focusedWindow;

            SearcherWindow.Show(
                editorWindow,
                searcher,
                AddStep,
                buttonRect.min + Vector2.up * 35.0f,
                a => { },
                new SearcherWindow.Alignment(SearcherWindow.Alignment.Vertical.Top,
                    SearcherWindow.Alignment.Horizontal.Left)
            );
        }

        void HandleDragDrop(Rect rect, int index)
        {
            var pipeline = assetTarget as BuildPipeline;
            var importer = target as BuildPipelineScriptedImporter;
            if (null == pipeline || null == importer)
            {
                return;
            }

            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.ContextClick:

                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!rect.Contains(evt.mousePosition))
                        return;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (IBuildStep step in DragAndDrop.objectReferences)
                        {
                            pipeline.BuildSteps.Insert(index, step);
                            m_IsModified = true;
                        }
                    }
                    break;
            }
        }

        void DrawNoneElementCallback(Rect rect)
        {
            ReorderableList.defaultBehaviours.DrawNoneElement(rect, false);
            HandleDragDrop(rect, 0);
        }

        void FooterCallbackDelegate(Rect rect)
        {
            var pipeline = assetTarget as BuildPipeline;
            var importer = target as BuildPipelineScriptedImporter;
            if (null == pipeline || null == importer)
            {
                return;
            }

            ReorderableList.defaultBehaviours.DrawFooter(rect, m_BuildStepsList);
            HandleDragDrop(rect, pipeline.BuildSteps.Count);
        }

        void ElementCallbackDelegate(Rect rect, int index, bool isActive, bool isFocused)
        {
            var pipeline = assetTarget as BuildPipeline;
            var importer = target as BuildPipelineScriptedImporter;
            if (null == pipeline || null == importer)
            {
                return;
            }

            var step = pipeline.BuildSteps[index];
            GUI.Label(rect, step != null ? step.Description : "<null>");
            HandleDragDrop(rect, index);
        }

        void ReorderCallbackDelegate(ReorderableList list)
        {
            m_IsModified = true;
        }

        void HeaderCallbackDelegate(Rect rect)
        {
//            GUI.Label(rect, new GUIContent("Build Steps"));
            HandleDragDrop(rect, 0);
        }

        void RemoveCallbackDelegate(ReorderableList list)
        {
            var pipeline = assetTarget as BuildPipeline;
            var importer = target as BuildPipelineScriptedImporter;
            if (null == pipeline || null == importer)
            {
                return;
            }

            pipeline.BuildSteps.RemoveAt(list.index);
            m_IsModified = true;
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(@"Packages/com.unity.entities/Unity.Build/GUI/uxml/BuildPipelineCustomInspector.uxml").CloneTree();
            root.AddStyleSheetAndVariant("BuildPipelineCustomInspector");
            m_CustomInspectorHeader = root.Q<Label>(className: "InspectorHeader__Label");
            root.Q<VisualElement>("BuildSteps__IMGUIContainer").Add(new IMGUIContainer(m_BuildStepsList.DoLayoutList));
            root.Q<VisualElement>("ApplyRevertButtons").Add(new IMGUIContainer(ApplyRevertGUI));
            root.Q<Button>("RunStep__SelectButton").clickable.clickedWithEventInfo += OnRunStepSelectorClicked;
            m_RunStepTextInput = root.Q<TextField>("RunStep__RunStepTypeName");
            SetRunStepValue();
            SetCustomInspectorHeader();
            return root;
        }

        void OnRunStepSelectorClicked(EventBase @event)
        {
            SearcherWindow.Show(
                EditorWindow.focusedWindow,
                new Searcher(
                    TypeSearcherDatabase.GetRunStepDatabase(new HashSet<Type>(RunStep.GetAvailableTypes(type => !IsShown(type)))),
                    new AddTypeSearcherAdapter("Select Run Script")),
                UpdateRunStep,
                @event.originalMousePosition + Vector2.up * 35.0f,
                a => { },
                new SearcherWindow.Alignment(SearcherWindow.Alignment.Vertical.Top,
                                             SearcherWindow.Alignment.Horizontal.Left)
            );
        }

        bool UpdateRunStep(SearcherItem item)
        {
            var pipeline = assetTarget as BuildPipeline;
            var importer = target as BuildPipelineScriptedImporter;
            if (null == pipeline || null == importer)
            {
                return false;
            }

            if (item is TypeSearcherItem typeItem)
            {
                if (TypeConstruction.TryConstruct<IRunStep>(typeItem.Type, out var step))
                {
                    pipeline.RunStep = step;
                    SetRunStepValue(pipeline);
                    m_IsModified = true;
                    return true;
                }
            }
            return false;
        }

        void SetRunStepValue(BuildPipeline pipeline = null)
            => m_RunStepTextInput.value = (pipeline ? pipeline : assetTarget as BuildPipeline)?.RunStep?.GetType()?.Name ?? string.Empty;

        void SetCustomInspectorHeader()
            => m_CustomInspectorHeader.text = $"{(assetTarget as BuildPipeline)?.name} (Build Pipeline Asset)";
    }
}
