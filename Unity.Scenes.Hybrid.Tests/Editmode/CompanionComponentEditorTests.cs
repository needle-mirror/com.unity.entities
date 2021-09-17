#if !UNITY_DISABLE_MANAGED_COMPONENTS
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Scenes.Editor;
using Unity.Scenes.Editor.Tests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Unity.Entities.Tests
{
    class MonoBehaviourComponentConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            AddTypeToCompanionWhiteList(typeof(ConversionTestCompanionComponent));
            Entities.ForEach((ConversionTestCompanionComponent component) =>
            {
                var entity = GetPrimaryEntity(component);
                DstEntityManager.AddComponentObject(entity, component);
            });
        }
    }

    [Serializable]
    public class TestWithSceneCameraCulling
    {
        [SerializeField] private GameObject[] GameObjects;
        [SerializeField] private Dictionary<GameObject, bool> GameObjectRenderedMap;
        [SerializeField] private bool SceneCameraRendered;
        [SerializeField] private bool IsSetUp;

        public void SetUp(GameObject[] gameObjects)
        {
            if (IsSetUp)
                return;

            IsSetUp = true;
            GameObjects = gameObjects;
            GameObjectRenderedMap = new Dictionary<GameObject, bool>();
            SceneCameraRendered = false;

            Camera.onPostRender += onPostRender;
            RenderPipelineManager.endFrameRendering += endFrameRendering;
        }

        public void TearDown()
        {
            if (IsSetUp)
            {
                Camera.onPostRender -= onPostRender;
                RenderPipelineManager.endFrameRendering -= endFrameRendering;
                IsSetUp = false;
            }
        }

        public bool IsGameObjectRendered(GameObject gameObject)
        {
            return GameObjectRenderedMap[gameObject];
        }

        public IEnumerator WaitForSceneCameraRender()
        {
            while (!SceneCameraRendered)
                yield return null;
        }

        private void endFrameRendering(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (Camera camera in cameras)
            {
                onPostRender(camera);
            }
        }

        private void onPostRender(Camera cam)
        {
            if (!SceneCameraRendered)
            {
                if (cam.cameraType == CameraType.SceneView && GameObjects.Length > 0)
                {
                    foreach(var gameObject in GameObjects)
                    {
                        GameObjectRenderedMap.Add(gameObject, StageUtility.IsGameObjectRenderedByCamera(gameObject, cam));
                    }

                    SceneCameraRendered = true;
                }
            }
        }

    }

    [Serializable]
    [TestFixture]
    class CompanionComponentsEditorTests
    {
        [SerializeField] public TestWithEditorLiveConversion TestWithEditorLiveConversion;
        [SerializeField] public TestWithSceneCameraCulling TestWithSceneCameraCulling = new TestWithSceneCameraCulling();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!TestWithEditorLiveConversion.OneTimeSetUp())
                return;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            TestWithEditorLiveConversion.OneTimeTearDown();
        }

        [SetUp]
        public void SetUp()
        {
            TestWithEditorLiveConversion.SetUp();
        }

        [TearDown]
        public void TearDown()
        {
            TestWithSceneCameraCulling.TearDown();
        }

        IEditModeTestYieldInstruction GetEnterPlayMode(bool editMode) => TestWithEditorLiveConversion.GetEnterPlayMode(editMode ? TestWithEditorLiveConversion.Mode.Edit : TestWithEditorLiveConversion.Mode.Play);

        //[UnityTest, Ignore("Unable to run graphical tests on CI right now.")]
        [UnityTest]
        public IEnumerator CompanionComponent_SceneCulling([Values]bool editMode, [Values]bool sceneViewShowRuntime)
        {
            SceneView sceneView;
            if (SceneView.sceneViews.Count == 0)
            {
                sceneView = EditorWindow.CreateWindow<SceneView>();
            }
            else
            {
                Assert.AreEqual(1, SceneView.sceneViews.Count, "There should only be 1 Scene View for this test");
                sceneView = (SceneView)SceneView.sceneViews[0];
            }

            SubSceneInspectorUtility.LiveConversionEnabled = true;
            SubSceneInspectorUtility.LiveConversionSceneViewShowRuntime = sceneViewShowRuntime;

            var subScene = TestWithEditorLiveConversion.CreateSubSceneFromObjects("TestSubScene", true, () =>
            {
                var go = new GameObject("TestGameObject");
                go.AddComponent<ConversionTestCompanionComponent>();
                return new List<GameObject> {go};
            });

            yield return GetEnterPlayMode(editMode);

            sceneView.Focus();

            {
                var objs = Resources.FindObjectsOfTypeAll<ConversionTestCompanionComponent>();

                // Why is it 4?
                // 1- Authoring object
                // 2- Companion object
                // 3- Entity Patcher world
                // 4- Entity Patcher shadow world
                Assert.AreEqual(4, objs.Length, "Incorrect number of game objects.");

                var sceneCameras = SceneView.GetAllSceneCameras();
                Assert.AreEqual(1, sceneCameras.Length, "This test should contain one Scene Camera.");

                var expectAuthoringRenderedInSceneView = !sceneViewShowRuntime;
                var expectRuntimeRenderedInSceneView = sceneViewShowRuntime;

                var gameObjects = new GameObject[objs.Length];
                for (int i = 0; i < objs.Length; i++)
                {
                    gameObjects[i] = objs[i].gameObject;
                }
                TestWithSceneCameraCulling.SetUp(gameObjects);

                yield return TestWithSceneCameraCulling.WaitForSceneCameraRender();

                foreach (var obj in objs)
                {
                    var go = obj.gameObject;
                    var goScene = go.scene;

                    var isGameObjectRenderedByCamera = TestWithSceneCameraCulling.IsGameObjectRendered(go);

                    // This is the authoring object
                    if (goScene == subScene.EditingScene)
                    {
                        Assert.AreEqual(expectAuthoringRenderedInSceneView, isGameObjectRenderedByCamera,
                            $"Expect Authoring Object rendered in Scene View={expectAuthoringRenderedInSceneView} but was Rendered={isGameObjectRenderedByCamera}");
                    }
                    else
                    {
                        // This is the CompanionLink object
                        Assert.AreEqual(expectRuntimeRenderedInSceneView, isGameObjectRenderedByCamera,
                            $"Expect Runtime Object rendered in Scene View={expectRuntimeRenderedInSceneView} but was Rendered={isGameObjectRenderedByCamera}");
                    }
                }
            }
        }
    }
}
#endif
