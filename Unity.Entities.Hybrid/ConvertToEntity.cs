using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities
{

#if false
    //@TODO: * SubScene conversion error if it has ConvertAndInjectGameObject
    //       * Error when ComponentDataWrapper without any converter or game object entity on top
    //       * Should there be a hierarchical injection mode?
    [CustomEditor(typeof(ConvertToEntity))]
    public class ConvertToEntityEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            ConvertToEntity convertToEntity = (ConvertToEntity)target;

            if (convertToEntity.gameObject.scene.isSubScene)
            {
                if (convertToEntity.ConversionMode == ConvertToEntity.Mode.ConvertAndInjectGameObject)
                    EditorGUILayout.HelpBox($"The SubScene will be fully converted, so this mode has no effect", MessageType.Warning, true);
                else
                    EditorGUILayout.HelpBox($"The SubScene will be fully converted, so this mode has no effect", MessageType.Info, true);
                return;
            }

            if (convertToEntity.ConversionMode == ConvertToEntity.Mode.ConvertAndInjectGameObject)
            {
                EditorGUILayout.HelpBox($"ConvertToEntity.ConvertAndDestroy is enabled on a parent.\nThe parent game objects will be destroyed and this game object will be attached to the entity.", MessageType.Info, true);
            }
        }
    }
#endif

    public class ConvertToEntity : MonoBehaviour
    {
        public enum Mode
        {
            ConvertAndDestroy,
            ConvertAndInjectGameObject
        }

        public Mode ConversionMode;
        
        void Awake()
        {
            if (World.Active != null)
            {
                // Root ConvertToEntity is responsible for converting the whole hierarchy
                if (transform.parent != null && transform.parent.GetComponentInParent<ConvertToEntity>() != null)
                    return;
                
                if (ConversionMode == Mode.ConvertAndDestroy)
                    ConvertHierarchy(gameObject);
                else
                    ConvertAndInjectOriginal(gameObject);
            }
            else
            {
                UnityEngine.Debug.LogWarning("ConvertEntity failed because there was no Active World", this);
            }
        }
        
        static void InjectOriginalComponents(EntityManager entityManager, Entity entity, Transform transform)
        {
            foreach (var com in transform.GetComponents<Component>())
            {
                if (com is GameObjectEntity || com is ConvertToEntity || com is ComponentDataProxyBase)
                    continue;
                
                entityManager.AddComponentObject(entity, com);
            }
        }

        public static void AddRecurse(EntityManager manager, Transform transform)
        {
            GameObjectEntity.AddToEntityManager(manager, transform.gameObject);
            
            var convert = transform.GetComponent<ConvertToEntity>();
            if (convert != null && convert.ConversionMode == Mode.ConvertAndInjectGameObject)
                return;
                
            foreach (Transform child in transform)
                AddRecurse(manager, child);
        }

        public static bool InjectOriginalComponents(World srcGameObjectWorld, EntityManager simulationWorld, Transform transform)
        {
            var convert = transform.GetComponent<ConvertToEntity>();

            if (convert != null && convert.ConversionMode == Mode.ConvertAndInjectGameObject)
            {
                var entity = GameObjectConversionUtility.GameObjectToConvertedEntity(srcGameObjectWorld, transform.gameObject);
                InjectOriginalComponents(simulationWorld, entity, transform);
                transform.parent = null;
                return true;
            }
            
            for (int i = 0; i < transform.childCount;)
            {
                if (!InjectOriginalComponents(srcGameObjectWorld, simulationWorld, transform.GetChild(i)))
                    i++;
            }

            return false;
        }

        public static void ConvertHierarchy(GameObject root)
        {
            var gameObjectWorld = GameObjectConversionUtility.CreateConversionWorld(World.Active, default(Hash128), false);
            
            AddRecurse(gameObjectWorld.EntityManager, root.transform);
            
            GameObjectConversionUtility.Convert(gameObjectWorld, World.Active);

            InjectOriginalComponents(gameObjectWorld, World.Active.EntityManager, root.transform);

            GameObject.Destroy(root);
            
            gameObjectWorld.Dispose();
        }
        
        
        public static void ConvertAndInjectOriginal(GameObject root)
        {
            var gameObjectWorld = GameObjectConversionUtility.CreateConversionWorld(World.Active, default(Hash128), false);
            
            GameObjectEntity.AddToEntityManager(gameObjectWorld.EntityManager, root);
            
            GameObjectConversionUtility.Convert(gameObjectWorld, World.Active);

            GameObjectConversionUtility.GameObjectToConvertedEntity(gameObjectWorld, root);
            
            gameObjectWorld.Dispose();
        }

    }
}