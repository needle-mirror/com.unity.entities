#if !UNITY_DISABLE_MANAGED_COMPONENTS && !UNITY_DOTSRUNTIME
using NUnit.Framework;
using Unity.Entities.Content;
using Unity.Entities.Serialization;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Unity.Collections;
using System.Threading;
using Unity.Entities.Build;
using Unity.Entities.Tests;
using Unity.Jobs;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental;
using UnityEditor.SceneManagement;
using Unity.Scenes.Editor;
using Unity.Scenes.Editor.Tests;
#if USING_PLATFORMS_PACKAGE
using Unity.Build;
#endif
#endif


namespace Unity.Scenes.Hybrid.Tests.Editmode.Content
{
    [TestFixture]
    public class RuntimeContentManagerTests
    {
        private static string MainScenePath => $"{TestAssetFolder}/RuntimeContentManagerTests.unity";
        private static string TestAssetFolder => $"Assets/{TestAssetFolderName}";
        private static string TestAssetFolderName => nameof(RuntimeContentManagerTests);
        private static string TestStreamingAssetsFolderName => $"Assets/StreamingAssets/{nameof(RuntimeContentManagerTests)}";
        private static string TestStreamingAssetsFullPath => $"{Application.streamingAssetsPath}/{nameof(RuntimeContentManagerTests)}";
        private static string GetAssetPath(string assetName)
        {
            return $"{TestAssetFolder}/{assetName}";
        }
        private static string DeleteStreamingAssetsFolder => GetSessionStateKey("DeleteStreamingAssetsFolder");
        private static string RefObjRuntimeId => GetSessionStateKey("RefObjRuntimeId");
        private static string DirectObjRuntimeId => GetSessionStateKey("DirectObjRuntimeId");
        private static string GetSessionStateKey(string name)
        {
            return $"{TestAssetFolder}.{name}";
        }

        private static string[] kDirectAssetPaths =
        {
            GetAssetPath("VertexLitDirect0.mat"),
            GetAssetPath("VertexLitDirect1.mat"),
            GetAssetPath("VertexLitDirect2.mat"),
            GetAssetPath("VertexLitDirect3.mat"),
            GetAssetPath("VertexLitDirect4.mat"),
            GetAssetPath("VertexLitDirect5.mat"),
            GetAssetPath("VertexLitDirect6.mat"),
            GetAssetPath("VertexLitDirect7.mat"),
        };

        private static string[] kRefAssetPaths =
        {
            GetAssetPath("VertexLitRef0.mat"),
            GetAssetPath("VertexLitRef1.mat"),
            GetAssetPath("VertexLitRef2.mat"),
            GetAssetPath("VertexLitRef3.mat"),
            GetAssetPath("VertexLitRef4.mat"),
            GetAssetPath("VertexLitRef5.mat"),
            GetAssetPath("VertexLitRef6.mat"),
            GetAssetPath("VertexLitRef7.mat"),
        };

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/StreamingAssets"))
                SessionState.SetString(DeleteStreamingAssetsFolder, "true");
            if (AssetDatabase.IsValidFolder(TestAssetFolder))
                AssetDatabase.DeleteAsset(TestAssetFolder);
            AssetDatabase.CreateFolder("Assets", TestAssetFolderName);

            Shader testShader = Shader.Find("VertexLit");

            for (int i = 0; i < kRefAssetPaths.Length; i++)
            {
                Material mat1 = new Material(testShader);
                mat1.color = new Color(1, i / (float)kRefAssetPaths.Length, 0);
                AssetDatabase.CreateAsset(mat1, kRefAssetPaths[i]);
            }
            for (int i = 0; i < kDirectAssetPaths.Length; i++)
            {
                Material mat1 = new Material(testShader);
                mat1.color = new Color(1, i / (float)kDirectAssetPaths.Length, 0);
                AssetDatabase.CreateAsset(mat1, kDirectAssetPaths[i]);
            }

            var mainScene = SubSceneTestsHelper.CreateScene(MainScenePath);

            // Create SubScene with WeakObjectReference
            SubScene refSubScene = SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("RefSubScene", true, mainScene, () =>
            {
                var gos = new List<GameObject>();
                foreach (var p in kDirectAssetPaths)
                {
                    var go1 = new GameObject("MaterialRefObject");
                    var comp1 = go1.AddComponent<WeakMaterialRefComponentAuthoring>();
                    var matObj1 = AssetDatabase.LoadAssetAtPath<Material>(p);
                    WeakObjectReference<Material> materialRef = new WeakObjectReference<Material>();
                    materialRef.Id = UntypedWeakReferenceId.CreateFromObjectInstance(matObj1);
                    comp1.matRef = materialRef;
                    SessionState.SetString(RefObjRuntimeId, materialRef.Id.ToString());
                    gos.Add(go1);
                }
                return gos;

            });

            // Create SubScene with direct reference
            SubScene directSubScene = SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("DirectSubScene", true, mainScene, () =>
            {
                var gos = new List<GameObject>();
                foreach (var p in kDirectAssetPaths)
                {
                    var go2 = new GameObject("MaterialDirectObject");
                    var comp2 = go2.AddComponent<WeakMaterialComponentAuthoring>();
                    comp2.mat = AssetDatabase.LoadAssetAtPath<Material>(p);
                    gos.Add(go2);
                }
                return gos;
            });

            var subSceneGuids = new HashSet<Entities.Hash128>() { refSubScene.SceneGUID, directSubScene.SceneGUID };
            var artifactKeys = new Dictionary<Entities.Hash128, ArtifactKey>();
            var settingsGuid = DotsGlobalSettings.Instance.GetClientGUID();

            EntitySceneBuildUtility.PrepareEntityBinaryArtifacts(settingsGuid, subSceneGuids, artifactKeys);
            EntitySceneBuildUtility.PrepareAdditionalFiles(default, artifactKeys.Keys.ToArray(), artifactKeys.Values.ToArray(), EditorUserBuildSettings.activeBuildTarget, DoCopy);

            var artifactHashes = new UnityEngine.Hash128[1];
            AssetDatabaseCompatibility.ProduceArtifactsRefreshIfNecessary(new ArtifactKey[1] { artifactKeys[directSubScene.SceneGUID] }, artifactHashes);
            AssetDatabaseCompatibility.GetArtifactPaths(artifactHashes[0], out var artifactPaths);
            var sectionIndex = EntityScenesPaths.GetSectionIndexFromPath(artifactPaths[0]);
            var address = $"{artifactHashes[0]}.{sectionIndex}";
            var ssh = SceneHeaderUtility.CreateSceneSectionHash(directSubScene.SceneGUID, sectionIndex, default);
            SessionState.SetString(DirectObjRuntimeId, ssh.ToString());
        }

        static void DoCopy(string src, string dst)
        {
            dst = $"{TestStreamingAssetsFullPath}/{dst}";
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            SessionState.EraseString(RefObjRuntimeId);
            SessionState.EraseString(DirectObjRuntimeId);
            if (SessionState.GetString(DeleteStreamingAssetsFolder, "") == "true")
            {
                //if we created Streaming assets folder, delete it
                AssetDatabase.DeleteAsset("Assets/StreamingAssets");
                SessionState.EraseString(DeleteStreamingAssetsFolder);
            }
            else
            {
                //otherwise just delete our subfolder
                AssetDatabase.DeleteAsset(TestStreamingAssetsFolderName);
            }

            AssetDatabase.DeleteAsset(TestAssetFolder);
        }

        void LoadThreadFunc(object context)
        {
            var p = ((UntypedWeakReferenceId, ManualResetEvent))context;
            for (int i = 0; i < 5; i++)
            {
                RuntimeContentManager.LoadObjectAsync(p.Item1);
                Thread.Sleep(1);
            }
            p.Item2.Set();
        }

        void ReleaseThreadFunc(object context)
        {
            var p = ((UntypedWeakReferenceId, ManualResetEvent))context;
            for (int i = 0; i <5; i++)
            {
                RuntimeContentManager.ReleaseObjectAsync(p.Item1);
                Thread.Sleep(1);
            }
            p.Item2.Set();
        }

        [UnityTest]
        public IEnumerator RuntimeContentManager_CanLoadAndReleaseFromThreads()
        {
            yield return new EnterPlayMode();

            Assert.IsTrue(InitializeCatalogForTest());
            while (RuntimeContentManager.UnfinishedObjectLoads() > 0)
            {
                RuntimeContentManager.ProcessQueuedCommands();
                yield return null;
            }

            var ids = RuntimeContentManager.GetObjectIds(Allocator.Persistent);
            var evts = new ManualResetEvent[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                new Thread(LoadThreadFunc).Start((ids[i], evts[i] = new ManualResetEvent(false)));
            }
            WaitHandle.WaitAll(evts);
            Assert.AreEqual(ids.Length * 5, RuntimeContentManager.UnfinishedObjectLoads());
            RuntimeContentManager.ProcessQueuedCommands();
            while (RuntimeContentManager.UnfinishedObjectLoads() > 0)
            {
                RuntimeContentManager.ProcessQueuedCommands();
                yield return null;
            }
            Assert.AreEqual(ids.Length, RuntimeContentManager.CompletedObjectLoads());
            for (int i = 0; i < ids.Length; i++)
            {
                evts[i].Reset();
                new Thread(ReleaseThreadFunc).Start((ids[i], evts[i]));
            }
            WaitHandle.WaitAll(evts);
            while (RuntimeContentManager.CompletedObjectLoads() > 0)
            {
                RuntimeContentManager.ProcessQueuedCommands();
                yield return null;
            }
            ids.Dispose();
        }

        struct LoadObjectJob : IJob
        {
            public UntypedWeakReferenceId id;
            public void Execute()
            {
                RuntimeContentManager.LoadObjectAsync(id);
            }
        }

        struct ReleaseObjectJob : IJob
        {
            public UntypedWeakReferenceId id;
            public void Execute()
            {
                RuntimeContentManager.ReleaseObjectAsync(id);
            }
        }

        [UnityTest]
        public IEnumerator RuntimeContentManager_CanLoadAndReleaseFromJobs()
        {
            yield return new EnterPlayMode();

            Assert.IsTrue(InitializeCatalogForTest());
            while (RuntimeContentManager.UnfinishedObjectLoads() > 0)
            {
                RuntimeContentManager.ProcessQueuedCommands();
                yield return null;
            }

            var ids = RuntimeContentManager.GetObjectIds(Allocator.Persistent);
            var jobs = new NativeArray<JobHandle>(ids.Length, Allocator.Persistent);
            for (int i = 0; i < ids.Length; i++)
            {
                jobs[i] = (new LoadObjectJob { id = ids[i] }).Schedule();
            }
            JobHandle.CompleteAll(jobs);
            Assert.AreEqual(ids.Length, RuntimeContentManager.UnfinishedObjectLoads());
            RuntimeContentManager.ProcessQueuedCommands();
            while (RuntimeContentManager.UnfinishedObjectLoads() > 0)
            {
                RuntimeContentManager.ProcessQueuedCommands();
                yield return null;
            }
            Assert.AreEqual(ids.Length, RuntimeContentManager.CompletedObjectLoads());
            for (int i = 0; i < ids.Length; i++)
            {
                jobs[i] = (new ReleaseObjectJob { id = ids[i] }).Schedule();
            }
            JobHandle.CompleteAll(jobs);
            while (RuntimeContentManager.CompletedObjectLoads() > 0)
            {
                RuntimeContentManager.ProcessQueuedCommands();
                yield return null;
            }
            ids.Dispose();
        }

        IEnumerator AssertCanLoadAndRelease<TObject>(UntypedWeakReferenceId id) where TObject : UnityEngine.Object
        {
            RuntimeContentManager.LoadObjectAsync(id);
            while (RuntimeContentManager.GetObjectLoadingStatus(id) < ObjectLoadingStatus.Completed)
            {
                RuntimeContentManager.ProcessQueuedCommands();
                yield return null;
            }

            Assert.IsTrue(RuntimeContentManager.GetObjectValue<TObject>(id) != null, $"Failed to load material with object id {id} from AssetDatabase.");
            RuntimeContentManager.ReleaseObjectAsync(id);
            RuntimeContentManager.ProcessQueuedCommands();
            Assert.AreEqual(ObjectLoadingStatus.None, RuntimeContentManager.GetObjectLoadingStatus(id));
        }

        [UnityTest]
        public IEnumerator RuntimeContentManager_CanLoadLocalAssets()
        {
            yield return new EnterPlayMode();

            Assert.IsTrue(InitializeCatalogForTest());
            var ids = RuntimeContentManager.GetObjectIds(Allocator.Persistent);
            for(int i = 0; i < ids. Length; i++)
                yield return AssertCanLoadAndRelease<UnityEngine.Object>(ids[i]);
            ids.Dispose();
        }

        bool InitializeCatalogForTest()
        {
            string catalogPath = Path.Combine(TestStreamingAssetsFullPath, RuntimeContentManager.RelativeCatalogPath);
            RuntimeContentManager.Cleanup(out var _);
            RuntimeContentManager.Initialize();
            return RuntimeContentManager.LoadLocalCatalogData(catalogPath, RuntimeContentManager.DefaultContentFileNameFunc, f => $"{TestStreamingAssetsFullPath}/{RuntimeContentManager.DefaultArchivePathFunc(f)}");
        }

        [UnityTest]
        public IEnumerator WeakObjectReference_CanLoadAndRelease()
        {
            yield return new EnterPlayMode();

            Assert.IsTrue(InitializeCatalogForTest());
            var ids = RuntimeContentManager.GetObjectIds(Allocator.Persistent);

            WeakObjectReference<UnityEngine.Object> matRef = default;
            matRef.Id = ids[0];// new UntypedWeakReferenceId { GlobalId = new RuntimeGlobalObjectId { AssetGUID = ids[0] }, GenerationType = WeakReferenceGenerationType.UnityObject};
            matRef.LoadAsync();
            RuntimeContentManager.ProcessQueuedCommands();
            Assert.IsTrue(matRef.LoadingStatus >= ObjectLoadingStatus.Loading);
            while (matRef.LoadingStatus < ObjectLoadingStatus.Completed)
            {
                RuntimeContentManager.ProcessQueuedCommands();
                yield return null;
            }
            Assert.NotNull(matRef.Result, $"Result: {matRef.Result}, Status: {matRef.LoadingStatus}");

            matRef.Release();
            RuntimeContentManager.ProcessQueuedCommands();
            Assert.AreEqual(ObjectLoadingStatus.None, matRef.LoadingStatus);
        }


        [Test]
        public void WeakObjectReference_WhenNotLoaded_LoadingStatus_IsNone()
        {
            WeakObjectReference<UnityEngine.Object> objRef = default;
            Assert.AreEqual(ObjectLoadingStatus.None, objRef.LoadingStatus);
        }

        [Test]
        public void WeakObjectReference_WhenNotLoaded_Value_IsNull()
        {
            WeakObjectReference<UnityEngine.Object> objRef = default;
            Assert.IsNull(objRef.Result);
        }
    }
}
#endif
