using JetBrains.Annotations;
using Unity.Mathematics;
using Unity.Properties;
using Unity.Platforms.UI;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor.Inspectors
{
    abstract class Float4x4ValueInspector<T> : PropertyInspector<T>
    {
        static Float4x4ValueInspector()
        {
            TypeConversion.Register((ref float4 v) => (Vector4) v);
            TypeConversion.Register((ref Vector4 v) => (float4) v);
        }

        public override VisualElement Build()
        {
            var root = new BindableElement
            {
                bindingPath = "Value"
            };

            for (var i = 0; i < 4; ++i)
            {
                var column = new Vector4Field { bindingPath = "c" + i };
                column.Query<FloatField>().ForEach(field => field.formatString = "0.###");
                InspectorUtility.AddRuntimeBar(column);
                root.Add(column);
            }

            return root;
        }
    }

    #if !ENABLE_TRANSFORM_V1
    #else
    [UsedImplicitly]
    sealed class LocalToParentInspector : Float4x4ValueInspector<LocalToParent>
    {
    }
#endif

    [UsedImplicitly]
    sealed class LocalToWorldInspector : Float4x4ValueInspector<LocalToWorld>
    {
    }

#if !ENABLE_TRANSFORM_V1
#else
    [UsedImplicitly]
    sealed class CompositeRotationInspector : Float4x4ValueInspector<CompositeRotation>
    {
    }

    [UsedImplicitly]
    sealed class CompositeScaleInspector : Float4x4ValueInspector<CompositeScale>
    {
    }

    [UsedImplicitly]
    sealed class ParentScaleInverseInspector : Float4x4ValueInspector<ParentScaleInverse>
    {
    }

    [UsedImplicitly]
    sealed class WorldToLocalInspector : Float4x4ValueInspector<WorldToLocal>
    {
    }
#endif

    abstract class Float3ValueInspector<T> : PropertyInspector<T>
    {
        static Float3ValueInspector()
        {
            TypeConversion.Register((ref float3 v) => (Vector3) v);
            TypeConversion.Register((ref Vector3 v) => (float3) v);
        }

        public override VisualElement Build()
        {
            var valueField = new Vector3Field { bindingPath = "Value" };
            valueField.Query<FloatField>().ForEach(field => field.formatString = "0.###");
            return valueField;
        }
    }

#if !ENABLE_TRANSFORM_V1
#else
    [UsedImplicitly]
    sealed class TranslationInspector : Float3ValueInspector<Translation>
    {
    }

    [UsedImplicitly]
    sealed class NonUniformScaleInspector : Float3ValueInspector<NonUniformScale>
    {
    }

    [UsedImplicitly]
    sealed class RotationEulerXYZInspector : Float4x4ValueInspector<RotationEulerXYZ>
    {
    }

    [UsedImplicitly]
    sealed class RotationEulerXZYInspector : Float4x4ValueInspector<RotationEulerXZY>
    {
    }

    [UsedImplicitly]
    sealed class RotationEulerYXZInspector : Float4x4ValueInspector<RotationEulerYXZ>
    {
    }

    [UsedImplicitly]
    sealed class RotationEulerYZXInspector : Float4x4ValueInspector<RotationEulerYZX>
    {
    }

    [UsedImplicitly]
    sealed class RotationEulerZXYInspector : Float4x4ValueInspector<RotationEulerZXY>
    {
    }

    [UsedImplicitly]
    sealed class RotationEulerZYXInspector : Float4x4ValueInspector<RotationEulerZYX>
    {
    }

    [UsedImplicitly]
    sealed class PostRotationEulerXYZInspector : Float4x4ValueInspector<PostRotationEulerXYZ>
    {
    }

    [UsedImplicitly]
    sealed class PostRotationEulerXZYInspector : Float4x4ValueInspector<PostRotationEulerXZY>
    {
    }

    [UsedImplicitly]
    sealed class PostRotationEulerYXZInspector : Float4x4ValueInspector<PostRotationEulerYXZ>
    {
    }

    [UsedImplicitly]
    sealed class PostRotationEulerYZXInspector : Float4x4ValueInspector<PostRotationEulerYZX>
    {
    }

    [UsedImplicitly]
    sealed class PostRotationEulerZXYInspector : Float4x4ValueInspector<PostRotationEulerZXY>
    {
    }

    [UsedImplicitly]
    sealed class PostRotationEulerZYXInspector : Float4x4ValueInspector<PostRotationEulerZYX>
    {
    }

    [UsedImplicitly]
    sealed class RotationPivotInspector : Float4x4ValueInspector<RotationPivot>
    {
    }

    [UsedImplicitly]
    sealed class RotationPivotTranslationInspector : Float4x4ValueInspector<RotationPivotTranslation>
    {
    }

    [UsedImplicitly]
    sealed class ScalePivotInspector : Float4x4ValueInspector<ScalePivot>
    {
    }

    [UsedImplicitly]
    sealed class ScalePivotTranslationInspector : Float4x4ValueInspector<ScalePivotTranslation>
    {
    }
#endif

    abstract class QuaternionValueInspector<T> : PropertyInspector<T>
    {
        static QuaternionValueInspector()
        {
            TypeConversion.Register((ref quaternion v) => (Vector4) v.value);
            TypeConversion.Register((ref Vector4 v) => new quaternion { value = v });
        }

        public override VisualElement Build()
        {
            var valueField = new Vector4Field { bindingPath = "Value" };
            valueField.Query<FloatField>().ForEach(field => field.formatString = "0.###");
            return valueField;
        }
    }

#if !ENABLE_TRANSFORM_V1
#else
    [UsedImplicitly]
    sealed class RotationInspector : QuaternionValueInspector<Rotation>
    {
    }

    [UsedImplicitly]
    sealed class PostRotationInspector : QuaternionValueInspector<PostRotation>
    {
    }
#endif

    abstract class DefaultValueInspector<T> : PropertyInspector<T>
    {
        public override VisualElement Build()
        {
            var root = new VisualElement();
            DoDefaultGui(root, "Value");
            return root;
        }
    }

    [UsedImplicitly]
    class ParentInspector : DefaultValueInspector<Parent>
    {
    }

    [UsedImplicitly]
    class PreviousParentInspector : DefaultValueInspector<PreviousParent>
    {
    }

    [UsedImplicitly]
    class ChildInspector : DefaultValueInspector<Child>
    {
    }
}
