using NUnit.Framework;
using Unity.Build;
using Unity.Entities.Build;
using Unity.Entities.Build.Editor;
using Unity.Entities.Conversion;
using Unity.Scenes.Editor.Tests;
using UnityEngine;
using Unity.Entities.Hybrid.Tests.Baking.SeparateAssembly;

namespace Unity.Entities.Hybrid.Tests.Baking
{
    internal class BakingAssemblyTests : BakingSystemFixtureBase
    {
        private BakingSystem m_BakingSystem;
        private TestLiveConversionSettings m_Settings;

        [SetUp]
        public override void Setup()
        {
            m_Settings.Setup(true);
            base.Setup();

            m_BakingSystem = World.GetOrCreateSystemManaged<BakingSystem>();
            var settings = MakeDefaultSettings();
            m_BakingSystem.PrepareForBaking(settings, default);

            m_Manager = World.EntityManager;
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            m_BakingSystem = null;
            m_Settings.TearDown();
        }

        [Test]
        public void BakingExcludeAssemblyTests()
        {
            m_BakingSystem = World.GetOrCreateSystemManaged<BakingSystem>();
            var manager = World.EntityManager;

            // Create the gameobject with the authoring component
            var go = CreateGameObject();
            go.AddComponent<ComponentInAssemblyAuthoring>();

            // Create the a setting with a filter and another one without a filter
            var normalSettings = MakeDefaultSettings();
            var exludeSettings = MakeDefaultSettings();
            var config = BuildConfiguration.CreateInstance((bs) =>
            {
                bs.hideFlags = HideFlags.HideAndDontSave;
                bs.SetComponent(new ConversionSystemFilterSettings("Unity.Entities.Hybrid.Tests.SeparateAssembly"));
            });
            exludeSettings.BuildConfiguration = config;

            // Bake the gameobject
            BakingUtility.BakeGameObjects(World, new[] {go}, normalSettings);

            // With regular baking it is expected that the baker runs and the component is added
            var entity = m_BakingSystem.GetEntity(go);
            Assert.AreEqual(true, manager.HasComponent<ComponentInAssemblyComponent>(entity));

            // With the filter we expect that the baker hasn't run and the component is not in the entity
            BakingUtility.BakeGameObjects(World, new[] {go}, exludeSettings);
            entity = m_BakingSystem.GetEntity(go);
            Assert.AreEqual(false, manager.HasComponent<ComponentInAssemblyComponent>(entity));

            // We repeat the same two checks to make sure nothing is left that affects this
            BakingUtility.BakeGameObjects(World, new[] {go}, normalSettings);
            entity = m_BakingSystem.GetEntity(go);
            Assert.AreEqual(true, manager.HasComponent<ComponentInAssemblyComponent>(entity));

            BakingUtility.BakeGameObjects(World, new[] {go}, exludeSettings);
            entity = m_BakingSystem.GetEntity(go);
            Assert.AreEqual(false, manager.HasComponent<ComponentInAssemblyComponent>(entity));
        }

        [Test]
        public void BakingExcludeAssemblyTests_BuiltInBuildsPath()
        {
            m_BakingSystem = World.GetOrCreateSystemManaged<BakingSystem>();
            var manager = World.EntityManager;

            // Create the gameobject with the authoring component
            var go = CreateGameObject();
            go.AddComponent<ComponentInAssemblyAuthoring>();

            // Create the a setting with a filter and another one without a filter
            var normalSettings = MakeDefaultSettings();
            var excludeSettings = MakeDefaultSettings();
            var playerSettings = (EntitiesClientSettings)DotsGlobalSettings.Instance.GetClientSettingAsset();
            playerSettings.FilterSettings = new BakingSystemFilterSettings("Unity.Entities.Hybrid.Tests.SeparateAssembly");
            excludeSettings.DotsSettings = playerSettings;
            normalSettings.DotsSettings = playerSettings;
            excludeSettings.IsBuiltInBuildsEnabled = true;

            // Bake the gameobject
            BakingUtility.BakeGameObjects(World, new[] {go}, normalSettings);

            // With regular baking it is expected that the baker runs and the component is added
            var entity = m_BakingSystem.GetEntity(go);
            Assert.AreEqual(true, manager.HasComponent<ComponentInAssemblyComponent>(entity));

            // With the filter we expect that the baker hasn't run and the component is not in the entity
            BakingUtility.BakeGameObjects(World, new[] {go}, excludeSettings);
            entity = m_BakingSystem.GetEntity(go);
            Assert.AreEqual(false, manager.HasComponent<ComponentInAssemblyComponent>(entity));

            // We repeat the same two checks to make sure nothing is left that affects this
            BakingUtility.BakeGameObjects(World, new[] {go}, normalSettings);
            entity = m_BakingSystem.GetEntity(go);
            Assert.AreEqual(true, manager.HasComponent<ComponentInAssemblyComponent>(entity));

            BakingUtility.BakeGameObjects(World, new[] {go}, excludeSettings);
            entity = m_BakingSystem.GetEntity(go);
            Assert.AreEqual(false, manager.HasComponent<ComponentInAssemblyComponent>(entity));
        }
    }
}
