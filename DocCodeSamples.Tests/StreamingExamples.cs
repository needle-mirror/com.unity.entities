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
            var guid = SceneSystem.GetSceneGUID(ref World.Unmanaged.GetExistingSystemState<SceneSystem>(), "Assets/Scenes/SampleScene.unity");
            var sceneEntity = SceneSystem.LoadSceneAsync(World.Unmanaged, guid);

            #endregion
        }
    }

    #region sceneloader_component

    // Runtime component, SceneSystem uses Entities.Hash128 to identify scenes.
    public struct SceneLoader : IComponentData
    {
        public Hash128 Guid;
    }

#if UNITY_EDITOR
    // Authoring component, a SceneAsset can only be used in the Editor
    public class SceneLoaderAuthoring : MonoBehaviour
    {
        public UnityEditor.SceneAsset Scene;

        class Baker : Baker<SceneLoaderAuthoring>
        {
            public override void Bake(SceneLoaderAuthoring authoring)
            {
                var path = AssetDatabase.GetAssetPath(authoring.Scene);
                var guid = AssetDatabase.GUIDFromAssetPath(path);
                AddComponent(new SceneLoader { Guid = guid });
            }
        }
    }
#endif

    #endregion

    #region sceneloadersystem

    [RequireMatchingQueriesForUpdate]
    public partial class SceneLoaderSystem : SystemBase
    {
        private EntityQuery m_NewRequests;

        protected override void OnCreate()
        {
            m_NewRequests = GetEntityQuery(typeof(SceneLoader));
        }

        protected override void OnUpdate()
        {
            var requests = m_NewRequests.ToComponentDataArray<SceneLoader>(Allocator.Temp);

            for (int i = 0; i < requests.Length; i += 1)
            {
                SceneSystem.LoadSceneAsync(World.Unmanaged, requests[i].Guid);
            }

            requests.Dispose();
            EntityManager.DestroyEntity(m_NewRequests);
        }
    }

    #endregion

    #region setsection

    public class SceneSection123Authoring : MonoBehaviour
    {
        class Baker : Baker<SceneSection123Authoring>
        {
            // TODO: This doesn't work with Baking, as it relied on ordering
            public override void Bake(SceneSection123Authoring authoring)
            {
                // This affects a single entity.
                // For a recursive approach, see `SceneSectionComponent`.
                SetSharedComponent(GetEntity(), new SceneSection{Section = 123});
            }
        }
    }

    #endregion

    public partial class SceneLoadingResolveOnly : SystemBase
    {
        protected override void OnUpdate()
        {
            Hash128 sceneGuid = default;

            #region sceneloading_resolveonly

            var loadParameters = new SceneSystem.LoadParameters {Flags = SceneLoadFlags.DisableAutoLoad};
            var sceneEntity = SceneSystem.LoadSceneAsync(World.Unmanaged, sceneGuid, loadParameters);

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

            SceneSystem.UnloadScene(World.Unmanaged, sceneEntity);

            #endregion

            Entity sceneSectionEntity = default;

            #region sceneloading_isstuffloaded

            var sceneLoaded = SceneSystem.IsSceneLoaded(World.Unmanaged, sceneEntity);
            var sectionLoaded = SceneSystem.IsSectionLoaded(World.Unmanaged, sceneSectionEntity);

            #endregion
        }
    }
}
