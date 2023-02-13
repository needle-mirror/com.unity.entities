using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEditor.AssetImporters;
using System.Reflection;
#if USING_PLATFORMS_PACKAGE
using Unity.Build;
#endif
using Unity.Entities.Conversion;
using Unity.Entities.Serialization;
using Unity.Profiling;

namespace Unity.Scenes.Editor
{
    [InitializeOnLoad]
    class TypeDependencyCache
    {
        const string SystemsVersion = "DOTSAllSystemsVersion";

        struct BakingNameAndVersion
        {
            public string FullName;
            public string UserName;
            public int    Version;
            public bool   Excluded;
            public Type   Type;
            public string AssemblyName;

            public void Init(BakingVersionAttribute bakingVersionAttribute, Type type, string fullName)
            {
                FullName = fullName;
                Type = type;
                AssemblyName = type.Assembly.GetName().Name;
                Version = bakingVersionAttribute.Version;
                UserName = bakingVersionAttribute.UserName;
                Excluded = bakingVersionAttribute.Excluded;
            }
        }

        static ProfilerMarker kRegisterComponentTypes = new ProfilerMarker("TypeDependencyCache.RegisterComponentTypes");
        static ProfilerMarker kRegisterBakingAssemblies = new ProfilerMarker("TypeDependencyCache.RegisterBakingAssemblies");

        static string ComponentTypeString(Type type) => $"DOTSType/{type.FullName}";
        static string ManagedTypeString(Type type) => $"DOTSManagedType/{type.FullName}";

        static unsafe TypeDependencyCache()
        {
            //TODO: Find a better way to enforce Version 2 compatibility
            bool v2Enabled = (bool)typeof(AssetDatabase).GetMethod("IsV2Enabled", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            if (!v2Enabled)
                throw new System.InvalidOperationException("com.unity.entities requires Asset Pipeline Version 2. Please enable Version 2 in Project Settings / Editor / Asset Pipeline / Mode");

            // Custom dependencies are transmitted to the import worker so dont spent time on registering them
            if (AssetDatabaseCompatibility.IsAssetImportWorkerProcess())
                return;

            using (kRegisterComponentTypes.Auto())
                RegisterComponentTypes();

            using(kRegisterBakingAssemblies.Auto())
                RegisterBakingAssemblies();

            int fileFormatVersion = SerializeUtility.CurrentFileFormatVersion;
            UnityEngine.Hash128 fileFormatHash = default;
            HashUnsafeUtilities.ComputeHash128(&fileFormatVersion, sizeof(int), &fileFormatHash);
            AssetDatabaseCompatibility.RegisterCustomDependency("EntityBinaryFileFormatVersion", fileFormatHash);

            int sceneFileFormatVersion = SceneMetaDataSerializeUtility.CurrentFileFormatVersion;
            UnityEngine.Hash128 sceneFileFormatHash = default;
            HashUnsafeUtilities.ComputeHash128(&sceneFileFormatVersion, sizeof(int), &sceneFileFormatHash);
            AssetDatabaseCompatibility.RegisterCustomDependency("SceneMetaDataFileFormatVersion", sceneFileFormatHash);
        }

        static void RegisterComponentTypes()
        {
            TypeManager.Initialize();

            AssetDatabaseCompatibility.UnregisterCustomDependencyPrefixFilter("DOTSType/");
            int typeCount = TypeManager.GetTypeCount();

            for (int i = 1; i < typeCount; ++i)
            {
                ref readonly var typeInfo = ref TypeManager.GetTypeInfo(new TypeIndex{ Value = i });
                var hash = typeInfo.StableTypeHash;
                AssetDatabaseCompatibility.RegisterCustomDependency(ComponentTypeString(typeInfo.Type),
                    new UnityEngine.Hash128(hash, hash));
            }
        }

        static unsafe void RegisterBakingAssemblies()
        {
            var bakers = TypeCache.GetTypesDerivedFrom<IBaker>();
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.BakingSystem | WorldSystemFilterFlags.EntitySceneOptimizations);
            BakingNameAndVersion[] versionedAssemblies = new BakingNameAndVersion[bakers.Count + systems.Count];

            int count = 0;
            // baker versions
            for (int i = 0; i != bakers.Count; i++)
            {
                var fullName = bakers[i].FullName;
                if (fullName == null)
                    continue;

                var bakingVersionAttribute = bakers[i].GetCustomAttribute<BakingVersionAttribute>();
                if ( bakingVersionAttribute != null)
                {
                    versionedAssemblies[count++].Init(bakingVersionAttribute, bakers[i], fullName);
                }
            }

            // baking system versions
            for (int i = 0; i != systems.Count; i++)
            {
                var fullName = systems[i].FullName;
                if (fullName == null)
                    continue;

                var bakingVersionAttribute = systems[i].GetCustomAttribute<BakingVersionAttribute>();
                if ( bakingVersionAttribute != null)
                {
                    versionedAssemblies[count++].Init(bakingVersionAttribute, systems[i], fullName);
                }
            }

            List<string> assemblies = new List<string>();
            UnityEngine.Hash128 hash = default;
            Dictionary<string, List<Type>> missingBakingVersionAttributePerAssembly = new Dictionary<string, List<Type>>();

            foreach (var bakerType in bakers)
            {
                var assembly = bakerType.Assembly;
                var assemblyName = assembly.GetName().Name;
                //If there is at least one baker marked with BakingVersion attribute, we don't register the dependency with the assembly but the value of the attribute
                var bakingVersionAttributes = Array.FindAll(versionedAssemblies, x => !x.Excluded && x.AssemblyName == assemblyName);
                if (bakingVersionAttributes.Length > 0)
                {
                    //If the bakerType doesn't have a baking version attribute, but is part of an assembly that have some. We need to warn the user to add the attribute on it
                    var missingBakingVersion = Array.FindAll(versionedAssemblies, x => !x.Excluded && x.Type == bakerType);
                    if (missingBakingVersion.Length == 0)
                    {
                        if (!missingBakingVersionAttributePerAssembly.TryGetValue(assemblyName, out var missingAttrib))
                            missingAttrib = new List<Type>();
                        missingAttrib.Add(bakerType);
                        missingBakingVersionAttributePerAssembly[assemblyName] = missingAttrib;
                    }
                    else
                    {
                        var value = Array.Find(bakingVersionAttributes, x => x.Type == bakerType);
                        var fullName = value.FullName;
                        fixed (char* str = fullName)
                        {
                            HashUnsafeUtilities.ComputeHash128(str, (ulong) (sizeof(char) * fullName.Length), &hash);
                        }
                        var userName = value.UserName;
                        if (userName != null)
                        {
                            fixed(char* str = userName)
                            {
                                HashUnsafeUtilities.ComputeHash128(str, (ulong)(sizeof(char) * userName.Length), &hash);
                            }
                        }

                        int version = value.Version;
                        HashUnsafeUtilities.ComputeHash128(&version, sizeof(int), &hash);
                    }
                }
                else if (!assemblies.Contains(assemblyName) && !Array.Exists(versionedAssemblies, x => x.Excluded && x.Type == bakerType))
                {
                    assemblies.Add(assemblyName);
                    var moduleVersionId = assembly.ManifestModule.ModuleVersionId;
                    fixed(char* str = moduleVersionId.ToString())
                    {
                        HashUnsafeUtilities.ComputeHash128(str, (ulong)(sizeof(char) * moduleVersionId.ToString().Length), &hash);
                    }
                }
            }

            foreach (var systemType in systems)
            {
                var assembly = systemType.Assembly;
                var assemblyName = assembly.GetName().Name;
                //If there is at least one baking system or entity scene optimization system marked with BakingVersion attribute, we don't register the dependency with the assembly but the value of the attribute
                var bakingVersionAttributes = Array.FindAll(versionedAssemblies, x => !x.Excluded && x.AssemblyName == assemblyName);
                if (bakingVersionAttributes.Length > 0)
                {
                    //If the bakerType doesn't have a baking version attribute, but is part of an assembly that have some. We need to warn the user to add the attribute on it
                    var missingBakingVersion = Array.FindAll(versionedAssemblies, x => !x.Excluded && x.Type == systemType);
                    if (missingBakingVersion.Length == 0)
                    {
                        if (!missingBakingVersionAttributePerAssembly.TryGetValue(assemblyName, out var missingAttrib))
                            missingAttrib = new List<Type>();
                        missingAttrib.Add(systemType);
                        missingBakingVersionAttributePerAssembly[assemblyName] = missingAttrib;
                    }
                    else
                    {
                        var value = Array.Find(bakingVersionAttributes, x => x.Type == systemType);
                        var fullName = value.FullName;
                        fixed (char* str = fullName)
                        {
                            HashUnsafeUtilities.ComputeHash128(str, (ulong)(sizeof(char) * fullName.Length), &hash);
                        }
                        var userName = value.UserName;
                        if (userName != null)
                        {
                            fixed(char* str = userName)
                            {
                                HashUnsafeUtilities.ComputeHash128(str, (ulong)(sizeof(char) * userName.Length), &hash);
                            }
                        }

                        int version = value.Version;
                        HashUnsafeUtilities.ComputeHash128(&version, sizeof(int), &hash);
                    }
                }
                else if (!assemblies.Contains(assemblyName) && !Array.Exists(versionedAssemblies, x => x.Excluded && x.Type == systemType))
                {
                    assemblies.Add(assemblyName);
                    var moduleVersionId = assembly.ManifestModule.ModuleVersionId;
                    fixed(char* str = moduleVersionId.ToString())
                    {
                        HashUnsafeUtilities.ComputeHash128(str, (ulong)(sizeof(char) * moduleVersionId.ToString().Length), &hash);
                    }
                }
            }
            AssetDatabaseCompatibility.RegisterCustomDependency(SystemsVersion, hash);

            PrintMissingBakingVersionAttributesWarning(missingBakingVersionAttributePerAssembly);
        }

        static void PrintMissingBakingVersionAttributesWarning(Dictionary<string, List<Type>> dict)
        {
            if (dict.Count > 0)
            {
                var str = "";
                foreach (var pair in dict)
                {
                    var listOfTypes = "";
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        var type = pair.Value[i];
                        listOfTypes += $"{type.FullName}";
                        if (i < pair.Value.Count - 1)
                            listOfTypes += ", ";
                    }

                    str += $"The assembly {pair.Key} is using the BakingVersion attribute. But the following Baker/Baking systems/EntitySceneOptimizations systems are missing the attribute:\n{listOfTypes}.\n\n";
                }
                Debug.LogWarning($"{str}Updating these bakers/systems won't trigger a scene import automatically. Please add a BakingVersion attribute on them and update their version number everytime you want them to be rerun before an import. " +
                                 $"\nOr don't use the attribute at all in their assembly to trigger scene import automatically after each changes in the assembly.");
            }
        }

        public static void AddComponentTypeDependency(AssetImportContext ctx, ComponentType type)
        {
            var typeString = ComponentTypeString(type.GetManagedType());
            ctx.DependsOnCustomDependency(typeString);
        }

        public static void AddManagedTypeDependency(AssetImportContext ctx, Type type)
        {
            var typeString = ManagedTypeString(type);
            ctx.DependsOnCustomDependency(typeString);
        }

        public static void AddAllSystemsDependency(AssetImportContext ctx)
        {
            ctx.DependsOnCustomDependency(SystemsVersion);
        }
    }
}
