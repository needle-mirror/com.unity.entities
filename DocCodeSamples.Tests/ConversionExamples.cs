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
