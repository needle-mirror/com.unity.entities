using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Entities.Conversion;
using Unity.Entities.Tests;
using Unity.Entities.Tests.Conversion;
using Unity.PerformanceTesting;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Unity.Scenes.Editor.Tests
{
    [Serializable]
    [TestFixture]
    class LiveConversionEditorPerformanceTests
    {
        [SerializeField] TestWithEditorLiveConversion m_Test;
        [SerializeField] TestWithObjects m_Objects;
        private const int MaxIterations = 30;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_Test.OneTimeSetUp();
            LiveConversionSettings.AdditionalConversionSystems.Clear();
            LiveConversionSettings.AdditionalConversionSystems.Add(typeof(TestConversionSystem));
            LiveConversionSettings.Mode = LiveConversionSettings.ConversionMode.IncrementalConversion;
            LiveConversionSettings.EnableInternalDebugValidation = false;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_Test.OneTimeTearDown();
            LiveConversionSettings.AdditionalConversionSystems.Clear();
        }

        [SetUp]
        public void SetUp()
        {
            m_Test.SetUp();
            m_Objects.SetUp();
            MeasureLiveConversionTime.IsFirst = true;
        }

        [TearDown]
        public void TearDown()
        {
            m_Objects.TearDown();
        }

        static string AssetPath(string name) => "Packages/com.unity.entities/Unity.Scenes.Editor.PerformanceTests/Assets/" + name;

        class TestConversionSystem : GameObjectConversionSystem
        {
            protected override void OnUpdate()
            {
#if ENABLE_ASPECTS
                // Force Entities into existence
                Entities.ForEach((TestMonoBehaviour t) => { GetPrimaryEntity(t); });
#endif
            }
        }


        public enum ObjectKind
        {
            Empty, Sphere
#if ENABLE_ASPECTS
            ,
            Tagged
#endif
        }

        GameObject CreateGameObject(ObjectKind objectKind)
        {
            switch (objectKind)
            {
                case ObjectKind.Empty:
                    return m_Objects.CreateGameObject();
                case ObjectKind.Sphere:
                    return m_Objects.CreatePrimitive(PrimitiveType.Sphere);
#if ENABLE_ASPECTS
                case ObjectKind.Tagged:
                    return m_Objects.CreateGameObject("", typeof(TestMonoBehaviour));
#endif
            }
            return null;
        }

        GameObject LoadPrefab(ObjectKind objectKind)
        {
            switch (objectKind)
            {
                case ObjectKind.Empty:
                    return AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath("Empty.prefab"));
                case ObjectKind.Sphere:
                    return AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath("Sphere.prefab"));
#if ENABLE_ASPECTS
                case ObjectKind.Tagged:
                    return AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath("Tagged.prefab"));
#endif
            }

            return null;
        }

        GameObject CreateHierarchy(int depth, int branching, ObjectKind kind, List<GameObject> output = null)
        {
            return CreateSubHierarchy(depth, branching, null, output);
            GameObject CreateSubHierarchy(int depth, int branching, Transform parent, List<GameObject> output)
            {
                var go = CreateGameObject(kind);
                var t = go.transform;
                if (parent != null)
                    t.SetParent(parent);
                output?.Add(go);
                if (depth > 0)
                {
                    for (int i = 0; i < branching; i++)
                        CreateSubHierarchy(depth - 1, branching, t, output);
                }
                return go;
            }
        }

        List<GameObject> CreateObjectSoupSubSceneObjects(int numObjects, ObjectKind kind)
        {
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            var objects = new List<GameObject>();
            for (int i = 0; i < numObjects; i++)
            {
                var obj = CreateGameObject(kind);
                objects.Add(obj);
                SceneManager.MoveGameObjectToScene(obj, subScene.EditingScene);
            }

            var target = CreateGameObject(kind);
            SceneManager.MoveGameObjectToScene(target, subScene.EditingScene);
            objects.Add(target);
            return objects;
        }

        GameObject CreateObjectSoupSubScene(int numObjects, ObjectKind kind, SubScene subScene)
        {
            for (int i = 0; i < numObjects; i++)
                SceneManager.MoveGameObjectToScene(CreateGameObject(kind), subScene.EditingScene);
            var target = CreateGameObject(kind);
            SceneManager.MoveGameObjectToScene(target, subScene.EditingScene);
            return target;
        }

        // This machinery for running performance tests is necessary for two reasons:
        // (a) as of writing, the performance test package doesn't support capturing profiler marker in editor tests
        // (b) running a performance test like this only once means that it potentially triggers JIT compilation. I've
        //     tried this many times now and it consistently increases runtimes by 10x-50x, so that means that it will
        //     be unstable based on the order that tests run in.
        struct MeasureLiveConversionTime
        {
            internal static bool IsFirst;
            private readonly EditorSubSceneLiveConversionSystem m_System;
            private readonly SampleGroup m_SampleGroup;
            public MeasureLiveConversionTime(World w)
            {
                m_System = w.GetExistingSystem<EditorSubSceneLiveConversionSystem>();
                m_System.MillisecondsTakenByUpdate = 0;
                m_SampleGroup = new SampleGroup(nameof(EditorSubSceneLiveConversionSystem));
            }

            public void Commit()
            {
                // intentionally skip the first sample
                if (!IsFirst)
                    Measure.Custom(m_SampleGroup, m_System.MillisecondsTakenByUpdate);
                IsFirst = false;
                m_System.MillisecondsTakenByUpdate = 0;
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_MoveHierarchyRoot([Values(1, 5, 10)]int depth, [Values] ObjectKind kind)
        {
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            var root = CreateHierarchy(depth, 2, kind);
            SceneManager.MoveGameObjectToScene(root, subScene.EditingScene);

            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < MaxIterations; i++)
                {
                    Undo.RecordObject(root.transform, "Change Transform");
                    root.transform.position += Vector3.one;
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_MoveHierarchyChildren([Values(1, 5, 10)]int depth, [Values] ObjectKind kind)
        {
            var objects = new List<GameObject>();
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            var root = CreateHierarchy(depth, 2, kind, objects);
            SceneManager.MoveGameObjectToScene(root, subScene.EditingScene);

            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                int n = MaxIterations;
                if (n > objects.Count)
                    n = objects.Count;
                for (int i = 0; i < n; i++)
                {
                    var p = objects[objects.Count - i - 1];
                    Undo.RecordObject(p.transform, "Change Transform");
                    p.transform.position += Vector3.one;
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_MoveOneOfMany([Values(1, 10, 100, 1000)]int numObjects, [Values] ObjectKind kind)
        {
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            GameObject target = CreateObjectSoupSubScene(numObjects, kind, subScene);
            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < MaxIterations; i++)
                {
                    Undo.RecordObject(target.transform, "Change Transform");
                    target.transform.position += Vector3.one;
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_EnableDisableHierarchyRoot([Values(1, 5, 8)]int depth, [Values] ObjectKind kind)
        {
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            var root = CreateHierarchy(depth, 2, kind);
            SceneManager.MoveGameObjectToScene(root, subScene.EditingScene);

            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < MaxIterations; i++)
                {
                    Undo.RecordObject(root, "Change Root");
                    root.SetActive(!root.activeInHierarchy);
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_EnableDisableOneOfMany([Values(1, 10, 100, 1000)]int numObjects, [Values] ObjectKind kind)
        {
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            GameObject target = CreateObjectSoupSubScene(numObjects, kind, subScene);
            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < MaxIterations; i++)
                {
                    Undo.RecordObject(target, "Change Transform");
                    target.SetActive(!target.activeInHierarchy);
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_CreateOneAmongMany([Values(1, 10, 100, 1000)]int numObjects, [Values] ObjectKind kind)
        {
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            CreateObjectSoupSubScene(numObjects, kind, subScene);
            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                var scene = Object.FindObjectOfType<SubScene>().EditingScene;
                for (int i = 0; i < MaxIterations; i++)
                {
                    var go = CreateGameObject(kind);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    Undo.RegisterCreatedObjectUndo(go, "Create one");
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_ReparentOneAmongMany([Values(100, 1000)]int numObjects, [Values] ObjectKind kind)
        {
            var list = CreateObjectSoupSubSceneObjects(numObjects, kind);
            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < MaxIterations; i++)
                {
                    Undo.SetTransformParent(list[i].transform, list[i + 1].transform, "Reparent");
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_ReparentHierarchy([Values(1, 5, 10)]int depth, [Values] ObjectKind kind)
        {
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            var root = CreateHierarchy(depth, 2, kind);
            SceneManager.MoveGameObjectToScene(root, subScene.EditingScene);
            var target = CreateGameObject(kind);
            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < MaxIterations; i++)
                {
                    if (root.transform.parent == null)
                        Undo.SetTransformParent(root.transform, target.transform, "Reparent");
                    else
                        Undo.SetTransformParent(root.transform, null, "Reparent");
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_ReparentIntoHierarchy([Values(5, 10)]int depth, [Values] ObjectKind kind)
        {
            var objects = new List<GameObject>();
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            var root = CreateHierarchy(depth, 2, kind, objects);
            SceneManager.MoveGameObjectToScene(root, subScene.EditingScene);
            var target = CreateGameObject(kind);
            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < MaxIterations; i++)
                {
                    if (i >= objects.Count)
                        break;
                    Undo.SetTransformParent(target.transform, objects[objects.Count - i - 1].transform, "reparent");
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_DeleteChildrenFromHierarchy([Values(5, 10)]int depth, [Values] ObjectKind kind)
        {
            var objects = new List<GameObject>();
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            var root = CreateHierarchy(depth, 2, kind, objects);
            SceneManager.MoveGameObjectToScene(root, subScene.EditingScene);
            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);

            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < MaxIterations; i++)
                {
                    if (i >= objects.Count)
                        break;
                    int last = objects.Count - 1;
                    Undo.DestroyObjectImmediate(objects[last]);
                    objects.RemoveAt(last);
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_DeleteHierarchyRoot([Values(5, 8)]int depth, [Values] ObjectKind kind)
        {
            var objects = new List<GameObject>();
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            for (int i = 0; i < 5; i++)
            {
                var root = CreateHierarchy(depth, 2, kind);
                objects.Add(root);
                SceneManager.MoveGameObjectToScene(root, subScene.EditingScene);
            }

            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                foreach (var root in objects)
                {
                    Undo.DestroyObjectImmediate(root);
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance]
        public IEnumerator LiveConversion_Performance_DeleteFromMany([Values(100, 1000)]int numObjects, [Values] ObjectKind kind)
        {
            var objects = CreateObjectSoupSubSceneObjects(numObjects, kind);
            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < MaxIterations; i++)
                {
                    if (i >= objects.Count)
                        break;
                    int last = objects.Count - 1;
                    Undo.DestroyObjectImmediate(objects[last]);
                    objects.RemoveAt(last);
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance, Ignore("DOTS-3826")]
        public IEnumerator LiveConversion_Performance_CreateHierarchy([Values(1, 5, 8)]int depth, [Values] ObjectKind kind)
        {
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                var scene = subScene.EditingScene;
                for (int i = 0; i < 5; i++)
                {
                    var root = CreateHierarchy(depth, 2, kind);
                    SceneManager.MoveGameObjectToScene(root, scene);
                    Undo.RegisterCreatedObjectUndo(root, "Create hierarchy");
                    Undo.FlushUndoRecordObjects();
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }
        }

        [UnityTest, Performance, EmbeddedPackageOnlyTest]
        public IEnumerator LiveConversion_Performance_TouchOnePrefabAmongManyObjects([Values(100, 1000)]int numObjects, [Values] ObjectKind prefabKind)
        {
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            var kind = ObjectKind.Empty;
#if ENABLE_ASPECTS
            kind = ObjectKind.Tagged;
#endif
            CreateObjectSoupSubScene(numObjects, kind, subScene);
            var prefab = LoadPrefab(prefabKind);
            var go = Object.Instantiate(prefab);
            m_Objects.RegisterForDestruction(go);
            SceneManager.MoveGameObjectToScene(go, subScene.EditingScene);
            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);

            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < 10; i++)
                {
                    prefab.isStatic = !prefab.isStatic;
                    PrefabUtility.SavePrefabAsset(prefab);
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }

            prefab.isStatic = false;
            PrefabUtility.SavePrefabAsset(prefab);
        }

        [UnityTest, Performance, EmbeddedPackageOnlyTest]
        public IEnumerator LiveConversion_Performance_UpdateManyPrefabs([Values(100)]int numObjects)
        {
            var subScene = m_Test.CreateEmptySubScene("TestSubScene", true);
            var prefab = LoadPrefab(ObjectKind.Sphere);
            for (int i = 0; i < numObjects; i++)
            {
                var go = Object.Instantiate(prefab);
                m_Objects.RegisterForDestruction(go);
                SceneManager.MoveGameObjectToScene(go, subScene.EditingScene);
            }

            var w = m_Test.GetLiveConversionWorld(TestWithEditorLiveConversion.Mode.Edit);
            yield return m_Test.UpdateEditorAndWorld(w);
            var measure = new MeasureLiveConversionTime(w);
            {
                for (int i = 0; i < 10; i++)
                {
                    prefab.isStatic = !prefab.isStatic;
                    PrefabUtility.SavePrefabAsset(prefab);
                    yield return m_Test.UpdateEditorAndWorld(w);
                    measure.Commit();
                }
            }

            prefab.isStatic = false;
            PrefabUtility.SavePrefabAsset(prefab);
        }
    }
}
