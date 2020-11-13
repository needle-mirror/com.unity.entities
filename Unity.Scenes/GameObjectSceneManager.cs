#if !UNITY_DOTSRUNTIME
using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.SceneManagement;

namespace Unity.Scenes
{
    struct SceneEquatable : IEquatable<SceneEquatable>
    {
        public int m_Handle;

        public bool Equals(SceneEquatable other)
        {
            return m_Handle == other.m_Handle;
        }

        public override bool Equals(object obj)
        {
            return obj is SceneEquatable other && Equals(other);
        }

        public override int GetHashCode()
        {
            return m_Handle;
        }

        public static unsafe implicit operator SceneEquatable(Scene scene) => *(SceneEquatable*)&scene;
        public static unsafe implicit operator Scene(SceneEquatable sceneEquatable) => *(Scene*) & sceneEquatable;
    }

    class GameObjectSceneManager
    {
        static bool s_AppDomainUnloadRegistered;
        static bool s_Initialized;
        static NativeHashMap<SceneEquatable, IntPtr> s_LoadedScenes;

        internal static void Initialize()
        {
            if (s_Initialized)
                return;
            s_Initialized = true;

            s_LoadedScenes = new NativeHashMap<SceneEquatable, IntPtr>(64, Allocator.Persistent);

            if (!s_AppDomainUnloadRegistered)
            {
                // important: this will always be called from a special unload thread (main thread will be blocking on this)
                AppDomain.CurrentDomain.DomainUnload += (_, __) =>
                {
                    if (s_Initialized)
                    {
                        s_LoadedScenes.Dispose();
                    }
                };
                s_AppDomainUnloadRegistered = true;
            }
        }

        internal static NativeHashMap<SceneEquatable, IntPtr> LoadedScenes => s_LoadedScenes;
    }

    unsafe struct GameObjectSceneRefCount
    {
        private int m_RefCount;
        private readonly Scene m_UnityScene;

        internal Scene Scene
        {
            get
            {
                return m_UnityScene;
            }
        }

        private GameObjectSceneRefCount(Scene unityScene)
        {
            m_RefCount = 0;
            m_UnityScene = unityScene;
        }

        internal void Release()
        {
            if (JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("You cannot Release a GameObjectSceneHandle from a Job.");

            m_RefCount--;
            if (m_RefCount <= 0)
            {
                if (m_RefCount < 0)
                    throw new InvalidOperationException($"UnitySceneHandle refcount is less than zero. It has been corrupted.");

                fixed (GameObjectSceneRefCount* thisPtr = &this)
                {
                    ReleaseScene(thisPtr);
                }
            }
        }
        internal void Retain()
        {
            if (JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("You cannot Retain a GameObjectSceneHandle from a Job.");

            m_RefCount++;
        }

        internal static GameObjectSceneRefCount* CreateOrRetainScene(Scene unityScene)
        {
            if(JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("You cannot CreateOrRetain a GameObjectSceneHandle from a Job");

            if (!unityScene.IsValid())
                throw new InvalidOperationException("Unity Scene is invalid.");

            SceneEquatable unitySceneEquatable = unityScene;

            // Lazy create the LoadedScenes container
            GameObjectSceneManager.Initialize();

            // First Check if we have it loaded
            var loadedScenes = GameObjectSceneManager.LoadedScenes;
            loadedScenes.TryGetValue(unityScene, out var unitySceneHandleIntPtr);
            GameObjectSceneRefCount* unitySceneHandle = (GameObjectSceneRefCount*)unitySceneHandleIntPtr;

            if(unitySceneHandle == null)
            {
                // Create a new handle
                unitySceneHandle = (GameObjectSceneRefCount*)UnsafeUtility.Malloc(sizeof(GameObjectSceneRefCount), 16, Allocator.Persistent);
                *unitySceneHandle = new GameObjectSceneRefCount(unityScene);

                loadedScenes[unitySceneEquatable] = (IntPtr)unitySceneHandle;
            }

            return unitySceneHandle;
        }

        private static void ReleaseScene(GameObjectSceneRefCount* unitySceneHandle)
        {
            if(JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("You cannot Release a GameObjectSceneHandle from a Job. This is likely because you are removing the last Scene Entity with a GameObject Scene reference from a job.");

            var unityScene = unitySceneHandle->Scene;
            var loadedScenes = GameObjectSceneManager.LoadedScenes;

            if (!loadedScenes.ContainsKey(unityScene))
                throw new InvalidOperationException($"Attempting to release a Scene that is not contained within LoadedScenes! {unityScene}");

            loadedScenes.Remove(unityScene);
            UnsafeUtility.Free(unitySceneHandle, Allocator.Persistent);

            // This may have been unloaded outside of the GameObjectSceneSystem, so we just do nothing
            if(unityScene.IsValid())
                SceneManager.UnloadSceneAsync(unityScene);
        }
    }
}
#endif
