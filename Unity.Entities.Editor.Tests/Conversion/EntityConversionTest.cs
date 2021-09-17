using NUnit.Framework;
using System.IO;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Editor.Tests
{
    class EntityConversionTest
    {
        Scene m_MainScene;
        GameObject m_GrandParent;
        GameObject m_Parent;
        GameObject m_Child;
        string m_TempAssetDir;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var guid = AssetDatabase.CreateFolder("Assets", Path.GetRandomFileName());
            m_TempAssetDir = AssetDatabase.GUIDToAssetPath(guid);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            AssetDatabase.DeleteAsset(m_TempAssetDir);
        }

        [SetUp]
        public void SetUp()
        {
            m_MainScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            EditorSceneManager.SetActiveScene(m_MainScene);

            var mainScenePath = Path.Combine(m_TempAssetDir, "test.unity");
            EditorSceneManager.SaveScene(m_MainScene, mainScenePath);

            m_GrandParent = new GameObject("GrandParent");
            m_Parent = new GameObject("Parent");
            m_Child = new GameObject("Child");

            m_Parent.transform.parent = m_GrandParent.transform;
            m_Child.transform.parent = m_Parent.transform;

            Selection.activeObject = m_GrandParent;
        }

        [TearDown]
        public void TearDown()
        {
            GameObject.DestroyImmediate(m_GrandParent);
            GameObject.DestroyImmediate(m_Parent);
            GameObject.DestroyImmediate(m_Child);
        }

        [Test]
        public void Should_Convert_Under_Subscene()
        {
            SubSceneContextMenu.CreateSubSceneAndAddSelection(m_GrandParent);

            var conversionStatusGrandParent = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_GrandParent);
            var conversionStatusParent = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_Parent);
            var conversionStatusChild = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_Child);

            Assert.That(conversionStatusGrandParent, Is.EqualTo(GameObjectConversionResultStatus.ConvertedBySubScene));
            Assert.That(conversionStatusParent, Is.EqualTo(GameObjectConversionResultStatus.ConvertedBySubScene));
            Assert.That(conversionStatusChild, Is.EqualTo(GameObjectConversionResultStatus.ConvertedBySubScene));
        }

        [Test]
        public void Should_Not_Convert_With_ConvertAndInject()
        {
            m_GrandParent.AddComponent<ConvertToEntity>().ConversionMode = ConvertToEntity.Mode.ConvertAndInjectGameObject;

            Assert.IsTrue(EntityConversionUtility.IsGameObjectConverted(m_GrandParent));
            Assert.IsFalse(EntityConversionUtility.IsGameObjectConverted(m_Parent));
            Assert.IsFalse(EntityConversionUtility.IsGameObjectConverted(m_Child));

            var conversionStatusGrandParent = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_GrandParent);
            var conversionStatusParent = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_Parent);
            var conversionStatusChild = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_Child);

            Assert.That(conversionStatusGrandParent, Is.EqualTo(GameObjectConversionResultStatus.ConvertedByConvertToEntity));
            Assert.That(conversionStatusParent, Is.EqualTo(GameObjectConversionResultStatus.NotConvertedByConvertAndInjectMode));
            Assert.That(conversionStatusChild, Is.EqualTo(GameObjectConversionResultStatus.NotConvertedByConvertAndInjectMode));
        }

        [Test]
        public void Should_Not_Convert_With_StopConvertToEntity()
        {
            m_GrandParent.AddComponent<StopConvertToEntity>();

            Assert.IsFalse(EntityConversionUtility.IsGameObjectConverted(m_GrandParent));
            Assert.IsFalse(EntityConversionUtility.IsGameObjectConverted(m_Parent));
            Assert.IsFalse(EntityConversionUtility.IsGameObjectConverted(m_Child));

            var conversionStatusGrandParent = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_GrandParent);
            var conversionStatusParent = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_Parent);
            var conversionStatusChild = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_Child);

            Assert.That(conversionStatusGrandParent, Is.EqualTo(GameObjectConversionResultStatus.NotConvertedByStopConvertToEntityComponent));
            Assert.That(conversionStatusParent, Is.EqualTo(GameObjectConversionResultStatus.NotConvertedByStopConvertToEntityComponent));
            Assert.That(conversionStatusChild, Is.EqualTo(GameObjectConversionResultStatus.NotConvertedByStopConvertToEntityComponent));
        }

        [Test]
        public void Should_Convert_By_Ancestor()
        {
            m_GrandParent.AddComponent<ConvertToEntity>().ConversionMode = ConvertToEntity.Mode.ConvertAndDestroy;

            Assert.IsTrue(EntityConversionUtility.IsGameObjectConverted(m_GrandParent));
            Assert.IsTrue(EntityConversionUtility.IsGameObjectConverted(m_Parent));
            Assert.IsTrue(EntityConversionUtility.IsGameObjectConverted(m_Child));

            var conversionStatusGrandParent = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_GrandParent);
            var conversionStatusParent = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_Parent);
            var conversionStatusChild = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(m_Child);

            Assert.That(conversionStatusGrandParent, Is.EqualTo(GameObjectConversionResultStatus.ConvertedByConvertToEntity));
            Assert.That(conversionStatusParent, Is.EqualTo(GameObjectConversionResultStatus.ConvertedByAncestor));
            Assert.That(conversionStatusChild, Is.EqualTo(GameObjectConversionResultStatus.ConvertedByAncestor));
        }
    }
}
