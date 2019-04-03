using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Unity.Entities
{
    static class DefaultWorldInitialization
    {
        static void DomainUnloadShutdown()
        {
            World.DisposeAllWorlds();
            ScriptBehaviourUpdateOrder.UpdatePlayerLoop();
        }

        static void GetBehaviourManagerAndLogException(World world, Type type)
        {
            try
            {
                world.GetOrCreateManager(type);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static void Initialize(string worldName, bool editorWorld)
        {
            var world = new World(worldName);
            World.Active = world;

            // Register hybrid injection hooks
            InjectionHookSupport.RegisterHook(new GameObjectArrayInjectionHook());
            InjectionHookSupport.RegisterHook(new TransformAccessArrayInjectionHook());
            InjectionHookSupport.RegisterHook(new ComponentArrayInjectionHook());

            PlayerLoopManager.RegisterDomainUnload(DomainUnloadShutdown, 10000);

            foreach (var ass in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var allTypes = ass.GetTypes();

                    // Create all ComponentSyste
                    var systemTypes = allTypes.Where(t =>
                        t.IsSubclassOf(typeof(ComponentSystemBase)) &&
                        !t.IsAbstract &&
                        !t.ContainsGenericParameters &&
                        (t.GetCustomAttributes(typeof(ComponentSystemPatchAttribute), true).Length == 0) &&
                        t.GetCustomAttributes(typeof(DisableAutoCreationAttribute), true).Length == 0);
                    foreach (var type in systemTypes)
                    {
                        if (editorWorld && type.GetCustomAttributes(typeof(ExecuteInEditMode), true).Length == 0)
                            continue;

                        GetBehaviourManagerAndLogException(world, type);
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Can happen for certain assembly during the GetTypes() step
                }
            }
            
            foreach (var ass in AppDomain.CurrentDomain.GetAssemblies())
            {
                var allTypes = ass.GetTypes();

                // Create all ComponentSystem
                var systemTypes = allTypes.Where(t => 
                    t.IsSubclassOf(typeof(ComponentSystemBase)) && 
                    !t.IsAbstract && 
                    !t.ContainsGenericParameters && 
                    (t.GetCustomAttributes(typeof(ComponentSystemPatchAttribute), true).Length > 0) &&
                    t.GetCustomAttributes(typeof(DisableAutoCreationAttribute), true).Length == 0);
                foreach (var type in systemTypes)
                {
                    if (editorWorld && type.GetCustomAttributes(typeof(ExecuteInEditMode), true).Length == 0)
                        continue;

                    world.AddComponentSystemPatch(type);
                }
            }

            ScriptBehaviourUpdateOrder.UpdatePlayerLoop(world);
        }
    }
}
