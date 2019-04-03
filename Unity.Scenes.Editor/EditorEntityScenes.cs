using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Entities.Streaming;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Unity.Entities.GameObjectConversionUtility;
using Hash128 = Unity.Entities.Hash128;
using Object = UnityEngine.Object;

namespace Unity.Scenes.Editor
{
    //@TODO: Public
    public class EditorEntityScenes
    {
        static readonly ProfilerMarker k_ProfileEntitiesSceneSave = new ProfilerMarker("EntitiesScene.Save");
        static readonly ProfilerMarker k_ProfileEntitiesSceneCreatePrefab = new ProfilerMarker("EntitiesScene.CreatePrefab");
        static readonly ProfilerMarker k_ProfileEntitiesSceneSaveHeader = new ProfilerMarker("EntitiesScene.WriteHeader");


        public static bool IsEntitySubScene(Scene scene)
        {
            return scene.isSubScene;
        }


        public static void WriteEntityScene(SubScene scene)
        {
            Entities.Hash128 guid = new GUID(AssetDatabase.AssetPathToGUID(scene.EditableScenePath));
            WriteEntityScene(scene.LoadedScene, guid, 0);
        }

        public static bool HasEntitySceneCache(Hash128 sceneGUID)
        {
            string headerPath = EntityScenesPaths.GetPathAndCreateDirectory(sceneGUID, EntityScenesPaths.PathType.EntitiesHeader, "");
            return File.Exists(headerPath);
        }

        public static SceneData[] WriteEntityScene(Scene scene, Hash128 sceneGUID, ConversionFlags conversionFlags)
        {
            var world = new World("ConversionWorld");
            var entityManager = world.GetOrCreateManager<EntityManager>();
            
            var boundsEntity = entityManager.CreateEntity(typeof(SceneBoundingVolume));
            entityManager.SetComponentData(boundsEntity, new SceneBoundingVolume { Value = MinMaxAABB.Empty } );
            
            ConvertScene(scene, sceneGUID, world, conversionFlags);
            EntitySceneOptimization.Optimize(world);

            var bounds = entityManager.GetComponentData<SceneBoundingVolume>(boundsEntity).Value;
            entityManager.DestroyEntity(boundsEntity);
            
            var sceneSections = new List<SceneData>();

            var subSectionList = new List<SceneSection>();
            entityManager.GetAllUniqueSharedComponentData(subSectionList);
            var extRefInfoEntities = new NativeArray<Entity>(subSectionList.Count, Allocator.Temp);

            NativeArray<Entity> entitiesInMainSection;
            
            var sectionGrp = entityManager.CreateComponentGroup(
                new EntityArchetypeQuery
                {
                    All = new[] {ComponentType.ReadWrite<SceneSection>()},
                    Options = EntityArchetypeQueryOptions.IncludePrefab | EntityArchetypeQueryOptions.IncludeDisabled
                }
            );

            {
                var section = new SceneSection {SceneGUID = sceneGUID, Section = 0};
                sectionGrp.SetFilter(new SceneSection { SceneGUID = sceneGUID, Section = 0 });
                entitiesInMainSection = sectionGrp.ToEntityArray(Allocator.TempJob);

                // Each section will be serialized in its own world, entities that don't have a section are part of the main scene.
                // An entity that holds the array of external references to the main scene is required for each section.
                // We need to create them all before we start moving entities to section scenes,
                // otherwise they would reuse entities that have been moved and mess up the remapping tables.
                for(int sectionIndex = 1; sectionIndex < subSectionList.Count; ++sectionIndex)
                {
                    var extRefInfoEntity = entityManager.CreateEntity();
                    entityManager.AddSharedComponentData(extRefInfoEntity, subSectionList[sectionIndex]);
                    extRefInfoEntities[sectionIndex] = extRefInfoEntity;
                }

                // Public references array, only on the main section.
                var refInfoEntity = entityManager.CreateEntity();
                entityManager.AddBuffer<PublicEntityRef>(refInfoEntity);
                entityManager.AddSharedComponentData(refInfoEntity, section);
                var publicRefs = entityManager.GetBuffer<PublicEntityRef>(refInfoEntity);

//                entityManager.Debug.CheckInternalConsistency();

                //@TODO do we need to keep this index? doesn't carry any additional info
                for (int i = 0; i < entitiesInMainSection.Length; ++i)
                {
                    PublicEntityRef.Add(ref publicRefs,
                        new PublicEntityRef {entityIndex = i, targetEntity = entitiesInMainSection[i]});
                }

                Debug.Assert(publicRefs.Length == entitiesInMainSection.Length);

                // Save main section
                var sectionWorld = new World("SectionWorld");
                var sectionManager = sectionWorld.GetOrCreateManager<EntityManager>();

                var entityRemapping = entityManager.CreateEntityRemapArray(Allocator.TempJob);
                sectionManager.MoveEntitiesFrom(entityManager, sectionGrp, entityRemapping);

                // The section component is only there to break the conversion world into different sections
                // We don't want to store that on the disk
                //@TODO: Component should be removed but currently leads to corrupt data file. Figure out why.
                //sectionManager.RemoveComponent(sectionManager.UniversalGroup, typeof(SceneSection));
                
                var sectionFileSize = WriteEntityScene(sectionManager, sceneGUID, "0");
                sceneSections.Add(new SceneData
                {
                    FileSize = sectionFileSize,
                    SceneGUID = sceneGUID,
                    SharedComponentCount = sectionManager.GetSharedComponentCount() - 1,
                    SubSectionIndex = 0,
                    BoundingVolume = bounds
                });

                entityRemapping.Dispose();
                sectionWorld.Dispose();
            }

            {
                // Index 0 is the default value of the shared component, not an actual section
                for(int subSectionIndex = 0; subSectionIndex < subSectionList.Count; ++subSectionIndex)
                {
                    var subSection = subSectionList[subSectionIndex];
                    if (subSection.Section == 0)
                        continue;
                    
                    sectionGrp.SetFilter(subSection);
                    var entitiesInSection = sectionGrp.ToEntityArray(Allocator.TempJob);

                    if (entitiesInSection.Length > 0)
                    {
                        // Fetch back the external reference entity we created earlier to not disturb the mapping
                        var refInfoEntity = extRefInfoEntities[subSectionIndex];
                        entityManager.AddBuffer<ExternalEntityRef>(refInfoEntity);
                        var externRefs = entityManager.GetBuffer<ExternalEntityRef>(refInfoEntity);

                        // Store the mapping to everything in the main section
                        //@TODO maybe we don't need all that? is this worth worrying about?
                        for (int i = 0; i < entitiesInMainSection.Length; ++i)
                        {
                            ExternalEntityRef.Add(ref externRefs, new ExternalEntityRef{entityIndex = i});
                        }

                        // Entities will be remapped to a contiguous range in the section world,
                        // so any range after that is fine for the external references
                        //@TODO why are we not mapping anything to entity 0? we use the range [1;count], hence +1
                        var externEntityIndexStart = entitiesInSection.Length + 1;

                        entityManager.AddComponentData(refInfoEntity,
                            new ExternalEntityRefInfo
                            {
                                SceneGUID = sceneGUID,
                                EntityIndexStart = externEntityIndexStart
                            });

                        var sectionWorld = new World("SectionWorld");
                        var sectionManager = sectionWorld.GetOrCreateManager<EntityManager>();

                        var entityRemapping = entityManager.CreateEntityRemapArray(Allocator.TempJob);

                        // Insert mapping for external references, conversion world entity to virtual index in section
                        for (int i = 0; i < entitiesInMainSection.Length; ++i)
                        {
                            EntityRemapUtility.AddEntityRemapping(ref entityRemapping, entitiesInMainSection[i],
                                new Entity {Index = i + externEntityIndexStart, Version = 1});
                        }

                        sectionManager.MoveEntitiesFrom(entityManager, sectionGrp, entityRemapping);
                        
                        // When writing the scene, references to missing entities are set to Entity.Null by default
                        // We obviously don't want that to happen to our external references, so we add explicit mapping
                        for (int i = 0; i < entitiesInMainSection.Length; ++i)
                        {
                            var entity = new Entity {Index = i + externEntityIndexStart, Version = 1};
                            EntityRemapUtility.AddEntityRemapping(ref entityRemapping, entity, entity);
                        }

                        // The section component is only there to break the conversion world into different sections
                        // We don't want to store that on the disk
                        //@TODO: Component should be removed but currently leads to corrupt data file. Figure out why.
                        //sectionManager.RemoveComponent(sectionManager.UniversalGroup, typeof(SceneSection));

                        var fileSize = WriteEntityScene(sectionManager, sceneGUID, subSection.Section.ToString(), entityRemapping);
                        sceneSections.Add(new SceneData
                        {
                            FileSize = fileSize,
                            SceneGUID = sceneGUID,
                            SharedComponentCount = sectionManager.GetSharedComponentCount() - 1,
                            SubSectionIndex = subSection.Section,
                            BoundingVolume = bounds
                        });

                        entityRemapping.Dispose();
                        sectionWorld.Dispose();
                    }

                    entitiesInSection.Dispose();
                }
            }

            {
                var noSectionGrp = entityManager.CreateComponentGroup(
                    new EntityArchetypeQuery
                    {
                        None = new[] {ComponentType.ReadWrite<SceneSection>()},
                        Options = EntityArchetypeQueryOptions.IncludePrefab | EntityArchetypeQueryOptions.IncludeDisabled
                    }
                );
                if (noSectionGrp.CalculateLength() != 0)
                    Debug.LogWarning($"{noSectionGrp.CalculateLength()} entities in the scene '{scene.path}' had no SceneSection and as a result were not serialized at all.");
            }
            
            sectionGrp.Dispose();
            entitiesInMainSection.Dispose();
            world.Dispose();
            
            // Save the new header
            var header = ScriptableObject.CreateInstance<SubSceneHeader>();
            header.Sections = sceneSections.ToArray();

            WriteHeader(sceneGUID, header);

            return sceneSections.ToArray();
        }
        
        static int WriteEntityScene(EntityManager scene, Entities.Hash128 sceneGUID, string subsection, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos = default(NativeArray<EntityRemapUtility.EntityRemapInfo>))
        {
            k_ProfileEntitiesSceneSave.Begin();
            
            var entitiesBinaryPath = EntityScenesPaths.GetPathAndCreateDirectory(sceneGUID, EntityScenesPaths.PathType.EntitiesBinary, subsection);
            var sharedDataPath = EntityScenesPaths.GetPathAndCreateDirectory(sceneGUID, EntityScenesPaths.PathType.EntitiesSharedComponents, subsection);

            GameObject sharedComponents;
    
            // Write binary entity file
            int entitySceneFileSize = 0;
            using (var writer = new StreamBinaryWriter(entitiesBinaryPath))
            {
                if (entityRemapInfos.IsCreated)
                    SerializeUtilityHybrid.Serialize(scene, writer, out sharedComponents, entityRemapInfos);
                else
                    SerializeUtilityHybrid.Serialize(scene, writer, out sharedComponents);
                entitySceneFileSize = (int)writer.Length;
            }
            
            // Write shared component data prefab
            k_ProfileEntitiesSceneCreatePrefab.Begin();
            //var oldPrefab = AssetDatabase.LoadMainAssetAtPath(sharedDataPath);
            //if (oldPrefab == null)
                //        PrefabUtility.CreatePrefab(sharedDataPath, sharedComponents, ReplacePrefabOptions.ReplaceNameBased);

            if(sharedComponents != null)
                PrefabUtility.SaveAsPrefabAsset(sharedComponents, sharedDataPath);

            //else
            //    PrefabUtility.Save
                //PrefabUtility.ReplacePrefab(sharedComponents, oldPrefab, ReplacePrefabOptions.ReplaceNameBased);
    
            Object.DestroyImmediate(sharedComponents);
            k_ProfileEntitiesSceneCreatePrefab.End();
            
            
            k_ProfileEntitiesSceneSave.End();
            return entitySceneFileSize;
        }
        
        static void WriteHeader(Entities.Hash128 sceneGUID, SubSceneHeader header)
        {
            k_ProfileEntitiesSceneSaveHeader.Begin();
    
            string headerPath = EntityScenesPaths.GetPathAndCreateDirectory(sceneGUID, EntityScenesPaths.PathType.EntitiesHeader, "");
            AssetDatabase.CreateAsset(header, headerPath);
    
            //subscene.CacheSceneInformation();
    
            k_ProfileEntitiesSceneSaveHeader.End();
        }
    }    
}

