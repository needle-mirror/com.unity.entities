using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEditor;
using System.Reflection;
using System.Text;
#if UNITY_EDITOR
using Unity.Entities.Build;
#endif
using Unity.Entities.Conversion;

namespace Unity.Entities
{
    class AssemblyEnabled
    {
        public bool Enabled;
    }

    /// <summary>
    ///  A mapping of UnityEngine.Component (represented by TypeManager.GetTypeIndex) => array of bakers that need to execute to bake the authoring data.
    /// </summary>
    internal struct BakerDataUtility
    {
        static Dictionary<TypeIndex, BakerData[]>             _IndexToBakerInstances;
        static Dictionary<Assembly, AssemblyEnabled>          _BakersByAssembly;

        public struct BakerData
        {
            public ProfilerMarker   Profiler;
            public IBaker           Baker;
            public AssemblyEnabled  AssemblyEnabled;
        }

#if UNITY_EDITOR
        public static void ApplyAssemblyFilter(ConversionSystemFilterSettings filter)
        {
            foreach (var assemblyEntry in _BakersByAssembly)
            {
                assemblyEntry.Value.Enabled = (filter == null || !filter.IsAssemblyExcluded(assemblyEntry.Key));
            }
        }

        public static void ApplyAssemblyFilter(BakingSystemFilterSettings filter)
        {
            foreach (var assemblyEntry in _BakersByAssembly)
            {
                assemblyEntry.Value.Enabled = (filter == null || !filter.IsAssemblyExcluded(assemblyEntry.Key));
            }
        }
#endif

        public static BakerData[] GetBakers(TypeIndex typeIndex)
        {
            if (!_IndexToBakerInstances.TryGetValue(typeIndex, out var bakerData))
                return null;

            return bakerData;
        }

        public static void Initialize()
        {
            if (_IndexToBakerInstances != null)
                return;

            _IndexToBakerInstances = new Dictionary<TypeIndex, BakerData[]>();
            _BakersByAssembly = new Dictionary<Assembly, AssemblyEnabled>();

#if UNITY_EDITOR
            using (var marker = new ProfilerMarker("Baking.RegisterBakers").Auto())
            {
                foreach (var type in TypeCache.GetTypesDerivedFrom(typeof(Baker<>)))
                {
                    if (!type.IsAbstract && !type.IsDefined(typeof(DisableAutoCreationAttribute)))
                    {
                        try
                        {
                            AddBaker(type);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
            }
#endif
        }

        static void AddBaker(Type type)
        {
            var baker = (IBaker) Activator.CreateInstance(type);
            var authoringType = baker.GetAuthoringType();

            // Try find with the authoring type in the TypeManager
            if (TypeManager.TryGetTypeIndex(authoringType, out var typeIndex))
            {
                AddBaker(type, baker, typeIndex);
            }

            // If BakeDerivedTypesAttribute is used in the baker,
            // try find derived types that are present in TypeManager
            if (type.IsDefined(typeof(BakeDerivedTypesAttribute)))
            {
#if UNITY_EDITOR
                // In the editor we can use TypeCache as an accelerator
                var types = TypeCache.GetTypesDerivedFrom(authoringType);
                foreach (var compatibleType in types)
                {
                    if (TypeManager.TryGetTypeIndex(compatibleType, out typeIndex))
                    {
                        AddBaker(type, baker, typeIndex);
                    }
                }
#else
                // At runtime we need to make a linear search for derived clases
                for (var i = 0; i < TypeManager.GetTypeCount(); ++i)
                {
                    var compatibleType = TypeManager.GetType(new TypeIndex { Value = i });
                    if (authoringType.IsAssignableFrom(compatibleType))
                    {
                        typeIndex = TypeManager.GetTypeIndex(compatibleType);
                        AddBaker(type, (IBaker) Activator.CreateInstance(type), typeIndex);
                    }
                }
#endif
            }
        }

        static void AddBaker(Type type, IBaker baker, TypeIndex typeIndex)
        {
            var bakerIndex = 0;
            if (!_IndexToBakerInstances.TryGetValue(typeIndex, out var bakers))
            {
                bakers = new BakerData[1];
            }
            else
            {
                bakerIndex = bakers.Length;
                Array.Resize(ref bakers, bakerIndex + 1);
            }

            if (!_BakersByAssembly.TryGetValue(type.Assembly, out var assemblyEnabled))
            {
                assemblyEnabled = new AssemblyEnabled();
                assemblyEnabled.Enabled = true;
                _BakersByAssembly.Add(type.Assembly, assemblyEnabled);
            }

            bakers[bakerIndex] = new BakerData
            {
                Baker = baker,
                Profiler = new ProfilerMarker(baker.GetType().Name),
                AssemblyEnabled = assemblyEnabled
            };

            _IndexToBakerInstances[typeIndex] = bakers;
        }

        /// <summary>
        /// Overrides the global list of bakers either adding new ones or replacing old ones.
        /// This is used for tests. Always make sure to dispose to revert the global state back to what it was.
        /// </summary>
        internal struct OverrideBakers : IDisposable
        {
            Dictionary<TypeIndex, BakerData[]>     OldBakers;

            public OverrideBakers(bool replaceExistingBakers, params Type[] bakerTypes)
            {
                Initialize();

                OldBakers = _IndexToBakerInstances;

                if (replaceExistingBakers)
                    _IndexToBakerInstances = new Dictionary<TypeIndex, BakerData[]>(OldBakers.Count);
                else
                    _IndexToBakerInstances = new Dictionary<TypeIndex, BakerData[]>(OldBakers);

                foreach (var type in bakerTypes)
                    AddBaker(type);
            }

            public void Dispose()
            {
                _IndexToBakerInstances = OldBakers;
            }
        }
    }
}
