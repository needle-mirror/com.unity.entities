using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Hybrid
{
#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(RestrictAuthoringInputToAttribute))]
    class RestrictAuthoringInputToPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            using (var changeScope = new EditorGUI.ChangeCheckScope())
            {
                var assignedValue = EditorGUI.ObjectField(position, label, property.objectReferenceValue, ((RestrictAuthoringInputToAttribute)this.attribute).Type, true);

                if (changeScope.changed)
                {
                    if (assignedValue is Component c)
                        property.objectReferenceValue = c.gameObject;
                    else if (assignedValue == null)
                        property.objectReferenceValue = null;
                }
            }

            EditorGUI.EndProperty();
        }
    }
#endif
}
