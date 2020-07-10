using NUnit.Framework;
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.TestTools;
#endif
using Unity.Entities;
using Unity.Entities.Tests;

namespace Unity.Scenes.Hybrid.Tests
{
    public class SectionMetadataTests : SubSceneTestFixture
    {
        public SectionMetadataTests() : base("Packages/com.unity.entities/Unity.Scenes.Hybrid.Tests/TestSceneWithSubScene/TestSubSceneWithSectionMetadata.unity")
        {
        }

        // Only works in Editor for now until we can support SubScene building with new build settings in a test
        [Test]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public void SectionMetadata()
        {
            using (var world = TestWorldSetup.CreateEntityWorld("World", false))
            {
                var resolveParams = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.DisableAutoLoad
                };
                var sceneSystem = world.GetOrCreateSystem<SceneSystem>();
                var sceneEntity = sceneSystem.LoadSceneAsync(SceneGUID, resolveParams);
                world.Update();
                var manager = world.EntityManager;
                var sectionEntities = manager.GetBuffer<ResolvedSectionEntity>(sceneEntity);

                Assert.AreEqual(3, sectionEntities.Length);
                Assert.IsTrue(manager.HasComponent<TestMetadata>(sectionEntities[0].SectionEntity));
                Assert.IsFalse(manager.HasComponent<TestMetadata>(sectionEntities[1].SectionEntity));
                Assert.IsTrue(manager.HasComponent<TestMetadata>(sectionEntities[2].SectionEntity));

                Assert.IsTrue(manager.HasComponent<TestMetadataTag>(sectionEntities[0].SectionEntity));
                Assert.IsFalse(manager.HasComponent<TestMetadataTag>(sectionEntities[1].SectionEntity));
                Assert.IsTrue(manager.HasComponent<TestMetadataTag>(sectionEntities[2].SectionEntity));

                // These components should not be added, instead an error is logged that meta info components can't contain entities or blob assets
                var filteredTypes = new[]
                {
                    typeof(TestMetadataWithEntity), typeof(TestMetadataWithBlobAsset), typeof(EcsTestSharedComp), typeof(EcsIntElement), typeof(EcsState1),
#if !UNITY_DISABLE_MANAGED_COMPONENTS
                    typeof(EcsTestManagedComponent)
#endif
                };

                foreach (var type in filteredTypes)
                {
                    var componentType = ComponentType.FromTypeIndex(TypeManager.GetTypeIndex(type));
                    Assert.IsFalse(manager.HasComponent(sectionEntities[0].SectionEntity, componentType));
                }

                Assert.AreEqual(0, manager.GetComponentData<TestMetadata>(sectionEntities[0].SectionEntity).SectionIndex);
                Assert.AreEqual(13, manager.GetComponentData<TestMetadata>(sectionEntities[0].SectionEntity).Value);
                Assert.AreEqual(42, manager.GetComponentData<TestMetadata>(sectionEntities[2].SectionEntity).SectionIndex);
                Assert.AreEqual(100, manager.GetComponentData<TestMetadata>(sectionEntities[2].SectionEntity).Value);

                var hash = EntityScenesPaths.GetSubSceneArtifactHash(SceneGUID, sceneSystem.BuildConfigurationGUID, ImportMode.Synchronous);
                Assert.IsTrue(hash.IsValid);
                AssetDatabaseCompatibility.GetArtifactPaths(hash, out var paths);
                var logPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesConversionLog);
                Assert.NotNull(logPath);
                var log = System.IO.File.ReadAllText(logPath);
                Assert.IsTrue(log.Contains("The component type must contains only blittable/basic data types"));
                Assert.IsFalse(log.Contains("entities in the scene 'TestSubSceneWithSectionMetadata' had no SceneSection and as a result were not serialized at all."));
            }
        }
    }
}
