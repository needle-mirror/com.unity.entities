using JetBrains.Annotations;
using Unity.Platforms.UI;
using Unity.Transforms;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
#if ENABLE_TRANSFORM_V1
    [UsedImplicitly]
    class TransformAspectInspector : PropertyInspector<EntityAspectContainer<TransformAspect>>
    {
        const string k_EditorPrefsTransformAspectInspectorBase = "com.unity.dots.editor.transform_aspect_inspector_";

        public override VisualElement Build()
        {
            var root = new VisualElement();
            var toolHandleLocalName = EditorGUIUtility.isProSkin ? "d_ToolHandleLocal" : "ToolHandleLocal";
            var toolHandleGlobalName = EditorGUIUtility.isProSkin ? "d_ToolHandleGlobal" : "ToolHandleGlobal";

            var localPosition = new Vector3Field {label = "Local Position", bindingPath = "LocalPosition"}.WithIconPrefix(toolHandleLocalName);
            var globalPosition = new Vector3Field {label = "Global Position", bindingPath = "Position"}.WithIconPrefix(toolHandleGlobalName);

            var localRotation = new Vector3Field {label = "Local Rotation", bindingPath = "LocalRotation"}.WithIconPrefix(toolHandleLocalName);
            var globalRotation = new Vector3Field {label = "Global Rotation", bindingPath = "Rotation"}.WithIconPrefix(toolHandleGlobalName);

            root.Add(new ContextualElement
            (
                k_EditorPrefsTransformAspectInspectorBase + "position",
                new ContextualElement.Item
                {
                    Element = localPosition,
                    ContextMenuLabel = localPosition.label
                },
                new ContextualElement.Item
                {
                    Element = globalPosition,
                    ContextMenuLabel = globalPosition.label
                }
            ));

            root.Add(new ContextualElement
            (
                k_EditorPrefsTransformAspectInspectorBase + "rotation",
                new ContextualElement.Item
                {
                    Element = localRotation,
                    ContextMenuLabel = localRotation.label
                },
                new ContextualElement.Item
                {
                    Element = globalRotation,
                    ContextMenuLabel = globalRotation.label
                }
            ));

            return root;
        }
    }
#else
    [UsedImplicitly]
    class TransformAspectInspector : PropertyInspector<EntityAspectContainer<TransformAspect>>
    {
        const string k_EditorPrefsTransformAspectInspectorBase = "com.unity.dots.editor.transform_aspect_inspector_";

        public override VisualElement Build()
        {
            var root = new VisualElement();
            var toolHandleLocalName = EditorGUIUtility.isProSkin ? "d_ToolHandleLocal" : "ToolHandleLocal";
            var toolHandleGlobalName = EditorGUIUtility.isProSkin ? "d_ToolHandleGlobal" : "ToolHandleGlobal";

            var localPosition = new Vector3Field {label = "Local Position", bindingPath = "LocalPosition"}.WithIconPrefix(toolHandleLocalName);
            var globalPosition = new Vector3Field {label = "Global Position", bindingPath = "Position"}.WithIconPrefix(toolHandleGlobalName);

            var localRotation = new Vector3Field {label = "Local Rotation", bindingPath = "LocalRotation"}.WithIconPrefix(toolHandleLocalName);
            var globalRotation = new Vector3Field {label = "Global Rotation", bindingPath = "Rotation"}.WithIconPrefix(toolHandleGlobalName);

            var hasLocalToParentComp = Target.World.EntityManager.HasComponent<LocalToParentTransform>(Target.Entity);
            var localUniformScale = new FloatField {label = "Local Uniform Scale", bindingPath = hasLocalToParentComp ? "LocalToParent.Scale" : "LocalToWorld.Scale"}.WithIconPrefix(toolHandleLocalName);
            var globalUniformScale = new FloatField {label = "Global Uniform Scale", bindingPath = "LocalToWorld.Scale"}.WithIconPrefix(toolHandleGlobalName);

            root.Add(new ContextualElement
            (
                k_EditorPrefsTransformAspectInspectorBase + "position",
                new ContextualElement.Item
                {
                    Element = localPosition,
                    ContextMenuLabel = localPosition.label
                },
                new ContextualElement.Item
                {
                    Element = globalPosition,
                    ContextMenuLabel = globalPosition.label
                }
            ));

            root.Add(new ContextualElement
            (
                k_EditorPrefsTransformAspectInspectorBase + "rotation",
                new ContextualElement.Item
                {
                    Element = localRotation,
                    ContextMenuLabel = localRotation.label
                },
                new ContextualElement.Item
                {
                    Element = globalRotation,
                    ContextMenuLabel = globalRotation.label
                }
            ));

            root.Add(new ContextualElement
            (
                k_EditorPrefsTransformAspectInspectorBase + "scale",
                new ContextualElement.Item
                {
                    Element = localUniformScale,
                    ContextMenuLabel = localUniformScale.label
                },
                new ContextualElement.Item
                {
                    Element = globalUniformScale,
                    ContextMenuLabel = globalUniformScale.label
                }
            ));

            return root;
        }
    }
#endif
}
