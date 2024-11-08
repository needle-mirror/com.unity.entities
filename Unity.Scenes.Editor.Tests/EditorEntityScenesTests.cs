using System.IO;
using NUnit.Framework;
using Unity.Entities.Tests;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEngine;
using Unity.Entities;
using World = Unity.Entities.World;

namespace Unity.Scenes.Tests
{
#if !UNITY_DISABLE_MANAGED_COMPONENTS
    public class EditorEntityScenesTests : ECSTestsFixture
    {
        public class MaterialRefComponent : IComponentData
        {
            public Material Value;
        }

        Material m_TestMaterial;
        public static string s_MaterialAssetPath = "Assets/TestMaterial.asset";

        public void CreateBasicMaterial()
        {
#if UNITY_EDITOR
            m_TestMaterial = new Material(Shader.Find("Transparent/Diffuse"));
            AssetDatabase.CreateAsset(m_TestMaterial, s_MaterialAssetPath);
#endif
        }

        [SetUp]
        public void setup()
        {
            CreateBasicMaterial();
        }

        [TearDown]
        public void Tearddown()
        {
#if UNITY_EDITOR
            AssetDatabase.DeleteAsset(s_MaterialAssetPath);
#endif
        }

        [Test]
        public void TestReadAndWriteWithObjectRef()
        {
            string binPath = "Temp/test.bin";
            string binRefPath = "Temp/test.bin.ref";

            using var dstWorld = new World("");
            var dstEntitymanager = dstWorld.EntityManager;

            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentData(entity, new MaterialRefComponent { Value = m_TestMaterial });
            m_Manager.AddComponentData(entity, new EcsTestData() { value = 5});

            EditorEntityScenes.Write(m_Manager, binPath, binRefPath);
            EditorEntityScenes.Read(dstEntitymanager, binPath, binRefPath);

            var dstEntity = dstEntitymanager.UniversalQuery.GetSingletonEntity();

            Assert.AreEqual(m_TestMaterial, m_Manager.GetComponentData<MaterialRefComponent>(entity).Value);
            Assert.AreEqual(m_TestMaterial, dstEntitymanager.GetComponentData<MaterialRefComponent>(dstEntity).Value);

            Assert.AreEqual(5, m_Manager.GetComponentData<EcsTestData>(entity).value);
            Assert.AreEqual(5, dstEntitymanager.GetComponentData<EcsTestData>(dstEntity).value);
        }
    }
#endif
}
