using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(GameObjectBeforeConversionGroup))]
[ConverterVersion("joe", 2)]
class TransformConversion : GameObjectConversionSystem
{
    private void Convert(Transform transform)
    {
        var entity = GetPrimaryEntity(transform);

#if !UNITY_2020_2_OR_NEWER
        // We have a dependency, but don't need to add it on 2020.2.
        // We special-case transforms there and can get away without it.
        DeclareDependency(transform, transform.parent);
#endif

        DstEntityManager.AddComponentData(entity, new LocalToWorld { Value = transform.localToWorldMatrix });
        if (DstEntityManager.HasComponent<Static>(entity))
            return;

        var hasParent = HasPrimaryEntity(transform.parent);
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
    }

    protected override void OnUpdate()
    {
        Entities.ForEach((Transform transform) =>
        {
            Convert(transform);
        });

        //@TODO: Remove this again once we add support for inheritance in queries
        Entities.ForEach((RectTransform transform) =>
        {
            Convert(transform);
        });
    }
}
