using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace Doc.CodeSamples.Tests
{
    public partial class SceneLoading101 : SystemBase
    {
        protected override void OnUpdate()
        {
            #region sceneloading_101

            // Note: calling GetSceneGUID is slow, please keep reading for a proper example.
            var sceneSystem = World.GetExistingSystem<SceneSystem>();
            var guid = sceneSystem.GetSceneGUID("Assets/Scenes/SampleScene.unity");
            var sceneEntity = sceneSystem.LoadSceneAsync(guid);

            #endregion
        }
    }

#if !UNITY_2020_2_OR_NEWER
// This is a hack to make the documentation compile with 2020.1 even though it uses the 2020.2 API
    static class AssetDatabase
    {
        public static string GetAssetPath(Object assetObject)
            => UnityEditor.AssetDatabase.GetAssetPath(assetObject);

        public static UnityEditor.GUID GUIDFromAssetPath(string assetPath)
            => new UnityEditor.GUID(UnityEditor.AssetDatabase.AssetPathToGUID(assetPath));
    }
#endif

    #region sceneloader_component

    // Runtime component, SceneSystem uses Entities.Hash128 to identify scenes.
    public struct SceneLoader : IComponentData
    {
        public Hash128 Guid;
    }

#if UNITY_EDITOR
    // Authoring component, a SceneAsset can only be used in the Editor
    public class SceneLoaderAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public UnityEditor.SceneAsset Scene;

        public void Convert(Entity entity, EntityManager dstManager,
            GameObjectConversionSystem conversionSystem)
        {
            var path = AssetDatabase.GetAssetPath(Scene);
            var guid = AssetDatabase.GUIDFromAssetPath(path);
            dstManager.AddComponentData(entity, new SceneLoader {Guid = guid});
        }
    }
#endif

    #endregion

    #region sceneloadersystem

    public partial class SceneLoaderSystem : SystemBase
    {
        private SceneSystem m_SceneSystem;
        private EntityQuery m_NewRequests;

        protected override void OnCreate()
        {
            m_SceneSystem = World.GetExistingSystem<SceneSystem>();
            m_NewRequests = GetEntityQuery(typeof(SceneLoader));
        }

        protected override void OnUpdate()
        {
            var requests = m_NewRequests.ToComponentDataArray<SceneLoader>(Allocator.Temp);

            for (int i = 0; i < requests.Length; i += 1)
            {
                m_SceneSystem.LoadSceneAsync(requests[i].Guid);
            }

            requests.Dispose();
            EntityManager.DestroyEntity(m_NewRequests);
        }
    }

    #endregion

    #region setsection

    public class SceneSection123Authoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public void Convert(Entity entity, EntityManager dstManager
            , GameObjectConversionSystem conversionSystem)
        {
            // This affects a single entity.
            // For a recursive approach, see `SceneSectionComponent`.
            var sceneSection = dstManager.GetSharedComponentData<SceneSection>(entity);
            sceneSection.Section = 123;
            dstManager.SetSharedComponentData(entity, sceneSection);
        }
    }

    #endregion

    public partial class SceneLoadingResolveOnly : SystemBase
    {
        protected override void OnUpdate()
        {
            Hash128 sceneGuid = default;

            #region sceneloading_resolveonly

            var sceneSystem = World.GetExistingSystem<SceneSystem>();
            var loadParameters = new SceneSystem.LoadParameters {Flags = SceneLoadFlags.DisableAutoLoad};
            var sceneEntity = sceneSystem.LoadSceneAsync(sceneGuid, loadParameters);

            #endregion
        }
    }

    public partial class SceneLoadingRequestSections : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity sceneEntity = default;

            #region sceneloading_requestsections

            // To keep the sample code short, we assume that the sections have been resolved.
            // And the code that ensures the code runs only once isn't included either.
            var sectionBuffer = EntityManager.GetBuffer<ResolvedSectionEntity>(sceneEntity);
            var sectionEntities = sectionBuffer.ToNativeArray(Allocator.Temp);

            for (int i = 0; i < sectionEntities.Length; i += 1)
            {
                if (i % 2 == 0)
                {
                    // Note that the condition includes section 0,
                    // nothing else will load if section 0 is missing.
                    EntityManager.AddComponent<RequestSceneLoaded>(sectionEntities[i].SectionEntity);
                }
            }

            sectionEntities.Dispose();

            #endregion

            #region sceneloading_unloadscene

            var sceneSystem = World.GetExistingSystem<SceneSystem>();
            sceneSystem.UnloadScene(sceneEntity);

            #endregion

            Entity sceneSectionEntity = default;

            #region sceneloading_isstuffloaded

            var sceneLoaded = sceneSystem.IsSceneLoaded(sceneEntity);
            var sectionLoaded = sceneSystem.IsSectionLoaded(sceneSectionEntity);

            #endregion
        }
    }
}
