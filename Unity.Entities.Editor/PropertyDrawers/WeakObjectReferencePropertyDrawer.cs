#if !UNITY_DOTSRUNTIME
using UnityEditor;
using UnityEngine;
using Unity.Entities.Content;
using Unity.Entities.Serialization;

namespace Unity.Entities.Editor
{
    [CustomPropertyDrawer(typeof(WeakObjectSceneReference), true)]
    [CustomPropertyDrawer(typeof(WeakObjectReference<>), true)]
    class WeakObjectReferencePropertyDrawerBase : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var targetObjectType = fieldInfo.FieldType == typeof(WeakObjectSceneReference) ? typeof(SceneAsset) : fieldInfo.FieldType.GetGenericArguments()[0];
            var gidName = $"{property.propertyPath}.Id.GlobalId.AssetGUID.Value";
            var propertyX = property.serializedObject.FindProperty($"{gidName}.x");
            var propertyY = property.serializedObject.FindProperty($"{gidName}.y");
            var propertyZ = property.serializedObject.FindProperty($"{gidName}.z");
            var propertyW = property.serializedObject.FindProperty($"{gidName}.w");
            var propertyObjectId = property.serializedObject.FindProperty($"{property.propertyPath}.Id.GlobalId.SceneObjectIdentifier0");
            var propertyIdType = property.serializedObject.FindProperty($"{property.propertyPath}.Id.GlobalId.IdentifierType");
            var genType = property.serializedObject.FindProperty($"{property.propertyPath}.Id.GenerationType");
            var uwrid = new UntypedWeakReferenceId(
                new Hash128((uint)propertyX.longValue, (uint)propertyY.longValue, (uint)propertyZ.longValue, (uint)propertyW.longValue),
                propertyObjectId.longValue,
                propertyIdType.intValue,
                targetObjectType == typeof(SceneAsset) ? WeakReferenceGenerationType.GameObjectScene : WeakReferenceGenerationType.UnityObject);
            var currObject = UntypedWeakReferenceId.GetEditorObject(uwrid);
            var selObj = EditorGUI.ObjectField(position, label, currObject, targetObjectType, false);
            if (selObj != currObject)
            {
                var uwr = UntypedWeakReferenceId.CreateFromObjectInstance(selObj);
                propertyX.longValue = uwr.GlobalId.AssetGUID.Value.x;
                propertyY.longValue = uwr.GlobalId.AssetGUID.Value.y;
                propertyZ.longValue = uwr.GlobalId.AssetGUID.Value.z;
                propertyW.longValue = uwr.GlobalId.AssetGUID.Value.w;
                propertyObjectId.longValue = uwr.GlobalId.SceneObjectIdentifier0;
                propertyIdType.intValue = uwr.GlobalId.IdentifierType;
                genType.enumValueIndex = (int)uwr.GenerationType;
            }
        }
    }
}
#endif
