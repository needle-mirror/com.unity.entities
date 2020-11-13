using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Conversion;
using UnityEngine;

namespace Unity.Entities.Tests.Conversion
{
    class ConversionDependencyTests : ConversionTestFixtureBase
    {
        ConversionDependencies m_Dependencies;
        [SetUp]
        public new void Setup()
        {
            m_Dependencies = new ConversionDependencies(true);
        }

        [TearDown]
        public new void TearDown()
        {
            m_Dependencies.Dispose();
        }

        static void AssertDependencyExists(DependencyTracker tracker, int key, GameObject dependent)
        {
            var dependents = tracker.GetAllDependents(key);
            int id = dependent.GetInstanceID();
            while (dependents.MoveNext())
            {
                if (id == dependents.Current)
                {
                    return;
                }
            }
            Assert.Fail("The dependent wasn't registered to the key of the dependency.");
        }

        [Test]
        public void GameObjectDependencies_AreCollected_WhenLiveLinked()
        {
            var goA = CreateGameObject("A");
            var goB = CreateGameObject("B");
            var dep = new ConversionDependencies(true);
            try
            {
                dep.DependOnGameObject(goA, goB);
                AssertDependencyExists(dep.GameObjectDependencyTracker, goB.GetInstanceID(), goA);
            }
            finally
            {
                dep.Dispose();
            }
        }

        [Test]
        public void GameObjectDependencies_WithInvalidDependent_Throws()
            => Assert.Throws<ArgumentNullException>(() => m_Dependencies.DependOnGameObject(null, CreateGameObject("Test")));

        private void CalculateDependents(int instanceId, NativeHashSet<int> outDependents)
        {
            using (var arr = new NativeArray<int>(1, Allocator.Persistent) {[0] = instanceId})
            {
                m_Dependencies.CalculateDependents(arr, outDependents);
            }
        }

        [Test]
        public void GameObjectDependencies_CalculateDependents_TransitiveDependentsAreIncluded()
        {
            var goA = CreateGameObject("A");
            var goB = CreateGameObject("B");
            var goC = CreateGameObject("C");

            m_Dependencies.DependOnGameObject(goA, goB);
            m_Dependencies.DependOnGameObject(goB, goC);
            int instanceId = goC.GetInstanceID();
            var dependents = new NativeHashSet<int>(0, Allocator.Temp);
            CalculateDependents(instanceId, dependents);
            Assert.IsTrue(dependents.Contains(goA.GetInstanceID()), "Failed to include transitive dependency");
            Assert.IsTrue(dependents.Contains(goB.GetInstanceID()), "Failed to include direct dependency");
            Assert.IsTrue(dependents.Contains(goC.GetInstanceID()), "Failed to include self among dependents");
            Assert.AreEqual(3, dependents.Count());
        }

        [Test]
        public void GameObjectDependencies_DependOnComponent_RegistersGameObjectDependency()
        {
            var goA = CreateGameObject("A");
            var goB = CreateGameObject("B");

            m_Dependencies.DependOnComponent(goA, goB.transform);
            int instanceId = goB.GetInstanceID();
            var dependents = new NativeHashSet<int>(0, Allocator.Temp);
            CalculateDependents(instanceId, dependents);
            Assert.IsTrue(dependents.Contains(goA.GetInstanceID()), "Failed to include direct dependency");
            Assert.IsTrue(dependents.Contains(goB.GetInstanceID()), "Failed to include self among dependents");
            Assert.AreEqual(2, dependents.Count());
        }

        [Test]
        public void GameObjectDependencies_ClearDependencies_RemovesDependencies()
        {
            var goA = CreateGameObject("A");
            var goB = CreateGameObject("B");
            var goC = CreateGameObject("C");

            m_Dependencies.DependOnGameObject(goA, goB);
            m_Dependencies.DependOnGameObject(goB, goC);
            var instances = new NativeArray<int>(1, Allocator.Temp) {[0] = goA.GetInstanceID()};
            m_Dependencies.ClearDependencies(instances);

            int instanceId = goC.GetInstanceID();
            var dependents = new NativeHashSet<int>(0, Allocator.Temp);
            CalculateDependents(instanceId, dependents);
            Assert.IsFalse(dependents.Contains(goA.GetInstanceID()), "Failed to remove dependency");
            Assert.IsTrue(dependents.Contains(goB.GetInstanceID()), "Failed to include direct dependency");
            Assert.IsTrue(dependents.Contains(goC.GetInstanceID()), "Failed to include self among dependents");
            Assert.AreEqual(2, dependents.Count());
        }

        [Test]
        public void GameObjectDependencies_CanRegisterDependencyOnTrueNull()
        {
            var goA = CreateGameObject("A");
            Assert.DoesNotThrow(() => m_Dependencies.DependOnGameObject(goA, null));
            Assert.DoesNotThrow(() => m_Dependencies.DependOnComponent(goA, null));
        }

        [Test]
        public void GameObjectDependencies_CanRegisterDependencyOnFakeNullGameObject()
        {
            var goA = CreateGameObject("A");
            var goB = new GameObject("B");
            UnityEngine.Object.DestroyImmediate(goB);
            Assert.IsTrue(goB == null);

            Assert.DoesNotThrow(() => m_Dependencies.DependOnGameObject(goA, goB));

            var instanceId = goB.GetInstanceID();
            var dependents = new NativeHashSet<int>(0, Allocator.Temp);
            CalculateDependents(instanceId, dependents);
            Assert.IsTrue(dependents.Contains(goA.GetInstanceID()), "Failed to include direct dependency");
            Assert.IsTrue(dependents.Contains(goB.GetInstanceID()), "Failed to include self among dependents");
            Assert.AreEqual(2, dependents.Count());
        }

        [Test]
        public void GameObjectDependencies_CanRegisterDependencyOnFakeNullComponent()
        {
            var goA = CreateGameObject("A");
            var goB = CreateGameObject("B");
            var c = goB.AddComponent<DependencyTestAuthoring>();
            UnityEngine.Object.DestroyImmediate(c);
            Assert.IsTrue(c == null);

            Assert.DoesNotThrow(() => m_Dependencies.DependOnComponent(goA, c));
        }

        [Test]
        public void GameObjectDependencies_RegisterComponentDependencyTracking_EnablesComponentTracker()
        {
            Assert.IsFalse(m_Dependencies.TryGetComponentDependencyTracker<Transform>(out _));
            m_Dependencies.RegisterComponentTypeForDependencyTracking<Transform>();
            Assert.True(m_Dependencies.TryGetComponentDependencyTracker<Transform>(out _));
        }

        [Test]
        public void GameObjectDependencies_RegisterComponentDependencyTracking_CanBeCalledMultipleTimes()
        {
            Assert.IsFalse(m_Dependencies.TryGetComponentDependencyTracker<Transform>(out _));
            m_Dependencies.RegisterComponentTypeForDependencyTracking<Transform>();
            m_Dependencies.RegisterComponentTypeForDependencyTracking<Transform>();
            Assert.True(m_Dependencies.TryGetComponentDependencyTracker<Transform>(out _));
        }

        [Test]
        public void GameObjectDependencies_DependOnComponent_WithComponentDependencyTracking_RegistersGameObjectDependency()
        {
            var goA = CreateGameObject("A");
            var goB = CreateGameObject("B");

            m_Dependencies.RegisterComponentTypeForDependencyTracking<Transform>();

            m_Dependencies.DependOnComponent(goA, goB.transform);
            int instanceId = goB.GetInstanceID();
            var dependents = new NativeHashSet<int>(0, Allocator.Temp);
            CalculateDependents(instanceId, dependents);
            Assert.IsTrue(dependents.Contains(goA.GetInstanceID()), "Failed to include direct dependency");
            Assert.IsTrue(dependents.Contains(goB.GetInstanceID()), "Failed to include self among dependents");
            Assert.AreEqual(2, dependents.Count());
            dependents.Clear();

            Assert.IsTrue(m_Dependencies.TryGetComponentDependencyTracker<Transform>(out var tracker));

            Assert.IsTrue(tracker.HasDependents(goB.GetInstanceID()));
            Assert.IsFalse(tracker.HasDependents(goB.transform.GetInstanceID()));

            var instances = new NativeArray<int>(1, Allocator.Temp) {[0] = instanceId};
            tracker.CalculateDirectDependents(instances, dependents);
            Assert.IsTrue(dependents.Contains(goA.GetInstanceID()), "Failed to include direct dependency");
            Assert.AreEqual(1, dependents.Count());
        }

        private void RegisterAssetDependencyTest(UnityEngine.Object asset)
        {
            var go = CreateGameObject("A");
            Assert.DoesNotThrow(() => m_Dependencies.DependOnAsset(go, asset));
            AssertDependencyExists(m_Dependencies.AssetDependencyTracker, asset.GetInstanceID(), go);

            var assets = new NativeArray<int>(1, Allocator.Temp) {[0] = asset.GetInstanceID()};
            var results = new NativeHashSet<int>(1, Allocator.Temp);
            m_Dependencies.CalculateAssetDependents(assets, results);
            var r = results.ToNativeArray(Allocator.Temp);
            Assert.AreEqual(1, r.Length);
            Assert.AreEqual(go.GetInstanceID(), r[0]);
        }

        [Test]
        public void AssetDependencies_AreCollected()
        {
            var prefab = LoadPrefab("Prefab");
            RegisterAssetDependencyTest(prefab);
        }

        [Test]
        public void AssetDependencies_CanRegisterDependencyOnTrueNull()
        {
            var goA = CreateGameObject("A");
            Assert.DoesNotThrow(() => m_Dependencies.DependOnAsset(goA, null));
        }

        [Test]
        public void AssetDependencies_CanRegisterDependencyOnFakeNullAsset()
        {
            var texture = new Texture2D(1, 1);
            UnityEngine.Object.DestroyImmediate(texture);
            RegisterAssetDependencyTest(texture);
        }

        [Test]
        public void AssetDependencies_CanRegisterDependencyOnSceneAsset()
        {
            var texture = new Texture2D(1, 1);
            RegisterAssetDependencyTest(texture);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
