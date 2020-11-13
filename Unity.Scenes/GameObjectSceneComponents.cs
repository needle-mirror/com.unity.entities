#if !UNITY_DOTSRUNTIME
using Unity.Entities;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    // The actual Load Request specific to GO Scenes, this is compiled with SceneLoadRequest to make up the full request
    struct RequestGameObjectSceneLoaded : IComponentData
    {
        public LoadSceneParameters loadParameters;
        public int priority;
        public bool activateOnLoad;
    }

    // Buffer of the SubScenes contained within a GO Scene
    struct GameObjectSceneSubScene : IBufferElementData
    {
        public Entity SceneEntity;
    }

    // This is the actual Scene value with a global handle for refcounting the Scene globally
    unsafe struct GameObjectSceneData : ISharedComponentData, IRefCounted
    {
        public Scene Scene;
        public GameObjectSceneRefCount* gameObjectSceneHandle;

        public void Retain()
        {
            gameObjectSceneHandle->Retain();
        }

        public void Release()
        {
            gameObjectSceneHandle->Release();
        }
    }

    // This component tracks the ResourceGUID Entity for requesting the GO Scene over LiveLink in a LiveLink Player
    struct GameObjectSceneDependency : IComponentData
    {
        public Entity Value;
    }

    // Replaces SceneReference for GameObjectScene
    struct GameObjectReference : IComponentData
    {
        public Hash128 SceneGUID;
    }
}
#endif
