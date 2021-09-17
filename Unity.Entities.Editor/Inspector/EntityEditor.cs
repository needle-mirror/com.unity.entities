using JetBrains.Annotations;
using System;
using System.Linq;
using Unity.Properties;
using Unity.Properties.UI;
using UnityEditor;
using UnityEngine.UIElements;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// This declares a custom inspector for the <see cref="EntitySelectionProxy"/> that can be used to override the
    /// default inspector.
    /// </summary>
    class EntityEditor : UnityEditor.Editor, IBinding
    {
        const int k_ContentGenerationDelayInMs = 15;

        // internal for tests
        internal readonly EntityInspectorContext m_Context = new EntityInspectorContext();

        BindableElement m_Root;
        bool m_Initialized;

        // Internal for test to avoid delaying the initialization
        internal bool ImmediateInitialize { get; set; }

        [RootEditor(supportsAddComponent: false), UsedImplicitly]
        public static Type GetEditor(UnityObject[] targets)
        {
            using (var pooled = targets.OfType<EntitySelectionProxy>().ToPooledList())
            {
                var list = pooled.List;
                if (list.Count == 0)
                    return null;

                var proxy = list[0];
                if (!proxy.Exists)
                    return null;
            }

            return InspectorUtility.Settings.Backend == InspectorSettings.InspectorBackend.Debug
                ? null
                : typeof(EntityEditor);
        }

        void OnEnable()
        {
            m_Root = new BindableElement() { name = "Entity Inspector", binding = this };
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        void OnBeforeAssemblyReload()
        {
            // After a domain reload, it's impossible to retrieve the Entity this editor is
            // referring to. So before the domain is unloaded, we ensure that:
            // 1. The active selection is not an EntitySelectionProxy, so we don't try to
            // re-select it once the domain is reloaded.
            if (Selection.activeObject is EntitySelectionProxy)
                Selection.activeObject = null;

            // 2. This editor no longer exists, so a locked inspector is not revived with
            // invalid data once the domain is reloaded.
            DestroyImmediate(this);
        }

        public override bool UseDefaultMargins() => false;

        protected override void OnHeaderGUI()
        {
            // Intentionally overriden to avoid the default header to be drawn.
        }

        public override VisualElement CreateInspectorGUI()
        {
            if (m_Initialized)
                return m_Root;

            m_Initialized = true;

            Resources.AddCommonVariables(m_Root);
            Resources.Templates.Inspector.InspectorStyle.AddStyles(m_Root);
            Resources.Templates.Inspector.EntityInspector.AddStyles(m_Root);
            Resources.Templates.DotsEditorCommon.AddStyles(m_Root);
            Initialize(target as EntitySelectionProxy);
            return m_Root;
        }

        void IBinding.PreUpdate()
        {
            if (m_Context.TargetExists()) return;
            m_Root.Clear();
        }

        void IBinding.Update()
        {
            // Nothing to do
        }

        void IBinding.Release()
        {
            // Nothing to do
        }

        void Initialize(EntitySelectionProxy proxy)
        {
            m_Context.SetContext(proxy);
            m_Root.Clear();
            m_Root.AddToClassList(UssClasses.Inspector.EntityInspector);

            var header = new EntityHeader(m_Context);
            var p = new PropertyElement();
            p.SetTarget(header);
            m_Root.Add(p);

            var label = new CenteredMessageElement { Message = "Loading ..." };
            m_Root.Add(label);

            if (ImmediateInitialize)
                InitializeContent();
            else
                m_Root.schedule.Execute(InitializeContent).StartingIn(k_ContentGenerationDelayInMs);

            void InitializeContent()
            {
                label.RemoveFromHierarchy();
                var presenter = new PropertyElement();
                var t = new EntityInspectorContent(m_Context);
                presenter.SetTarget(t);
                m_Root.Add(presenter);
                m_Root.ForceUpdateBindings();
            }
        }

        readonly struct EntityInspectorContent
        {
            [CreateProperty, TabView("EntityInspector"), UsedImplicitly]
            ITabContent[] Tabs { get; }

            public EntityInspectorContent(EntityInspectorContext entityInspectorContext)
            {
                Tabs = new ITabContent[]
                {
                    new ComponentsTab(entityInspectorContext),
                    new RelationshipsTab(entityInspectorContext)
                };
            }
        }
    }
}
