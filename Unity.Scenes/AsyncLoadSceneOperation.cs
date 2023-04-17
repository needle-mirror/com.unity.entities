using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_DOTSRUNTIME
using Unity.Runtime.IO;
#else
using Unity.Entities.Content;
#endif
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
        public int SceneSize;
        public int CompressedSceneSize;
        public string ScenePath;
        public Codec Codec;
        public bool BlockUntilFullyLoaded;
        public BlobAssetReference<DotsSerialization.BlobHeader> BlobHeader;
        public BlobAssetOwner BlobHeaderOwner;
        public Entity SceneSectionEntity;

#if !UNITY_DOTSRUNTIME
        public UntypedWeakReferenceId UnityObjectRefId;
#endif
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
            WaitingForUnityObjectReferencesLoad,
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
            public void* Ptr;
            public SerializeUtility.WorldDeserializationStatus DeserializationStatus;
            public ReadHandle ReadHandle;
            public UnsafeList<ReadCommand> ReadCommands;
            public bool FreeChunks;

            public void Execute()
            {
                Memory.Unmanaged.Free(Ptr, Allocator.Persistent);

                if (FreeChunks)
                {
                    int length = DeserializationStatus.MegaChunkInfoList.Length;
                    for (int i = 0; i < length; ++i)
                    {
                        var chunks = DeserializationStatus.MegaChunkInfoList[i];
                        EntityComponentStore.FreeContiguousChunks((Chunk*)chunks.MegaChunkAddress, chunks.MegaChunkSize);
                    }
                }

                DeserializationStatus.Dispose();
                if (ReadHandle.IsValid())
                    ReadHandle.Dispose();
                if (ReadCommands.IsCreated)
                    ReadCommands.Dispose();
            }
        }

        public void Dispose()
        {
            if (_LoadingStatus == LoadingStatus.WaitingForResourcesLoad || _LoadingStatus == LoadingStatus.WaitingForEntitiesLoad)
            {
                var freeJob = new FreeJob { Ptr = _FileContent, DeserializationStatus = _DeserializationStatus, ReadCommands = _ReadCommands, ReadHandle = _ReadHandle };
                freeJob.FreeChunks = true;
                freeJob.Schedule(_ReadHandle.JobHandle);
            }
#if !UNITY_DOTSRUNTIME
            if (_UnityObjectRefId.IsValid)
                RuntimeContentManager.ReleaseObjectAsync(_UnityObjectRefId);
#endif

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            _Data.PostLoadCommandBuffer?.Dispose();
#endif
            _Data.BlobHeaderOwner.Release();
            _DeserializationResultArray.Dispose(_EntityManager.ExclusiveEntityTransactionDependency);
        }

        struct AsyncLoadSceneJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public byte*                        FileContent;            // Only to use when deserializing from memory
            public long                         FileLength;

            public GCHandle                     LoadingOperationHandle;
#if !UNITY_DOTSRUNTIME
            public GCHandle                     ObjectReferencesHandle;
#endif
            public ExclusiveEntityTransaction   Transaction;
            public NativeArray<SerializeUtility.WorldDeserializationResult> DeserializationResult;

            static readonly ProfilerMarker k_ProfileDeserializeWorld = new ProfilerMarker("AsyncLoadSceneJob.DeserializeWorld");
            [NativeDisableUnsafePtrRestriction]
            public SerializeUtility.WorldDeserializationStatus DeserializationStatus;
            public BlobAssetReference<DotsSerialization.BlobHeader> BlobHeader;
            public Entity SceneSectionEntity;

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
                    SerializeUtility.WorldDeserializationResult deserializationResult;
                    // Deserializing from memory loaded file
                    if (FileContent != null)
                    {
                        using (var reader = new MemoryBinaryReader(FileContent, FileLength))
                        {
                            k_ProfileDeserializeWorld.Begin();
                            SerializeUtility.DeserializeWorldInternal(Transaction, reader, out deserializationResult, objectReferences);
                            k_ProfileDeserializeWorld.End();
                        }
                    }
                    else
                    {
                        k_ProfileDeserializeWorld.Begin();
                        var dotsReader = DotsSerialization.CreateReader(ref BlobHeader.Value);
                        SerializeUtility.EndDeserializeWorld(Transaction, dotsReader, ref DeserializationStatus, out deserializationResult, objectReferences);
                        k_ProfileDeserializeWorld.End();
                    }
                    Transaction.EntityManager.AddSharedComponentManaged(Transaction.EntityManager.UniversalQueryWithSystems, new SceneTag { SceneEntity = SceneSectionEntity });
                    DeserializationResult[0] = deserializationResult;
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
        ref EntityManager           _EntityManager => ref _Data.EntityManager;
        bool                    _BlockUntilFullyLoaded => _Data.BlockUntilFullyLoaded;

#if !UNITY_DOTSRUNTIME
        UntypedWeakReferenceId _UnityObjectRefId;
#if UNITY_EDITOR
        RuntimeContentManager.InstanceHandle _UnityObjectRefsHandle;
#endif
#endif

        LoadingStatus           _LoadingStatus;
        string                  _LoadingFailure;
        Exception               _LoadingException;

        private byte*            _FileContent;
        ReadHandle               _ReadHandle;
        UnsafeList<ReadCommand>  _ReadCommands;

        private double _StartTime;
        private SerializeUtility.WorldDeserializationStatus _DeserializationStatus;
        private NativeArray<SerializeUtility.WorldDeserializationResult> _DeserializationResultArray;
        public SerializeUtility.WorldDeserializationResult DeserializationResult => _DeserializationResultArray[0];


        public AsyncLoadSceneOperation(AsyncLoadSceneData asyncLoadSceneData)
        {
            _Data = asyncLoadSceneData;
#if !UNITY_DOTSRUNTIME
            _UnityObjectRefId = asyncLoadSceneData.UnityObjectRefId;
#endif
            _LoadingStatus = LoadingStatus.NotStarted;
            _DeserializationResultArray = new NativeArray<SerializeUtility.WorldDeserializationResult>(1, Allocator.Persistent);
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
        public UntypedWeakReferenceId StealReferencedUnityObjects()
        {
            var tmp = _UnityObjectRefId;
            _UnityObjectRefId = default;
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

                if (_Data.BlobHeader.IsCreated)
                {
                    var dotsReader = DotsSerialization.CreateReader(ref _Data.BlobHeader.Value);
                    _ReadHandle = SerializeUtility.BeginDeserializeWorld(_ScenePath, dotsReader, out _DeserializationStatus, out _ReadCommands);
                }
                else
                {
                    throw new InvalidOperationException("BlobHeader must be valid");
                }

#if !UNITY_DOTSRUNTIME
                if (_UnityObjectRefId.IsValid)
                {
#if UNITY_EDITOR
                    _UnityObjectRefsHandle = RuntimeContentManager.LoadInstanceAsync(_UnityObjectRefId);
                    RuntimeContentManager.WaitForInstanceCompletion(_UnityObjectRefsHandle);
#else
                    RuntimeContentManager.LoadObjectAsync(_UnityObjectRefId);
                    RuntimeContentManager.WaitForObjectCompletion(_UnityObjectRefId);
#endif
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
            if (_LoadingStatus == LoadingStatus.NotStarted)
            {
                if (_SceneSize == 0)
                    return;

                try
                {
                    Assert.IsFalse(string.IsNullOrEmpty(_ScenePath));
                    _StartTime = Time.realtimeSinceStartup;

                    // Load the file into memory if it's a compressed one, then decompress via a job and deserialize from memory
                    if (_Data.Codec != Codec.None)
                    {
                        _FileContent = (byte*) Memory.Unmanaged.Allocate(_SceneSize, 16, Allocator.Persistent);
                        _ReadCommands = new UnsafeList<ReadCommand>(1, Allocator.Persistent);
                        _ReadCommands.Add(new ReadCommand
                        {
                            Buffer = _FileContent,
                            Offset = 0,
                            Size = _SceneSize
                        });
#if ENABLE_PROFILER
                        // When AsyncReadManagerMetrics are available, mark up the file read for more informative IO metrics.
                        // Metrics can be retrieved by AsyncReadManagerMetrics.GetMetrics
                        _ReadHandle = AsyncReadManager.Read(_ScenePath, _ReadCommands.Ptr, 1, subsystem: AssetLoadingSubsystem.EntitiesScene);
#else
                        _ReadHandle = AsyncReadManager.Read(_ScenePath, _ReadCommands.Ptr, 1);
#endif
                    }

                    // Asynchronous deserialization from file, the BeginDeserializeWorld call will schedule the reads, the End call will perform the deserialization itself
                    else
                    {
                        if (_Data.BlobHeader.IsCreated)
                        {
                            var dotsReader = DotsSerialization.CreateReader(ref _Data.BlobHeader.Value);
                            _ReadHandle = SerializeUtility.BeginDeserializeWorld(_ScenePath, dotsReader, out _DeserializationStatus, out _ReadCommands);
                        }
                        else
                        {
                            throw new InvalidOperationException("BlobHeader must be valid");
                        }
                    }

#if !UNITY_DOTSRUNTIME
                    if (_UnityObjectRefId.IsValid)
                    {
#if UNITY_EDITOR
                        _UnityObjectRefsHandle = RuntimeContentManager.LoadInstanceAsync(_UnityObjectRefId);
#else
                        RuntimeContentManager.LoadObjectAsync(_UnityObjectRefId);
#endif
                        _LoadingStatus = LoadingStatus.WaitingForUnityObjectReferencesLoad;
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
            if (_LoadingStatus == LoadingStatus.WaitingForUnityObjectReferencesLoad)
            {
#if UNITY_EDITOR
                if (RuntimeContentManager.GetInstanceLoadingStatus(_UnityObjectRefsHandle) == ObjectLoadingStatus.Completed)
#else
                if (RuntimeContentManager.GetObjectLoadingStatus(_UnityObjectRefId) == ObjectLoadingStatus.Completed)
#endif
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

                    System.Console.WriteLine($"Streamed scene with {totalTime * 1000,4:f0}ms latency from {_ScenePath}");
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
            UnityEngine.Object[] objectReferences = null;
#if UNITY_EDITOR
            if (_UnityObjectRefsHandle.IsValid)
            {
                SerializeUtilityHybrid.DeserializeObjectReferences(RuntimeContentManager.GetInstanceValue<ReferencedUnityObjects>(_UnityObjectRefsHandle), out objectReferences);
                RuntimeContentManager.ReleaseInstancesAsync(_UnityObjectRefsHandle);
            }
#else
            if(_UnityObjectRefId.IsValid)
                SerializeUtilityHybrid.DeserializeObjectReferences(RuntimeContentManager.GetObjectValue<ReferencedUnityObjects>(_UnityObjectRefId), out objectReferences);
#endif
            var loadJob = new AsyncLoadSceneJob
            {
                Transaction = transaction,
                LoadingOperationHandle = GCHandle.Alloc(this),
                ObjectReferencesHandle = GCHandle.Alloc(objectReferences),
                DeserializationStatus = _DeserializationStatus,
                BlobHeader = _Data.BlobHeader,
                FileContent = _FileContent,
                FileLength = _SceneSize,
                DeserializationResult = _DeserializationResultArray,
                SceneSectionEntity = _Data.SceneSectionEntity
            };
#else
            var loadJob = new AsyncLoadSceneJob
            {
                Transaction = transaction,
                LoadingOperationHandle = GCHandle.Alloc(this),
                DeserializationStatus = _DeserializationStatus,
                BlobHeader = _Data.BlobHeader,
                FileContent = _FileContent,
                FileLength = _SceneSize,
                DeserializationResult = _DeserializationResultArray,
                SceneSectionEntity = _Data.SceneSectionEntity
            };
#endif

#if !UNITY_DOTSRUNTIME
            var loadJobHandle = loadJob.Schedule(JobHandle.CombineDependencies(
                _EntityManager.ExclusiveEntityTransactionDependency,
                _ReadHandle.JobHandle));
#else

            JobHandle decompressJob = default;
            if (_Data.Codec != Codec.None)
            {
                decompressJob = new DecompressJob()
                {
                    Codec = _Data.Codec,
                    AsyncOp = _ReadHandle.mAsyncOp,
                    DecompressedData = _FileContent,
                    DecompressedDataSize = _Data.SceneSize
                }.Schedule(_ReadHandle.mJobHandle);
            }

            var loadJobHandle = loadJob.Schedule(JobHandle.CombineDependencies(
                _EntityManager.ExclusiveEntityTransactionDependency,
                _ReadHandle.JobHandle,
                decompressJob));
#endif
            _EntityManager.ExclusiveEntityTransactionDependency = loadJobHandle;
            _DeserializationStatus = default; // _DeserializationStatus is disposed by AsyncLoadSceneJob
            var freeJob = new FreeJob { Ptr = _FileContent, ReadCommands = _ReadCommands, ReadHandle = _ReadHandle };
            freeJob.Schedule(loadJobHandle);

            _FileContent = null;
            _ReadCommands = default;
            _ReadHandle = default;
        }

#if !UNITY_DOTSRUNTIME
        static readonly ProfilerMarker s_PostProcessScene = new ProfilerMarker(nameof(PostProcessScene));
#endif
        void PostProcessScene()
        {
#if !UNITY_DOTSRUNTIME
            using var marker = s_PostProcessScene.Auto();
#endif
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            if (_Data.PostLoadCommandBuffer != null)
            {
                _Data.PostLoadCommandBuffer.CommandBuffer.Playback(_EntityManager);
                _Data.PostLoadCommandBuffer.Dispose();
                _Data.PostLoadCommandBuffer = null;
            }
#endif
            SceneSectionStreamingSystem.AddStreamingWorldSystems(_EntityManager.World);
            var group = _EntityManager.World.GetOrCreateSystemManaged<ProcessAfterLoadGroup>();
            group.Update();
            _EntityManager.CompleteAllTrackedJobs();
            _EntityManager.World.DestroyAllSystemsAndLogException();
            using var missingSceneTag = _EntityManager.CreateEntityQuery(ComponentType.Exclude<SceneTag>());
            if (!missingSceneTag.IsEmptyIgnoreFilter)
                _EntityManager.AddSharedComponentManaged(missingSceneTag, new SceneTag { SceneEntity = _Data.SceneSectionEntity });
        }
    }

#if UNITY_DOTSRUNTIME
    public unsafe struct DecompressJob : IJob
    {
        public AsyncOp AsyncOp;
        internal Codec Codec;
        public unsafe byte* DecompressedData;
        public int DecompressedDataSize;

        public void Execute()
        {
            AsyncOp.GetData(out var compressedData, out var compressedDataSize);
            bool result = CodecService.Decompress(Codec, compressedData, compressedDataSize, DecompressedData, DecompressedDataSize);

            if (!result)
                throw new Exception("Failed to decompress using codec " + Codec.ToString());
        }
    }
#endif
}
