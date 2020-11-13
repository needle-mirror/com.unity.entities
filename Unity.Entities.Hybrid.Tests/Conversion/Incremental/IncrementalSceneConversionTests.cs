#if UNITY_2020_2_OR_NEWER
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Tests;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Tests.Conversion
{
    class IncrementalSceneConversionTests
    {
        private TestWithObjects _Objects;
        private World DestinationWorld;
        private World ConversionWorld;

        [SetUp]
        public void SetUp()
        {
            _Objects.SetUp();
            DestinationWorld = new World("Test World");
        }

        [TearDown]
        public void TearDown()
        {
            ConversionWorld?.Dispose();
            DestinationWorld.Dispose();
            _Objects.TearDown();
        }

        [OneTimeSetUp]
        public void SetUpOnce()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            SceneManager.SetActiveScene(scene);
        }

        [OneTimeTearDown]
        public void TearDownOnce()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        void InitializeIncrementalConversion()
        {
            // TODO - if missing these conversion flags, it will cause an exception in ConvertIncrementalInitialize
            // Need better exception
            var settings = new GameObjectConversionSettings(DestinationWorld, ConversionFlags)
            {
                Systems = TestWorldSetup.GetDefaultInitSystemsFromEntitiesPackage(WorldSystemFilterFlags.GameObjectConversion).ToList()
            };
            settings.Systems.Add(typeof(InterceptConvertedGameObjects));
            ConversionWorld = GameObjectConversionUtility.InitializeIncrementalConversion(SceneManager.GetActiveScene(), settings);
        }

        private class InterceptConvertedGameObjects : GameObjectConversionSystem
        {
            public static readonly List<GameObject> GameObjectsConverted = new List<GameObject>();
            protected override void OnUpdate()
            {
                GameObjectsConverted.Clear();
                Entities.ForEach((Transform t) => GameObjectsConverted.Add(t.gameObject));
            }
        }

        private const GameObjectConversionUtility.ConversionFlags ConversionFlags =
            GameObjectConversionUtility.ConversionFlags.GameViewLiveLink |
            GameObjectConversionUtility.ConversionFlags.AddEntityGUID;

        static void CheckAgainstFullConversion(World destinationWorld)
        {
            var dstEntityManager = destinationWorld.EntityManager;
            using (var fullConversionWorld = new World("FullConversion"))
            {
                using (var blobAssetStore = new BlobAssetStore())
                {
                    var conversionSettings = GameObjectConversionSettings.FromWorld(fullConversionWorld, blobAssetStore);
                    conversionSettings.ConversionFlags = ConversionFlags;

                    GameObjectConversionUtility.ConvertScene(SceneManager.GetActiveScene(), conversionSettings);

                    const EntityManagerDifferOptions options =
                        EntityManagerDifferOptions.IncludeForwardChangeSet |
                        EntityManagerDifferOptions.ValidateUniqueEntityGuid;

                    using (var blobAssetCache = new BlobAssetCache(Allocator.TempJob))
                    {
                        EntityDiffer.PrecomputeBlobAssetCache(fullConversionWorld.EntityManager, EntityManagerDiffer.EntityGuidQueryDesc, blobAssetCache);
                        using (var changes = EntityDiffer.GetChanges(
                            dstEntityManager,
                            fullConversionWorld.EntityManager,
                            options,
                            EntityManagerDiffer.EntityGuidQueryDesc,
                            blobAssetCache,
                            Allocator.TempJob
                        ))
                        {
                            Assert.IsFalse(changes.AnyChanges, "Full conversion and incremental conversion do not match!");
                        }
                    }
                }
            }
        }


        [Test]
        public void IncrementalConversion_WhenAddingNewGameObjectToSubScene_MatchesFullConversion()
        {
            InitializeIncrementalConversion();
            var go = _Objects.CreateGameObject("Hello");
            var args = new IncrementalConversionBatch
            {
                ReconvertHierarchyInstanceIds = new NativeArray<int>(new[] {go.GetInstanceID()}, Allocator.TempJob),
                ChangedComponents = new List<Component> {go.transform}
            };
            args.EnsureFullyInitialized();
            GameObjectConversionUtility.ConvertIncremental(ConversionWorld, ConversionFlags, ref args);
            args.Dispose();
            CheckAgainstFullConversion(DestinationWorld);
        }

        [Test]
        public void IncrementalConversion_WhenChangingTransformWithoutDependency_DoesNotCauseReconversion()
        {
            var root = _Objects.CreateGameObject("Root");
            var child = _Objects.CreateGameObject("Child");
            var a = child.AddComponent<DependsOnTransformTestAuthoring>();
            a.Dependency = root.transform;
            a.SkipDependency = true;
            root.transform.position = new Vector3(0, 0, 0);
            child.transform.SetParent(root.transform);
            InitializeIncrementalConversion();

            // change the parent's position
            root.transform.position = new Vector3(0, 1, 2);
            var args = new IncrementalConversionBatch
            {
                ChangedComponents = new List<Component> { root.transform }
            };
            args.EnsureFullyInitialized();
            GameObjectConversionUtility.ConvertIncremental(ConversionWorld, ConversionFlags, ref args);
            args.Dispose();

            // because there is no dependency on the transform of the child, we do not reconvert it and have invalid
            // data
            var t = DestinationWorld.EntityManager.CreateEntityQuery(typeof(DependsOnTransformTestAuthoring.Component))
                .GetSingleton<DependsOnTransformTestAuthoring.Component>();
            Assert.AreNotEqual(t.LocalToWorld, child.transform.localToWorldMatrix);
        }

        [Test]
        public void IncrementalConversion_NoChanges_DoesNotCauseReconversion()
        {
            for (int i = 0; i < 10; i++)
                _Objects.CreateGameObject("Hello" + i);
            InitializeIncrementalConversion();

            var args = new IncrementalConversionBatch();
            args.EnsureFullyInitialized();

            // this is necessary: if nothing is updated, the interception system never updates and we never clear the
            // list.
            InterceptConvertedGameObjects.GameObjectsConverted.Clear();
            GameObjectConversionUtility.ConvertIncremental(ConversionWorld, ConversionFlags, ref args);
            args.Dispose();

            Assert.IsEmpty(InterceptConvertedGameObjects.GameObjectsConverted);
        }

        [Test]
        public void IncrementalConversion_ChangeOneObject_OnlyChangesThatObject()
        {
            var root = _Objects.CreateGameObject("Root");
            var child = _Objects.CreateGameObject("Child");
            child.transform.SetParent(root.transform);

            InitializeIncrementalConversion();

            {
                var args = new IncrementalConversionBatch
                {
                    ChangedInstanceIds = new NativeArray<int>(new[] {root.GetInstanceID()}, Allocator.TempJob),
                };
                args.EnsureFullyInitialized();
                GameObjectConversionUtility.ConvertIncremental(ConversionWorld, ConversionFlags, ref args);
                args.Dispose();
            }
            CollectionAssert.AreEquivalent(InterceptConvertedGameObjects.GameObjectsConverted, new [] { root });

            {
                var args = new IncrementalConversionBatch
                {
                    ChangedInstanceIds = new NativeArray<int>(new[] {child.GetInstanceID()}, Allocator.TempJob)
                };
                args.EnsureFullyInitialized();
                GameObjectConversionUtility.ConvertIncremental(ConversionWorld, ConversionFlags, ref args);
                args.Dispose();
            }
            CollectionAssert.AreEquivalent(InterceptConvertedGameObjects.GameObjectsConverted, new [] { child });
        }

        [Test]
        public void IncrementalConversion_ReconvertHierarchy_ReconvertsChildren()
        {
            var root = _Objects.CreateGameObject("Root");
            var child = _Objects.CreateGameObject("Child");
            child.transform.SetParent(root.transform);

            InitializeIncrementalConversion();

            {
                var args = new IncrementalConversionBatch
                {
                    ReconvertHierarchyInstanceIds = new NativeArray<int>(new[] {root.GetInstanceID()}, Allocator.TempJob),
                };
                args.EnsureFullyInitialized();
                GameObjectConversionUtility.ConvertIncremental(ConversionWorld, ConversionFlags, ref args);
                args.Dispose();
            }
            CollectionAssert.AreEquivalent(InterceptConvertedGameObjects.GameObjectsConverted, new [] { root, child });
        }

        [Test]
        public void IncrementalConversion_ConvertedEntitiesAccessor_ReturnsAllEntities()
        {
            var root = _Objects.CreateGameObject("Root");

            InitializeIncrementalConversion();

            {
                var args = new IncrementalConversionBatch
                {
                    ChangedInstanceIds = new NativeArray<int>(new[] {root.GetInstanceID()}, Allocator.TempJob),
                };
                args.EnsureFullyInitialized();
                GameObjectConversionUtility.ConvertIncremental(ConversionWorld, ConversionFlags, ref args);
                args.Dispose();
            }
            CollectionAssert.AreEquivalent(InterceptConvertedGameObjects.GameObjectsConverted, new [] { root });
        }
    }
}
#endif
