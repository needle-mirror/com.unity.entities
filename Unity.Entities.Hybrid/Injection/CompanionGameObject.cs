#if !UNITY_DISABLE_MANAGED_COMPONENTS
using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.Entities
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad] // ensures type manager is initialized on domain reload when not playing
#endif
    static unsafe class AttachToEntityClonerInjection
    {
        // Injection is used to keep everything GameObject related outside of Unity.Entities

        static AttachToEntityClonerInjection()
        {
            InitializeTypeManager();
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeTypeManager()
        {
            ManagedComponentStore.InstantiateHybridComponent = InstantiateHybridComponentDelegate;
        }

        /// <summary>
        /// This method will handle the cloning of Hybrid Components (if any) during the batched instantiation of an Entity
        /// </summary>
        /// <param name="srcArray">Array of source managed component indices. One per <paramref name="componentCount"/></param>
        /// <param name="componentCount">Number of component being instantiated</param>
        /// <param name="dstEntities">Array of destination entities. One per <paramref name="instanceCount"/></param>
        /// <param name="dstArray">Array of destination managed component indices. One per <paramref name="componentCount"/>*<paramref name="instanceCount"/>. All indices for the first component stored first etc.</param>
        /// <param name="instanceCount">Number of instances being created</param>
        /// <param name="managedComponentStore">Managed Store that owns the instances we create</param>
        static void InstantiateHybridComponentDelegate(int* srcArray, int componentCount, Entity* dstEntities, int* dstArray, int instanceCount, ManagedComponentStore managedComponentStore)
        {
            object[] gameObjectInstances = null;
                
            for (int src = 0; src < componentCount; ++src)
            {
                object sourceComponent = managedComponentStore.GetManagedComponent(srcArray[src]);
                if ((sourceComponent as UnityEngine.Component)?.gameObject.GetComponent<CompanionLink>() == null)
                {
                    for (int i = 0; i < instanceCount; ++i)
                        managedComponentStore.SetManagedComponentValue(dstArray[i], sourceComponent);
                }
                else
                {
                    var unityComponent = (UnityEngine.Component) sourceComponent;

                    if (gameObjectInstances == null)
                    {
                        gameObjectInstances = new object[instanceCount];

                        for (int i = 0; i < instanceCount; ++i)
                        {
                            var instance = GameObject.Instantiate(unityComponent.gameObject);
                            instance.name = CompanionLink.GenerateCompanionName(dstEntities[i]);
                            gameObjectInstances[i] = instance;
                            instance.hideFlags |= HideFlags.HideInHierarchy;
                        }
                    }

                    for (int i = 0; i < instanceCount; i++)
                    {
                        var componentInInstance = ((GameObject)gameObjectInstances[i]).GetComponent(unityComponent.GetType());
                        managedComponentStore.SetManagedComponentValue(dstArray[i], componentInInstance);
                    }
                }

                dstArray += instanceCount;
            }
        }
    }
}
#endif
