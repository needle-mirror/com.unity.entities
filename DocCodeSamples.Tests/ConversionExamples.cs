using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

// Embedded code cannot be larger than the following line, otherwise it will wrap
// =======================================================================================

#pragma warning disable 649
#region conversion101
// Authoring component
class FooAuthoring : MonoBehaviour
{
    public float Value;
}

// Runtime component
struct Foo : IComponentData
{
    public float SquaredValue;
}

// Conversion system, running in the conversion world
class FooConversion : GameObjectConversionSystem
{
    protected override void OnUpdate()
    {
        // Iterate over all authoring components of type FooAuthoring
        Entities.ForEach((FooAuthoring input) =>
        {
            // Get the destination world entity associated with the authoring GameObject
            var entity = GetPrimaryEntity(input);

            // Do the conversion and add the ECS component
            DstEntityManager.AddComponentData(entity, new Foo
            {
                SquaredValue = input.Value * input.Value
            });
        });
    }
}
#endregion

#region PrefabReference
// Authoring component
public class PrefabReference : MonoBehaviour
{
    public GameObject Prefab;
}

// Runtime component
public struct PrefabEntityReference : IComponentData
{
    public Entity Prefab;
}
#endregion

#region PrefabConverterDeclare
[UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup))]
class PrefabConverterDeclare : GameObjectConversionSystem
{
    protected override void OnUpdate()
    {
        Entities.ForEach((PrefabReference prefabReference) =>
        {
            DeclareReferencedPrefab(prefabReference.Prefab);
        });
    }
}
#endregion

#region PrefabConverter
class PrefabConverter : GameObjectConversionSystem
{
    protected override void OnUpdate()
    {
        Entities.ForEach((PrefabReference prefabReference) =>
        {
            var entity = GetPrimaryEntity(prefabReference);
            var prefab = GetPrimaryEntity(prefabReference.Prefab);

            var component = new PrefabEntityReference {Prefab = prefab};
            DstEntityManager.AddComponentData(entity, component);
        });
    }
}
#endregion

namespace docnamespace_IConvertGameObjectToEntity
{
#region IConvertGameObjectToEntity

// Authoring component
class FooAuthoring : MonoBehaviour, IConvertGameObjectToEntity
{
    public float Value;

    public void Convert(Entity entity, EntityManager dstManager,
        GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new Foo {SquaredValue = Value * Value});
    }
}

// Runtime component
struct Foo : IComponentData
{
    public float SquaredValue;
}
#endregion
}

namespace docnamespace_IDeclareReferencedPrefabs
{
#region IDeclareReferencedPrefabs
public class PrefabReference : MonoBehaviour, IDeclareReferencedPrefabs
{
    public GameObject Prefab;

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        referencedPrefabs.Add(Prefab);
    }
}
#endregion
}

namespace docnamespace_ConverterVersion1
{
#region ConverterVersion1
public class SomeComponentAuthoring : MonoBehaviour
{
    public int SomeValue;
}

[ConverterVersion("Fabrice", 140)]
public class SomeComponentConversion : GameObjectConversionSystem
{
    protected override void OnUpdate()
    {
        // ...
    }
}
#endregion
}

namespace docnamespace_ConverterVersion2
{
#region ConverterVersion2
[ConverterVersion("Fabrice", 140)]
public class SomeComponentAuthoring : MonoBehaviour, IConvertGameObjectToEntity
{
    public int SomeValue;

    public void Convert(Entity entity, EntityManager dstManager,
        GameObjectConversionSystem conversionSystem)
    {
        // ...
    }
}
#endregion
}


#if !UNITY_DISABLE_MANAGED_COMPONENTS
namespace docnamespace_DependencyOnAsset
{
#region DependencyOnAsset
public struct BoundsComponent : IComponentData
{
    public Bounds Bounds;
}

[ConverterVersion("unity", 1)]
public class MeshBoundingBoxDependency : MonoBehaviour, IConvertGameObjectToEntity
{
    public Mesh Mesh;

    public void Convert(Entity entity, EntityManager dstManager,
        GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new BoundsComponent {
            Bounds = Mesh.bounds
        });
        // Declare the dependency on the asset. Note the lack of a check for null.
        conversionSystem.DeclareAssetDependency(gameObject, Mesh);
    }
}
#endregion

#region NoDependencyOnAssetReference
public class MeshComponent : IComponentData
{
    public Mesh Mesh;
}

[ConverterVersion("unity", 1)]
public class MeshReference : MonoBehaviour, IConvertGameObjectToEntity
{
    public Mesh Mesh;

    public void Convert(Entity entity, EntityManager dstManager,
        GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new MeshComponent {
            Mesh = Mesh
        });
        // No need to declare a dependency here, we're merely referencing an asset.
    }
}
#endregion

#region DependencyOnName
public struct NameComponent : IComponentData {
    public Unity.Collections.FixedString32 Name;
}

[ConverterVersion("unity", 1)]
public class NameFromGameObject : MonoBehaviour, IConvertGameObjectToEntity
{
    public GameObject Other;

    public void Convert(Entity entity, EntityManager dstManager,
        GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new NameComponent {
            Name = Other.name
        });
        // Note the lack of a null check
        conversionSystem.DeclareDependency(gameObject, Other);
    }
}
#endregion

#region DependencyOnComponent
[ConverterVersion("unity", 1)]
public class MeshFromOtherComponent : MonoBehaviour, IConvertGameObjectToEntity
{
    public MeshFilter MeshFilter;

    public void Convert(Entity entity, EntityManager dstManager,
        GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new MeshComponent {
            Mesh = MeshFilter.sharedMesh
        });
        // Note the lack of a null check
        conversionSystem.DeclareDependency(gameObject, MeshFilter);
    }
}
#endregion

#region DependencyOnTransformComponent
public struct Offset : IComponentData
{
    public Unity.Mathematics.float3 Value;
}

[ConverterVersion("unity", 1)]
public class ReadFromOwnTransform : MonoBehaviour, IConvertGameObjectToEntity
{
    public void Convert(Entity entity, EntityManager dstManager,
        GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new Offset {
            Value = transform.position
        });

        // We need to explicitly declare a dependency on the transform data,
        // even when it is on the same object.
        conversionSystem.DeclareDependency(gameObject, transform);
    }
}
#endregion

#region DependencyOnOtherMeshFilterComponent
[ConverterVersion("unity", 1)]
public class ReadFromOtherMeshFilter : MonoBehaviour, IConvertGameObjectToEntity
{
    public GameObject Other;

    public void Convert(Entity entity, EntityManager dstManager,
        GameObjectConversionSystem conversionSystem)
    {
        if (Other != null) {
            var meshFilter = Other.GetComponent<MeshFilter>();
            dstManager.AddComponentData(entity, new MeshComponent {
                Mesh = meshFilter.sharedMesh
            });

            // In this case, we need a null-check: meshFilter can only be
            // accessed when Other is not null.
            // It would be simpler to expose a reference to a Meshfilter on this
            // MonoBehaviour.
            conversionSystem.DeclareDependency(gameObject, meshFilter);
        }

        // Note the lack of a null-check
        conversionSystem.DeclareDependency(gameObject, Other);
    }
}
#endregion

#region GetPrimaryEntityFailure
public struct EntityReference : IComponentData
{
    public Entity Entity;
}

[ConverterVersion("unity", 1)]
public class GetEntityReference : MonoBehaviour, IConvertGameObjectToEntity
{
    public GameObject Other;

    public void Convert(Entity entity, EntityManager dstManager,
        GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new EntityReference {
            Entity = conversionSystem.GetPrimaryEntity(Other)
        });

        // This line is required right now, unfortunately.
        // Note the lack of a null-check.
        conversionSystem.DeclareDependency(gameObject, Other);
    }
}
#endregion
}
#endif

#if !UNITY_DISABLE_MANAGED_COMPONENTS
namespace docnamespace_HybridComponent
{
    using Doohickey = UnityEngine.Camera;

    #region HybridComponent_ConversionSystem
    [ConverterVersion("unity", 1)]
    public class DoohickeyConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((Doohickey doohickey) =>
            {
                AddHybridComponent(doohickey);
            });
        }
    }
    #endregion
}
#endif
