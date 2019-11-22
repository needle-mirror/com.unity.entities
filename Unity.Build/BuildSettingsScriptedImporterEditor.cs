using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Properties;
using Unity.Properties.Editor;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using UnityEditor.Searcher;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Build
{
    [CustomEditor(typeof(BuildSettingsScriptedImporter))]
    sealed class BuildSettingsScriptedImporterEditor : ScriptedImporterEditor
    {
        static class ClassNames
        {
            public const string BaseClassName = "build-settings";
            public const string Dependencies = BaseClassName + "__asset-dependencies";
            public const string Header = BaseClassName + "__asset-header";
            public const string HeaderLabel = BaseClassName + "__asset-header-label";
            public const string BuildAction = BaseClassName + "__build-action";
            public const string BuildDropdown = BaseClassName + "__build-dropdown";
            public const string AddComponent = BaseClassName + "__add-component-button";
        }

        struct BuildAction
        {
            public string Name;
            public Action<BuildSettings> Action;
        }

        static readonly BuildAction k_Build = new BuildAction
        {
            Name = "Build",
            Action = bs =>
            {
                var result = bs.Build();
                result.LogResult();
            }
        };

        static readonly BuildAction k_BuildAndRun = new BuildAction
        {
            Name = "Build and Run",
            Action = (bs) =>
            {
                var buildResult = bs.Build();
                buildResult.LogResult();
                if (buildResult.Failed)
                {
                    return;
                }

                using (var runResult = bs.Run())
                {
                    runResult.LogResult();
                }
            }
        };

        static readonly BuildAction k_Run = new BuildAction
        {
            Name = "Run",
            Action = (bs) =>
            {
                using (var result = bs.Run())
                {
                    result.LogResult();
                }
            }
        };

        // Needed because properties don't handle root collections well.
        class DependenciesWrapper
        {
            [Properties.Property] readonly List<BuildSettings> m_Dependencies = new List<BuildSettings>();

            public IEnumerable<BuildSettings> Dependencies
            {
                get => m_Dependencies;
                set
                {
                    m_Dependencies.Clear();
                    m_Dependencies.AddRange(value);
                }
            }
        }

        const string k_CurrentActionKey = "BuildAction-CurrentAction";

        bool m_IsModified, m_LastEditState;
        BindableElement m_BuildSettingsRoot;
        readonly DependenciesWrapper m_DependenciesWrapper = new DependenciesWrapper();

        protected override bool needsApplyRevert { get; } = true;
        public override bool showImportedObject { get; } = false;
        BuildAction CurrentBuildAction => BuildActions[CurrentActionIndex];

        static List<BuildAction> BuildActions { get; } = new List<BuildAction>
        {
            k_Build,
            k_BuildAndRun,
            k_Run,
        };

        static int CurrentActionIndex
        {
            get => EditorPrefs.HasKey(k_CurrentActionKey) ? EditorPrefs.GetInt(k_CurrentActionKey) : BuildActions.IndexOf(k_BuildAndRun);
            set => EditorPrefs.SetInt(k_CurrentActionKey, value);
        }

        public override void OnEnable()
        {
            BuildSettings.AssetChanged += OnBuildSettingsImported;
            base.OnEnable();
        }

        void OnBuildSettingsImported(BuildSettings obj)
        {
            if (null != m_BuildSettingsRoot)
            {
                Refresh(m_BuildSettingsRoot);
            }
        }

        public override void OnDisable()
        {
            BuildSettings.AssetChanged -= OnBuildSettingsImported;
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
            Revert();
        }

        protected override void ResetValues()
        {
            Revert();
            m_IsModified = false;
            base.ResetValues();
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            m_BuildSettingsRoot = new BindableElement();
            m_BuildSettingsRoot.AddStyleSheetAndVariant(ClassNames.BaseClassName);
            Refresh(m_BuildSettingsRoot);

            root.contentContainer.Add(m_BuildSettingsRoot);
            root.contentContainer.Add(new IMGUIContainer(ApplyRevertGUI));
            return root;
        }

        void Refresh(BindableElement root)
        {
            root.Clear();

            var settings = assetTarget as BuildSettings;
            if (null == settings)
            {
                return;
            }

            m_LastEditState = AssetDatabase.IsOpenForEdit(settings);
            var openedForEditUpdater = UIUpdaters.MakeBinding(settings, root);
            openedForEditUpdater.OnPreUpdate += updater =>
            {
                if (!updater.Source)
                {
                    return;
                }
                m_LastEditState = AssetDatabase.IsOpenForEdit(updater.Source);
            };
            root.binding = openedForEditUpdater;

            RefreshHeader(root, settings);
            RefreshDependencies(root, settings);
            RefreshComponents(root, settings);
        }

        void RefreshHeader(BindableElement root, BuildSettings settings)
        {
            var headerRoot = new VisualElement();
            headerRoot.AddToClassList(ClassNames.Header);
            root.Add(headerRoot);

            // Refresh Name Label
            var nameLabel = new Label(settings.name);
            nameLabel.AddToClassList(ClassNames.HeaderLabel);
            headerRoot.Add(nameLabel);

            var labelUpdater = UIUpdaters.MakeBinding(settings, nameLabel);
            labelUpdater.OnUpdate += (binding) =>
            {
                if (binding.Source != null && binding.Source)
                {
                    binding.Element.text = binding.Source.name;
                }
            };
            nameLabel.binding = labelUpdater;

            // Refresh Build&Run Button
            var dropdownButton = new VisualElement();
            dropdownButton.style.flexDirection = FlexDirection.Row;
            dropdownButton.style.justifyContent = Justify.FlexEnd;
            nameLabel.Add(dropdownButton);

            var dropdownActionButton = new Button { text = BuildActions[CurrentActionIndex].Name };
            dropdownActionButton.AddToClassList(ClassNames.BuildAction);
            dropdownActionButton.clickable = new Clickable(() => CurrentBuildAction.Action(settings));
            dropdownActionButton.SetEnabled(true);
            dropdownButton.Add(dropdownActionButton);

            var actionUpdater = UIUpdaters.MakeBinding(this, dropdownActionButton);
            actionUpdater.OnUpdate += (binding) =>
            {
                if (binding.Source != null && binding.Source)
                {
                    binding.Element.text = CurrentBuildAction.Name;
                }
            };
            dropdownActionButton.binding = actionUpdater;

            var dropdownActionPopup = new PopupField<BuildAction>(BuildActions, CurrentActionIndex, a => string.Empty, a => a.Name);
            dropdownActionPopup.AddToClassList(ClassNames.BuildDropdown);
            dropdownActionPopup.RegisterValueChangedCallback(evt =>
            {
                CurrentActionIndex = BuildActions.IndexOf(evt.newValue);
                dropdownActionButton.clickable = new Clickable(() => CurrentBuildAction.Action(settings));
            });
            dropdownButton.Add(dropdownActionPopup);

            // Refresh Asset Field
            var assetField = new ObjectField { objectType = typeof(BuildSettings) };
            assetField.Q<VisualElement>(className: "unity-object-field__selector").SetEnabled(false);
            assetField.SetValueWithoutNotify(assetTarget);
            headerRoot.Add(assetField);

            var assetUpdater = UIUpdaters.MakeBinding(settings, assetField);
            assetField.SetEnabled(m_LastEditState);
            assetUpdater.OnPreUpdate += updater => updater.Element.SetEnabled(m_LastEditState);
            assetField.binding = assetUpdater;
        }

        void RefreshDependencies(BindableElement root, BuildSettings settings)
        {
            m_DependenciesWrapper.Dependencies = FilterDependencies(settings, settings.Dependencies);

            var dependencyElement = new PropertyElement();
            dependencyElement.AddToClassList(ClassNames.BaseClassName);
            dependencyElement.SetTarget(m_DependenciesWrapper);
            dependencyElement.OnChanged += element =>
            {
                settings.Dependencies.Clear();
                settings.Dependencies.AddRange(FilterDependencies(settings, m_DependenciesWrapper.Dependencies));
                Refresh(root);
                m_IsModified = true;
            };
            dependencyElement.SetEnabled(m_LastEditState);
            root.Add(dependencyElement);

            var foldout = dependencyElement.Q<Foldout>();
            foldout.AddToClassList(ClassNames.Dependencies);
            foldout.Q<Toggle>().AddToClassList("component-container__component-header");
            foldout.contentContainer.AddToClassList("component-container__component-fields");

            var dependencyUpdater = UIUpdaters.MakeBinding(settings, dependencyElement);
            dependencyUpdater.OnPreUpdate += updater => updater.Element.SetEnabled(m_LastEditState);
            dependencyElement.binding = dependencyUpdater;
        }

        IEnumerable<BuildSettings> FilterDependencies(BuildSettings settings, IEnumerable<BuildSettings> dependencies)
        {
            foreach (var dependency in dependencies)
            {
                if (dependency == null || !dependency || dependency == settings || dependency.HasDependency(settings))
                    yield return null;
                else
                    yield return dependency;
            }
        }

        void RefreshComponents(BindableElement root, BuildSettings settings)
        {
            // Refresh Components
            var componentRoot = new BindableElement();
            var components = settings.GetComponents();
            foreach (var component in components)
            {
                componentRoot.Add(GetComponentElement(settings, component));
            }
            componentRoot.SetEnabled(m_LastEditState);
            root.Add(componentRoot);

            var componentUpdater = UIUpdaters.MakeBinding(settings, componentRoot);
            componentUpdater.OnUpdate += updater => updater.Element.SetEnabled(m_LastEditState);
            componentRoot.binding = componentUpdater;

            // Refresh Add Component Button
            var addComponentButton = new Button();
            addComponentButton.AddToClassList(ClassNames.AddComponent);
            addComponentButton.RegisterCallback<MouseUpEvent>(evt =>
            {
                var databases = new[]
                {
                    TypeSearcherDatabase.GetBuildSettingsDatabase(new HashSet<Type>(settings.GetComponents().Select(com => com.GetType()))),
                };

                var searcher = new Searcher(databases, new AddTypeSearcherAdapter("Add Component"));
                var editorWindow = EditorWindow.focusedWindow;
                var button = evt.target as Button;

                SearcherWindow.Show(editorWindow, searcher, AddType,
                    button.worldBound.min + Vector2.up * 15.0f, a => { },
                    new SearcherWindow.Alignment(SearcherWindow.Alignment.Vertical.Top, SearcherWindow.Alignment.Horizontal.Left));
            });
            addComponentButton.SetEnabled(m_LastEditState);
            root.contentContainer.Add(addComponentButton);

            var addComponentButtonUpdater = UIUpdaters.MakeBinding(settings, addComponentButton);
            addComponentButtonUpdater.OnPreUpdate += updater => updater.Element.SetEnabled(m_LastEditState);
            addComponentButton.binding = addComponentButtonUpdater;
        }

        void Revert()
        {
            var buildSettings = assetTarget as BuildSettings;
            var importer = target as BuildSettingsScriptedImporter;
            if (null == buildSettings || null == importer)
            {
                return;
            }

            BuildSettings.DeserializeFromPath(assetTarget as BuildSettings, importer.assetPath);
            Refresh(m_BuildSettingsRoot);
        }

        void Save()
        {
            var buildSettings = assetTarget as BuildSettings;
            var importer = target as BuildSettingsScriptedImporter;
            if (null == buildSettings || null == importer)
            {
                return;
            }

            buildSettings.SerializeToPath(importer.assetPath);
        }

        bool AddType(SearcherItem arg)
        {
            if (!(arg is TypeSearcherItem typeItem))
            {
                return false;
            }
            var type = typeItem.Type;
            (assetTarget as BuildSettings)?.SetComponent(type, TypeConstruction.Construct<IBuildSettingsComponent>(type));
            Refresh(m_BuildSettingsRoot);
            m_IsModified = true;
            return true;

        }

        VisualElement GetComponentElement(BuildSettings container, object component)
        {
            var componentType = component.GetType();
            var element = (VisualElement)Activator.CreateInstance(typeof(ComponentContainerElement<,,>)
                .MakeGenericType(typeof(BuildSettings), typeof(IBuildSettingsComponent), componentType), container, component);

            if (element is IChangeHandler changeHandler)
            {
                changeHandler.OnChanged += () => { m_IsModified = true; };
            }

            return element;
        }
    }
}
