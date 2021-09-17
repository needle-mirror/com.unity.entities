#if !UNITY_DOTSRUNTIME
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe.NotBurstCompatible;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    public struct DisableLiveConversion : IComponentData
    {
    }

    public enum LiveConversionMode
    {
        Disabled = 0,
        LiveConvertStandalonePlayer,
        SceneViewShowsAuthoring,
        SceneViewShowsRuntime,
    }

    struct LiveConversionChangeSet : IDisposable
    {
        public Hash128         SceneGUID;
        public EntityChangeSet Changes;
        public string          SceneName;
        public bool            UnloadAllPreviousEntities;
        public int             FramesToRetainBlobAssets;

        public void Dispose()
        {
            Changes.Dispose();
        }

        #if UNITY_EDITOR
        public byte[] Serialize()
        {
            var buffer = new UnsafeAppendBuffer(1024, 16, Allocator.Persistent);

            EntityChangeSetSerialization.ResourcePacket.SerializeResourcePacket(Changes, ref buffer);

            buffer.Add(SceneGUID);
            buffer.AddNBC(SceneName);
            buffer.Add(UnloadAllPreviousEntities);
            buffer.Add(FramesToRetainBlobAssets);

            return buffer.ToBytesNBC();
        }

        #endif

        unsafe public static LiveConversionChangeSet Deserialize(EntityChangeSetSerialization.ResourcePacket resource, GlobalAssetObjectResolver resolver)
        {
            var reader = resource.ChangeSet.AsReader();

            LiveConversionChangeSet changeSet;
            changeSet.Changes = EntityChangeSetSerialization.Deserialize(&reader, resource.GlobalObjectIds, resolver);
            reader.ReadNext(out changeSet.SceneGUID);
            reader.ReadNextNBC(out changeSet.SceneName);
            reader.ReadNext(out changeSet.UnloadAllPreviousEntities);
            reader.ReadNext(out changeSet.FramesToRetainBlobAssets);

            return changeSet;
        }
    }


    class LiveConversionPatcher
    {
        public struct LiveConvertedSceneState : ISystemStateComponentData, IEquatable<LiveConvertedSceneState>
        {
            public Hash128 Scene;

            public bool Equals(LiveConvertedSceneState other)
            {
                return Scene.Equals(other.Scene);
            }

            public override int GetHashCode()
            {
                return Scene.GetHashCode();
            }
        }


        private World _DstWorld;
        EntityQuery _AddedScenesQuery;
        private EntityQuery _RemovedScenesQuery;
        public LiveConversionPatcher(World destinationWorld)
        {
            _DstWorld = destinationWorld;

            _AddedScenesQuery = _DstWorld.EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(SceneTag)},
                Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabled
            });
            _AddedScenesQuery.SetSharedComponentFilter(new SceneTag { SceneEntity = Entity.Null});

            _RemovedScenesQuery = _DstWorld.EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(LiveConvertedSceneState)},
                None = new ComponentType[] {typeof(SceneReference)},
            });
        }

        public void Dispose()
        {
            _AddedScenesQuery.Dispose();
            _RemovedScenesQuery.Dispose();
        }

        // @Todo: move to IJobEntity once system is ISystem or SystemBase
        struct RemoveLiveConversionSceneState : IJobEntityBatch
        {
            public Hash128 DeleteGuid;
            public  EntityCommandBuffer Commands;

            [ReadOnly] public ComponentTypeHandle<LiveConvertedSceneState> LiveConvertedSceneStateHandle;
            [ReadOnly] public EntityTypeHandle EntitiesHandle;

            public void Execute(Entity entity, ref LiveConvertedSceneState scene)
            {
                if (scene.Scene == DeleteGuid)
                    Commands.RemoveComponent<LiveConvertedSceneState>(entity);
            }

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var entities = batchInChunk.GetNativeArray(EntitiesHandle);
                var liveLinkedSceneStates = batchInChunk.GetNativeArray(LiveConvertedSceneStateHandle);
                int count = batchInChunk.Count;
                for (int i = 0; i < count; ++i)
                {
                    var liveLinkedSceneState = liveLinkedSceneStates[i];
                    Execute(entities[i], ref liveLinkedSceneState);
                }
            }
        }
        public void UnloadScene(Hash128 sceneGUID)
        {
            var dstEntities = _DstWorld.EntityManager;
            var sceneSystem = _DstWorld.GetExistingSystem<SceneSystem>();
            var sceneEntity = sceneSystem.GetLiveConvertedSceneEntity(sceneGUID);
            dstEntities.RemoveComponent<DisableSceneResolveAndLoad>(sceneEntity);
            dstEntities.RemoveComponent<LiveConvertedSceneState>(sceneEntity);
            sceneSystem.UnloadScene(sceneEntity, SceneSystem.UnloadParameters.DestroySectionProxyEntities | SceneSystem.UnloadParameters.DontRemoveRequestSceneLoaded);
            // Cleanup leftover LiveConvertedScene system state
            // (This happens if the scene entity got destroyed)
            var job = new RemoveLiveConversionSceneState
            {
                DeleteGuid = sceneGUID,
                Commands = new EntityCommandBuffer(Allocator.TempJob),
                LiveConvertedSceneStateHandle = _DstWorld.EntityManager.GetComponentTypeHandle<LiveConvertedSceneState>(true),
                EntitiesHandle = _DstWorld.EntityManager.GetEntityTypeHandle()
            };
            job.Run(_RemovedScenesQuery);

            job.Commands.Playback(dstEntities);
            job.Commands.Dispose();
        }

        public void ApplyPatch(LiveConversionChangeSet changeSet)
        {
            var dstEntities = _DstWorld.EntityManager;
            var sceneSystem = _DstWorld.GetExistingSystem<SceneSystem>();
            Entity sectionEntity = Entity.Null;
            var sceneEntity = sceneSystem.GetLiveConvertedSceneEntity(changeSet.SceneGUID);

            //@TODO: Check if the scene or section is requested to be loaded
            if (sceneEntity == Entity.Null)
            {
                Debug.LogWarning($"'{changeSet.SceneName}' ({{changeSet.sceneGUID}}) was ignored in live conversion since it is not loaded.");
                return;
            }

            var patcherBlobAssetSystem = _DstWorld.GetOrCreateSystem<EntityPatcherBlobAssetSystem>();
            patcherBlobAssetSystem.SetFramesToRetainBlobAssets(changeSet.FramesToRetainBlobAssets);

            // Unload scene
            if (changeSet.UnloadAllPreviousEntities)
            {
                //@Todo: Can we try to keep scene & section entities alive? (In case user put custom data on it)
                sceneSystem.UnloadScene(sceneEntity, SceneSystem.UnloadParameters.DestroySectionProxyEntities | SceneSystem.UnloadParameters.DontRemoveRequestSceneLoaded);

                // Create section
                sectionEntity = dstEntities.CreateEntity();
                dstEntities.AddComponentData(sectionEntity, new SceneSectionStreamingSystem.StreamingState { Status = SceneSectionStreamingSystem.StreamingStatus.Loaded});
                dstEntities.AddComponentData(sectionEntity, new DisableSceneResolveAndLoad());
                dstEntities.AddComponentData(sectionEntity, new SceneEntityReference {SceneEntity = sceneEntity});

                // Configure scene
                dstEntities.AddComponentData(sceneEntity, new DisableSceneResolveAndLoad());
                dstEntities.AddComponentData(sceneEntity, new LiveConvertedSceneState { Scene = changeSet.SceneGUID });

                dstEntities.AddBuffer<ResolvedSectionEntity>(sceneEntity).Add(new ResolvedSectionEntity { SectionEntity = sectionEntity});

#if UNITY_EDITOR
                dstEntities.SetName(sectionEntity, "SceneSection (Live converted): " + changeSet.SceneName);
                dstEntities.SetName(sceneEntity, "Scene (Live converted): " + changeSet.SceneName);
#endif
            }
            else
            {
                var resolvedSectionEntities = dstEntities.GetBuffer<ResolvedSectionEntity>(sceneEntity);
                if (resolvedSectionEntities.Length > 0)
                {
                    sectionEntity = resolvedSectionEntities[0].SectionEntity;
                }
            }

            // SceneTag.SceneEntity == Entity.Null is reserved for new entities added via live link.
            if (_AddedScenesQuery.CalculateChunkCount() != 0)
            {
                Debug.LogWarning("SceneTag.SceneEntity must not reference Entity.Null. Destroying Entities.");
                dstEntities.DestroyEntity(_AddedScenesQuery);
            }

            EntityPatcher.ApplyChangeSet(_DstWorld.EntityManager, changeSet.Changes);

            if (sectionEntity != Entity.Null)
            {
                dstEntities.SetSharedComponentData(_AddedScenesQuery, new SceneTag {SceneEntity = sectionEntity});
            }

            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }
    }
}
#endif
