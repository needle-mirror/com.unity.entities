using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Entities.Conversion;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;

#pragma warning disable 162

namespace Unity.Entities
{
    public static class GameObjectConversionUtility
    {
        static ProfilerMarker s_ConvertScene = new ProfilerMarker("GameObjectConversionUtility.ConvertScene");
        static ProfilerMarker s_CreateConversionWorld = new ProfilerMarker("Create World & Systems");
        static ProfilerMarker s_DestroyConversionWorld = new ProfilerMarker("DestroyWorld");
        static ProfilerMarker s_CreateEntitiesForGameObjects = new ProfilerMarker("CreateEntitiesForGameObjects");
        static ProfilerMarker s_UpdateConversionSystems = new ProfilerMarker("UpdateConversionSystems");
        static ProfilerMarker s_UpdateExportSystems = new ProfilerMarker("UpdateExportSystems");
        static ProfilerMarker s_CreateCompanionGameObjects = new ProfilerMarker("CreateCompanionGameObjects");
        static ProfilerMarker s_AddPrefabComponentDataTag = new ProfilerMarker("AddPrefabComponentDataTag");
        static ProfilerMarker s_GenerateLinkedEntityGroups = new ProfilerMarker("GenerateLinkedEntityGroups");

        [Flags]
        public enum ConversionFlags : uint
        {
            AddEntityGUID = 1 << 0,
            ForceStaticOptimization = 1 << 1,
            AssignName = 1 << 2,
            SceneViewLiveConversion = 1 << 3,
            GameViewLiveConversion = 1 << 4,
            IsBuildingForPlayer = 1 << 5
        }

        internal static World CreateConversionWorld(GameObjectConversionSettings settings, Scene scene = default)
        {
            using (s_CreateConversionWorld.Auto())
            {
                var gameObjectWorld = new World($"GameObject -> Entity Conversion '{settings.DebugConversionName}'", WorldFlags.Live | WorldFlags.Conversion | WorldFlags.Staging);
                var mappingSystem = new GameObjectConversionMappingSystem(settings);
                gameObjectWorld.AddSystem(mappingSystem);
                if (mappingSystem.IsLiveConversion)
                    mappingSystem.PrepareForLiveConversion(scene);

                var systemTypes = settings.Systems ?? DefaultWorldInitialization.GetAllSystems(settings.FilterFlags);

                var includeExport = settings.SupportsExporting;
                AddConversionSystems(gameObjectWorld, systemTypes.Concat(settings.ExtraSystems), includeExport);

                settings.ConversionWorldCreated?.Invoke(gameObjectWorld);

                return gameObjectWorld;
            }
        }

        struct DeclaredReferenceObjectsTag : IComponentData {}

        static void DeclareReferencedObjects(World gameObjectWorld, GameObjectConversionMappingSystem mappingSystem)
        {
            var newAllEntitiesQuery = mappingSystem.Entities
                .WithNone<DeclaredReferenceObjectsTag>()
                .ToEntityQuery();

            var newGoEntitiesQuery = mappingSystem.GetEntityQuery(
                new EntityQueryDesc
                {
                    None = new ComponentType[] { typeof(DeclaredReferenceObjectsTag) },
                    All = new ComponentType[] { typeof(Transform) }
                },
                new EntityQueryDesc
                {
                    None = new ComponentType[] { typeof(DeclaredReferenceObjectsTag) },
                    All = new ComponentType[] { typeof(RectTransform) }
                });

            var prefabDeclarers = new List<IDeclareReferencedPrefabs>();
            var declaredPrefabs = new List<GameObject>();

            // loop until no new entities discovered that might need following
            while (!newAllEntitiesQuery.IsEmptyIgnoreFilter)
            {
                using (var newGoEntities = newGoEntitiesQuery.ToEntityArray(Allocator.TempJob))
                {
                    // fetch components that implement IDeclareReferencedPrefabs
                    foreach (var newGoEntity in newGoEntities)
                    {
                        ((Transform)gameObjectWorld.EntityManager.Debug.GetComponentBoxed(newGoEntity, typeof(Transform))).GetComponents(prefabDeclarers);

                        // let each component declare any prefab refs it knows about
                        foreach (var prefabDeclarer in prefabDeclarers)
                            prefabDeclarer.DeclareReferencedPrefabs(declaredPrefabs);

                        prefabDeclarers.Clear();
                    }
                }

                // mark as seen for next loop
                gameObjectWorld.EntityManager.AddComponent<DeclaredReferenceObjectsTag>(newAllEntitiesQuery);

                foreach (var declaredPrefab in declaredPrefabs)
                    mappingSystem.DeclareReferencedPrefab(declaredPrefab);
                declaredPrefabs.Clear();

                // give systems a chance to declare prefabs and assets
                gameObjectWorld.GetExistingSystem<GameObjectDeclareReferencedObjectsGroup>().Update();
            }

            // clean up the markers
            gameObjectWorld.EntityManager.RemoveComponent<DeclaredReferenceObjectsTag>(gameObjectWorld.EntityManager.UniversalQuery);
        }

        struct Conversion : IDisposable
        {
            public GameObjectConversionMappingSystem MappingSystem { get; }

            public Conversion(World conversionWorld)
            {
                MappingSystem = conversionWorld.GetExistingSystem<GameObjectConversionMappingSystem>();
                MappingSystem.BeginConversion();
            }

            public void Dispose()
            {
                MappingSystem.EndConversion();
            }
        }

        internal static void Convert(World conversionWorld)
        {
            using (var conversion = new Conversion(conversionWorld))
            {
                using (s_UpdateConversionSystems.Auto())
                {
                    DeclareReferencedObjects(conversionWorld, conversion.MappingSystem);

                    conversion.MappingSystem.CreatePrimaryEntities();

                    conversionWorld.GetExistingSystem<GameObjectBeforeConversionGroup>().Update();
                    conversionWorld.GetExistingSystem<GameObjectConversionGroup>().Update();
                    conversionWorld.GetExistingSystem<GameObjectAfterConversionGroup>().Update();
                }

                using (s_AddPrefabComponentDataTag.Auto())
                    conversion.MappingSystem.AddPrefabComponentDataTag();

#if !UNITY_DISABLE_MANAGED_COMPONENTS
                using (s_CreateCompanionGameObjects.Auto())
                    conversion.MappingSystem.CreateCompanionGameObjects();
#endif

                using (s_GenerateLinkedEntityGroups.Auto())
                    conversion.MappingSystem.GenerateLinkedEntityGroups();

                using (s_UpdateExportSystems.Auto())
                    conversionWorld.GetExistingSystem<GameObjectExportGroup>()?.Update();
            }
        }

        internal static void ConvertIncremental(World conversionWorld, IEnumerable<GameObject> gameObjects, NativeList<int> changedAssetInstanceIds, ConversionFlags flags)
        {
            var args = new IncrementalConversionBatch
            {
                ReconvertHierarchyInstanceIds = new NativeArray<int>(gameObjects.Select(go => go.GetInstanceID()).ToArray(), Allocator.TempJob),
                ChangedComponents = new List<Component>()
            };
            if (changedAssetInstanceIds.IsCreated)
                args.ChangedAssets = changedAssetInstanceIds;
            ConvertIncremental(conversionWorld, flags, ref args);
            args.ReconvertHierarchyInstanceIds.Dispose();
        }

        internal static void ConvertIncremental(World conversionWorld, ConversionFlags flags, ref IncrementalConversionBatch batch)
        {
            using (var conversion = new Conversion(conversionWorld))
            {
                conversion.MappingSystem.BeginIncrementalConversionPreparation(flags, ref batch);
                conversionWorld.GetExistingSystem<ConversionSetupGroup>().Update();
                conversion.MappingSystem.FinishIncrementalConversionPreparation();

                FinishConvertIncremental(conversionWorld, conversion);
            }
        }

        static void FinishConvertIncremental(World conversionWorld, Conversion conversion)
        {
            using (s_UpdateConversionSystems.Auto())
            {
                conversionWorld.GetExistingSystem<GameObjectBeforeConversionGroup>().Update();
                conversionWorld.GetExistingSystem<GameObjectConversionGroup>().Update();
                conversionWorld.GetExistingSystem<GameObjectAfterConversionGroup>().Update();
            }

            using (s_GenerateLinkedEntityGroups.Auto())
                conversion.MappingSystem.GenerateLinkedEntityGroups();

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            using (s_CreateCompanionGameObjects.Auto())
                conversion.MappingSystem.CreateCompanionGameObjects();
#endif

            conversionWorld.EntityManager.DestroyEntity(conversionWorld.EntityManager.UniversalQuery);
        }

        internal static World InitializeIncrementalConversion(Scene scene, GameObjectConversionSettings settings)
        {
            using (s_ConvertScene.Auto())
            {
                var conversionWorld = CreateConversionWorld(settings, scene);
                using (var conversion = new Conversion(conversionWorld))
                {
                    using (s_CreateEntitiesForGameObjects.Auto())
                        conversion.MappingSystem.CreateEntitiesForGameObjects(scene);

                    Convert(conversionWorld);

                    conversionWorld.EntityManager.DestroyEntity(conversionWorld.EntityManager.UniversalQuery);
                }

                return conversionWorld;
            }
        }

        public static Entity ConvertGameObjectHierarchy(GameObject root, GameObjectConversionSettings settings)
        {
            using (s_ConvertScene.Auto())
            {
                Entity convertedEntity;
                using (var conversionWorld = CreateConversionWorld(settings))
                using (var conversion = new Conversion(conversionWorld))
                {
                    using (s_CreateEntitiesForGameObjects.Auto())
                        conversion.MappingSystem.AddGameObjectOrPrefab(root);

                    Convert(conversionWorld);

                    convertedEntity = conversion.MappingSystem.GetPrimaryEntity(root);

                    settings.ConversionWorldPreDispose?.Invoke(conversionWorld);

                    s_DestroyConversionWorld.Begin();
                }
                s_DestroyConversionWorld.End();
                return convertedEntity;
            }
        }

        public static void ConvertScene(Scene scene, GameObjectConversionSettings settings)
        {
            using (s_ConvertScene.Auto())
            {
                using (var conversionWorld = CreateConversionWorld(settings, scene))
                using (var conversion = new Conversion(conversionWorld))
                {
                    using (s_CreateEntitiesForGameObjects.Auto())
                        conversion.MappingSystem.CreateEntitiesForGameObjects(scene);
#if UNITY_EDITOR
                    if (settings.PrefabRoot != null)
                    {
                        conversion.MappingSystem.DeclareReferencedPrefab(settings.PrefabRoot);
                    }
#endif
                    Convert(conversionWorld);

                    settings.ConversionWorldPreDispose?.Invoke(conversionWorld);

                    s_DestroyConversionWorld.Begin();
                }
                s_DestroyConversionWorld.End();
            }
        }

        struct ConversionRootGroups : DefaultWorldInitialization.IIdentifyRootGroups
        {
            public bool IsRootGroup(Type type) => type == typeof(ConversionSetupGroup) ||
                                                 type == typeof(GameObjectDeclareReferencedObjectsGroup) ||
                                                 type == typeof(GameObjectBeforeConversionGroup) ||
                                                 type == typeof(GameObjectConversionGroup) ||
                                                 type == typeof(GameObjectAfterConversionGroup);
        }

        static void AddConversionSystems(World gameObjectWorld, IEnumerable<Type> systemTypes, bool includeExport)
        {
            var incremental = gameObjectWorld.GetOrCreateSystem<ConversionSetupGroup>();
            var declareConvert = gameObjectWorld.GetOrCreateSystem<GameObjectDeclareReferencedObjectsGroup>();
            var earlyConvert = gameObjectWorld.GetOrCreateSystem<GameObjectBeforeConversionGroup>();
            var convert = gameObjectWorld.GetOrCreateSystem<GameObjectConversionGroup>();
            var lateConvert = gameObjectWorld.GetOrCreateSystem<GameObjectAfterConversionGroup>();

            var export = includeExport ? gameObjectWorld.GetOrCreateSystem<GameObjectExportGroup>() : null;

            {
                // for various reasons, this system needs to be present before any other system initializes
                var system = gameObjectWorld.GetOrCreateSystem<IncrementalChangesSystem>();
                incremental.AddSystemToUpdateList(system);
            }

            DefaultWorldInitialization.AddSystemToRootLevelSystemGroupsInternal(gameObjectWorld, systemTypes, convert, new ConversionRootGroups());
#if UNITY_EDITOR
            foreach (var system in gameObjectWorld.Systems)
            {
                if (system is GameObjectConversionSystem gocs)
                {
                    // TODO we should log all conversion systems and their enabled/disabled state
                    gocs.Enabled = gocs.ShouldRunConversionSystem();
                }
            }
#endif

            incremental.SortSystems();
            declareConvert.SortSystems();
            earlyConvert.SortSystems();
            convert.SortSystems();
            lateConvert.SortSystems();
            export?.SortSystems();
        }

        // USED FOR IL-POSTPROCESSING AUTHORING COMPONENTS
        public static void ConvertGameObjectsToEntitiesField(GameObjectConversionSystem conversionSystem, GameObject[] gameObjects, out Entity[] entities)
        {
            entities = new Entity[gameObjects.Length];
            for (var i = 0; i < entities.Length; ++i)
                entities[i] = conversionSystem.GetPrimaryEntity(gameObjects[i]);
        }
    }
}
