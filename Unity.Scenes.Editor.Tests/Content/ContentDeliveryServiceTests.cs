using NUnit.Framework;
using System;
using System.Collections;
using System.IO;
using Unity.Collections;
using Unity.Entities.Content;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Scenes.Editor.Tests
{
    [TestFixture]
    public class ContentDeliveryServiceTests
    {
        Mathematics.Random random = new Mathematics.Random();
        string DataPath => $"{Application.persistentDataPath}/{nameof(ContentDeliveryServiceTests)}/Data";
        string BuildPath => $"{DataPath}/Build";
        string UpdatePath => $"{DataPath}/Update";
        string PublishPath => $"{DataPath}/Publish";
        string CachePath => $"{DataPath}/Cache";

        [OneTimeSetUp]
        public void Setup()
        {
            random.InitState(1234);
            if(Directory.Exists(DataPath))
                Directory.Delete(DataPath, true);
            Directory.CreateDirectory(DataPath);
            Directory.CreateDirectory(BuildPath);
            Directory.CreateDirectory(UpdatePath);
            Directory.CreateDirectory(PublishPath);
            BuildTestData(10, 256, 8, "start");
            BuildTestData(10, 256, 8, "extra1");
            BuildTestData(10, 256, 8, "update");
            RemoteContentCatalogBuildUtility.PublishContent(BuildPath, PublishPath, f => new string[] { f.Substring(f.LastIndexOf('_') + 1) });
        }

        void BuildTestData(int fileCount, int fileSize, int changeCount, string setName)
        {
            try
            {
                for (int i = 0; i < fileCount; i++)
                { 
                    CreateTestFile($"{BuildPath}/file{i}_{setName}", fileSize);
                    if (File.Exists($"{UpdatePath}/file{i}_{setName}"))
                        File.Delete($"{UpdatePath}/file{i}_{setName}");
                    File.Copy($"{BuildPath}/file{i}_{setName}", $"{UpdatePath}/file{i}_{setName}", true);
                    ModifyTestFile($"{UpdatePath}/file{i}_{setName}", changeCount);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            Directory.Delete(DataPath, true);
        }

        void ModifyTestFile(string path, int changeCount)
        {
            var data = File.ReadAllBytes(path);
            for (int i = 0; i < changeCount; i++)
            {
                var ii = random.NextUInt() % (data.Length - 1);
                data[ii] = (byte)(random.NextInt() % 255);
            }
            File.Delete(path);
            File.WriteAllBytes(path, data);
        }

        bool CompareFiles(string fileA, string fileB)
        {
            var dataA = File.ReadAllBytes(fileA);
            var dataB = File.ReadAllBytes(fileB);
            if(dataA.Length != dataB.Length)
                return false;
            for (int i = 0; i < dataA.Length; i++)
                if(dataA[i] != dataB[i])
                    return false;
            return true;
        }

        void CreateTestFile(string path, int length)
        {
            using (var fs = File.OpenWrite(path))
            {
                var data = new byte[length];
                for(int i = 0; i < length; i++)
                    data[i] = (byte)(random.NextInt() % 255);
                fs.Write(data, 0, length);
            }
        }

        ContentDeliveryService CreateService()
        {
            var cds = new ContentDeliveryService();
            cds.AddDownloadService(new ContentDownloadService("default", CachePath, 1, 5));
            return cds;
        }

        IEnumerator UpdateService(ContentDeliveryService cds)
        {
            var update = new ContentDeliveryGlobalState.ContentUpdateContext { cachePath = CachePath, remoteUrlRoot = $"file://{PublishPath}/", initialContentSet = "start" };
            ContentDeliveryGlobalState.ContentUpdateState updateState = ContentDeliveryGlobalState.ContentUpdateState.None;
            while (!update.Update(cds, ref updateState))
            {
                cds.Process();
                yield return null;
            }
            update = null;
        }

        IEnumerator DownloadFile(ContentDeliveryService cds, RemoteContentId id)
        {
            cds.DeliverContent(id);
            while (cds.GetDeliveryStatus(id).State < ContentDeliveryService.DeliveryState.ContentDownloaded)
            {
                cds.Process();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator WhenContentDelivered_WithRemoteId_FileExistsInCache()
        {
            using (var cds = CreateService())
            {
                yield return UpdateService(cds);
                var id = new RemoteContentId($"file3_extra1");
                yield return DownloadFile(cds, id);
                var status = cds.GetDeliveryStatus(id);
                Assert.AreEqual(ContentDeliveryService.DeliveryState.ContentDownloaded, status.State);
                Assert.IsTrue(File.Exists(status.DownloadStatus.LocalPath.ToString()));
                Assert.IsTrue(CompareFiles(status.DownloadStatus.LocalPath.ToString(), $"{BuildPath}/file3_extra1"), "Downloaded file does not match built file.");
                File.Delete(status.DownloadStatus.LocalPath.ToString());
            }
        }

        [UnityTest]
        public IEnumerator WhenContentDelivered_WithInvalidRemoteId_StatusFailed()
        {
            using (var cds = CreateService())
            {
                yield return UpdateService(cds);
                var id = new RemoteContentId($"invalid");
                yield return DownloadFile(cds, id);
                var status = cds.GetDeliveryStatus(id);
                Assert.AreEqual(ContentDeliveryService.DeliveryState.Failed, status.State);
            }
        }

        [UnityTest]
        public IEnumerator WhenContentUpdated_FilesMatchUpdatedBuild()
        {
            using (var cds = CreateService())
            {
                yield return UpdateService(cds);
                var id = cds.DeliverContent("update");
                while (cds.GetDeliveryStatus(id).State < ContentDeliveryService.DeliveryState.ContentDownloaded)
                {
                    cds.Process();
                    yield return null;
                }
                var res = new NativeList<ContentDeliveryService.DeliveryStatus>(10, Allocator.Temp);
                cds.GetDeliveryStatus(id, ref res);
                foreach (var s in res)
                {
                    Assert.IsTrue(File.Exists(s.DownloadStatus.LocalPath.ToString()));
                    var origFile = $"{BuildPath}/{s.ContentId.Name}";
                    Assert.IsTrue(CompareFiles(origFile, s.DownloadStatus.LocalPath.ToString()));
                }
                res.Dispose();
                cds.CancelAllDeliveries();

                RemoteContentCatalogBuildUtility.PublishContent(UpdatePath, PublishPath, f => new string[] { f.Substring(f.LastIndexOf('_') + 1) });
                yield return UpdateService(cds);
                id = cds.DeliverContent("update");
                while (cds.GetDeliveryStatus(id).State < ContentDeliveryService.DeliveryState.ContentDownloaded)
                {
                    cds.Process();
                    yield return null;
                }

                res = new NativeList<ContentDeliveryService.DeliveryStatus>(10, Allocator.Temp);
                cds.GetDeliveryStatus(id, ref res);
                foreach (var s in res)
                {
                    Assert.IsTrue(File.Exists(s.DownloadStatus.LocalPath.ToString()));
                    var origFile = $"{UpdatePath}/{s.ContentId.Name}";
                    Assert.IsTrue(CompareFiles(origFile, s.DownloadStatus.LocalPath.ToString()));
                }
                res.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator WhenContentDeliveryCancelled_StateIsCancelled_FileDoesNotExist()
        {
            using (var cds = CreateService())
            {
                yield return UpdateService(cds);

                cds.AddDownloadService(new ContentDownloadService("default", CachePath, 50, 5, () => new NonCompletingDownloadOperation()));

                var id = new RemoteContentId($"file3_extra1");
                cds.DeliverContent(id);
                while (cds.GetDeliveryStatus(id).State < ContentDeliveryService.DeliveryState.DownloadingContent)
                {
                    cds.Process();
                    yield return null;
                }

                var status = cds.GetDeliveryStatus(id);
                Assert.AreEqual(ContentDeliveryService.DeliveryState.DownloadingContent, status.State);
                Assert.IsTrue(cds.CancelDelivery(id));
                status = cds.GetDeliveryStatus(id);
                Assert.AreEqual(ContentDeliveryService.DeliveryState.Cancelled, status.State);
                Assert.IsFalse(File.Exists(status.DownloadStatus.LocalPath.ToString()));

                //make sure re-downloading a cancelled id works
                cds.AddDownloadService(new ContentDownloadService("default", CachePath, 60));
                yield return DownloadFile(cds, id);
                status = cds.GetDeliveryStatus(id);
                Assert.AreEqual(ContentDeliveryService.DeliveryState.ContentDownloaded, status.State);
                Assert.IsTrue(File.Exists(status.DownloadStatus.LocalPath.ToString()));
                File.Delete(status.DownloadStatus.LocalPath.ToString());
            }
        }
        public static object[] uriTestCases =
        {
                new object[]{"sdfsdfqsfd", false },
                new object[]{"http://dsasdg/", true },
                new object[]{ "https://dsasdg/", true },
                new object[]{"ftp://adfadf/", true},
                new object[]{"file://c:/dir/sdsdfasd/", true},
                new object[]{"file://dir/sdsdfasd/", true},
                new object[]{"file:/dir//sdsdfasd/", false},
                new object[]{"file://../dir//sdsdfasd/", false},
                new object[]{"blah://qfasdfqdsf/qsdf/", false },
                new object[]{"http://??", false },
                new object[]{"https:\\dfqasdf/", false },
                new object[]{"https:://adfqdf/", false },
                new object[]{"https:://c:/adfqdf/", false },
                new object[]{"localhost", false },
                new object[]{"http://localhost/", true },
                new object[]{"http://localhost:8000/", true },
                new object[]{"http://local:host:8000/", false },
                new object[]{"http://192.168.1.1/", true },
                new object[]{"192.168.1.1", false },
                new object[]{"http://192.168.1.1:8000", true},
                new object[]{"https://d3szac7xzgrbxg.cloudfront.net/content-1/", true }
            };

        [Test]
        [TestCaseSource(nameof(uriTestCases))]
        public void UriValidation(string uri, bool expected)
        {
            Assert.AreEqual(expected, ContentDeliveryGlobalState.IsValidURLRoot(uri), $"Failed with test case uri '{uri}'");
        }
    }
}
