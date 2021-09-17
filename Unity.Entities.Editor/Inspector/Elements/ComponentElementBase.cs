using Unity.Entities.Editor.Inspectors;
using Unity.Properties;
using Unity.Properties.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    abstract class ComponentElementBase : BindableElement
    {
        public ComponentPropertyType Type { get; private set; }
        public string Path { get; private set; }
        protected string DisplayName { get; private set; }
        protected EntityInspectorContext Context { get; private set; }
        protected EntityContainer Container { get; private set; }

        protected ComponentElementBase(IComponentProperty property, EntityInspectorContext context)
        {
            Type = property.Type;
            Path = property.Name;
            DisplayName = ComponentsUtility.GetComponentDisplayName(property.Name);
            Context = context;
            Container = Context.EntityContainer;
        }

        protected PropertyElement CreateContent<TValue>(IComponentProperty property, ref TValue value)
        {
            Resources.Templates.Inspector.InspectorStyle.AddStyles(this);

            InspectorUtility.CreateComponentHeader(this, property.Type, DisplayName);
            var foldout = this.Q<Foldout>(className: UssClasses.Inspector.Component.Header);
            var toggle = foldout.Q<Toggle>();
            var container = Container;

            toggle.AddManipulator(new ContextualMenuManipulator(evt => { OnPopulateMenu(evt.menu); }));

            if (!Context.IsReadOnly) 
            {
                if (Type == ComponentPropertyType.ChunkComponent)
                    foldout.contentContainer.Add(new HelpBox("Chunk component data is shared between multiple entities", HelpBoxMessageType.Info));
            
                if (Type == ComponentPropertyType.SharedComponent)
                    foldout.contentContainer.Add(new HelpBox("Changing shared values will move entities between chunks", HelpBoxMessageType.Info));
            }
            
            var content = new PropertyElement();
            foldout.contentContainer.Add(content);
            content.AddContext(Context);
            content.SetTarget(value);
            content.OnChanged += OnComponentChanged;

            foldout.contentContainer.AddToClassList(UssClasses.Inspector.Component.Container);

            if (container.IsReadOnly)
            {
                SetReadonly(foldout);
                foldout.RegisterCallback<ClickEvent, EntityInspectorContext>(OnClicked, Context, TrickleDown.TrickleDown);
            }

            return content;
        }

        protected virtual void SetReadonly(VisualElement root)
        {
            root.contentContainer.SetEnabled(false);
        }

        protected abstract void OnComponentChanged(PropertyElement element, PropertyPath path);

        protected abstract void OnPopulateMenu(DropdownMenu menu);

        static void OnClicked(ClickEvent evt, EntityInspectorContext context)
        {
            var element = (VisualElement)evt.target;
            OnClicked(evt, context, element);
        }

        static void OnClicked(ClickEvent evt, EntityInspectorContext context, VisualElement current)
        {
            switch (current)
            {
                case Foldout foldout:
                    if (!foldout.Q<Toggle>().worldBound.Contains(evt.position))
                        break;
                    foldout.value = !foldout.value;
                    break;
                case ObjectField objectField:
                    var display = objectField.Q(className: UssClasses.UIToolkit.ObjectField.Display);
                    if (null == display)
                        break;
                    if (!display.worldBound.Contains(evt.position))
                        break;

                    if (evt.clickCount == 1)
                        EditorGUIUtility.PingObject(objectField.value);
                    else
                    {
                        var value = objectField.value;
                        if (null != value && value)
                            Selection.activeObject = value;
                    }
                    break;
                case EntityField entityField:
                    var input = entityField.Q(className: "unity-entity-field__input");
                    if (null == input)
                        break;
                    if (!input.worldBound.Contains(evt.position))
                        break;

                    if (evt.clickCount > 1)
                    {
                        var world = context.World;
                        if (null == world || !world.IsCreated)
                            break;
                        if (!context.EntityManager.SafeExists(entityField.value))
                            break;

                        EntitySelectionProxy.SelectEntity(context.World, entityField.value);
                    }
                    break;
            }

            for (var i = 0; i < current.childCount; ++i)
            {
                OnClicked(evt, context, current[i]);
            }
        }
    }
}
