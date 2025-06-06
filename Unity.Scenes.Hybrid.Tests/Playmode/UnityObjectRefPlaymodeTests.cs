using NUnit.Framework;
using System.Collections;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Scenes.Hybrid.Tests
{
    public class UnityObjectRefPlaymodeTests : SubSceneTestFixture
    {
        public UnityObjectRefPlaymodeTests()
        {
            PlayModeScenePath = @"Packages/com.unity.entities/Unity.Scenes.Hybrid.Tests/TestSceneWithSubSceneUnityObjectRef/SubSceneUnityObjectRef.unity";
            BuildScenePath = @"Packages/com.unity.entities/Unity.Scenes.Hybrid.Tests/TestSceneWithSubSceneUnityObjectRef/TestSceneWithSubSceneUnityObjectRef.unity";
            BuildSceneGUID = new Unity.Entities.Hash128("51692308fedb8d7428b3beb091f12d89");
        }

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            base.SetUpOnce();
        }

        [OneTimeTearDown]
        public void OneTimeTeardown()
        {
            base.TearDownOnce();
        }

        public struct UnityObjectRefWithComponent : IComponentData
        {
            public UnityObjectRef<ReferencedComponent> UnityObjectRef;
        }

        [UnityTest]
        public IEnumerator SerializeObjectReferences_Includes_Components()
        {
#if !UNITY_EDITOR
            using var worldA = CreateEntityWorld("World A");

            Assert.IsTrue(BuildSceneGUID.IsValid);
            var initialContentFilesNumber = Loading.ContentLoadInterface.GetContentFiles(Unity.Entities.Content.RuntimeContentManager.Namespace).Length;

            // Load Scene asynchronously and wait until it has completed loading
            var worldAScene = SceneSystem.LoadSceneAsync(worldA.Unmanaged, BuildSceneGUID);
            Assert.IsFalse(SceneSystem.IsSceneLoaded(worldA.Unmanaged, worldAScene));

            while (!SceneSystem.IsSceneLoaded(worldA.Unmanaged, worldAScene))
            {
                worldA.Update();
                yield return null;
            }

            //Check that the Component is there and has the correct value
            var query = worldA.EntityManager.CreateEntityQuery(typeof(UnityObjectRefWithComponent));
            Assert.AreEqual(1, query.CalculateEntityCount());

            var entities = query.ToEntityArray(Allocator.Temp);
            var objectRefData = worldA.EntityManager.GetComponentData<UnityObjectRefWithComponent>(entities[0]);
            Assert.AreEqual(1, objectRefData.UnityObjectRef.Value.Field);

            SceneSystem.UnloadScene(worldA.Unmanaged, worldAScene);
            worldA.Update();
#endif
            yield return null;
        }

    }
}
