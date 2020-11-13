using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Hybrid.Tests;
using Unity.Entities.Tests;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Unity.Scenes.Editor.Tests
{
    /*
     * These tests provide some coverage for LiveLink in the editor. LiveLink, by default, is used in edit mode and in
     * play mode whenever there is an open subscene. Its contents are converted to entities in the background, that is
     * the essential feature of LiveLink.
     *
     * The setup here is as follows:
     *  - all subscenes are created in a new temporary directory per test,
     *  - that directory is cleaned up when the test finished,
     *  - we also flush the entity scene paths cache to get rid of any subscene build files,
     *  - we clearly separate all tests into setup and test, because the latter might run in play mode.
     * That last point is crucial: Entering playmode serializes the test fixture, but not the contents of variables
     * within the coroutine that represents a test. This means that you cannot rely on the values of any variables and
     * you can get very nasty exceptions by assigning a variable from setup in play mode (due to the way enumerator
     * functions are compiled). Any data that needs to persist between edit and play mode must be stored on the class
     * itself.
     */
    [Serializable]
    [TestFixture]
    class LiveLinkEditorTests
    {
        [SerializeField] TestWithTempAssets m_Assets;
        [SerializeField] TestWithCustomDefaultGameObjectInjectionWorld m_DefaultWorld;
        [SerializeField] TestWithSubScenes m_SubSceneTest;
        [SerializeField] TestWithLiveConversion m_LiveConversionTest;

        [SerializeField] EnterPlayModeOptions m_EnterPlayModeOptions;
        [SerializeField] bool m_UseEnterPlayerModeOptions;
        [SerializeField] string m_PrefabPath;
        [SerializeField] Material m_TestMaterial;
        [SerializeField] Texture m_TestTexture;


        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (m_Assets.TempAssetDir != null)
            {
                // this setup code is run again when we switch to playmode
                return;
            }

            // Create a temporary folder for test assets
            m_Assets.SetUp();
            m_DefaultWorld.Setup();
            m_SubSceneTest.Setup();
            m_LiveConversionTest.Setup();
            m_EnterPlayModeOptions = EditorSettings.enterPlayModeOptions;
            m_UseEnterPlayerModeOptions = EditorSettings.enterPlayModeOptionsEnabled;

            m_TestTexture = AssetDatabase.LoadAssetAtPath<Texture>(AssetPath("TestTexture.asset"));
            m_TestMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetPath("TestMaterial.mat"));

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // Clean up all test assets
            m_Assets.TearDown();
            m_DefaultWorld.TearDown();
            m_SubSceneTest.TearDown();
            m_LiveConversionTest.TearDown();
            EditorSettings.enterPlayModeOptions = m_EnterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = m_UseEnterPlayerModeOptions;
        }

        [SetUp]
        public void SetUp()
        {
            if (EditorApplication.isPlaying)
                return;
            World.DefaultGameObjectInjectionWorld?.Dispose();
            World.DefaultGameObjectInjectionWorld = null;
        }

        static string AssetPath(string name) => "Packages/com.unity.entities/Unity.Scenes.Editor.Tests/Assets/" + name;
        static string ScenePath(string name) => AssetPath(name) + ".unity";

        static void OpenAllSubScenes() => SubSceneInspectorUtility.EditScene(SubScene.AllSubScenes.ToArray());

        Scene CreateTmpScene() => SubSceneTestsHelper.CreateScene(m_Assets.GetNextPath() + ".unity");

        SubScene CreateSubSceneFromObjects(string name, bool keepOpen, Func<List<GameObject>> createObjects) =>
            SubSceneTestsHelper.CreateSubSceneInSceneFromObjects(name, keepOpen, CreateTmpScene(), createObjects);

        SubScene CreateEmptySubScene(string name, bool keepOpen) => CreateSubSceneFromObjects(name, keepOpen, null);

        static World GetLiveLinkWorld(Mode playmode, bool removeWorldFromPlayerLoop = true)
        {
            if (playmode == Mode.Edit)
                DefaultWorldInitialization.DefaultLazyEditModeInitialize();

            var world = World.DefaultGameObjectInjectionWorld;
            if (removeWorldFromPlayerLoop)
            {
                // This should be a fresh world, but make sure that it is not part of the player loop so we have manual
                // control on its updates.
                ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
            }

            return world;
        }

        static IEditModeTestYieldInstruction GetEnterPlayMode(Mode playmode)
        {
            if (playmode == Mode.Play)
                return new EnterPlayMode();
            // ensure that the editor world is initialized
            var world = GetLiveLinkWorld(playmode);
            world.Update();
            return null;
        }

        static IEnumerator UpdateEditorAndWorld(World w)
        {
            yield return null;
            w.Update();
        }

        public enum Mode
        {
            Play,
            Edit
        }

        [UnityTest]
        public IEnumerator OpenSubScene_StaysOpen_WhenEnteringPlayMode()
        {
            {
                CreateEmptySubScene("TestSubScene", true);
            }

            yield return GetEnterPlayMode(Mode.Play);

            {
                var subScene = Object.FindObjectOfType<SubScene>();
                Assert.IsTrue(subScene.IsLoaded);
            }
        }

        [UnityTest]
        public IEnumerator ClosedSubScene_StaysClosed_WhenEnteringPlayMode()
        {
            {
                CreateEmptySubScene("TestSubScene", false);
            }

            yield return GetEnterPlayMode(Mode.Play);

            {
                var subScene = Object.FindObjectOfType<SubScene>();
                Assert.IsFalse(subScene.IsLoaded);
            }
        }

        [UnityTest]
        public IEnumerator ClosedSubScene_CanBeOpened_InPlayMode()
        {
            {
                CreateEmptySubScene("TestSubScene", false);
            }

            yield return GetEnterPlayMode(Mode.Play);

            {
                var subScene = Object.FindObjectOfType<SubScene>();
                Assert.IsFalse(subScene.IsLoaded);
                SubSceneInspectorUtility.EditScene(subScene);
                yield return null;
                Assert.IsTrue(subScene.IsLoaded);
            }
        }

        [UnityTest]
        public IEnumerator LiveLink_ConvertsSubScenes([Values]Mode mode)
        {
            {
                var scene = CreateTmpScene();
                SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("TestSubScene1", true, scene, () =>
                {
                    var go = new GameObject("TestGameObject1");
                    go.AddComponent<TestComponentAuthoring>().IntValue = 1;
                    return new List<GameObject> {go};
                });
                SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("TestSubScene2", true, scene, () =>
                {
                    var go = new GameObject("TestGameObject2");
                    go.AddComponent<TestComponentAuthoring>().IntValue = 2;
                    return new List<GameObject> {go};
                });
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var subSceneQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<SubScene>());
                var subScenes = subSceneQuery.ToComponentArray<SubScene>();
                var subSceneObjects = Object.FindObjectsOfType<SubScene>();
                foreach (var subScene in subSceneObjects)
                    Assert.Contains(subScene, subScenes);

                var componentQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());

                Assert.AreEqual(2, componentQuery.CalculateEntityCount(), "Expected a game object to be converted");
                using (var components =
                    componentQuery.ToComponentDataArray<TestComponentAuthoring.UnmanagedTestComponent>(
                        Allocator.TempJob))
                {
                    Assert.IsTrue(components.Contains(new TestComponentAuthoring.UnmanagedTestComponent {IntValue = 1}),
                        "Failed to find contents of subscene 1");
                    Assert.IsTrue(components.Contains(new TestComponentAuthoring.UnmanagedTestComponent {IntValue = 2}),
                        "Failed to find contents of subscene 2");
                }
            }
        }

        [UnityTest]
        public IEnumerator LiveLink_RemovesDeletedSubScene([Values]Mode mode)
        {
            {
                var scene = CreateTmpScene();
                SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("TestSubScene1", true, scene, () =>
                {
                    var go = new GameObject("TestGameObject");
                    go.AddComponent<TestComponentAuthoring>().IntValue = 1;
                    return new List<GameObject> {go};
                });
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var subScene = Object.FindObjectOfType<SubScene>();
                var subSceneQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<SubScene>());
                Assert.Contains(subScene, subSceneQuery.ToComponentArray<SubScene>(), "SubScene was not loaded");

                var componentQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, componentQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(1,
                    componentQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);

                Object.DestroyImmediate(subScene.gameObject);

                yield return UpdateEditorAndWorld(w);

                Assert.IsTrue(subSceneQuery.IsEmptyIgnoreFilter, "SubScene was not unloaded");
                Assert.AreEqual(0, componentQuery.CalculateEntityCount());
            }
        }

        [UnityTest]
        public IEnumerator LiveLink_ConvertsObjects([Values]Mode mode)
        {
            {
                CreateSubSceneFromObjects("TestSubScene", true, () =>
                {
                    var go = new GameObject("TestGameObject");
                    go.AddComponent<TestComponentAuthoring>();
                    return new List<GameObject> {go};
                });
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
            }
        }

        public static IEnumerable TestCases
        {
            get
            {
                {
                    var tc = new TestCaseData(Mode.Edit) {HasExpectedResult = true};
                    yield return tc;
                }

                {
#if UNITY_2020_2_OR_NEWER
                    var tc = new TestCaseData(Mode.Play);
#else
                    var tc = new TestCaseData(Mode.Play).Ignore("Doesn't currently work, since Undo.RegisterCreatedObjectUndo isn't reliably picked up by Undo.postprocessModifications and Scenes are never marked dirty in play mode. A reconversion is never triggered.");
#endif
                    tc.HasExpectedResult = true;
                    yield return tc;
                }
            }
        }

        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(TestCases))]
        public IEnumerator LiveLink_CreatesEntities_WhenObjectIsCreated(Mode mode)
        {
            {
                CreateEmptySubScene("TestSubScene", true);
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var subScene = Object.FindObjectOfType<SubScene>();
                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(0, testTagQuery.CalculateEntityCount());

                SceneManager.SetActiveScene(subScene.EditingScene);
                var go = new GameObject("Parent", typeof(TestComponentAuthoring));
                var subGo = new GameObject("Child", typeof(TestComponentAuthoring));
                subGo.transform.parent = go.transform;
                Undo.RegisterCreatedObjectUndo(go, "Create new object");
                Assert.AreEqual(go.scene, subScene.EditingScene);

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(2, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(),
                    "Expected an entity to be removed, undo failed");
                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(2, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted, redo failed");
            }
        }

        [UnityTest]
        public IEnumerator LiveLink_DisableLiveLinkComponentWorks([Values]Mode mode)
        {
            {
                CreateSubSceneFromObjects("TestSubScene", true, () =>
                {
                    var go = new GameObject("TestGameObject");
                    var authoringComponent = go.AddComponent<TestComponentAuthoring>();
                    authoringComponent.IntValue = 42;
                    return new List<GameObject> {go};
                });
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);
                var manager = w.EntityManager;

                var sceneSystem = w.GetExistingSystem<SceneSystem>();
                var subScene = Object.FindObjectOfType<SubScene>();

                var sceneEntity = sceneSystem.GetSceneEntity(subScene.SceneGUID);
                var sectionEntity = manager.GetBuffer<ResolvedSectionEntity>(sceneEntity)[0].SectionEntity;

                Assert.AreNotEqual(Entity.Null, sceneEntity);

                var sceneInstance = sceneSystem.LoadSceneAsync(subScene.SceneGUID,
                    new SceneSystem.LoadParameters
                    {
                        Flags = SceneLoadFlags.NewInstance | SceneLoadFlags.BlockOnStreamIn |
                                SceneLoadFlags.BlockOnImport
                    });

                yield return UpdateEditorAndWorld(w);

                var sectionInstance = manager.GetBuffer<ResolvedSectionEntity>(sceneInstance)[0].SectionEntity;

                var sceneQuery = w.EntityManager.CreateEntityQuery(
                    ComponentType.ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>(),
                    ComponentType.ReadWrite<SceneTag>());
                sceneQuery.SetSharedComponentFilter(new SceneTag {SceneEntity = sectionEntity});

                var instanceQuery = w.EntityManager.CreateEntityQuery(
                    ComponentType.ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>(),
                    ComponentType.ReadWrite<SceneTag>());
                instanceQuery.SetSharedComponentFilter(new SceneTag {SceneEntity = sectionInstance});

                Assert.AreEqual(42, sceneQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);
                Assert.AreEqual(42,
                    instanceQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);

                manager.AddComponent<DisableLiveLink>(sceneInstance);

                var authoring = Object.FindObjectOfType<TestComponentAuthoring>();

                Undo.RecordObject(authoring, "Change component value");
                authoring.IntValue = 117;
                Undo.FlushUndoRecordObjects();

                Undo.IncrementCurrentGroup();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(117, sceneQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);
                Assert.AreEqual(42,
                    instanceQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);

                w.EntityManager.DestroyEntity(sceneInstance);
                yield return UpdateEditorAndWorld(w);
            }
        }

        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(TestCases))]
        public IEnumerator LiveLink_CreatesEntities_WhenObjectMovesBetweenScenes(Mode mode)
        {
            {
                CreateEmptySubScene("TestSubScene", true);
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var subScene = Object.FindObjectOfType<SubScene>();
                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(0, testTagQuery.CalculateEntityCount());

                var go = new GameObject("TestGameObject");
                go.AddComponent<TestComponentAuthoring>();
                Undo.MoveGameObjectToScene(go, subScene.EditingScene, "Test Move1");

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(),
                    "Expected an entity to be removed, undo failed");
                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted, redo failed");
            }
        }

        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(TestCases))]
        public IEnumerator LiveLink_CreatesAndDestroyEntities_WhenObjectMovesBetweenSubScenes_FromAToB(Mode mode) =>
            LiveLink_CreatesAndDestroyEntities_WhenObjectMovesBetweenSubScenes(mode, true);

        [UnityTest,TestCaseSource(typeof(LiveLinkEditorTests), nameof(TestCases))]
        public IEnumerator LiveLink_CreatesAndDestroyEntities_WhenObjectMovesBetweenSubScenes_FromBToA(Mode mode) =>
            LiveLink_CreatesAndDestroyEntities_WhenObjectMovesBetweenSubScenes(mode, false);

        IEnumerator LiveLink_CreatesAndDestroyEntities_WhenObjectMovesBetweenSubScenes(Mode mode, bool forward)
        {
            var scene = CreateTmpScene();
            var goA = new GameObject("TestGameObjectA");
            var goB = new GameObject("TestGameObjectB");
            goA.AddComponent<TestComponentAuthoring>().IntValue = 1;
            goB.AddComponent<TestComponentAuthoring>().IntValue = 2;

            var subSceneA = SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("TestSubSceneA", true, scene, () => new List<GameObject> { goA });
            var subSceneB = SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("TestSubSceneB", true, scene, () => new List<GameObject> { goB });

            var w = GetLiveLinkWorld(mode);
            yield return UpdateEditorAndWorld(w);

            using (var q = w.EntityManager.CreateEntityQuery(typeof(SceneTag), typeof(EntityGuid)))
            using (var entities = q.ToEntityArray(Allocator.TempJob))
            {
                Assert.That(entities.Length, Is.EqualTo(2));
                Assert.That(w.EntityManager.GetSharedComponentData<SceneTag>(entities[0]).SceneEntity, Is.Not.EqualTo(w.EntityManager.GetSharedComponentData<SceneTag>(entities[1]).SceneEntity));
            }

            Undo.MoveGameObjectToScene(goA, subSceneB.EditingScene, "Move from A to B");

            yield return UpdateEditorAndWorld(w);

            using (var q = w.EntityManager.CreateEntityQuery(typeof(SceneTag), typeof(EntityGuid)))
            using (var entities = q.ToEntityArray(Allocator.TempJob))
            {
                Assert.That(entities.Length, Is.EqualTo(2));
                Assert.That(w.EntityManager.GetSharedComponentData<SceneTag>(entities[0]).SceneEntity, Is.EqualTo(w.EntityManager.GetSharedComponentData<SceneTag>(entities[1]).SceneEntity));
            }
        }

#if UNITY_2020_2_OR_NEWER
        // This test documents a fix for DOTS-3020, but the failure only occurs when the world is part of the player
        // loop. This also means that this test will fail when the editor does not have focus, which is why it is
        // disabled by default.
        [Ignore("Needs DOTS-3216 to work reliably on CI.")]
        [Explicit]
        [UnityTest]
        [Repeat(10)]
        public IEnumerator LiveLink_DestroysAndCreatesEntities_WhenClosingThenOpeningSubScene([Values(1,2,3,4,5,6,7,8,9,10)]int framesBetweenSteps)
        {
            if (!Application.isFocused)
                throw new Exception("This test can only run when the editor has focus. The test needs the player loop to be updated and this does not happen when the editor is not in focus.");
            var scene = CreateTmpScene();
            var go = new GameObject("TestGameObjectA");
            go.AddComponent<TestComponentAuthoring>().IntValue = 1;
            var subScene = SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("TestSubSceneA", true, scene, () => new List<GameObject> { go });

            // This error only happens when the editor itself triggers an update via the player loop.
            var w = GetLiveLinkWorld(Mode.Edit, false);
            for (var i = 0; i < framesBetweenSteps; i++)
            {
                yield return null;
            }

            var openedSceneEntityCount = w.EntityManager.UniversalQuery.CalculateEntityCount();
            SubSceneInspectorUtility.CloseSceneWithoutSaving(subScene);

            for (var i = 0; i < framesBetweenSteps; i++)
            {
                yield return null;
            }

            var closedSceneEntityCount = w.EntityManager.UniversalQuery.CalculateEntityCount();
            Assert.That(closedSceneEntityCount, Is.Not.EqualTo(openedSceneEntityCount));

            Assert.That(SubSceneInspectorUtility.CanEditScene(subScene), Is.True);
            SubSceneInspectorUtility.EditScene(subScene);

            for (var i = 0; i < framesBetweenSteps; i++)
            {
                yield return null;
            }

            var reopenedSceneEntityCount = w.EntityManager.UniversalQuery.CalculateEntityCount();
            Assert.That(reopenedSceneEntityCount, Is.EqualTo(openedSceneEntityCount));
        }
#endif

        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(TestCases))]
        public IEnumerator LiveLink_DestroysEntities_WhenObjectMovesScenes(Mode mode)
        {
            var mainScene = CreateTmpScene();

            {
                SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("TestSubScene", true, mainScene, () =>
                {
                    var go = new GameObject("TestGameObject");
                    go.AddComponent<TestComponentAuthoring>();
                    return new List<GameObject> {go};
                });
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");

                var go = Object.FindObjectOfType<TestComponentAuthoring>().gameObject;
                var scene = SceneManager.GetActiveScene();
                Undo.MoveGameObjectToScene(go, scene, "Move out of subscene");

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(), "Expected an entity to be removed");
                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted, undo failed");
                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(),
                    "Expected an entity to be removed, redo failed");
            }
        }

        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(TestCases))]
        public IEnumerator LiveLink_DestroysEntities_WhenObjectIsDestroyed(Mode mode)
        {
            {
                CreateSubSceneFromObjects("TestSubScene", true, () =>
                {
                    var go = new GameObject("TestGameObject");
                    go.AddComponent<TestComponentAuthoring>();
                    var childGo = new GameObject("Child");
                    childGo.transform.SetParent(go.transform);
                    childGo.AddComponent<TestComponentAuthoring>();
                    return new List<GameObject> {go};
                });
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);
                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(2, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");

                var go = Object.FindObjectOfType<TestComponentAuthoring>().gameObject;
                if (go.transform.parent != null)
                    go = go.transform.parent.gameObject;
                Undo.DestroyObjectImmediate(go);

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(), "Expected an entity to be removed");
                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(2, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted, undo failed");
                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(),
                    "Expected an entity to be removed, redo failed");
            }
        }

        [UnityTest]
        public IEnumerator LiveLink_DestroysEntities_WhenSubSceneBehaviourIsDisabled([Values]Mode mode)
        {
            {
                CreateSubSceneFromObjects("TestSubScene", true, () =>
                {
                    var go = new GameObject("TestGameObject");
                    go.AddComponent<TestComponentAuthoring>();
                    return new List<GameObject> {go};
                });
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);
                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");

                var subscene = Object.FindObjectOfType<SubScene>();

                subscene.enabled = false;
                yield return UpdateEditorAndWorld(w);
                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(), "Expected an entity to be removed");

                subscene.enabled = true;
                yield return UpdateEditorAndWorld(w);
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
            }
        }

        [UnityTest]
        public IEnumerator LiveLink_DestroysEntities_WhenSubSceneObjectIsDisabled([Values]Mode mode)
        {
            {
                CreateSubSceneFromObjects("TestSubScene", true, () =>
                {
                    var go = new GameObject("TestGameObject");
                    go.AddComponent<TestComponentAuthoring>();
                    return new List<GameObject> {go};
                });
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);
                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");

                var subscene = Object.FindObjectOfType<SubScene>();

                subscene.gameObject.SetActive(false);
                yield return UpdateEditorAndWorld(w);
                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(), "Expected an entity to be removed");

                subscene.gameObject.SetActive(true);
                yield return UpdateEditorAndWorld(w);
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted, undo failed");
            }
        }

        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(TestCases))]
        public IEnumerator LiveLink_SupportsAddComponentAndUndo(Mode mode)
        {
            {
                CreateEmptySubScene("TestSubScene", true);
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var subScene = Object.FindObjectOfType<SubScene>();
                var go = new GameObject("TestGameObject");
                Undo.MoveGameObjectToScene(go, subScene.EditingScene, "Test Move");
                Undo.IncrementCurrentGroup();

                yield return UpdateEditorAndWorld(w);

                Undo.AddComponent<TestComponentAuthoring>(go);
                Undo.IncrementCurrentGroup();
                Assert.IsNotNull(go.GetComponent<TestComponentAuthoring>());

                yield return UpdateEditorAndWorld(w);

                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted and gain a component");

                Undo.PerformUndo();
                Assert.IsNull(go.GetComponent<TestComponentAuthoring>());

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted and lose a component, undo add failed");

                Undo.PerformRedo();
                Assert.IsNotNull(go.GetComponent<TestComponentAuthoring>());

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted and gain a component, redo add failed");
            }
        }


        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(TestCases))]
        public IEnumerator LiveLink_SupportsRemoveComponentAndUndo(Mode mode)
        {
            {
                CreateEmptySubScene("TestSubScene", true);
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var subScene = Object.FindObjectOfType<SubScene>();
                var go = new GameObject("TestGameObject");
                go.AddComponent<TestComponentAuthoring>();
                Undo.MoveGameObjectToScene(go, subScene.EditingScene, "Test Move");
                Undo.IncrementCurrentGroup();

                yield return UpdateEditorAndWorld(w);

                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted with a component");

                Undo.DestroyObjectImmediate(go.GetComponent<TestComponentAuthoring>());
                Undo.IncrementCurrentGroup();

                Assert.IsNull(go.GetComponent<TestComponentAuthoring>());

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted and lose a component");

                Undo.PerformUndo();
                Assert.IsNotNull(go.GetComponent<TestComponentAuthoring>());

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted and gain a component, undo remove failed");

                Undo.PerformRedo();
                Assert.IsNull(go.GetComponent<TestComponentAuthoring>());

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(0, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted and lose a component, redo remove failed");
            }
        }

        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(TestCases))]
        public IEnumerator LiveLink_ReflectsChangedComponentValues(Mode mode)
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);

                var go = new GameObject("TestGameObject");
                var authoring = go.AddComponent<TestComponentAuthoring>();
                authoring.IntValue = 15;
                SceneManager.MoveGameObjectToScene(go, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(mode);
            {
                var w = GetLiveLinkWorld(mode);

                var authoring = Object.FindObjectOfType<TestComponentAuthoring>();
                Assert.AreEqual(authoring.IntValue, 15);

                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(15,
                    testTagQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);

                Undo.RecordObject(authoring, "Change component value");
                authoring.IntValue = 2;

                // it takes an extra frame to establish that something has changed when using RecordObject unless Flush is called
                Undo.FlushUndoRecordObjects();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(2, testTagQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue,
                    "Expected a component value to change");

                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(15, testTagQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue,
                    "Expected a component value to change, undo failed");

                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(2, testTagQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue,
                    "Expected a component value to change, redo failed");
            }
        }

        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(TestCases))]
        public IEnumerator LiveLink_DisablesEntity_WhenGameObjectIsDisabled(Mode mode)
        {
            {
                CreateSubSceneFromObjects("TestSubScene", true, () =>
                {
                    var go = new GameObject("TestGameObject");
                    go.AddComponent<TestComponentAuthoring>();
                    var child = new GameObject("Child");
                    child.transform.SetParent(go.transform);
                    return new List<GameObject> {go};
                });
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var queryWithoutDisabled =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, queryWithoutDisabled.CalculateEntityCount(),
                    "Expected a game object to be converted");

                var go = Object.FindObjectOfType<TestComponentAuthoring>().gameObject;
                Undo.RecordObject(go, "DisableObject");
                go.SetActive(false);
                Undo.FlushUndoRecordObjects();

                yield return UpdateEditorAndWorld(w);

                var queryWithDisabled = w.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>(),
                        ComponentType.ReadWrite<Disabled>()
                    },
                    Options = EntityQueryOptions.IncludeDisabled
                });
                Assert.AreEqual(1, queryWithDisabled.CalculateEntityCount(),
                    "Expected a game object to be converted and disabled");
                Assert.AreEqual(0, queryWithoutDisabled.CalculateEntityCount(),
                    "Expected a game object to be converted and disabled");

                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(0, queryWithDisabled.CalculateEntityCount(),
                    "Expected a game object to be converted and enabled");
                Assert.AreEqual(1, queryWithoutDisabled.CalculateEntityCount(),
                    "Expected a game object to be converted and enabled");

                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, queryWithDisabled.CalculateEntityCount(),
                    "Expected a game object to be converted and disabled");
                Assert.AreEqual(0, queryWithoutDisabled.CalculateEntityCount(),
                    "Expected a game object to be converted and disabled");
            }
        }

        [UnityTest, EmbeddedPackageOnlyTest]
        public IEnumerator LiveLink_WithTextureDependency_ChangeCausesReconversion([Values]Mode mode)
        {
            {
                EditorSceneManager.OpenScene(ScenePath("SceneWithTextureDependency"));
                OpenAllSubScenes();
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var testQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<ConversionDependencyData>());
                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.IsTrue(testQuery.GetSingleton<ConversionDependencyData>().HasTexture);
                Assert.AreEqual(m_TestTexture.filterMode,
                    testQuery.GetSingleton<ConversionDependencyData>().TextureFilterMode,
                    "Initial conversion reported the wrong value");

                m_TestTexture.filterMode = m_TestTexture.filterMode == FilterMode.Bilinear
                    ? FilterMode.Point
                    : FilterMode.Bilinear;
                AssetDatabase.SaveAssets();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(m_TestTexture.filterMode,
                    testQuery.GetSingleton<ConversionDependencyData>().TextureFilterMode,
                    "Updated conversion shows the wrong value");
            }
        }

        [UnityTest, EmbeddedPackageOnlyTest]
        public IEnumerator LiveLink_WithMaterialDependency_ChangeCausesReconversion([Values] Mode mode)
        {
            {
                EditorSceneManager.OpenScene(ScenePath("SceneWithMaterialDependency"));
                OpenAllSubScenes();
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var testQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<ConversionDependencyData>());
                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(m_TestMaterial.color, testQuery.GetSingleton<ConversionDependencyData>().MaterialColor);

                m_TestMaterial.color = m_TestMaterial.color == Color.blue ? Color.red : Color.blue;
                AssetDatabase.SaveAssets();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(m_TestMaterial.color, testQuery.GetSingleton<ConversionDependencyData>().MaterialColor,
                    "The game object with the asset dependency has not been reconverted");
            }
        }

        [UnityTest, EmbeddedPackageOnlyTest]
        public IEnumerator LiveLink_WithMultipleScenes_WithAssetDependencies_ChangeCausesReconversion([Values]Mode mode)
        {
            {
                EditorSceneManager.OpenScene(ScenePath("SceneWithMaterialDependency"));
                EditorSceneManager.OpenScene(ScenePath("SceneWithTextureDependency"), OpenSceneMode.Additive);
                OpenAllSubScenes();
            }

            yield return GetEnterPlayMode(mode);

            {
                var w = GetLiveLinkWorld(mode);

                var testQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<ConversionDependencyData>());
                Assert.AreEqual(2, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Entity textureEntity, materialEntity;
                using (var entities = testQuery.ToEntityArray(Allocator.TempJob))
                {
                    if (GetData(entities[0]).HasMaterial)
                    {
                        materialEntity = entities[0];
                        textureEntity = entities[1];
                    }
                    else
                    {
                        materialEntity = entities[1];
                        textureEntity = entities[0];
                    }
                }

                Assert.AreEqual(m_TestMaterial.color, GetData(materialEntity).MaterialColor);
                Assert.AreEqual(m_TestTexture.filterMode, GetData(textureEntity).TextureFilterMode);

                m_TestMaterial.color = m_TestMaterial.color == Color.blue ? Color.red : Color.blue;
                AssetDatabase.SaveAssets();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(m_TestMaterial.color, GetData(materialEntity).MaterialColor,
                    "The game object with the material asset dependency has not been reconverted");

                m_TestTexture.filterMode = m_TestTexture.filterMode == FilterMode.Bilinear
                    ? FilterMode.Point
                    : FilterMode.Bilinear;
                AssetDatabase.SaveAssets();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(m_TestTexture.filterMode, GetData(textureEntity).TextureFilterMode,
                    "The game object with the texture asset dependency has not been reconverted.");

                ConversionDependencyData GetData(Entity e) =>
                    w.EntityManager.GetComponentData<ConversionDependencyData>(e);
            }
        }

        [UnityTest]
        public IEnumerator LiveLink_LoadAndUnload_WithChanges([Values] Mode mode)
        {
            {
                CreateSubSceneFromObjects("TestSubScene", true, () =>
                {
                    var go = new GameObject("TestGameObject");
                    var authoring = go.AddComponent<TestComponentAuthoring>();
                    authoring.Material = m_TestMaterial;
                    authoring.IntValue = 15;

                    return new List<GameObject> {go};
                });
            }

            yield return GetEnterPlayMode(mode);

            {
                var authoring = Object.FindObjectOfType<TestComponentAuthoring>();
                Assert.AreEqual(authoring.IntValue, 15);
                Assert.AreEqual(authoring.Material, m_TestMaterial);

                var w = GetLiveLinkWorld(mode);

                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(15,
                    testTagQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);

                var testSceneQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<SceneReference>());
                Assert.AreEqual(1, testSceneQuery.CalculateEntityCount());

                Undo.RecordObject(authoring, "Change component value");
                authoring.IntValue = 2;

                // it takes an extra frame to establish that something has changed when using RecordObject unless Flush is called
                Undo.FlushUndoRecordObjects();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(2, testTagQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue,
                    "Expected a component value to change");

                var subScene = Object.FindObjectOfType<SubScene>();
                Assert.IsNotNull(subScene);

                subScene.gameObject.SetActive(false);
                yield return UpdateEditorAndWorld(w);
                Assert.AreEqual(0, testSceneQuery.CalculateEntityCount(),
                    "Expected no Scene Entities after disabling the SubScene MonoBehaviour");

                subScene.gameObject.SetActive(true);
                yield return UpdateEditorAndWorld(w);
                Assert.AreEqual(1, testSceneQuery.CalculateEntityCount(),
                    "Expected Scene Entity after enabling the SubScene MonoBehaviour");

                // Do conversion again
                Undo.RecordObject(authoring, "Change component value");
                authoring.IntValue = 42;

                // it takes an extra frame to establish that something has changed when using RecordObject unless Flush is called
                Undo.FlushUndoRecordObjects();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(),
                    "Expected a game object to be converted after unloading and loading subscene");
                Assert.AreEqual(42, testTagQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue,
                    "Expected a component value to change after unloading and loading subscene");
            }
        }

        [UnityTest]
        public IEnumerator LiveLink_ReconvertsBlobAssets()
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);

                var go = new GameObject("TestGameObject");
                var authoring = go.AddComponent<TestComponentWithBlobAssetAuthoring>();
                authoring.Version = 1;
                SceneManager.MoveGameObjectToScene(go, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(Mode.Edit);
            {
                var w = GetLiveLinkWorld(Mode.Edit);

                var authoring = Object.FindObjectOfType<TestComponentWithBlobAssetAuthoring>();

                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentWithBlobAssetAuthoring.Component>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(1,
                    testTagQuery.GetSingleton<TestComponentWithBlobAssetAuthoring.Component>().BlobAssetRef.Value);

                Undo.RecordObject(authoring, "Change component value");
                authoring.Version = 2;

                // it takes an extra frame to establish that something has changed when using RecordObject unless Flush is called
                Undo.FlushUndoRecordObjects();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(2,
                    testTagQuery.GetSingleton<TestComponentWithBlobAssetAuthoring.Component>().BlobAssetRef.Value,
                    "Expected a blob asset value to change");

                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(1,
                    testTagQuery.GetSingleton<TestComponentWithBlobAssetAuthoring.Component>().BlobAssetRef.Value,
                    "Expected a blob asset value to change, undo failed");

                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(2,
                    testTagQuery.GetSingleton<TestComponentWithBlobAssetAuthoring.Component>().BlobAssetRef.Value,
                    "Expected a blob asset value to change, redo failed");
            }
        }

        [UnityTest, EmbeddedPackageOnlyTest]
        public IEnumerator LiveLink_ChangingPrefabInstanceWorks()
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);
                m_PrefabPath = m_Assets.GetNextPath("Test.prefab");
                var root = new GameObject();
                var child = new GameObject();
                child.AddComponent<TestComponentAuthoring>().IntValue = 3;
                child.transform.SetParent(root.transform);
                SceneManager.MoveGameObjectToScene(root, subScene.EditingScene);
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, m_PrefabPath, InteractionMode.AutomatedAction);
            }

            yield return GetEnterPlayMode(Mode.Edit);

            {
                var w = GetLiveLinkWorld(Mode.Edit);

                var authoring = Object.FindObjectOfType<TestComponentAuthoring>();
                Assert.AreEqual(authoring.IntValue, 3);

                var testTagQuery = w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(3, testTagQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m_PrefabPath);
                prefab.GetComponentInChildren<TestComponentAuthoring>().IntValue = 15;
                PrefabUtility.SavePrefabAsset(prefab, out var success);
                Assert.IsTrue(success, "Failed to save prefab asset");
                AssetDatabase.SaveAssets();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(15, testTagQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);
            }
        }

        [UnityTest, EmbeddedPackageOnlyTest]
        public IEnumerator LiveLink_DeletingFromPrefabInstanceWorks()
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);
                m_PrefabPath = m_Assets.GetNextPath("Test.prefab");
                var root = new GameObject("Root");
                root.AddComponent<TestComponentAuthoring>().IntValue = 42;
                var child = new GameObject("Child");
                child.AddComponent<TestComponentAuthoring>().IntValue = 3;
                child.transform.SetParent(root.transform);
                SceneManager.MoveGameObjectToScene(root, subScene.EditingScene);
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, m_PrefabPath, InteractionMode.AutomatedAction);
            }

            yield return GetEnterPlayMode(Mode.Edit);

            {
                var w = GetLiveLinkWorld(Mode.Edit);

                var testTagQuery = w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>());
                Assert.AreEqual(2, testTagQuery.CalculateEntityCount(), "Expected two game objects to be converted");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m_PrefabPath);
                Object.DestroyImmediate(prefab.transform.GetChild(0).gameObject, true);
                PrefabUtility.SavePrefabAsset(prefab, out var success);
                Assert.IsTrue(success, "Failed to save prefab asset");
                AssetDatabase.SaveAssets();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(42, testTagQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);
            }
        }

        [UnityTest]
        public IEnumerator LiveLink_RunsWithSectionsNotYetLoaded([Values]Mode mode)
        {
            {
                CreateSubSceneFromObjects("TestSubScene", true, () =>
                {
                    var go = new GameObject("TestGameObject");
                    var authoringComponent = go.AddComponent<TestComponentAuthoring>();
                    authoringComponent.IntValue = 42;
                    return new List<GameObject> { go };
                });
            }

            yield return GetEnterPlayMode(mode);
            {
                var w = GetLiveLinkWorld(mode);
                var manager = w.EntityManager;

                var sceneSystem = w.GetExistingSystem<SceneSystem>();
                var subScene = Object.FindObjectOfType<SubScene>();

                var sceneEntity = sceneSystem.GetSceneEntity(subScene.SceneGUID);
                Assert.AreNotEqual(Entity.Null, sceneEntity);

                var sceneInstance = sceneSystem.LoadSceneAsync(subScene.SceneGUID,
                    new SceneSystem.LoadParameters
                    {
                        Flags = SceneLoadFlags.NewInstance | SceneLoadFlags.BlockOnStreamIn |
                                SceneLoadFlags.BlockOnImport
                    });

                yield return UpdateEditorAndWorld(w);

                var sectionInstance = manager.GetBuffer<ResolvedSectionEntity>(sceneInstance)[0].SectionEntity;

                var instanceQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<TestComponentAuthoring.UnmanagedTestComponent>(), ComponentType.ReadWrite<SceneTag>());
                instanceQuery.SetSharedComponentFilter(new SceneTag {SceneEntity = sectionInstance});

                Assert.AreEqual(42, instanceQuery.GetSingleton<TestComponentAuthoring.UnmanagedTestComponent>().IntValue);

                //Clear resolved scene sections and ensure the patcher doesn't error
                //this emulates an async scene not yet fully loaded
                manager.GetBuffer<ResolvedSectionEntity>(sceneInstance).Clear();

                var authoring = Object.FindObjectOfType<TestComponentAuthoring>();

                //Change the authoring component value in order to force the LiveLink patcher to run
                //Expect no errors
                Undo.RecordObject(authoring, "Change component value");
                authoring.IntValue = 117;
                Undo.FlushUndoRecordObjects();

                yield return UpdateEditorAndWorld(w);

                //Clean up scene instance
                w.EntityManager.DestroyEntity(sceneInstance);
                yield return UpdateEditorAndWorld(w);
            }
        }


        [UnityTest]
        public IEnumerator LiveLink_SceneWithIsBuildingForEditorConversion([Values]Mode mode)
        {
            var subScene = CreateSubSceneFromObjects("TestSubScene", true, () =>
            {
                var go = new GameObject("TestGameObject", typeof(TestComponentAuthoringIsBuildingForEditor));
                return new List<GameObject> { go };
            });

            var buildSettings = default(Unity.Entities.Hash128);
            var originalHash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, true, ImportMode.Synchronous);
            Assert.IsTrue(originalHash.IsValid);

            yield return GetEnterPlayMode(mode);
            {
                var w = GetLiveLinkWorld(mode);
                var componentQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<IntTestData>());
                Assert.AreEqual(1, componentQuery.GetSingleton<IntTestData>().Value);
            }
        }

#if UNITY_2020_2_OR_NEWER
        [UnityTest]
        public IEnumerator IncrementalConversion_WithDependencyOnDeletedComponent_ReconvertsAllDependents_Edit()
        {
            // This is a test for a very specific case: Declaring dependencies on components that are deleted at the
            // time of conversion but that are later restored.
            // This is happening in this case because:
            //  "B" has a dependency on "A".
            //  We delete "A" before the conversion happens.
            //  We then restore "A", which must trigger a reconversion of "B".

            {
                var subScene = CreateEmptySubScene("TestSubScene", true);

                var a = new GameObject("A");
                var aAuthoring = a.AddComponent<DependsOnComponentTransitiveTestAuthoring>();
                aAuthoring.SelfValue = 4;
                var b = new GameObject("B");
                var bAuthoring = b.AddComponent<DependsOnComponentTransitiveTestAuthoring>();
                bAuthoring.Dependency = aAuthoring;
                bAuthoring.SelfValue = 15;
                SceneManager.MoveGameObjectToScene(a, subScene.EditingScene);
                SceneManager.MoveGameObjectToScene(b, subScene.EditingScene);
                Undo.DestroyObjectImmediate(a);
                // ensure that we have processed the destroy event from above
                yield return null;
            }

            yield return GetEnterPlayMode(Mode.Edit);
            {
                var w = GetLiveLinkWorld(Mode.Edit);
                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<DependsOnComponentTransitiveTestAuthoring.Component>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                EntitiesAssert.Contains(w.EntityManager,
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 15}));

                Undo.PerformUndo();

                // In the editor, undoing the deletion would restore the reference, but this doesn't immediately work
                // in code. So we're doing it manually for now.
                var a = GameObject.Find("A");
                var b = GameObject.Find("B");
                b.GetComponent<DependsOnComponentTransitiveTestAuthoring>().Dependency =
                    a.GetComponent<DependsOnComponentTransitiveTestAuthoring>();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(2, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                EntitiesAssert.Contains(w.EntityManager,
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 4}),
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 5}));
            }
        }

        [UnityTest]
        public IEnumerator IncrementalConversion_WithComponentDependencies_ReconvertsAllDependents([Values]Mode mode)
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);

                var a = new GameObject("A");
                var aAuthoring = a.AddComponent<DependsOnComponentTransitiveTestAuthoring>();
                aAuthoring.SelfValue = 1;
                var b = new GameObject("B");
                var bAuthoring = b.AddComponent<DependsOnComponentTransitiveTestAuthoring>();
                bAuthoring.Dependency = aAuthoring;
                var c = new GameObject("C");
                var cAuthoring = c.AddComponent<DependsOnComponentTransitiveTestAuthoring>();
                cAuthoring.Dependency = bAuthoring;
                SceneManager.MoveGameObjectToScene(a, subScene.EditingScene);
                SceneManager.MoveGameObjectToScene(b, subScene.EditingScene);
                SceneManager.MoveGameObjectToScene(c, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(mode);
            {
                var w = GetLiveLinkWorld(mode);

                var a = GameObject.Find("A");
                var authoring = a.GetComponent<DependsOnComponentTransitiveTestAuthoring>();
                Assert.AreEqual(1, authoring.SelfValue);

                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(ComponentType
                        .ReadWrite<DependsOnComponentTransitiveTestAuthoring.Component>());
                Assert.AreEqual(3, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                EntitiesAssert.Contains(w.EntityManager,
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 1}),
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 2}),
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 3}));

                Undo.RecordObject(authoring, "Change component value");
                authoring.SelfValue = 42;

                // it takes an extra frame to establish that something has changed when using RecordObject unless Flush is called
                Undo.FlushUndoRecordObjects();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(3, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                EntitiesAssert.Contains(w.EntityManager,
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 42}),
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 43}),
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 44}));

                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(3, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                EntitiesAssert.Contains(w.EntityManager,
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 1}),
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 2}),
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 3}));

                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(3, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                EntitiesAssert.Contains(w.EntityManager,
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 42}),
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 43}),
                    EntityMatch.Partial(new DependsOnComponentTransitiveTestAuthoring.Component {Value = 44}));
            }
        }

        [UnityTest]
        public IEnumerator IncrementalConversion_AddingStaticOptimizeEntity_ReconvertsObject([Values] Mode mode)
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);

                var a = new GameObject("Root");
                SceneManager.MoveGameObjectToScene(a, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(mode);
            {
                var w = GetLiveLinkWorld(mode);

                var testTagQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<LocalToWorld>());
                var staticTagQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<Static>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(0, staticTagQuery.CalculateEntityCount(), "Expected a non-static entity");

                Undo.AddComponent<StaticOptimizeEntity>(GameObject.Find("Root"));

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, staticTagQuery.CalculateEntityCount(), "Expected a static entity");

                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(0, staticTagQuery.CalculateEntityCount(), "Expected a non-static entity");

                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, staticTagQuery.CalculateEntityCount(), "Expected a static entity");
            }
        }

        [UnityTest]
        public IEnumerator IncrementalConversion_AddingStaticOptimizeEntityToChild_ReconvertsObject([Values]Mode mode)
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);

                var a = new GameObject("Root");
                var c = new GameObject("Child");
                c.transform.SetParent(a.transform);
                new GameObject("ChildChild").transform.SetParent(c.transform);
                SceneManager.MoveGameObjectToScene(a, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(mode);
            {
                var w = GetLiveLinkWorld(mode);

                var testTagQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<LocalToWorld>());
                var staticTagQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<Static>());
                Assert.AreEqual(3, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(0, staticTagQuery.CalculateEntityCount(), "Expected a non-static entity");

                Undo.AddComponent<StaticOptimizeEntity>(GameObject.Find("Child"));

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(2, staticTagQuery.CalculateEntityCount(), "Expected a static entity");

                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(3, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(0, staticTagQuery.CalculateEntityCount(), "Expected a non-static entity");

                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(2, staticTagQuery.CalculateEntityCount(), "Expected a static entity");
            }
        }

        [UnityTest]
        public IEnumerator IncrementalConversion_WithTransformDependency_ReconvertsAllDependents([Values]Mode mode)
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);

                var a = new GameObject("A");
                var aAuthoring = a.AddComponent<DependsOnTransformTestAuthoring>();
                aAuthoring.Dependency = a.transform;
                SceneManager.MoveGameObjectToScene(a, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(mode);
            {
                var w = GetLiveLinkWorld(mode);

                var a = GameObject.Find("A");

                var testTagQuery =
                    w.EntityManager.CreateEntityQuery(
                        ComponentType.ReadWrite<DependsOnTransformTestAuthoring.Component>());
                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(a.transform.localToWorldMatrix,
                    testTagQuery.GetSingleton<DependsOnTransformTestAuthoring.Component>().LocalToWorld);

                Undo.RecordObject(a.transform, "Change component value");
                a.transform.position = new Vector3(1, 2, 3);

                // it takes an extra frame to establish that something has changed when using RecordObject unless Flush is called
                Undo.FlushUndoRecordObjects();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(a.transform.localToWorldMatrix,
                    testTagQuery.GetSingleton<DependsOnTransformTestAuthoring.Component>().LocalToWorld);

                Undo.PerformUndo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(a.transform.localToWorldMatrix,
                    testTagQuery.GetSingleton<DependsOnTransformTestAuthoring.Component>().LocalToWorld);

                Undo.PerformRedo();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testTagQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(a.transform.localToWorldMatrix,
                    testTagQuery.GetSingleton<DependsOnTransformTestAuthoring.Component>().LocalToWorld);
            }
        }

        [UnityTest]
        public IEnumerator IncrementalConversion_WithTextureDependencyInScene_ChangeCausesReconversion()
        {

            {
                var subScene = CreateEmptySubScene("TestSubScene", true);
                var a = new GameObject("A");
                var aAuthoring = a.AddComponent<DependencyTestAuthoring>();
                var texture = new Texture2D(16, 16) {filterMode = FilterMode.Bilinear};
                aAuthoring.Texture = texture;
                SceneManager.MoveGameObjectToScene(a, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(Mode.Edit);

            {
                var w = GetLiveLinkWorld(Mode.Edit);

                var a = GameObject.Find("A");
                var texture = a.GetComponent<DependencyTestAuthoring>().Texture;
                Assert.IsTrue(texture != null);
                var testQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<ConversionDependencyData>());
                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.IsTrue(testQuery.GetSingleton<ConversionDependencyData>().HasTexture);
                Assert.AreEqual(texture.filterMode, testQuery.GetSingleton<ConversionDependencyData>().TextureFilterMode,
                    "Initial conversion reported the wrong value");

                Undo.RecordObject(texture, "Change texture filtering");
                texture.filterMode = FilterMode.Point;
                Undo.FlushUndoRecordObjects();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(texture.filterMode, testQuery.GetSingleton<ConversionDependencyData>().TextureFilterMode,
                    "Updated conversion shows the wrong value");

                Undo.PerformUndo();
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(texture.filterMode, testQuery.GetSingleton<ConversionDependencyData>().TextureFilterMode,
                    "Updated conversion shows the wrong value");

                Undo.PerformRedo();
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(texture.filterMode, testQuery.GetSingleton<ConversionDependencyData>().TextureFilterMode,
                    "Updated conversion shows the wrong value");

                Undo.PerformUndo();
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(texture.filterMode, testQuery.GetSingleton<ConversionDependencyData>().TextureFilterMode,
                    "Updated conversion shows the wrong value");
            }
        }

        [UnityTest]
        public IEnumerator IncrementalConversion_WithTextureDependencyInScene_DestroyAndRecreateCausesReconversion()
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);
                var a = new GameObject("A");
                var aAuthoring = a.AddComponent<DependencyTestAuthoring>();
                var texture = new Texture2D(16, 16);
                aAuthoring.Texture = texture;
                SceneManager.MoveGameObjectToScene(a, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(Mode.Edit);

            {
                var w = GetLiveLinkWorld(Mode.Edit);

                var a = GameObject.Find("A");
                var texture = a.GetComponent<DependencyTestAuthoring>().Texture;
                Assert.IsTrue(texture != null);
                var testQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<ConversionDependencyData>());
                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.IsTrue(testQuery.GetSingleton<ConversionDependencyData>().HasTexture);

                Undo.DestroyObjectImmediate(texture);

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.IsFalse(testQuery.GetSingleton<ConversionDependencyData>().HasTexture);

                Undo.PerformUndo();
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.IsTrue(testQuery.GetSingleton<ConversionDependencyData>().HasTexture);

                Undo.PerformRedo();
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.IsFalse(testQuery.GetSingleton<ConversionDependencyData>().HasTexture);

                Undo.PerformUndo();
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.IsTrue(testQuery.GetSingleton<ConversionDependencyData>().HasTexture);
            }
        }

        [UnityTest]
        public IEnumerator IncrementalConversion_WithHybridComponent_ChangeCausesReconversion()
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);
                var a = new GameObject("A");
                var aAuthoring = a.AddComponent<HybridComponentTestAuthoring>();
                aAuthoring.Value = 16;
                SceneManager.MoveGameObjectToScene(a, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(Mode.Edit);

            {
                var w = GetLiveLinkWorld(Mode.Edit);

                var a = GameObject.Find("A");
                var authoring = a.GetComponent<HybridComponentTestAuthoring>();
                var testQuery = w.EntityManager.CreateEntityQuery(typeof(HybridComponentTestAuthoring), typeof(Entity));
                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(16, GetHybridComponent().Value);

                Undo.RecordObject(authoring, "Change value");
                authoring.Value = 7;
                Undo.FlushUndoRecordObjects();

                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(7, GetHybridComponent().Value);

                Undo.PerformUndo();
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(16, GetHybridComponent().Value);

                Undo.PerformRedo();
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(7, GetHybridComponent().Value);

                Undo.PerformUndo();
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(16, GetHybridComponent().Value);

                HybridComponentTestAuthoring GetHybridComponent() =>
                    w.EntityManager.GetComponentObject<HybridComponentTestAuthoring>(testQuery.GetSingletonEntity());
            }
        }

        [UnityTest]
        public IEnumerator IncrementalConversion_WithHybridComponent_UnrelatedChangeDoesNotCauseReconversion()
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);
                var a = new GameObject("A");
                var aAuthoring = a.AddComponent<HybridComponentTestAuthoring>();
                aAuthoring.Value = 16;
                var b = new GameObject("B");
                SceneManager.MoveGameObjectToScene(a, subScene.EditingScene);
                SceneManager.MoveGameObjectToScene(b, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(Mode.Edit);
            {
                var w = GetLiveLinkWorld(Mode.Edit);
                var a = GameObject.Find("A");
                var authoring = a.GetComponent<HybridComponentTestAuthoring>();
                var b = GameObject.Find("B");
                var testQuery = w.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<HybridComponentTestAuthoring>());
                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(16, GetHybridComponent().Value);

                // Change the value, but don't record any undo events. This way the change is not propagated, but can be
                // used as a sentinel.
                authoring.Value = 7;

                Undo.RegisterCompleteObjectUndo(b, "Test Undo");
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(16, GetHybridComponent().Value);

                Undo.PerformUndo();
                yield return UpdateEditorAndWorld(w);

                Assert.AreEqual(1, testQuery.CalculateEntityCount(), "Expected a game object to be converted");
                Assert.AreEqual(16, GetHybridComponent().Value);

                HybridComponentTestAuthoring GetHybridComponent() =>
                    w.EntityManager.GetComponentObject<HybridComponentTestAuthoring>(testQuery.GetSingletonEntity());
            }
        }

        private static readonly (string, Func<GameObject>)[] TransformTestCaseData =
        {
            ("SingleObject", () => new GameObject("Root")),
            ("RootAndChild", () =>
            {
                var root = new GameObject("Root");
                var c = new GameObject("Child");
                c.transform.SetParent(root.transform);
                c.transform.localPosition = new Vector3(1, 1, 1);
                return root;
            }),
            ("RootAndChildren", () =>
            {
                var root = new GameObject("Root");
                for (int i = 0; i < 5; i++)
                {
                    var c = new GameObject("Child " + i);
                    c.transform.SetParent(root.transform);
                    c.transform.localPosition = new Vector3(i, i, i);
                    c.transform.localScale = new Vector3(i, i, 1);
                    c.transform.localRotation = Quaternion.Euler(i * 15, 0, -i * 15);
                }
                return root;
            }),
            ("DeepHierarchy", () =>
            {
                var root = new GameObject("Root");
                var current = root;
                for (int i = 0; i < 10; i++)
                {
                    var c = new GameObject("Child " + i);
                    c.transform.SetParent(current.transform);
                    c.transform.localPosition = new Vector3(1, 1, 1);
                    current = c;
                }
                return root;
            }),
            ("SingleStaticObject", () =>
            {
                var root = new GameObject("Root");
                root.AddComponent<StaticOptimizeEntity>();
                return root;
            }),
            ("RootAndStaticChild", () =>
            {
                var root = new GameObject("Root");
                var c = new GameObject("Child");
                c.transform.SetParent(root.transform);
                c.transform.localPosition = new Vector3(1, 1, 1);
                c.AddComponent<StaticOptimizeEntity>();
                return root;
            }),
            ("RootAndStaticChildWithChild", () =>
            {
                var root = new GameObject("Root");
                var c = new GameObject("Child");
                c.transform.SetParent(root.transform);
                c.transform.localPosition = new Vector3(1, 1, 1);
                c.AddComponent<StaticOptimizeEntity>();
                var cc = new GameObject("ChildChild");
                cc.transform.SetParent(root.transform);
                cc.transform.localPosition = new Vector3(1, 1, 1);
                return root;
            }),
            ("StaticRootAndChild", () =>
            {
                var root = new GameObject("Root");
                root.AddComponent<StaticOptimizeEntity>();
                var c = new GameObject("Child");
                c.transform.SetParent(root.transform);
                c.transform.localPosition = new Vector3(1, 1, 1);
                return root;
            }),
            ("RootAndChildren_WithSomeStatic", () =>
            {
                var root = new GameObject("Root");
                for (int i = 0; i < 5; i++)
                {
                    var c = new GameObject("Child " + i);
                    c.transform.SetParent(root.transform);
                    c.transform.localPosition = new Vector3(i, i, i);
                    c.transform.localScale = new Vector3(i, i, 1);
                    c.transform.localRotation = Quaternion.Euler(i * 15, 0, -i * 15);
                    if (i % 2 == 0)
                        c.AddComponent<StaticOptimizeEntity>();
                }
                return root;
            }),
        };

        public static IEnumerable TransformTestCases
        {
            get
            {
                foreach (var entry in TransformTestCaseData)
                {
                    var tc = new TestCaseData(entry.Item2).SetName(entry.Item1);
                    tc.HasExpectedResult = true;
                    yield return tc;
                }
            }
        }

        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(TransformTestCases))]
        public IEnumerator IncrementalConversion_WithHierarchy_PatchesTransforms(Func<GameObject> makeGameObject)
        {
            {
                var subScene = CreateEmptySubScene("TestSubScene", true);

                var root = makeGameObject();
                SceneManager.MoveGameObjectToScene(root, subScene.EditingScene);
            }

            yield return GetEnterPlayMode(Mode.Edit);
            {
                var w = GetLiveLinkWorld(Mode.Edit);

                var roots = Object.FindObjectOfType<SubScene>().EditingScene.GetRootGameObjects();
                var stack = new Stack<GameObject>(roots);
                var objs = new List<Transform>();
                while (stack.Count > 0)
                {
                    var top = stack.Pop();
                    objs.Add(top.transform);
                    var transform = top.transform;
                    int n = transform.childCount;
                    for (int i = 0; i < n; i++)
                        stack.Push(transform.GetChild(i).gameObject);
                }

                foreach (var t in objs)
                {
                    Undo.RecordObject(t, "change transform");
                    t.position += new Vector3(1, -1, 0);
                    t.rotation *= Quaternion.Euler(15, 132, 0.1f);
                    t.localScale *= 1.5f;
                    Undo.FlushUndoRecordObjects();

                    yield return UpdateEditorAndWorld(w);
                }
            }
        }


#if false // remove to enable fuzz testing
        static IEnumerable<int> FuzzTestingSeeds()
        {
            for (int i = 0; i < 300; i++)
                yield return i;
        }

        [UnityTest, Explicit]
        public IEnumerator IncrementalConversion_FuzzTesting_Edit([ValueSource(nameof(FuzzTestingSeeds))]int seed) => IncrementalConversion_FuzzTesting(Fuzz.FullFuzzer, seed);
        [UnityTest, Explicit]
        public IEnumerator IncrementalConversion_FuzzTesting_Hierarchy_Edit([ValueSource(nameof(FuzzTestingSeeds))]int seed) => IncrementalConversion_FuzzTesting(Fuzz.HierarchyFuzzer, seed);
        [UnityTest, Explicit]
        public IEnumerator IncrementalConversion_FuzzTesting_HierarchyWithStatic_Edit([ValueSource(nameof(FuzzTestingSeeds))]int seed) => IncrementalConversion_FuzzTesting(Fuzz.HierarchyWithStaticFuzzer, seed);
        [UnityTest, Explicit]
        public IEnumerator IncrementalConversion_FuzzTesting_Transform_Edit([ValueSource(nameof(FuzzTestingSeeds))]int seed) => IncrementalConversion_FuzzTesting(Fuzz.TransformFuzzer, seed);
        [UnityTest, Explicit]
        public IEnumerator IncrementalConversion_FuzzTesting_Dependencies_Edit([ValueSource(nameof(FuzzTestingSeeds))]int seed) => IncrementalConversion_FuzzTesting(Fuzz.DependenciesFuzzer, seed);
        [UnityTest, Explicit]
        public IEnumerator IncrementalConversion_FuzzTesting_EnabledDisabled_Edit([ValueSource(nameof(FuzzTestingSeeds))]int seed) => IncrementalConversion_FuzzTesting(Fuzz.HierarchyWithToggleEnabled, seed);
#endif
        IEnumerator IncrementalConversion_FuzzTesting(Fuzz.FuzzerSetup fuzzer, int seed)
        {
            {
                CreateEmptySubScene("TestSubScene", true);
            }

            yield return GetEnterPlayMode(Mode.Edit);

            {
                var w = GetLiveLinkWorld(Mode.Edit);

                var subScene = Object.FindObjectOfType<SubScene>();

                DependsOnComponentTestAuthoring.Versions.Clear();
                Console.WriteLine($"Running fuzz test with seed {seed} and the following actions:");
                foreach (var wa in fuzzer.WeightedActions)
                    Console.WriteLine(" - " + wa.Item1);
                var state = Fuzz.NewState(seed, SceneManager.GetActiveScene(), subScene.EditingScene);
                SceneManager.SetActiveScene(subScene.EditingScene);
                int stepsWithoutUpdate = 0;
                const int steps = 100;
                const int updateThreshold = 20;
                for (int s = 0; s < steps; s++)
                {
                    Fuzz.FuzzerAction fuzzerAction = stepsWithoutUpdate >= updateThreshold ? Fuzz.FuzzerAction.UpdateFrame : fuzzer.SampleAction(ref state.Rng);
                    Fuzz.Command cmd;
                    while (!Fuzz.BuildCommand(ref state, fuzzerAction, out cmd))
                        fuzzerAction = fuzzer.SampleAction(ref state.Rng);

                    Console.WriteLine("Fuzz." + Fuzz.FormatCommand(cmd) + ",");
                    Fuzz.ApplyCommand(ref state, cmd);
                    if (fuzzerAction == Fuzz.FuzzerAction.UpdateFrame)
                    {
                        stepsWithoutUpdate = 0;
                        yield return UpdateEditorAndWorld(w);
                    }
                    else
                        stepsWithoutUpdate++;
                }
            }
        }

        [UnityTest, TestCaseSource(typeof(LiveLinkEditorTests), nameof(FuzzTestCases))]
        public IEnumerator IncrementalConversionTests(List<Fuzz.Command> commands) => RunCommands(commands);

        public static IEnumerable FuzzTestCases
        {
            get
            {
                foreach (var entry in FuzzerTestCaseData)
                {
                    var tc = new TestCaseData(entry.Item2).SetName(entry.Item1);
                    tc.HasExpectedResult = true;
                    yield return tc;
                }
            }
        }

        private static readonly ValueTuple<string, List<Fuzz.Command>>[] FuzzerTestCaseData =
        {
            ("Create_ThenMoveOutOfSubscene", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.UpdateFrameCommand(),
            }),
            ("Reparent_ThenDeleteChild", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 2),
                Fuzz.ReparentCommand(1, 2),
                Fuzz.DeleteGameObjectCommand(2),
                Fuzz.UpdateFrameCommand(),
            }),
            ("Create_ThenInvalidateHierarchy", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.InvalidateHierarchyCommand(1),
                Fuzz.UpdateFrameCommand(),
            }),
            ("Create_Invalidate_ThenMoveParent", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.InvalidateGameObjectCommand(1),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.UpdateFrameCommand(),
            }),
            ("Create_InvalidateHierarchy_ThenMoveParent", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.InvalidateHierarchyCommand(1),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.UpdateFrameCommand(),
            }),
            ("Create_ThenDelete", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.InvalidateGameObjectCommand(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.DeleteGameObjectCommand(1),
                Fuzz.UpdateFrameCommand(),
            }),
            ("Create_ThenInvalidateTwice", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 0),
                Fuzz.InvalidateGameObjectCommand(0),
                Fuzz.InvalidateGameObjectCommand(0),
                Fuzz.UpdateFrameCommand(),
            }),
            ("MoveToRoot_ThenBackToSubScene", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.MoveToSubSceneCommand(0),
                Fuzz.UpdateFrameCommand(),
            }),
            ("Reparent_ThenMoveNewParentToRootScene", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 0),
                Fuzz.CreateGameObjectCommand(1, 0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ReparentCommand(1, 0),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.UpdateFrameCommand(),
            }),
            ("MoveToRootSceneAndBack_ThenReparent", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 0),
                Fuzz.CreateGameObjectCommand(1, 0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.MoveToSubSceneCommand(0),
                Fuzz.ReparentCommand(1, 0),
                Fuzz.UpdateFrameCommand(),
            }),
            ("MoveBetweenScenes_ThenAddChild", new List<Fuzz.Command>
            {
               Fuzz.CreateGameObjectCommand(0, 0),
               Fuzz.CreateGameObjectCommand(1, 0),
               Fuzz.UpdateFrameCommand(),
               Fuzz.MoveToRootSceneCommand(0),
               Fuzz.MoveToSubSceneCommand(0),
               Fuzz.MoveToRootSceneCommand(0),
               Fuzz.MoveToSubSceneCommand(0),
               Fuzz.UpdateFrameCommand(),
               Fuzz.ReparentCommand(1, 0)
            }),
            ("Reparent_ThenDeleteParentsParent", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 0),
                Fuzz.CreateGameObjectCommand(1, 0),
                Fuzz.CreateGameObjectCommand(2, 0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ReparentCommand(2, 1),
                Fuzz.DeleteGameObjectCommand(0),
                Fuzz.UpdateFrameCommand(),
            }),
            ("MoveBetweenScenes_ThenDeleteChild", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.MoveToSubSceneCommand(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.DeleteGameObjectCommand(1),
                Fuzz.UpdateFrameCommand(),
            }),
            ("Reparent_ThenMoveWithChildren", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 0),
                Fuzz.CreateGameObjectCommand(1, 1),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ReparentCommand(1, 0),
                Fuzz.MoveToRootSceneCommand(0),
            }),
            ("ReparentDirectlyAfterCreate_ThenDelete", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.UpdateFrameCommand(),
                Fuzz.CreateGameObjectCommand(2, 1),
                Fuzz.ReparentCommand(3, 1),
                Fuzz.UpdateFrameCommand(),
                Fuzz.DeleteGameObjectCommand(3),
            }),
            ("MoveIn_ThenDirtyChild_ThenMoveOut", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.MoveToSubSceneCommand(0),
                Fuzz.InvalidateHierarchyCommand(1),
                Fuzz.MoveToRootSceneCommand(0),
            }),
            ("Create_ThenInvalidate_ThenMoveOut_ThenMakeChildARoot", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.InvalidateHierarchyCommand(1),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.ReparentCommand(1, -1),
            }),
            ("CreateTwo_ThenAddDependency_ThenMoveOutAndBackIn", new List<Fuzz.Command>
            {
                // This test is challenging because it adds a dependency to something that is then moved out of the
                // scene and returned later.
                Fuzz.CreateGameObjectCommand(0, 0),
                Fuzz.CreateGameObjectCommand(1, 0),
                Fuzz.SetDependencyCommand(0, 1),
                Fuzz.MoveToRootSceneCommand(1),
                Fuzz.UpdateFrameCommand(),
                Fuzz.MoveToSubSceneCommand(1),
            }),
            ("CreateTwo_ThenAddDependency_ThenChangeAndDelete_ThenUndo", new List<Fuzz.Command>
            {
                // This test is similar to the previous test but again very challenging: We set up a dependency of 0 on
                // 1, then we modify and delete 1. This causes a reconversion of 0 during which 1 doesn't exist anymore.
                // Finally, we undo the deletion of 1 which then _cannot_ cause a reconversion of 0.
                Fuzz.CreateGameObjectCommand(0, 0),
                Fuzz.CreateGameObjectCommand(1, 0),
                Fuzz.SetDependencyCommand(0, 1),
                Fuzz.UpdateFrameCommand(),
                Fuzz.InvalidateGameObjectCommand(1),
                Fuzz.DeleteGameObjectCommand(1),
                Fuzz.UpdateFrameCommand(),
                Fuzz.UndoCommand()
            }),
            ("CreateAndAddSelfDependency_ThenInvalidate", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 0),
                Fuzz.SetDependencyCommand(0, 0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.TouchComponent(0),
            }),
            ("CreateTwoWithDependency_ThenMoveInAndOutAndDelete", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0),
                Fuzz.CreateGameObjectCommand(1),
                Fuzz.SetDependencyCommand(1, 0),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.MoveToSubSceneCommand(0),
                Fuzz.DeleteGameObjectCommand(0),
            }),
            ("CreateTwoWithMutualDependency_ThenReSetDependencyAndCreateAnother", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0),
                Fuzz.CreateGameObjectCommand(1),
                Fuzz.SetDependencyCommand(0, 1),
                Fuzz.SetDependencyCommand(1, 0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.SetDependencyCommand(0, 1),
                Fuzz.SetDependencyCommand(1, 0),
                Fuzz.CreateGameObjectCommand(2),
            }),
            ("CreateObjectWithChild_ThenMarkStatic", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ToggleStaticOptimizeEntity(0),
            }),
            ("CreateAndDisable_ThenAddChild", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0),
                Fuzz.ToggleObjectActive(0),
                Fuzz.CreateGameObjectCommand(1),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ReparentCommand(1, 0),
            }),
            ("CreateHierarchyAndDisable_ThenRemoveChild", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.ToggleObjectActive(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ReparentCommand(1, -1),
            }),
            ("EntangleHierarchy_ThenDisable_ThenEntangleMore", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.CreateGameObjectCommand(2),
                Fuzz.CreateGameObjectCommand(3, 1),
                Fuzz.ReparentCommand(3, 1),

                Fuzz.UpdateFrameCommand(),
                Fuzz.ToggleObjectActive(2),
                Fuzz.ToggleObjectActive(4),
                Fuzz.ToggleObjectActive(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ToggleObjectActive(1),

                Fuzz.ReparentCommand(2, 4),
            }),
            ("CreateAndDisableRootWithChild_ThenAttachChildToChild", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.ToggleObjectActive(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.CreateGameObjectCommand(2),
                Fuzz.ReparentCommand(2, 1),
            }),
            ("CreateAndDeactivateRoot_ThenMakeChildARoot", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.ToggleObjectActive(0),
                Fuzz.ReparentCommand(1, -1),
            }),
            ("CreateHierarchyWithDisabledGroups_ThenMoveRootOutOfSubscene", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 2),
                Fuzz.ReparentCommand(1, 2),
                Fuzz.ToggleObjectActive(0),
                Fuzz.ToggleObjectActive(2),
                Fuzz.UpdateFrameCommand(),
                Fuzz.MoveToRootSceneCommand(0),
                Fuzz.ToggleObjectActive(2),
            }),
            ("CreateDisabledHierarchy_ThenEnable_AndThenDisableChild", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.CreateGameObjectCommand(2, 0),
                Fuzz.ReparentCommand(2, 1),
                Fuzz.ToggleObjectActive(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ToggleObjectActive(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ToggleObjectActive(1),
            }),
            ("CreateInactiveHierarchy_ThenEnableWhileMovingChildOut", new List<Fuzz.Command>
            {
                // This tests that a child is not removed due to being contained in the LinkedEntityGroup of the root
                // when it is moved elsewhere before the root is deleted.
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.ToggleObjectActive(0),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ToggleObjectActive(0),
                Fuzz.ReparentCommand(1, -1),
                Fuzz.DeleteGameObjectCommand(0),
            }),
            ("LongIntricateSetup_ThenFinallyReparentToChangeALinkedEntityGroup", new List<Fuzz.Command>
            {
                // This test checks that entity reference change patches are applied to an entity, even when that entity
                // is removed from a LinkedEntityGroup at the same time.
                Fuzz.CreateGameObjectCommand(0),
                Fuzz.CreateGameObjectCommand(1),
                Fuzz.CreateGameObjectCommand(2, 1),
                Fuzz.ToggleObjectActive(3),
                Fuzz.DeleteGameObjectCommand(1),
                Fuzz.UpdateFrameCommand(),
                Fuzz.CreateGameObjectCommand(4, 1),
                Fuzz.ReparentCommand(4, 3),
                Fuzz.MoveToRootSceneCommand(2),
                Fuzz.UpdateFrameCommand(),
                Fuzz.CreateGameObjectCommand(6, 0),
                Fuzz.MoveToSubSceneCommand(2),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ReparentCommand(5, 6),
            }),
            ("CreateTwoDisabledRoots_ThenMoveChildFromOneToAnotherAndDeletePreviousRoot", new List<Fuzz.Command>
            {
                Fuzz.CreateGameObjectCommand(0, 1),
                Fuzz.ToggleObjectActive(0),
                Fuzz.CreateGameObjectCommand(2),
                Fuzz.ToggleObjectActive(2),
                Fuzz.UpdateFrameCommand(),
                Fuzz.ReparentCommand(1, 2),
                Fuzz.DeleteGameObjectCommand(0),
            })
        };

        IEnumerator RunCommands(List<Fuzz.Command> commands)
        {
            {
                CreateEmptySubScene("TestSubScene", true);
            }

            yield return GetEnterPlayMode(Mode.Edit);

            {
                var w = GetLiveLinkWorld(Mode.Edit);
                var subScene = Object.FindObjectOfType<SubScene>();

                DependsOnComponentTestAuthoring.Versions.Clear();
                Console.WriteLine($"Running test with {commands.Count} commands");
                var state = Fuzz.NewState(0, SceneManager.GetActiveScene(), subScene.EditingScene);
                SceneManager.SetActiveScene(subScene.EditingScene);
                for (int c = 0; c < commands.Count; c++)
                {
                    var cmd = commands[c];
                    Console.WriteLine("Fuzz." + Fuzz.FormatCommand(cmd) + ",");
                    Fuzz.ApplyCommand(ref state, cmd);
                    if (cmd.FuzzerAction == Fuzz.FuzzerAction.UpdateFrame)
                        yield return UpdateEditorAndWorld(w);
                }

                yield return UpdateEditorAndWorld(w);
            }
        }

        internal static class Fuzz
        {
            public static State NewState(int seed, Scene rootScene, Scene subScene) => new State
            {
                RootScene = rootScene,
                SubScene = subScene,
                GameObjectsInSubscene = new List<int>(),
                GameObjectsInRootScene = new List<int>(),
                IdToGameObject = new List<GameObject>(),
                GameObjectToId = new Dictionary<GameObject, int>(),
                Rng = Mathematics.Random.CreateFromIndex((uint)seed)
            };

            public struct State
            {
                public Scene RootScene;
                public Scene SubScene;
                public int UndoStackSize;
                public int RedoStackSize;
                public int NextGameObject;
                public int Step;
                public List<int> GameObjectsInSubscene;
                public List<int> GameObjectsInRootScene;
                public List<GameObject> IdToGameObject;
                public Dictionary<GameObject, int> GameObjectToId;
                public Mathematics.Random Rng;
                public int TotalNumObjects => GameObjectsInSubscene.Count + GameObjectsInRootScene.Count;
            }

            public struct Command
            {
                public FuzzerAction FuzzerAction;
                public int TargetGameObjectId;
                public int AdditionalData;

                public Command(FuzzerAction a, int target, int data = 0)
                {
                    FuzzerAction = a;
                    TargetGameObjectId = target;
                    AdditionalData = data;
                }

                public override string ToString() => $"{FuzzerAction}({TargetGameObjectId}, {AdditionalData})";
            }

            public static string FormatCommand(Command cmd)
            {
                switch (cmd.FuzzerAction)
                {
                    case FuzzerAction.CreateGameObject:
                        return nameof(CreateGameObjectCommand) + $"({cmd.TargetGameObjectId}, {cmd.AdditionalData})";
                    case FuzzerAction.DeleteGameObject:
                        return nameof(DeleteGameObjectCommand) + $"({cmd.TargetGameObjectId})";
                    case FuzzerAction.ReparentGameObject:
                        return nameof(ReparentCommand) + $"({cmd.TargetGameObjectId}, {cmd.AdditionalData})";
                    case FuzzerAction.Undo:
                        return nameof(UndoCommand) + "()";
                    case FuzzerAction.Redo:
                        return nameof(RedoCommand) + "()";
                    case FuzzerAction.TouchComponent:
                        return nameof(TouchComponent) + $"({cmd.TargetGameObjectId})";
                    case FuzzerAction.MoveGameObjectToRootScene:
                        return nameof(MoveToRootSceneCommand) + $"({cmd.TargetGameObjectId})";
                    case FuzzerAction.MoveGameObjectToSubScene:
                        return nameof(MoveToSubSceneCommand) + $"({cmd.TargetGameObjectId})";
                    case FuzzerAction.InvalidateGameObject:
                        return nameof(InvalidateGameObjectCommand) + $"({cmd.TargetGameObjectId})";
                    case FuzzerAction.InvalidateGameObjectHierarchy:
                        return nameof(InvalidateHierarchyCommand) + $"({cmd.TargetGameObjectId})";
                    case FuzzerAction.SetDependency:
                        return nameof(SetDependencyCommand) + $"({cmd.TargetGameObjectId}, {cmd.AdditionalData})";
                    case FuzzerAction.ToggleStaticOptimizeEntity:
                        return nameof(ToggleStaticOptimizeEntity) + $"({cmd.TargetGameObjectId})";
                    case FuzzerAction.ToggleObjectActive:
                        return nameof(ToggleObjectActive) + $"({cmd.TargetGameObjectId})";
                    case FuzzerAction.Translate:
                        return nameof(Translate) + $"({cmd.TargetGameObjectId}, {cmd.AdditionalData})";
                    case FuzzerAction.Scale:
                        return nameof(Scale) + $"({cmd.TargetGameObjectId}, {cmd.AdditionalData})";
                    case FuzzerAction.Rotate:
                        return nameof(Rotate) + $"({cmd.TargetGameObjectId}, {cmd.AdditionalData})";
                    case FuzzerAction.UpdateFrame:
                        return nameof(UpdateFrameCommand) + "()";
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            public static Command CreateGameObjectCommand(int id, int numChildren = 0) =>
                new Command(FuzzerAction.CreateGameObject, id, numChildren);

            public static Command DeleteGameObjectCommand(int id) => new Command(FuzzerAction.DeleteGameObject, id);
            public static Command UpdateFrameCommand() => new Command(FuzzerAction.UpdateFrame, 0);
            public static Command MoveToRootSceneCommand(int id) => new Command(FuzzerAction.MoveGameObjectToRootScene, id);
            public static Command MoveToSubSceneCommand(int id) => new Command(FuzzerAction.MoveGameObjectToSubScene, id);
            public static Command RedoCommand() => new Command(FuzzerAction.Redo, 0);
            public static Command UndoCommand() => new Command(FuzzerAction.Undo, 0);
            public static Command InvalidateGameObjectCommand(int id) => new Command(FuzzerAction.InvalidateGameObject, id);

            public static Command ToggleStaticOptimizeEntity(int id) =>
                new Command(FuzzerAction.ToggleStaticOptimizeEntity, id);
            public static Command ToggleObjectActive(int id) => new Command(FuzzerAction.ToggleObjectActive, id);
            public static Command Translate(int id, int seed) =>
                new Command(FuzzerAction.ToggleStaticOptimizeEntity, id, seed);
            public static Command Rotate(int id, int seed) =>
                new Command(FuzzerAction.ToggleStaticOptimizeEntity, id, seed);
            public static Command Scale(int id, int seed) =>
                new Command(FuzzerAction.ToggleStaticOptimizeEntity, id, seed);
            public static Command InvalidateHierarchyCommand(int id) =>
                new Command(FuzzerAction.InvalidateGameObjectHierarchy, id);

            public static Command TouchComponent(int id) => new Command(FuzzerAction.TouchComponent, id);

            public static Command ReparentCommand(int id, int newParent) =>
                new Command(FuzzerAction.ReparentGameObject, id, newParent);

            public static Command SetDependencyCommand(int id, int dependsOn) =>
                new Command(FuzzerAction.SetDependency, id, dependsOn);

            static IEnumerable<(int, GameObject)> GetSubtree(State state, GameObject root)
            {
                var toDelete = new Stack<GameObject>();
                toDelete.Push(root);
                while (toDelete.Count > 0)
                {
                    var top = toDelete.Pop();
                    int n = top.transform.childCount;
                    for (int i = 0; i < n; i++)
                        toDelete.Push(top.transform.GetChild(i).gameObject);
                    yield return (state.GameObjectToId[top], top);
                }
            }

            static IEnumerable<(int, GameObject)> GetSubtree(State state, int id)
            {
                var root = state.IdToGameObject[id];
                return GetSubtree(state, root);
            }

            static void DeleteGameObject(ref State state, int id)
            {
                var root = state.IdToGameObject[id];
                foreach (var (c, go) in GetSubtree(state, id))
                {
                    state.GameObjectsInSubscene.Remove(c);
                    state.GameObjectsInRootScene.Remove(c);
                }

                Undo.DestroyObjectImmediate(root);
            }

            static GameObject CreateGameObject(ref State state)
            {
                int id = state.NextGameObject;
                state.NextGameObject += 1;
                var go = new GameObject(id.ToString());
                go.AddComponent<DependsOnComponentTestAuthoring>();
                state.GameObjectToId.Add(go, id);
                state.IdToGameObject.Add(go);
                state.GameObjectsInSubscene.Add(id);
                return go;
            }

            static void CreateGameObject(ref State state, int numChildren)
            {
                var go = CreateGameObject(ref state);
                for (int i = 0; i < numChildren; i++)
                {
                    var c = CreateGameObject(ref state);
                    c.transform.SetParent(go.transform);
                }

                Undo.RegisterCreatedObjectUndo(go, "step " + state.Step);
            }

            static void UpdateStateAfterRedoUndo(ref State state)
            {
                state.GameObjectsInRootScene.Clear();
                state.GameObjectsInSubscene.Clear();
                foreach (var root in state.SubScene.GetRootGameObjects())
                {
                    foreach (var (id, _) in GetSubtree(state, root))
                        state.GameObjectsInSubscene.Add(id);
                }

                foreach (var root in state.RootScene.GetRootGameObjects())
                {
                    if (root.TryGetComponent<SubScene>(out _))
                        continue;
                    foreach (var (id, _) in GetSubtree(state, root))
                        state.GameObjectsInRootScene.Add(id);
                }
            }

            static void BumpVersion(GameObject go)
            {
                var v = DependsOnComponentTestAuthoring.Versions;
                v.TryGetValue(go, out var version);
                version++;
                v[go] = version;
            }

            public static void ApplyCommand(ref State state, Command command)
            {
                var name = "step " + state.Step;
                var gos = state.IdToGameObject;
                GameObject target = null;
                if (command.TargetGameObjectId >= 0 && command.TargetGameObjectId < gos.Count)
                    target = gos[command.TargetGameObjectId];
                bool incrementUndo = true;
                switch (command.FuzzerAction)
                {
                    case FuzzerAction.CreateGameObject:
                    {
                        if (command.TargetGameObjectId > -1 && command.TargetGameObjectId != state.NextGameObject)
                            throw new InvalidOperationException(
                                $"The requested game object id {command.TargetGameObjectId} is invalid, use {state.NextGameObject} instead");
                        CreateGameObject(ref state, command.AdditionalData);
                        break;
                    }
                    case FuzzerAction.DeleteGameObject:
                    {
                        DeleteGameObject(ref state, command.TargetGameObjectId);
                        break;
                    }
                    case FuzzerAction.ReparentGameObject:
                    {
                        Transform other = null;
                        if (command.AdditionalData >= 0)
                            other = gos[command.AdditionalData].transform;
                        Undo.SetTransformParent(target.transform, other, name);
                        BumpVersion(target);
                        break;
                    }
                    case FuzzerAction.Undo:
                    {
                        Undo.PerformUndo();
                        UpdateStateAfterRedoUndo(ref state);
                        // unclear whose GO's version we should bump
                        state.RedoStackSize++;
                        state.UndoStackSize--;
                        incrementUndo = false;
                        break;
                    }
                    case FuzzerAction.Redo:
                    {
                        Undo.PerformRedo();
                        UpdateStateAfterRedoUndo(ref state);
                        // unclear whose GO's version we should bump
                        state.RedoStackSize--;
                        state.UndoStackSize++;
                        incrementUndo = false;
                        break;
                    }
                    case FuzzerAction.ToggleObjectActive:
                    {
                        Undo.RecordObject(target, name);
                        target.SetActive(!target.activeSelf);
                        BumpVersion(target);
                        break;
                    }
                    case FuzzerAction.TouchComponent:
                    {
                        Undo.RegisterCompleteObjectUndo(target.GetComponent<DependsOnComponentTestAuthoring>(), name);
                        BumpVersion(target);
                        break;
                    }
                    case FuzzerAction.MoveGameObjectToRootScene:
                    {
                        Undo.MoveGameObjectToScene(target, state.RootScene, name);
                        foreach (var (c, g) in GetSubtree(state, command.TargetGameObjectId))
                        {
                            BumpVersion(g);
                            state.GameObjectsInSubscene.Remove(c);
                            state.GameObjectsInRootScene.Add(c);
                        }
                        break;
                    }
                    case FuzzerAction.MoveGameObjectToSubScene:
                    {
                        Undo.MoveGameObjectToScene(target, state.SubScene, name);
                        foreach (var (c, g) in GetSubtree(state, command.TargetGameObjectId))
                        {
                            BumpVersion(g);
                            state.GameObjectsInRootScene.Remove(c);
                            state.GameObjectsInSubscene.Add(c);
                        }
                        break;
                    }
                    case FuzzerAction.InvalidateGameObject:
                    {
                        Undo.RegisterCompleteObjectUndo(target, name);
                        BumpVersion(target);
                        break;
                    }
                    case FuzzerAction.InvalidateGameObjectHierarchy:
                    {
                        Undo.RegisterFullObjectHierarchyUndo(target, name);
                        BumpVersion(target);
                        break;
                    }
                    case FuzzerAction.SetDependency:
                    {
                        var dependency = gos[command.AdditionalData];
                        var d = target.GetComponent<DependsOnComponentTestAuthoring>();
                        Undo.RegisterCompleteObjectUndo(d, name);
                        BumpVersion(target);
                        d.Other = dependency;
                        break;
                    }
                    case FuzzerAction.ToggleStaticOptimizeEntity:
                    {
                        var staticOpt = target.GetComponent<StaticOptimizeEntity>();
                        if (staticOpt == null)
                            Undo.AddComponent<StaticOptimizeEntity>(target);
                        else
                            Undo.DestroyObjectImmediate(staticOpt);
                        break;
                    }
                    case FuzzerAction.Translate:
                    {
                        var transform = target.transform;
                        Undo.RegisterCompleteObjectUndo(transform, name);
                        BumpVersion(target);
                        var x = command.AdditionalData;
                        transform.localPosition += new Vector3(x * 0.01f, x * 0.02f, x * -0.01f);
                        break;
                    }
                    case FuzzerAction.Rotate:
                    {
                        var transform = target.transform;
                        Undo.RegisterCompleteObjectUndo(transform, name);
                        BumpVersion(target);
                        var x = command.AdditionalData;
                        transform.localPosition += new Vector3(x * 0.01f, x * 0.02f, x * -0.01f);
                        break;
                    }
                    case FuzzerAction.Scale:
                    {
                        var transform = target.transform;
                        Undo.RegisterCompleteObjectUndo(transform, name);
                        BumpVersion(target);
                        var x = command.AdditionalData;
                        transform.localScale = Vector3.Scale(transform.localScale, new Vector3(1 + x * 0.01f, 1 + x * 0.02f, 1 + x * -0.01f));
                        break;
                    }
                    case FuzzerAction.UpdateFrame:
                        incrementUndo = false;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (incrementUndo)
                {
                    state.UndoStackSize++;
                    Undo.IncrementCurrentGroup();
                    Undo.FlushUndoRecordObjects();
                }
                state.Step++;
            }

            static int Sample(ref Mathematics.Random rng, List<int> xs) => xs[rng.NextInt(0, xs.Count)];

            static int Sample(ref Mathematics.Random rng, List<int> xs, List<int> ys)
            {
                var idx = rng.NextInt(0, xs.Count + ys.Count);
                if (idx < xs.Count)
                    return xs[idx];
                idx -= xs.Count;
                return ys[idx];
            }

            static bool CanReparent(GameObject p, GameObject c)
            {
                // p should not already be the parent of c
                if (c.transform.parent == p.transform || p.scene != c.scene)
                    return false;
                // otherwise, make sure that c is not above p
                var t = p.transform;
                while (t != null)
                {
                    if (t == c.transform)
                        return false;
                    t = t.parent;
                }

                return true;
            }

            static int GetRoot(ref State state, int id)
            {
                var go = state.IdToGameObject[id];
                while (go.transform.parent != null)
                    go = go.transform.parent.gameObject;
                return state.GameObjectToId[go];
            }

            public static bool BuildCommand(ref State state, FuzzerAction fuzzerAction, out Command command)
            {
                command = new Command
                {
                    FuzzerAction = fuzzerAction
                };
                switch (fuzzerAction)
                {
                    case FuzzerAction.CreateGameObject:
                        command.TargetGameObjectId = state.NextGameObject;
                        // num children
                        command.AdditionalData = state.Rng.NextInt(0, 5);
                        return true;
                    case FuzzerAction.ReparentGameObject:
                    {
                        int totalCount = state.TotalNumObjects;
                        if (totalCount <= 1)
                            return false;

                        command.TargetGameObjectId = Sample(ref state.Rng, state.GameObjectsInSubscene, state.GameObjectsInRootScene);
                        var child = state.IdToGameObject[command.TargetGameObjectId];
                        if (child.transform.parent != null && state.Rng.NextFloat() < .1f + 1f / totalCount)
                        {
                            // make sure to also cover the case that game objects are moved to the root
                            command.AdditionalData = -1;
                            return true;
                        }

                        const int maxAttempts = 5;
                        for (int i = 0; i < maxAttempts; i++)
                        {
                            int idx = Sample(ref state.Rng, state.GameObjectsInSubscene, state.GameObjectsInRootScene);
                            if (idx == command.TargetGameObjectId)
                                continue;
                            var parent = state.IdToGameObject[idx];
                            if (CanReparent(parent, child))
                            {
                                command.AdditionalData = idx;
                                return true;
                            }
                        }

                        return false;
                    }
                    case FuzzerAction.Undo:
                        return state.UndoStackSize > 0;
                    case FuzzerAction.Redo:
                        return state.RedoStackSize > 0;
                    case FuzzerAction.MoveGameObjectToSubScene:
                    {
                        if (state.GameObjectsInRootScene.Count == 0)
                            return false;
                        command.TargetGameObjectId = GetRoot(ref state, Sample(ref state.Rng, state.GameObjectsInRootScene));
                        return true;
                    }
                    case FuzzerAction.ToggleObjectActive:
                    case FuzzerAction.DeleteGameObject:
                    {
                        if (state.TotalNumObjects == 0)
                            return false;
                        command.TargetGameObjectId = Sample(ref state.Rng, state.GameObjectsInSubscene, state.GameObjectsInRootScene);
                        return true;
                    }
                    case FuzzerAction.MoveGameObjectToRootScene:
                    {
                        if (state.GameObjectsInSubscene.Count == 0)
                            return false;
                        command.TargetGameObjectId = GetRoot(ref state, Sample(ref state.Rng, state.GameObjectsInSubscene));
                        return true;
                    }
                    case FuzzerAction.TouchComponent:
                    case FuzzerAction.InvalidateGameObject:
                    case FuzzerAction.InvalidateGameObjectHierarchy:
                    {
                        if (state.GameObjectsInSubscene.Count == 0)
                            return false;
                        command.TargetGameObjectId = Sample(ref state.Rng, state.GameObjectsInSubscene);
                        return true;
                    }
                    case FuzzerAction.SetDependency:
                    {
                        if (state.TotalNumObjects == 0)
                            return false;
                        command.TargetGameObjectId = Sample(ref state.Rng, state.GameObjectsInSubscene, state.GameObjectsInRootScene);
                        command.AdditionalData = Sample(ref state.Rng, state.GameObjectsInSubscene, state.GameObjectsInRootScene);
                        return true;
                    }
                    case FuzzerAction.ToggleStaticOptimizeEntity:
                    {
                        if (state.TotalNumObjects == 0)
                            return false;
                        command.TargetGameObjectId = Sample(ref state.Rng, state.GameObjectsInSubscene, state.GameObjectsInRootScene);
                        return true;
                    }
                    case FuzzerAction.Translate:
                    case FuzzerAction.Rotate:
                    case FuzzerAction.Scale:
                    {
                        if (state.TotalNumObjects == 0)
                            return false;
                        command.TargetGameObjectId = Sample(ref state.Rng, state.GameObjectsInSubscene, state.GameObjectsInRootScene);
                        // seed for transform change
                        command.AdditionalData = state.Rng.NextInt(-5, 6);
                        return true;
                    }
                    case FuzzerAction.UpdateFrame:
                        return true;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            public enum FuzzerAction
            {
                CreateGameObject,
                DeleteGameObject,
                ReparentGameObject,
                Undo,
                Redo,
                TouchComponent,
                MoveGameObjectToRootScene,
                MoveGameObjectToSubScene,
                InvalidateGameObject,
                InvalidateGameObjectHierarchy,
                SetDependency,
                ToggleStaticOptimizeEntity,
                ToggleObjectActive,
                Translate,
                Rotate,
                Scale,
                UpdateFrame,
            }

            public static FuzzerSetup HierarchyFuzzer => new FuzzerSetup
            {
                WeightedActions = new List<(FuzzerAction, int)>
                {
                    (FuzzerAction.CreateGameObject, 15),
                    (FuzzerAction.DeleteGameObject, 10),
                    (FuzzerAction.ReparentGameObject, 10),
                    //(FuzzerAction.Undo, 10),
                    //(FuzzerAction.Redo, 10),
                    (FuzzerAction.MoveGameObjectToRootScene, 10),
                    (FuzzerAction.MoveGameObjectToSubScene, 10),
                    (FuzzerAction.UpdateFrame, 10),
                },
            };

            public static FuzzerSetup HierarchyWithToggleEnabled => HierarchyFuzzer.With(
                (FuzzerAction.ToggleObjectActive, 30)
            );

            public static FuzzerSetup HierarchyWithStaticFuzzer => HierarchyFuzzer.With(
                (FuzzerAction.ToggleStaticOptimizeEntity, 10)
            );

            public static FuzzerSetup TransformFuzzer => HierarchyFuzzer.With(
                (FuzzerAction.ToggleStaticOptimizeEntity, 10),
                (FuzzerAction.Translate, 10),
                (FuzzerAction.Rotate, 10),
                (FuzzerAction.Scale, 10)
            );

            public static FuzzerSetup DependenciesFuzzer => new FuzzerSetup
            {
                WeightedActions = new List<(FuzzerAction, int)>
                {
                    (FuzzerAction.CreateGameObject, 20),
                    (FuzzerAction.DeleteGameObject, 10),
                    (FuzzerAction.ReparentGameObject, 10),
                    (FuzzerAction.Undo, 10),
                    (FuzzerAction.Redo, 10),
                    (FuzzerAction.TouchComponent, 15),
                    (FuzzerAction.MoveGameObjectToRootScene, 10),
                    (FuzzerAction.MoveGameObjectToSubScene, 10),
                    (FuzzerAction.InvalidateGameObject, 15),
                    (FuzzerAction.InvalidateGameObjectHierarchy, 15),
                    (FuzzerAction.SetDependency, 20),
                    (FuzzerAction.UpdateFrame, 10)
                },
            };

            public static FuzzerSetup FullFuzzer => new FuzzerSetup
            {
                WeightedActions = new List<(FuzzerAction, int)>
                {
                    (FuzzerAction.CreateGameObject, 20),
                    (FuzzerAction.DeleteGameObject, 10),
                    (FuzzerAction.ReparentGameObject, 10),
                    (FuzzerAction.Undo, 10),
                    (FuzzerAction.Redo, 10),
                    (FuzzerAction.TouchComponent, 15),
                    (FuzzerAction.MoveGameObjectToRootScene, 10),
                    (FuzzerAction.MoveGameObjectToSubScene, 10),
                    (FuzzerAction.InvalidateGameObject, 15),
                    (FuzzerAction.InvalidateGameObjectHierarchy, 15),
                    (FuzzerAction.SetDependency, 20),
                    (FuzzerAction.ToggleStaticOptimizeEntity, 10),
                    (FuzzerAction.ToggleObjectActive, 10),
                    (FuzzerAction.Translate, 10),
                    (FuzzerAction.Rotate, 10),
                    (FuzzerAction.Scale, 10),
                    (FuzzerAction.UpdateFrame, 10),
                },
            };

            public struct FuzzerSetup
            {
                private int _totalWeight;
                private int[] _runningSumWeights;
                public List<(FuzzerAction, int)> WeightedActions;

                public FuzzerAction SampleAction(ref Mathematics.Random rng)
                {
                    if (_runningSumWeights == null)
                    {
                        _runningSumWeights = new int[WeightedActions.Count];
                        int s = 0;
                        for (int i = 0; i < _runningSumWeights.Length; i++)
                        {
                            s += WeightedActions[i].Item2;
                            _runningSumWeights[i] = s;
                        }

                        _totalWeight = s;
                    }

                    int w = rng.NextInt(0, _totalWeight);
                    for (int i = 0; i < _runningSumWeights.Length; i++)
                    {
                        if (w < _runningSumWeights[i])
                            return WeightedActions[i].Item1;
                    }

                    return WeightedActions[WeightedActions.Count - 1].Item1;
                }

                public FuzzerSetup With(params (FuzzerAction action, int weight)[] more)
                {
                    var fs = new FuzzerSetup {
                        WeightedActions = new List<(FuzzerAction, int)>(WeightedActions)
                    };
                    fs.WeightedActions.AddRange(more);
                    return fs;
                }
            }
        }
#endif
    }
}
