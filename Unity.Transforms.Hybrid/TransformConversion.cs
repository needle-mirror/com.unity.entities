using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
[UpdateInGroup(typeof(GameObjectBeforeConversionGroup))]
[ConverterVersion("joe", 2)]
partial class TransformConversion : GameObjectConversionSystem
{
    const float k_Tolerance = 0.001f;

#if !ENABLE_TRANSFORM_V1
    bool AreNearlyEqual(in float4x4 a, in float4x4 b, float tolerance)
    {
        for (int i = 0; i < 4; ++i)
        {
            for (int j = 0; j < 4; ++j)
            {
                if (math.abs(a[i][j] - b[i][j]) > tolerance)
                    return false;
            }
        }
        return true;
    }
#endif

    private void Convert(Transform transform)
    {
        var entity = GetPrimaryEntity(transform);

        DstEntityManager.AddComponentData(entity, new LocalToWorld { Value = transform.localToWorldMatrix });
        if (DstEntityManager.HasComponent<Static>(entity))
            return;

        var hasParent = HasPrimaryEntity(transform.parent);

#if !ENABLE_TRANSFORM_V1
        var localToWorldMatrix = (float4x4)transform.localToWorldMatrix;
        var localToWorldTransform = UniformScaleTransform.FromMatrix(localToWorldMatrix);
        DstEntityManager.AddComponentData(entity, new LocalToWorldTransform
        {
            Value = localToWorldTransform
        });

        var post = math.mul(localToWorldTransform.ToInverseMatrix(), localToWorldMatrix);
        if (!AreNearlyEqual(post, float4x4.identity, k_Tolerance))
        {
            DstEntityManager.AddComponentData(entity, new PostTransformMatrix
            {
                Value = post
            });
        }

        if (hasParent)
        {
            var parentToWorldTransform = UniformScaleTransform.FromMatrix(transform.parent.localToWorldMatrix);
            var localToParentTransform = parentToWorldTransform.InverseTransformTransform(localToWorldTransform);

            DstEntityManager.AddComponentData(entity, new LocalToParentTransform
            {
                Value = localToParentTransform
            });
            DstEntityManager.AddComponentData(entity, new Parent { Value = GetPrimaryEntity(transform.parent) });
        }
#else
        if (hasParent)
        {
            DstEntityManager.AddComponentData(entity, new Translation { Value = transform.localPosition });
            DstEntityManager.AddComponentData(entity, new Rotation { Value = transform.localRotation });

            if (transform.localScale != Vector3.one)
                DstEntityManager.AddComponentData(entity, new NonUniformScale { Value = transform.localScale });

            DstEntityManager.AddComponentData(entity, new Parent { Value = GetPrimaryEntity(transform.parent) });
            DstEntityManager.AddComponentData(entity, new LocalToParent());
        }
        else
        {
            DstEntityManager.AddComponentData(entity, new Translation { Value = transform.position });
            DstEntityManager.AddComponentData(entity, new Rotation { Value = transform.rotation });
            if (transform.lossyScale != Vector3.one)
                DstEntityManager.AddComponentData(entity, new NonUniformScale { Value = transform.lossyScale });
        }
#endif
    }
    protected override void OnUpdate()
    {
        Entities.ForEach((Transform transform) =>
        {
            Convert(transform);
        }).WithoutBurst().Run();

        Entities.ForEach((RectTransform transform) =>
        {
            Convert(transform);
        }).WithoutBurst().Run();
    }
}
