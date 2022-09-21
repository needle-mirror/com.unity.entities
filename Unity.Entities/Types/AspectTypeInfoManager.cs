#if !UNITY_DOTSRUNTIME
using System;
using System.Linq;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    struct AspectTypeInfoManager
    {
        struct AspectTypeInfoTag { };
        static readonly SharedStatic<AspectTypeInfo> s_AspectTypeInfo = SharedStatic<AspectTypeInfo>.GetOrCreate<AspectTypeInfoTag>();
        static UnsafeMultiHashMap<ulong, AspectType> m_ArchetypeAspectMap;

        static void InitializeAspectTypeInfo()
        {
            var aspectTypes = TypeManager.GetAllAspectTypes();
            if (aspectTypes == null || !aspectTypes.Any())
                return;

            s_AspectTypeInfo.Data = new AspectTypeInfo
            {
                AspectTypes = new UnsafeList<AspectType>(aspectTypes.Length, Allocator.Persistent),
                AspectRequiredComponents = new UnsafeMultiHashMap<AspectType, ComponentType>(aspectTypes.Length, Allocator.Persistent),
                AspectExcludedComponents = new UnsafeMultiHashMap<AspectType, ComponentType>(aspectTypes.Length, Allocator.Persistent)
            };

            for (var i = 0; i < aspectTypes.Length; ++i)
            {
                var aspectType = AspectType.FromTypeIndex(i);
                var aspectManagedType = aspectType.GetManagedType();

                var requiredComponentsProperty = aspectManagedType.GetProperty("RequiredComponents", BindingFlags.Public | BindingFlags.Static);
                var requiredComponents = requiredComponentsProperty?.GetValue(null, null) as ComponentType[];

                var excludeComponentsProperty = aspectManagedType.GetProperty("ExcludeComponents", BindingFlags.Public | BindingFlags.Static);
                var excludeComponents = excludeComponentsProperty?.GetValue(null, null) as ComponentType[];

                s_AspectTypeInfo.Data.AspectTypes.AddNoResize(aspectType);
                if (requiredComponents != null)
                {
                    foreach (var required in requiredComponents)
                        s_AspectTypeInfo.Data.AspectRequiredComponents.Add(aspectType, required);
                }

                if (excludeComponents != null)
                {
                    foreach (var excluded in excludeComponents)
                        s_AspectTypeInfo.Data.AspectExcludedComponents.Add(aspectType, excluded);
                }
            }
        }

        internal static void Dispose()
        {
            s_AspectTypeInfo.Data.Dispose();
            if (m_ArchetypeAspectMap.IsCreated)
                m_ArchetypeAspectMap.Dispose();
        }

        internal static NativeArray<AspectType> GetAspectTypesFromEntity(World world, Entity entity)
        {
            if (s_AspectTypeInfo.Data.AspectTypes.Length == 0)
                InitializeAspectTypeInfo();

            var archetype = world.EntityManager.GetChunk(entity).Archetype;
            var aspectTypes = new NativeList<AspectType>(16, Allocator.Temp);
            if (!m_ArchetypeAspectMap.IsCreated)
                m_ArchetypeAspectMap = new UnsafeMultiHashMap<ulong, AspectType>(16, Allocator.Persistent);

            // If mapping already exists, return those values.
            if (m_ArchetypeAspectMap.ContainsKey(archetype.StableHash))
            {
                foreach (var at in m_ArchetypeAspectMap.GetValuesForKey(archetype.StableHash))
                    aspectTypes.Add(at);
            }
            else
            {
                // Otherwise, build up the mapping.
                var componentTypes = archetype.GetComponentTypes();
                foreach (var at in s_AspectTypeInfo.Data.AspectTypes)
                {
                    if (ContainAllRequiredComponents(componentTypes, at) && !ContainAnyExcludeComponents(componentTypes, at))
                    {
                        m_ArchetypeAspectMap.Add(archetype.StableHash, at);
                        aspectTypes.Add(at);
                    }
                }

                componentTypes.Dispose();
            }

            var result = aspectTypes.ToArray(Allocator.Temp);
            aspectTypes.Dispose();
            return result;
        }

        static bool ContainAllRequiredComponents(NativeArray<ComponentType> componentTypes, AspectType aspectType)
        {
            foreach (var comp in s_AspectTypeInfo.Data.AspectRequiredComponents.GetValuesForKey(aspectType))
            {
                if (!componentTypes.Contains(comp))
                    return false;
            }

            return true;
        }

        static bool ContainAnyExcludeComponents(NativeArray<ComponentType> componentTypes, AspectType aspectType)
        {
            foreach (var comp in s_AspectTypeInfo.Data.AspectExcludedComponents.GetValuesForKey(aspectType))
            {
                if (componentTypes.Contains(comp))
                    return true;
            }

            return false;
        }
    }
}
#endif
