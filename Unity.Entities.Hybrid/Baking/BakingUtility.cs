using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;
using Unity.Entities.Hybrid.Baking;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;

#pragma warning disable 162

namespace Unity.Entities
{
    internal static class BakingUtility
    {
        internal static readonly string s_BakeSceneStr = "BakingUtility.BakeScene";
        internal static readonly string s_BakeGameObjectsStr = "BakingUtility.BakeGameObjects";
        internal static readonly string s_BakingSystemGroupStr = "BakingUtility.BakingSystemGroup";
        internal static readonly string s_PostBakingSystemGroupStr = "BakingUtility.PostBakingSystemGroup";
        internal static readonly string s_PreBakingSystemGroupStr = "BakingUtility.PreBakingSystemGroup";
        internal static readonly string s_TransformBakingSystemGroupStr = "BakingUtility.TransformBakingSystemGroup";
        internal static readonly string s_BakingStripSystemStr = "BakingUtility.BakingStripSystem";
        internal static readonly string s_BakingCompanionComponentSystemStr = "BakingUtility.BakingCompanionComponentSystem";
        internal static readonly string s_LinkedEntityGroupSystemStr = "BakingUtility.LinkedEntityGroupBaking";

        static readonly ProfilerMarker s_BakeScene = new ProfilerMarker(s_BakeSceneStr);
        static readonly ProfilerMarker s_BakeGameObjects = new ProfilerMarker(s_BakeGameObjectsStr);
        static readonly ProfilerMarker s_BakingSystemGroup = new ProfilerMarker(s_BakingSystemGroupStr);
        static readonly ProfilerMarker s_PostBakingSystemGroup = new ProfilerMarker(s_PostBakingSystemGroupStr);
        static readonly ProfilerMarker s_PreBakingSystemGroup = new ProfilerMarker(s_PreBakingSystemGroupStr);
        static readonly ProfilerMarker s_TransformBakingSystemGroup = new ProfilerMarker(s_TransformBakingSystemGroupStr);
        static readonly ProfilerMarker s_BakingStripSystem = new ProfilerMarker(s_BakingStripSystemStr);
        static readonly ProfilerMarker s_BakingCompanionComponentSystem = new ProfilerMarker(s_BakingCompanionComponentSystemStr);
        static readonly ProfilerMarker s_LinkedEntityGroup = new ProfilerMarker(s_LinkedEntityGroupSystemStr);

        [Flags]
        public enum BakingFlags : uint
        {
            AddEntityGUID = 1 << 0,
            ForceStaticOptimization = 1 << 1,
            AssignName = 1 << 2,
            SceneViewLiveConversion = 1 << 3,
            GameViewLiveConversion = 1 << 4,
            IsBuildingForPlayer = 1 << 5
        }

        internal static string[] CollectImportantProfilerMarkerStrings()
        {
                return new string [] {
                     s_BakeSceneStr,
                     s_BakingSystemGroupStr,
                     s_BakingStripSystemStr,
                     s_PostBakingSystemGroupStr,
                     s_TransformBakingSystemGroupStr
                };
        }

        internal static bool BakeScene(World conversionWorld, Scene scene, BakingSettings settings, bool incremental, IncrementalBakingChangeTracker changeTracker)
        {
#if UNITY_EDITOR
#if ENABLE_CLOUD_SERVICES_ANALYTICS
            var watch = Stopwatch.StartNew();
#endif
#endif

            using (s_BakeScene.Auto())
            {
                var bakingSystem = conversionWorld.GetOrCreateSystemManaged<BakingSystem>();

                if(!incremental)
                    bakingSystem.PrepareForBaking(settings, scene);

                GameObject[] cleanRootGameObjects = null;
                if (!incremental)
                    cleanRootGameObjects = scene.GetRootGameObjects();

                PreprocessBake(conversionWorld, settings, bakingSystem);

                // We can't early out as Preprocess might have done some actions that need to be completed by PostProcess
                // An example of this is LinkedEntityGroupBakingCleanUp
                bakingSystem.Bake(changeTracker, cleanRootGameObjects);

                PostprocessBake(conversionWorld, settings, bakingSystem);

            }
#if UNITY_EDITOR
#if ENABLE_CLOUD_SERVICES_ANALYTICS
            watch.Stop();

            if(incremental)
                BakingAnalytics.SendAnalyticsEvent(watch.ElapsedMilliseconds, BakingAnalytics.EventType.IncrementalBaking);
            else
                BakingAnalytics.SendAnalyticsEvent(watch.ElapsedMilliseconds, BakingAnalytics.EventType.OpeningSubScene);
#endif
#endif

            return true;
        }

        static void PreprocessBake(World conversionWorld, BakingSettings settings, BakingSystem bakingSystem)
        {
            //TODO: DOTS-5452
            var systemTypes = settings.Systems ?? DefaultWorldInitialization.GetAllSystems(settings.FilterFlags);
            AddBakingSystems(conversionWorld, systemTypes.Concat(settings.ExtraSystems));
#if UNITY_EDITOR
#if ENABLE_CLOUD_SERVICES_ANALYTICS
            BakingAnalytics.BakingSystemTypes = systemTypes;
#endif
#endif

            using (s_PreBakingSystemGroup.Auto())
            {
                var preGroup = conversionWorld.GetOrCreateSystemManaged<PreBakingSystemGroup>();
                preGroup.Update();
            }
        }

        static void PostprocessBake(World conversionWorld, BakingSettings settings, BakingSystem bakingSystem)
        {
            using (s_TransformBakingSystemGroup.Auto())
            {
                var transformBakingSystemGroup = conversionWorld.GetExistingSystemManaged<TransformBakingSystemGroup>();
                transformBakingSystemGroup.Update();
            }

            using (s_BakingSystemGroup.Auto())
            {
                var bakingSystemGroup = conversionWorld.GetExistingSystemManaged<BakingSystemGroup>();
                bakingSystemGroup.Update();
            }

            bakingSystem.UpdateReferencedEntities();

            using (s_LinkedEntityGroup.Auto())
            {
                var legSystem = conversionWorld.GetOrCreateSystemManaged<LinkedEntityGroupBaking>();
                legSystem.Update();
            }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            using (s_BakingCompanionComponentSystem.Auto())
            {
                var companionComponentSystem = conversionWorld.GetOrCreateSystemManaged<BakingCompanionComponentSystem>();
                companionComponentSystem.Update();
            }
#endif
            using (s_PostBakingSystemGroup.Auto())
            {
                var postGroup = conversionWorld.GetOrCreateSystemManaged<PostBakingSystemGroup>();
                postGroup.Update();
            }

            using (s_BakingStripSystem.Auto())
            {
                var stripSystem = conversionWorld.GetOrCreateSystemManaged<BakingStripSystem>();
                stripSystem.Update();
            }
        }

        internal static void BakeGameObjects(World conversionWorld, GameObject[] rootGameObjects, BakingSettings settings)
        {
            using (s_BakeGameObjects.Auto())
            {
                var bakingSystem = conversionWorld.GetOrCreateSystemManaged<BakingSystem>();
                bakingSystem.PrepareForBaking(settings, default);
                PreprocessBake(conversionWorld, settings, bakingSystem);
                bakingSystem.Bake(default, rootGameObjects);
                PostprocessBake(conversionWorld, settings, bakingSystem);
            }
        }

        struct BakingRootGroups : DefaultWorldInitialization.IIdentifyRootGroups
        {
            public bool IsRootGroup(Type type) => type == typeof(BakingSystemGroup) ||
                                     type == typeof(PostBakingSystemGroup) ||
                                     type == typeof(TransformBakingSystemGroup) ||
                                     type == typeof(PreBakingSystemGroup);
        }

        static void AddBakingSystems(World gameObjectWorld, IEnumerable<Type> systemTypes)
        {
            var bakeSystemGroup = gameObjectWorld.GetOrCreateSystemManaged<BakingSystemGroup>();
            var postBakingSystemGroup = gameObjectWorld.GetOrCreateSystemManaged<PostBakingSystemGroup>();
            var preBakingSystemGroup = gameObjectWorld.GetOrCreateSystemManaged<PreBakingSystemGroup>();
            var transformBakingSystemGroup = gameObjectWorld.GetOrCreateSystemManaged<TransformBakingSystemGroup>();

            DefaultWorldInitialization.AddSystemToRootLevelSystemGroupsInternal(gameObjectWorld, systemTypes, bakeSystemGroup, new BakingRootGroups());

            bakeSystemGroup.SortSystems();
            postBakingSystemGroup.SortSystems();
            transformBakingSystemGroup.SortSystems();
            preBakingSystemGroup.SortSystems();
        }

        static void AddSystemAndLogException(World world, ComponentSystemGroup group, Type type)
        {
            try
            {
                var system = world.GetOrCreateSystemManaged(type);
                group.AddSystemToUpdateList(system);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to add baking system {type}, exception following");
                Debug.LogException(e);
            }
        }

#if UNITY_EDITOR
        // These are used for internal tests
        internal static HashSet<ComponentType> AdditionalCompanionComponentTypes = new();
        internal static void AddAdditionalCompanionComponentType(ComponentType newType)
        {
            AdditionalCompanionComponentTypes.Add(newType);
        }
#endif
    }
}
