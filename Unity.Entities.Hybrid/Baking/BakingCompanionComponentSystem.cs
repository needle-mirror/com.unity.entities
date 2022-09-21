using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities.Conversion;
using Unity.Profiling;

namespace Unity.Entities
{
#if !UNITY_DISABLE_MANAGED_COMPONENTS
    [DisableAutoCreation]
    internal partial class BakingCompanionComponentSystem : SystemBase
    {
        EntityQuery                             m_CompanionComponentsQuery;
        EntityQuery                             m_RemoveCompanionComponentsQuery;
        HashSet<ComponentType>                  m_CompanionTypeSet = new HashSet<ComponentType>();
        bool                                    m_CompanionQueryDirty = true;
        internal static string                  k_CreateCompanionGameObjectsMarkerName = "Create Companion GameObjects";
        ProfilerMarker                          m_CreateCompanionGameObjectsMarker = new ProfilerMarker(k_CreateCompanionGameObjectsMarkerName);

        unsafe void CreateCompanionGameObjects()
        {
            using (m_CreateCompanionGameObjectsMarker.Auto())
            {
                var bakingSystem = World.GetExistingSystemManaged<BakingSystem>();

                UpdateCompanionQuery();

                // Clean up Companion Components
                EntityManager.RemoveComponent<CompanionLink>(m_RemoveCompanionComponentsQuery);

                var mcs = EntityManager.GetCheckedEntityDataAccess()->ManagedComponentStore;

                var archetypeChunkArray = m_CompanionComponentsQuery.ToArchetypeChunkArray(Allocator.Temp);

                Archetype* prevArchetype = null;
                var unityObjectTypeOffset = -1;
                var unityObjectTypeSizeOf = -1;
                var unityObjectTypes = new NativeList<TypeManager.TypeInfo>(10, Allocator.Temp);

                foreach(var archetypeChunk in archetypeChunkArray)
                {
                    var archetype = archetypeChunk.Archetype.Archetype;

                    if (prevArchetype != archetype)
                    {
                        unityObjectTypeOffset = -1;
                        unityObjectTypeSizeOf = -1;
                        unityObjectTypes.Clear();

                        for (int i = 0; i < archetype->TypesCount; i++)
                        {
                            var typeInfo = TypeManager.GetTypeInfo(archetype->Types[i].TypeIndex);
                            if (typeInfo.Category == TypeManager.TypeCategory.UnityEngineObject)
                            {
                                if (unityObjectTypeOffset == -1)
                                {
                                    unityObjectTypeOffset = archetype->Offsets[i];
                                    unityObjectTypeSizeOf = archetype->SizeOfs[i];
                                }

                                unityObjectTypes.Add(typeInfo);
                            }
                        }

                        // For some reason, this archetype had no UnityEngineObjects
                        if (unityObjectTypeOffset == -1)
                            throw new System.InvalidOperationException("CompanionComponent Query produced Archetype without a Unity Engine Object");
                    }

                    var chunk = archetypeChunk.m_Chunk;
                    var count = chunk->Count;
                    var entities = (Entity*)chunk->Buffer;

                    for (int entityIndex = 0; entityIndex < count; entityIndex++)
                    {
                        var entity = entities[entityIndex];

                        var managedIndex = *(int*)(chunk->Buffer + (unityObjectTypeOffset + unityObjectTypeSizeOf * entityIndex));
                        var obj = (UnityEngine.Component)mcs.GetManagedComponent(managedIndex);
                        var authoringGameObject = obj.gameObject;
                        bool wasActive = authoringGameObject.activeSelf;

                        try
                        {
                            if(wasActive)
                                authoringGameObject.SetActive(false);

                            // Replicate the authoringGameObject, we then strip Components we don't care about
                            var companionGameObject = UnityEngine.Object.Instantiate(authoringGameObject);
                            #if UNITY_EDITOR
                            CompanionGameObjectUtility.SetCompanionName(entity, companionGameObject);
                            #endif

                            var components = authoringGameObject.GetComponents<UnityEngine.Component>();
                            foreach (var component in components)
                            {
                                // This is possible if a MonoBehaviour is added but the Script cannot be found
                                // We can remove this if we disallow custom Hybrid Components
                                if (component == null)
                                    continue;

                                var type = component.GetType();
                                if (type == typeof(UnityEngine.Transform))
                                    continue;

                                var typeIndex = TypeManager.GetTypeIndex(type);
                                bool foundType = false;
                                for (int i = 0; i < unityObjectTypes.Length; i++)
                                {
                                    if (unityObjectTypes[i].TypeIndex == typeIndex)
                                    {
                                        foundType = true;
                                        break;
                                    }
                                }

                                if (foundType)
                                {
                                    EntityManager.AddComponentObject(entity, companionGameObject.GetComponent(type));
                                }
                                else
                                {
                                    // Remove all instances of this component from the companion, we don't want them
                                    var unwantedComponents = companionGameObject.GetComponents(type);
                                    foreach (var unwanted in unwantedComponents)
                                    {
                                        //@TODO some components have [RequireComponent] dependencies to each other, and will require special handling for deletion
                                        UnityEngine.Object.DestroyImmediate(unwanted);
                                    }
                                }
                            }
                            EntityManager.AddComponentData(entity, new CompanionLink { Companion = companionGameObject });

                            // We only move into the companion scene if we're currently doing live conversion.
                            // Otherwise this is handled by de-serialisation.
                            #if UNITY_EDITOR
                            if (bakingSystem.IsLiveConversion())
                            {
                                CompanionGameObjectUtility.MoveToCompanionScene(companionGameObject, true);
                            }
                            #endif

                            // Can't detach children before instantiate because that won't work with a prefab
                            for (int child = companionGameObject.transform.childCount - 1; child >= 0; child -= 1)
                                UnityEngine.Object.DestroyImmediate(companionGameObject.transform.GetChild(child).gameObject);
                        }
                        catch (System.Exception exception)
                        {
                            Debug.LogException(exception, authoringGameObject);
                        }
                        finally
                        {
                            if (wasActive)
                                authoringGameObject.SetActive(true);
                        }
                    }
                }

                archetypeChunkArray.Dispose();
                unityObjectTypes.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            CreateCompanionGameObjects();
        }

        private void UpdateCompanionQuery()
        {
            if (!m_CompanionQueryDirty)
                return;

            foreach (var type in CompanionComponentSupportedTypes.Types)
            {
                m_CompanionTypeSet.Add(type);
            }

#if UNITY_EDITOR
            if (BakingUtility.AdditionalCompanionComponentTypes.Count > 0)
            {
                foreach (var additionalType in BakingUtility.AdditionalCompanionComponentTypes)
                    m_CompanionTypeSet.Add(additionalType);
            }
#endif

            var entityQueryDesc = new EntityQueryDesc
            {
                Any = m_CompanionTypeSet.ToArray(),
                All = new ComponentType[] {typeof(BakedEntity)},
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            };
            m_CompanionComponentsQuery = EntityManager.CreateEntityQuery(entityQueryDesc);
            m_CompanionComponentsQuery.SetOrderVersionFilter();

            m_RemoveCompanionComponentsQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(CompanionLink)},
                None = m_CompanionTypeSet.ToArray(),
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            });

            m_CompanionQueryDirty = false;
        }
    }
#endif
}
