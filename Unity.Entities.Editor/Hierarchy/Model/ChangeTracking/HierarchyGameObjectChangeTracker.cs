using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Editor.Bridge;
using Unity.Jobs;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Editor
{
    readonly struct HierarchyGameObjectChanges : IDisposable
    {
        public readonly NativeList<Scene> LoadedScenes;
        public readonly NativeList<Scene> UnloadedScenes;
        public readonly NativeList<GameObjectChangeTrackerEvent> GameObjectChangeTrackerEvents;

        public bool HasChanges() => !(LoadedScenes.Length == 0 && UnloadedScenes.Length == 0);
        
        public HierarchyGameObjectChanges(Allocator allocator)
        {
            LoadedScenes = new NativeList<Scene>(allocator);
            UnloadedScenes = new NativeList<Scene>(allocator);
            GameObjectChangeTrackerEvents = new NativeList<GameObjectChangeTrackerEvent>(allocator);
        }

        public void Clear()
        {
            LoadedScenes.Clear();
            UnloadedScenes.Clear();
            GameObjectChangeTrackerEvents.Clear();
        }
        
        public void Dispose()
        {
            LoadedScenes.Dispose();
            UnloadedScenes.Dispose();
            GameObjectChangeTrackerEvents.Dispose();
        }
    }

    readonly struct GameObjectChangeTrackerEvent
    {
        public readonly EventType Type;
        public readonly int InstanceId;
        public readonly int OptionalNewParent;

        public GameObjectChangeTrackerEvent(EventType eventType, int instanceId, int optionalNewParent = 0)
        {
            InstanceId = instanceId;
            Type = eventType;
            OptionalNewParent = optionalNewParent;
        }

        internal static GameObjectChangeTrackerEvent CreatedOrChanged(GameObject obj)
            => new GameObjectChangeTrackerEvent(EventType.CreatedOrChanged, obj.GetInstanceID());

        internal static GameObjectChangeTrackerEvent Destroyed(GameObject obj)
            => new GameObjectChangeTrackerEvent(EventType.Destroyed, obj.GetInstanceID());

        internal static GameObjectChangeTrackerEvent Moved(GameObject obj, Transform newParent)
            => new GameObjectChangeTrackerEvent(EventType.Moved, obj.GetInstanceID(), newParent ? newParent.gameObject.GetInstanceID() : 0);

        internal GameObjectChangeTrackerEvent Merge(ref GameObjectChangeTrackerEvent eventToApply)
            => new GameObjectChangeTrackerEvent(Type | eventToApply.Type, InstanceId, eventToApply.OptionalNewParent);

        [Flags]
        public enum EventType : byte
        {
            CreatedOrChanged = 1 << 0,
            Moved = 1 << 1,
            Destroyed = 1 << 2
        }
    }

    class HierarchyGameObjectChangeTracker : IDisposable
    {
        readonly HashSet<Scene> m_LoadedScenes = new HashSet<Scene>();
        readonly HashSet<Scene> m_UnloadedScenes = new HashSet<Scene>();

        public HierarchyGameObjectChangeTracker(Allocator allocator)
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);

                if (scene.isSubScene || !scene.isLoaded)
                    continue;
                
                m_LoadedScenes.Add(scene);
            }
        }

        public void Dispose()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
        }

        void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene.isSubScene)
                return;
            
            m_UnloadedScenes.Remove(scene);
            m_LoadedScenes.Add(scene);
        }
        
        void OnSceneClosed(Scene scene)
        {
            if (scene.isSubScene)
                return;
            
            m_UnloadedScenes.Add(scene);
            m_LoadedScenes.Remove(scene);
        }

        public void GetChanges(HierarchyGameObjectChanges changes)
        {
            changes.Clear();

            if (m_LoadedScenes.Count == 0 && m_UnloadedScenes.Count == 0)
                return;
            
            foreach (var scene in m_LoadedScenes)
                changes.LoadedScenes.Add(scene);
                    
            foreach (var scene in m_UnloadedScenes)
                changes.UnloadedScenes.Add(scene);
            
            m_LoadedScenes.Clear();
            m_UnloadedScenes.Clear();
        }

        [BurstCompile]
        internal struct RecordEventJob : IJob
        {
            public NativeList<GameObjectChangeTrackerEvent> Events;
            public GameObjectChangeTrackerEvent Event;

            public void Execute()
            {
                for (var i = 0; i < Events.Length; i++)
                {
                    ref var existingEvent = ref Events.ElementAt(i);
                    if (existingEvent.InstanceId != Event.InstanceId || existingEvent.Type > Event.Type)
                        continue;

                    var e = Events[i].Merge(ref Event);
                    Events[i] = e;
                    return;
                }

                Events.Add(Event);
            }
        }
    }
}
