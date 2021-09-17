#pragma warning disable 649
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor.Tests
{
    using EntityInspectorTypes;

    namespace EntityInspectorTypes
    {
        class InspectorTestWindow : EditorWindow
        {
            public void AddRoot(VisualElement element)
            {
                rootVisualElement.Add(element);
            }
        }

        struct BufferComponent : IBufferElementData
        {
            public float Value;
        }

        struct TagComponent : IComponentData
        {
        }

        struct StructComponent : IComponentData
        {
            public float Value;
        }

#if  !UNITY_DISABLE_MANAGED_COMPONENTS
        class ClassComponent : IComponentData
        {
            public float Value;
        }
#endif
        struct SharedStructComponent : ISharedComponentData
        {
            public float Value;
        }
    }

    [TestFixture]
    class EntityInspectorTests
    {
        World m_World;
        Entity m_Entity;
        InspectorSettings.InspectorBackend m_PreviousBackend;
        EntityEditor m_Editor;
        EntitySelectionProxy m_Proxy;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_World = new World("Entity Inspector tests");
            m_PreviousBackend = InspectorUtility.Settings.Backend;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_World.Dispose();
            InspectorUtility.Settings.Backend = m_PreviousBackend;
        }

        [SetUp]
        public void SetUp()
        {
            InspectorUtility.Settings.Backend = InspectorSettings.InspectorBackend.Normal;

            m_Entity = m_World.EntityManager.CreateEntity();

            m_Proxy = EntitySelectionProxy.CreateInstance(m_World, m_Entity);
            var editor = UnityEditor.Editor.CreateEditor(m_Proxy);
            Assert.That(editor, Is.TypeOf<EntityEditor>());
            m_Editor = (EntityEditor)editor;
            m_Editor.ImmediateInitialize = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Proxy);
            Object.DestroyImmediate(m_Editor);
            m_World.EntityManager.DestroyEntity(m_Entity);
        }

        [Test]
        [TestCase(InspectorSettings.InspectorBackend.Debug, false)]
        [TestCase(InspectorSettings.InspectorBackend.Normal, true)]
        public void CustomEditor_WhenSettingIsSet_CanBeOverriden(InspectorSettings.InspectorBackend mode,
            bool shouldBeUsed)
        {
            InspectorUtility.Settings.Backend = mode;
            var editor = UnityEditor.Editor.CreateEditor(m_Proxy);
            try
            {
                if (shouldBeUsed)
                    Assert.That(editor, Is.TypeOf<EntityEditor>());
                else
                    Assert.That(editor, !Is.TypeOf<EntityEditor>());
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void Entity_WithNoComponents_HasEmptyInspector()
        {
            var root = m_Editor.CreateInspectorGUI();
            var list = root.Query<ComponentElementBase>().ToList();
            Assert.That(list.Count, Is.EqualTo(0));
        }

        [Test]
        public void Entity_WithComponents_HasMatchingComponentElements()
        {
            m_World.EntityManager.AddComponent<TagComponent>(m_Entity);
            m_World.EntityManager.AddComponent<StructComponent>(m_Entity);
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            m_World.EntityManager.AddComponent<ClassComponent>(m_Entity);
#endif
            m_World.EntityManager.AddSharedComponentData(m_Entity, new SharedStructComponent());

            var root = m_Editor.CreateInspectorGUI();
            var query = root.Query<ComponentElementBase>();
            var list = query.ToList();
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            Assert.That(list.Count, Is.EqualTo(4));
#else
            Assert.That(list.Count, Is.EqualTo(3));
#endif
            Assert.That(list.OfType<TagElement>().Count(), Is.EqualTo(1));
            Assert.That(list.OfType<ComponentElement<StructComponent>>().Count(), Is.EqualTo(1));
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            Assert.That(list.OfType<ComponentElement<ClassComponent>>().Count(), Is.EqualTo(1));
#endif
            Assert.That(list.OfType<ComponentElement<SharedStructComponent>>().Count(), Is.EqualTo(1));
            Is.EqualTo(1);
        }

        [Test]
        public void Inspector_WhenComponentAreAddedOrRemoved_UpdatesProperly()
        {
            var root = m_Editor.CreateInspectorGUI();
            var list = new List<ComponentElementBase>();
            root.Query<ComponentElementBase>().ToList(list);
            Assert.That(list.Count, Is.EqualTo(0));
            list.Clear();

            m_World.EntityManager.AddComponent<TagComponent>(m_Entity);
            root.ForceUpdateBindings();
            root.Query<ComponentElementBase>().ToList(list);
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list.OfType<TagElement>().Count(), Is.EqualTo(1));
            list.Clear();

            m_World.EntityManager.AddComponent<StructComponent>(m_Entity);
            root.ForceUpdateBindings();
            root.Query<ComponentElementBase>().ToList(list);
            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(list.OfType<TagElement>().Count(), Is.EqualTo(1));
            Assert.That(list.OfType<ComponentElement<StructComponent>>().Count(), Is.EqualTo(1));
            list.Clear();

            m_World.EntityManager.RemoveComponent<TagComponent>(m_Entity);
            root.ForceUpdateBindings();
            root.Query<ComponentElementBase>().ToList(list);
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list.OfType<ComponentElement<StructComponent>>().Count(), Is.EqualTo(1));
            list.Clear();

            m_World.EntityManager.RemoveComponent<StructComponent>(m_Entity);
            root.ForceUpdateBindings();
            root.Query<ComponentElementBase>().ToList(list);
            Assert.That(list.Count, Is.EqualTo(0));
            list.Clear();
        }

        [Test]
        public void Inspector_WhenComponentValueIsUpdated_UpdatesProperly()
        {
            var entityEditor = (EntityEditor)m_Editor;
            m_World.EntityManager.AddComponent<StructComponent>(m_Entity);
            var root = m_Editor.CreateInspectorGUI();
            // Needed to get the events fired up.
            var window = ScriptableObject.CreateInstance<InspectorTestWindow>();
            window.Show();
            window.AddRoot(root);
            try
            {
                var element = root.Q<ComponentElement<StructComponent>>();
                var field = element.Q<FloatField>();
                Assert.That(field.value, Is.EqualTo(0.0f));

                m_World.EntityManager.SetComponentData(m_Entity, new StructComponent { Value = 15.0f });
                root.ForceUpdateBindings();
                Assert.That(field.value, Is.EqualTo(15.0f));

                field.value = 100.0f;
                var data = m_World.EntityManager.GetComponentData<StructComponent>(m_Entity);
                root.ForceUpdateBindings();
                if (entityEditor.m_Context.IsReadOnly)
                {
                    Assert.That(data.Value, Is.EqualTo(15.0f));
                    Assert.That(field.value, Is.EqualTo(15.0f));
                }
                else
                {
                    Assert.That(data.Value, Is.EqualTo(100.0f));
                    Assert.That(field.value, Is.EqualTo(100.0f));
                }
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void Entity_WithBuffers_CanNavigateThroughItems()
        {
            var buffer = m_World.EntityManager.AddBuffer<BufferComponent>(m_Entity);
            for (var i = 0; i < 10; ++i)
                buffer.Add(new BufferComponent {Value = i});

            var root = m_Editor.CreateInspectorGUI();
            var query = root.Query<ComponentElementBase>();
            var list = query.ToList();
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(root.Q(className: "unity-properties__list-element__size").enabledInHierarchy, Is.False);
            Assert.That(root.Q("properties-list-content").enabledInHierarchy, Is.False);
            Assert.That(root.Q(className: "unity-properties__list-element__add-item-button").enabledInHierarchy, Is.False);

            Assert.That(root.Q(className: "unity-properties__pagination-element__pagination-size").enabledInHierarchy, Is.True);
            // When inspecting a buffer, on the first page, the previous button is disabled, but the parent will be enabled.
            Assert.That(root.Q(className: "unity-properties__pagination-element__previous-page-button").enabledInHierarchy, Is.False);
            Assert.That(root.Q(className: "unity-properties__pagination-element__previous-page-button").parent.enabledInHierarchy, Is.True);
            Assert.That(root.Q(className: "unity-properties__pagination-element__elements-range").enabledInHierarchy, Is.True);
            Assert.That(root.Q(className: "unity-properties__pagination-element__next-page-button").enabledInHierarchy, Is.True);
        }
    }
}
#pragma warning restore 649
