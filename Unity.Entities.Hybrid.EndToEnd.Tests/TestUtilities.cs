using System;
using System.Collections.Generic;
using Unity.Scenes;

namespace Unity.Entities.Hybrid.EndToEnd.Tests
{
    public static class TestUtilities
    {
        [Flags]
        public enum SystemCategories
        {
            Streaming = 1
        }

        public static void RegisterSystems(World world, SystemCategories categories)
        {
            var systems = new List<Type>();

            if (categories.HasFlag(SystemCategories.Streaming))
            {
                systems.AddRange(new []
                {
                    typeof(SceneSystemGroup),
                    typeof(SceneSystem),
                    typeof(ResolveSceneReferenceSystem),
                    typeof(SceneSectionStreamingSystem)
                });
            }

            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);
        }
    }
}