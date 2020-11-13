#if !UNITY_DISABLE_MANAGED_COMPONENTS && !DOTS_HYBRID_COMPONENTS_DEBUG
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes.Editor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[ExecuteAlways]
[UpdateInGroup(typeof(LiveLinkEditorSystemGroup))]
[UpdateAfter(typeof(EditorSubSceneLiveLinkSystem))]
class EditorCompanionGameObjectUpdateSystem : ComponentSystem
{
    EntityQuery m_WithoutHideFlagsSet;
    EntityQuery m_WithoutPreviewSceneTag;

    struct SceneAndMask
    {
        public Scene scene;
        public ulong mask;
    }

    FixedList512<SceneAndMask> m_CompanionScenes;

    protected override void OnCreate()
    {
        m_CompanionScenes = new FixedList512<SceneAndMask>();

        m_WithoutHideFlagsSet = Entities
            .WithAll<CompanionLink>()
            .WithNone<EditorCompanionHideFlagsSet>()
            .ToEntityQuery();

        m_WithoutPreviewSceneTag = Entities
            .WithAll<CompanionLink>()
            .WithAll<EditorRenderData>()
            .WithNone<Camera>()
            .WithNone<CapsuleCollider>()
            .WithNone<MeshCollider>()
            .WithNone<BoxCollider>()
            .WithNone<SphereCollider>()
            .WithNone<EditorCompanionInPreviewSceneTag>()
            .ToEntityQuery();
    }

    protected override void OnDestroy()
    {
        foreach (var sceneAndMask in m_CompanionScenes)
        {
            EditorSceneManager.ClosePreviewScene(sceneAndMask.scene);
        }
    }

    protected override void OnUpdate()
    {
        Entities.With(m_WithoutHideFlagsSet).ForEach((CompanionLink link) =>
        {
            link.Companion.hideFlags = CompanionLink.CompanionFlags;
        });

        EntityManager.AddComponent<EditorCompanionHideFlagsSet>(m_WithoutHideFlagsSet);

        Entities.With(m_WithoutPreviewSceneTag).ForEach((EditorRenderData renderData, CompanionLink link) =>
        {
            foreach (var sceneAndMask in m_CompanionScenes)
            {
                if (sceneAndMask.mask == renderData.SceneCullingMask)
                {
                    EditorSceneManager.MoveGameObjectToScene(link.Companion, sceneAndMask.scene);
                    return;
                }
            }

            var scene = EditorSceneManager.NewPreviewScene();

            m_CompanionScenes.Add(new SceneAndMask
            {
                scene = scene,
                mask = renderData.SceneCullingMask
            });

            EditorSceneManager.SetSceneCullingMask(scene, renderData.SceneCullingMask);
            EditorSceneManager.MoveGameObjectToScene(link.Companion, scene);
        });
        EntityManager.AddComponent<EditorCompanionInPreviewSceneTag>(m_WithoutPreviewSceneTag);
    }
}
#endif
