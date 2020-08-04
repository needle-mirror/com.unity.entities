using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core.Compression;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.IO.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
#if !NET_DOTS
using System.Linq;
#endif

namespace Unity.Scenes
{
    struct AsyncLoadSceneData
    {
        public EntityManager EntityManager;
        public int ExpectedObjectReferenceCount;
        public int SceneSize;
        public int CompressedSceneSize;
        public string ResourcesPathObjRefs;
        public string ScenePath;
        public Codec Codec;
        public bool BlockUntilFullyLoaded;
        public NativeArray<Entities.Hash128> Dependencies;

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        public PostLoadCommandBuffer PostLoadCommandBuffer;
#endif
    }

    unsafe class AsyncLoadSceneOperation
    {
        public enum LoadingStatus
        {
            Completed,
            NotStarted,
            WaitingForAssetBundleLoad,
            WaitingForAssetLoad,
            WaitingForResourcesLoad,
            WaitingForEntitiesLoad,
            WaitingForSceneDeserialization
        }

        public override string ToString()
        {
            return $"AsyncLoadSceneJob({_ScenePath})";
        }

        unsafe struct FreeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public void* ptr;
            public Allocator allocator;
            public ReadHandle readHandle;

            public void Execute()
            {
                UnsafeUtility.Free(ptr, allocator);
                if(readHandle.IsValid())
                    readHandle.Dispose();
            }
        }

        public void Dispose()
        {
            if (_LoadingStatus == LoadingStatus.Completed)
            {
                new FreeJob { ptr = _FileContent, allocator = Allocator.Persistent }.Schedule();
            }
            else if (_LoadingStatus == LoadingStatus.WaitingForResourcesLoad || _LoadingStatus == LoadingStatus.WaitingForEntitiesLoad)
            {
                new FreeJob { ptr = _FileContent, allocator = Allocator.Persistent }.Schedule(_ReadHandle.JobHandle);
            }
            else if (_LoadingStatus == LoadingStatus.WaitingForSceneDeserialization)
            {
                _EntityManager.ExclusiveEntityTransactionDependency.Complete();
                new FreeJob { ptr = _FileContent, allocator = Allocator.Persistent }.Schedule();
            }

#if !UNITY_DOTSRUNTIME
            if (_SceneBundleHandles != null)
            {
                foreach (var h in _SceneBundleHandles)
                    h.Release();
            }
#endif

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            _Data.PostLoadCommandBuffer?.Dispose();
#endif
        }

        struct AsyncLoadSceneJob : IJob
        {
            public GCHandle                     LoadingOperationHandle;
#if !UNITY_DOTSRUNTIME
            public GCHandle                     ObjectReferencesHandle;
#endif
            public ExclusiveEntityTransaction   Transaction;
            [NativeDisableUnsafePtrRestriction]
            public byte*                        FileContent;

            static readonly ProfilerMarker k_ProfileDeserializeWorld = new ProfilerMarker("AsyncLoadSceneJob.DeserializeWorld");

            public void Execute()
            {
                var loadingOperation = (AsyncLoadSceneOperation)LoadingOperationHandle.Target;
                LoadingOperationHandle.Free();

                object[] objectReferences = null;
#if !UNITY_DOTSRUNTIME
                objectReferences = (object[]) ObjectReferencesHandle.Target;
                ObjectReferencesHandle.Free();
#endif

                try
                {
                    using (var reader = new MemoryBinaryReader(FileContent))
                    {
                        k_ProfileDeserializeWorld.Begin();
                        SerializeUtility.DeserializeWorld(Transaction, reader, objectReferences);
                        k_ProfileDeserializeWorld.End();
                    }
                }
                catch (Exception exc)
                {
                    loadingOperation._LoadingFailure = exc.Message;
                    loadingOperation._LoadingException = exc;
                }
            }
        }

        AsyncLoadSceneData      _Data;
        string                  _ScenePath => _Data.ScenePath;
        int                     _SceneSize => _Data.SceneSize;
        int                     _ExpectedObjectReferenceCount => _Data.ExpectedObjectReferenceCount;
        string                  _ResourcesPathObjRefs => _Data.ResourcesPathObjRefs;
        ref EntityManager           _EntityManager => ref _Data.EntityManager;
        bool                    _BlockUntilFullyLoaded => _Data.BlockUntilFullyLoaded;

#if !UNITY_DOTSRUNTIME
        ReferencedUnityObjects  _ResourceObjRefs;
        List<SceneBundleHandle> _SceneBundleHandles;
        AssetBundleRequest      _AssetRequest;
#endif

        LoadingStatus           _LoadingStatus;
        string                  _LoadingFailure;
        Exception               _LoadingException;

        byte*                    _FileContent;
        ReadHandle               _ReadHandle;

        private double _StartTime;

        public AsyncLoadSceneOperation(AsyncLoadSceneData asyncLoadSceneData)
        {
            _Data = asyncLoadSceneData;
            _LoadingStatus = LoadingStatus.NotStarted;
        }

        public bool IsCompleted
        {
            get
            {
                return _LoadingStatus == LoadingStatus.Completed;
            }
        }

        public string ErrorStatus
        {
            get
            {
                if (_LoadingStatus == LoadingStatus.Completed)
                    return _LoadingFailure;
                else
                    return null;
            }
        }

        public Exception Exception => _LoadingException;

#if !UNITY_DOTSRUNTIME
        public List<SceneBundleHandle> StealBundles()
        {
            var tmp = _SceneBundleHandles;
            _SceneBundleHandles = null;
            return tmp;
        }
#endif

        private void UpdateBlocking()
        {
            if (_LoadingStatus == LoadingStatus.Completed)
                return;
            if (_SceneSize == 0)
                return;

            try
            {
                Assert.IsFalse(string.IsNullOrEmpty(_ScenePath));
                _StartTime = Time.realtimeSinceStartup;
                ReadCommand cmd;

                _FileContent = (byte*)UnsafeUtility.Malloc(_SceneSize, 16, Allocator.Persistent);
                cmd.Buffer = _FileContent;
                cmd.Offset = 0;
                cmd.Size = _SceneSize;

#if ENABLE_PROFILER && UNITY_2020_2_OR_NEWER
                // When AsyncReadManagerMetrics are available, mark up the file read for more informative IO metrics.
                // Metrics can be retrieved by AsyncReadManagerMetrics.GetMetrics
                _ReadHandle = AsyncReadManager.Read(_ScenePath, &cmd, 1, subsystem: AssetLoadingSubsystem.EntitiesScene);
#else
                _ReadHandle = AsyncReadManager.Read(_ScenePath, &cmd, 1);
#endif

#if !UNITY_DOTSRUNTIME
                if (_ExpectedObjectReferenceCount != 0)
                {
                    if (SceneBundleHandle.UseAssetBundles)
                    {
                        _SceneBundleHandles = SceneBundleHandle.LoadSceneBundles(_ResourcesPathObjRefs, _Data.Dependencies, true);
                        if(_SceneBundleHandles.Count > 0)
                            _ResourceObjRefs = _SceneBundleHandles[0].AssetBundle?.LoadAsset<ReferencedUnityObjects>(Path.GetFileName(_ResourcesPathObjRefs));
                    }
                    else
                    {
#if UNITY_EDITOR
                        var resourceRequests = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(_ResourcesPathObjRefs);
                        _ResourceObjRefs = (ReferencedUnityObjects)resourceRequests[0];
#endif
                    }
                }
#endif
                ScheduleSceneRead();
                _EntityManager.EndExclusiveEntityTransaction();
                PostProcessScene();
            }
            catch (Exception e)
            {
                _LoadingFailure = e.Message;
                _LoadingException = Exception;
            }
            _LoadingStatus = LoadingStatus.Completed;
        }

        private void UpdateAsync()
        {
            //@TODO: Try to overlap Resources load and entities scene load

            // Begin Async resource load
            if (_LoadingStatus == LoadingStatus.NotStarted)
            {
                if (_SceneSize == 0)
                    return;

                try
                {
                    Assert.IsFalse(string.IsNullOrEmpty(_ScenePath));
                    _StartTime = Time.realtimeSinceStartup;
                    ReadCommand cmd;

                    _FileContent = (byte*) UnsafeUtility.Malloc(_SceneSize, 16, Allocator.Persistent);
                    cmd.Buffer = _FileContent;
                    cmd.Offset = 0;
                    cmd.Size = _SceneSize;

#if ENABLE_PROFILER && UNITY_2020_2_OR_NEWER
                    // When AsyncReadManagerMetrics are available, mark up the file read for more informative IO metrics.
                    // Metrics can be retrieved by AsyncReadManagerMetrics.GetMetrics
                    _ReadHandle = AsyncReadManager.Read(_ScenePath, &cmd, 1, subsystem: AssetLoadingSubsystem.EntitiesScene);
#else
                    _ReadHandle = AsyncReadManager.Read(_ScenePath, &cmd, 1);
#endif

#if !UNITY_DOTSRUNTIME
                    if (_ExpectedObjectReferenceCount != 0)
                    {
                        if (SceneBundleHandle.UseAssetBundles)
                        {
                            _SceneBundleHandles = SceneBundleHandle.LoadSceneBundles(_ResourcesPathObjRefs, _Data.Dependencies, false);
                            _LoadingStatus = LoadingStatus.WaitingForAssetBundleLoad;
                        }
                        else
                        {
#if UNITY_EDITOR
                            var resourceRequests = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(_ResourcesPathObjRefs);
                            _ResourceObjRefs = (ReferencedUnityObjects)resourceRequests[0];
                            _LoadingStatus = LoadingStatus.WaitingForResourcesLoad;
#endif
                        }
                    }
                    else
                    {
                        _LoadingStatus = LoadingStatus.WaitingForEntitiesLoad;
                    }
#else
                    _LoadingStatus = LoadingStatus.WaitingForEntitiesLoad;
#endif
                }
                catch (Exception e)
                {
                    _LoadingFailure = e.Message;
                    _LoadingException = e;
                    _LoadingStatus = LoadingStatus.Completed;
                }
            }

#if !UNITY_DOTSRUNTIME
            // Once async asset bundle load is done, we can read the asset
            if (_LoadingStatus == LoadingStatus.WaitingForAssetBundleLoad)
            {
                string error = null;
                if (SceneBundleHandle.CheckLoadingStatus(_SceneBundleHandles, ref error))
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        _LoadingFailure = error;
                        _LoadingStatus = LoadingStatus.Completed;
                    }
                    var fileName = Path.GetFileName(_ResourcesPathObjRefs);
                    _AssetRequest = _SceneBundleHandles[0].AssetBundle.LoadAssetAsync(fileName);
                    _LoadingStatus = LoadingStatus.WaitingForAssetLoad;
                }
            }

            // Once async asset bundle load is done, we can read the asset
            if (_LoadingStatus == LoadingStatus.WaitingForAssetLoad)
            {
                if (!_AssetRequest.isDone)
                    return;

                if (!_AssetRequest.asset)
                {
                    _LoadingFailure = $"Failed to load Asset '{_ResourcesPathObjRefs}'";
                    _LoadingStatus = LoadingStatus.Completed;
                    return;
                }

                _ResourceObjRefs = _AssetRequest.asset as ReferencedUnityObjects;

                if (_ResourceObjRefs == null)
                {
                    _LoadingFailure = $"Failed to load object references resource '{_ResourcesPathObjRefs}'";
                    _LoadingStatus = LoadingStatus.Completed;
                    return;
                }

                _LoadingStatus = LoadingStatus.WaitingForEntitiesLoad;
            }

            // Once async resource load is done, we can async read the entity scene data
            if (_LoadingStatus == LoadingStatus.WaitingForResourcesLoad)
            {
                if (_ResourceObjRefs == null)
                {
                    _LoadingFailure = $"Failed to load object references resource '{_ResourcesPathObjRefs}'";
                    _LoadingStatus = LoadingStatus.Completed;
                    return;
                }

                _LoadingStatus = LoadingStatus.WaitingForEntitiesLoad;
            }
#endif

            if (_LoadingStatus == LoadingStatus.WaitingForEntitiesLoad)
            {
                // All jobs in DOTS Runtime when singlethreaded will be executed immediately
                // so if we were to create a job for IO, we would block, which is a guaranteed deadlock on the web
                // so we must early out until the async read has completed without waiting on the jobhandle.
#if UNITY_DOTSRUNTIME && UNITY_SINGLETHREADED_JOBS
                if (_ReadHandle.Status == ReadStatus.InProgress)
                    return;
                if (_ReadHandle.Status == ReadStatus.Failed)
                {
                    _LoadingFailure = $"Failed to read '{_ScenePath}'";
                    _LoadingStatus = LoadingStatus.Completed;
                    return;
                }
                Assert.IsTrue(_ReadHandle.Status == ReadStatus.Complete);

                if (_Data.Codec != Codec.None)
                {
                    _ReadHandle.mAsyncOp.GetData(out var compressedData, out var compressedDataSize);
                    bool result = CodecService.Decompress(_Data.Codec, compressedData, compressedDataSize, _FileContent, _Data.SceneSize);
                    Assert.IsTrue(result, $"Failed to decompress '{_ScenePath}' using codec '{_Data.Codec}'");
                }
#endif
                try
                {
                    _LoadingStatus = LoadingStatus.WaitingForSceneDeserialization;
                    ScheduleSceneRead();

                    if (_BlockUntilFullyLoaded)
                    {
                        _EntityManager.ExclusiveEntityTransactionDependency.Complete();
                    }
                }
                catch (Exception e)
                {
                    _LoadingFailure = e.Message;
                    _LoadingException = e;
                    _LoadingStatus = LoadingStatus.Completed;
                }
            }

            // Complete Loading status
            if (_LoadingStatus == LoadingStatus.WaitingForSceneDeserialization)
            {
                if (_EntityManager.ExclusiveEntityTransactionDependency.IsCompleted)
                {
                    _EntityManager.EndExclusiveEntityTransaction();
                    PostProcessScene();
                    _LoadingStatus = LoadingStatus.Completed;
                    var currentTime = Time.realtimeSinceStartup;
                    var totalTime = currentTime - _StartTime;
                    System.Console.WriteLine($"Streamed scene with {totalTime * 1000,3:f0}ms latency from {_ScenePath}");
                }
            }
        }

        public void Update()
        {
            if (_BlockUntilFullyLoaded)
            {
                UpdateBlocking();
            }
            else
            {
                UpdateAsync();
            }
        }

        void ScheduleSceneRead()
        {
            var transaction = _EntityManager.BeginExclusiveEntityTransaction();
#if !UNITY_DOTSRUNTIME
            SerializeUtilityHybrid.DeserializeObjectReferences(_ResourceObjRefs, out var objectReferences);

            var loadJob = new AsyncLoadSceneJob
            {
                Transaction = transaction,
                LoadingOperationHandle = GCHandle.Alloc(this),
                ObjectReferencesHandle = GCHandle.Alloc(objectReferences),
                FileContent = _FileContent
            };
#else
            var loadJob = new AsyncLoadSceneJob
            {
                Transaction = transaction,
                LoadingOperationHandle = GCHandle.Alloc(this),
                FileContent = _FileContent
            };
#endif

            _EntityManager.ExclusiveEntityTransactionDependency = loadJob.Schedule(JobHandle.CombineDependencies(_EntityManager.ExclusiveEntityTransactionDependency, _ReadHandle.JobHandle));
        }

        void PostProcessScene()
        {
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            if (_Data.PostLoadCommandBuffer != null)
            {
                _Data.PostLoadCommandBuffer.CommandBuffer.Playback(_EntityManager);
                _Data.PostLoadCommandBuffer.Dispose();
                _Data.PostLoadCommandBuffer = null;
            }
#endif
            var group = _EntityManager.World.GetOrCreateSystem<ProcessAfterLoadGroup>();
            group.Update();
        }
    }
}
