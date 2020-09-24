using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.Scenes.Editor.Tests;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Hash128 = UnityEngine.Hash128;

namespace Unity.Scenes.Tests
{
    public class AssetDependencyTrackerTests : ECSTestsFixture
    {
        TestWithTempAssets _Temp;

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            _Temp.TearDown();
        }

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            _Temp.SetUp();
        }

        [Test]
        public void TestMissingAsset()
        {
            using(var test = new AssetDependencyTracker<int>(typeof(TestImporter), "stuff"))
            using(var list = new NativeList<AssetDependencyTracker<int>.Completed>(0, Allocator.Persistent))
            {
                var guid = GUID.Generate();
                test.Add(guid, 1, true);

                test.GetCompleted(list);

                AssertDontExist(list, guid, 1);

                AssetDatabase.Refresh();
                test.GetCompleted(list);
                Assert.AreEqual(0, list.Length);
            }
        }


        // One asset registered with multiple guid & user data
        [Test]
        public void MultiTargetCompletion()
        {
            using(var test = new AssetDependencyTracker<int>(typeof(TestImporter), "stuff"))
            using(var list = new NativeList<AssetDependencyTracker<int>.Completed>(0, Allocator.Persistent))
            {
                var path = _Temp.GetNextPath();
                var guid = WriteFileAndRefresh(path, "boing");

                test.Add(guid, 0, true);
                test.Add(guid, 1, true);
                test.Add(guid, 2, false);

                // Remove this one again
                // (Ensure that we dont get events about user data we already removed)
                test.Remove(guid, 1);

                test.GetCompleted(list);

                Assert.AreEqual(1, test.TotalAssets);
                Assert.AreEqual(0, test.InProgressAssets);

                Assert.AreEqual(2, list.Length);
                Assert.AreEqual(guid, list[0].Asset);
                Assert.AreEqual(guid, list[1].Asset);

                Assert.AreEqual(2, list[0].UserKey);
                Assert.AreEqual(0, list[1].UserKey);

                Assert.AreEqual(list[1].ArtifactID, list[0].ArtifactID);
                Assert.AreNotEqual(default(Hash128), list[0].ArtifactID);
            }
        }

        [Test]
        public void ImbalancedRegistrationThrows()
        {
            using (var test = new AssetDependencyTracker<int>(typeof(TestImporter), "stuff"))
            {
                var guid = GUID.Generate();
                test.Add(guid, 0, true);

                Assert.Throws<ArgumentException>(() =>
                {
                    test.Add(guid, 0, true);
                });
                Assert.Throws<ArgumentException>(() =>
                {
                    test.Remove(guid, 8);
                });
                Assert.Throws<ArgumentException>(() =>
                {
                    test.Remove(default, 0);
                });
            }
        }

        [Test]
        public void RemoveTwiceThrows()
        {
            using (var test = new AssetDependencyTracker<int>(typeof(TestImporter), "stuff"))
            {
                var guid = GUID.Generate();
                test.Add(guid, 0, true);
                test.Remove(guid, 0);

                Assert.Throws<ArgumentException>(() =>
                {
                    test.Remove(guid, 0);
                });
            }
        }

        GUID WriteFileAndRefresh(string path, string contents)
        {
            File.WriteAllText(path, contents);
            AssetDatabase.Refresh();
            return new GUID(AssetDatabase.AssetPathToGUID(path));
        }

        [Test]
        public void TestSyncAssetModifiedDuringImport()
        {
            var path = _Temp.GetNextPath();
            var guid = WriteFileAndRefresh(path, "Boing");

            using (var test = new AssetDependencyTracker<int>(typeof(TestImporter), "stuff"))
            using (var list = new NativeList<AssetDependencyTracker<int>.Completed>(0, Allocator.Persistent))
            {
                File.WriteAllText(path, "Boings");
                test.Add(guid, 1, false);
                test.GetCompleted(list);

                AssertOne(list, guid, 1, "Boings");
            }
        }

        [UnityTest]
        public IEnumerator AsyncChangeIsDetected()
        {
            var path = _Temp.GetNextPath();

            using (var test = new AssetDependencyTracker<int>(typeof(TestImporter), "stuff"))
            using (var list = new NativeList<AssetDependencyTracker<int>.Completed>(0, Allocator.Persistent))
            {
                var guid = WriteFileAndRefresh(path, "a");

                test.Add(guid, 1, true);

                // There is no guarantee on when the change will be detected we have to just wait for it. We should eventually get exactly one change.
                test.GetCompleted(list);
                while (list.IsEmpty)
                {
                    yield return null;
                    test.AddCompleted(list);
                }

                AssertOne(list, guid, 1, "a");
                var hash0 = list[0].ArtifactID;

                WriteFileAndRefresh(path, "b");
                test.GetCompleted(list);
                while (list.IsEmpty)
                {
                    yield return null;
                    test.AddCompleted(list);
                }

                AssertOne(list, guid, 1, "b");
                var hash1 = list[0].ArtifactID;

                Assert.AreNotEqual(hash0, hash1);
            }
        }

        [UnityTest]
        public IEnumerator AsyncRemoveIsDetected()
        {
            var path = _Temp.GetNextPath();

            using (var test = new AssetDependencyTracker<int>(typeof(TestImporter), "stuff"))
            using (var list = new NativeList<AssetDependencyTracker<int>.Completed>(0, Allocator.Persistent))
            {
                var guid = WriteFileAndRefresh(path, "Boings");
                test.Add(guid, 1, true);

                test.GetCompleted(list);
                while (list.IsEmpty)
                {
                    yield return null;
                    test.AddCompleted(list);
                }
                AssertOne(list, guid, 1, "Boings");

                File.Delete(path);
                AssetDatabase.Refresh();
                test.GetCompleted(list);

                AssertDontExist(list, guid, 1);
            }
        }

        static void AssertDontExist(NativeList<AssetDependencyTracker<int>.Completed> list, GUID guid, int userData)
        {
            Assert.AreEqual(1, list.Length, "Completed List must have 1 element");
            Assert.AreEqual(guid, list[0].Asset, "Received GUID doesn't match");
            Assert.AreEqual(userData, list[0].UserKey, "UserData doesn't match");
            Assert.AreEqual(default(Hash128), list[0].ArtifactID, "Artifact should not exist");
        }

        static void AssertOne(NativeList<AssetDependencyTracker<int>.Completed> list, GUID guid, int userData, string content)
        {
            Assert.AreEqual(1, list.Length, "Completed List must have 1 element");
            Assert.AreEqual(guid, list[0].Asset, "Received GUID doesn't match");
            Assert.AreEqual(userData, list[0].UserKey, "UserData doesn't match");
            Assert.AreNotEqual(default(Hash128), list[0].ArtifactID, "Artifact should exist");
            AssetDatabaseCompatibility.GetArtifactPaths(list[0].ArtifactID, out var paths);

            Assert.AreEqual(paths.Length, 2);
            var path = paths.First(p => p.EndsWith("output"));
            Assert.AreEqual(content, File.ReadAllText(path));
        }
    }
}
