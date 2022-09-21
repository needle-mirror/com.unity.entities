using System;
using Unity.Collections;
using Unity.Scenes;
using UnityEditor;

namespace Unity.Entities.Editor
{
    struct SubSceneNodeMapping : IDisposable
    {
        struct SubSceneData
        {
            public int Id;
            public Entity Entity;
            public Hash128 SceneHash;
        }

        NativeList<SubSceneData> m_KnownHashes;

        public SubSceneNodeMapping(Allocator allocator)
        {
            m_KnownHashes = new NativeList<SubSceneData>(32, allocator);
        }

        public int SubSceneCount => m_KnownHashes.Length;

        public Hash128 GetSceneHashFromNode(HierarchyNodeHandle nodeHandle)
        {
            var id = nodeHandle.Index;
            for (var i = 0; i < m_KnownHashes.Length; i++)
            {
                if (m_KnownHashes[i].Id == id)
                    return m_KnownHashes[i].SceneHash;
            }

            return default;
        }

        public bool TryGetSubSceneIdFromSceneEntityFromCache(Entity sceneEntity, out int id)
        {
            // try return the id based on the entity first
            for (var i = 0; i < m_KnownHashes.Length; i++)
            {
                var data = m_KnownHashes[i];
                if (data.Entity == sceneEntity)
                {
                    id = data.Id;
                    return true;
                }
            }

            id = 0;
            return false;
        }

        public unsafe int GetSubSceneIdFromSceneEntity(Entity sceneEntity, EntityDataAccess* access)
        {
            // try to get the id of the subscene based on the entity first
            if (!TryGetSubSceneIdFromSceneEntityFromCache(sceneEntity, out var id))
            {
                // if it's not in the cache then get the hash from EntityDataAccess and find the id based on it
                var sceneHash = access->GetComponentData<SceneReference>(sceneEntity).SceneGUID;
                id = GetIdFromHash(sceneHash, sceneEntity);
            }

            return id;
        }

        int GetIdFromHash(Hash128 sceneHash, Entity sceneEntity = default)
        {
            for (var i = 0; i < m_KnownHashes.Length; i++)
            {
                var data = m_KnownHashes[i];
                if (data.SceneHash == sceneHash)
                {
                    if (sceneEntity != Entity.Null)
                    {
                        data.Entity = sceneEntity;
                        m_KnownHashes[i] = data;
                    }

                    return data.Id;
                }
            }

            // at this point we need to create new id
            {
                var data = new SubSceneData { Id = m_KnownHashes.Length + 1, SceneHash = sceneHash, Entity = sceneEntity };
                m_KnownHashes.Add(data);
                return data.Id;
            }
        }

        public int GetIdFromScenePath(string scenePath)
        {
            var sceneHash = AssetDatabase.GUIDFromAssetPath(scenePath);
            return GetIdFromHash(sceneHash);
        }

        public int GetIdFromSubSceneComponent(SubScene subScene)
        {
            var sceneHash = subScene.SceneGUID;
            return GetIdFromHash(sceneHash);
        }

        public void Dispose()
        {
            m_KnownHashes.Dispose();
        }

        public void Clear() => m_KnownHashes.Clear();
    }
}
