using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Entities;
using Unity.Entities.Tests;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityEngine.Entities.Tests
{
    class GameObjectConversionTests : ECSTestsFixture
    {
        public static void ConvertSceneAndApplyDiff(Scene scene, World previousStateShadowWorld, World dstEntityWorld)
        {
            using (var cleanConvertedEntityWorld = new World("Clean Entity Conversion World"))
            {
                GameObjectConversionUtility.ConvertScene(scene, cleanConvertedEntityWorld, true);
                WorldDiffer.DiffAndApply(cleanConvertedEntityWorld, previousStateShadowWorld, dstEntityWorld);
            }
        }
        
        [Test]
        public void ConvertGameObject_HasOnlyTransform_ProducesEntityWithPositionAndRotation([Values]bool useDiffing)
        {
            // Prepare scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            SceneManager.SetActiveScene(scene);
            
            var go = new GameObject("Test Conversion");
            go.transform.localPosition = new Vector3(1, 2, 3);
            
            // Convert
            if (useDiffing)
            {
                var shadowWorld = new World("Shadow");
                ConvertSceneAndApplyDiff(scene, shadowWorld, m_Manager.World);
                shadowWorld.Dispose();
            }
            else
            {
                GameObjectConversionUtility.ConvertScene(scene, m_Manager.World);
            }
            
            // Check
            var entities = m_Manager.GetAllEntities();
            Assert.AreEqual(1, entities.Length);
            var entity = entities[0];

            Assert.AreEqual(useDiffing ? 3 : 2, m_Manager.GetComponentCount(entity));
            Assert.IsTrue(m_Manager.HasComponent<Position>(entity));
            Assert.IsTrue(m_Manager.HasComponent<Rotation>(entity));
            if (useDiffing)
                Assert.IsTrue(m_Manager.HasComponent<EntityGuid>(entity));

            Assert.AreEqual(new float3(1, 2, 3), m_Manager.GetComponentData<Position>(entity).Value);
            Assert.AreEqual(quaternion.identity, m_Manager.GetComponentData<Rotation>(entity).Value);
            
            // Unload
            EditorSceneManager.UnloadSceneAsync(scene);
        }
        
        [Test]
        public void ConversionIgnoresMissingMonoBehaviour()
        {
            TestTools.LogAssert.Expect(LogType.Warning, new Regex("missing"));
            var scene = EditorSceneManager.OpenScene("Packages/com.unity.entities/Unity.Entities.Hybrid.Tests/MissingMonoBehaviour.unity");
            var world = new World("Temp");
            GameObjectConversionUtility.ConvertScene(scene, world);
            world.Dispose();
        }
        
        //@TODO: Test Prefabs
        //@TODO: Test GameObject -> Entity ID mapping

    }
}
