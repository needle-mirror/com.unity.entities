using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Build;
using UnityEditor;
using UnityEditor.SceneManagement;
using AssetImportContext = UnityEditor.Experimental.AssetImporters.AssetImportContext;

namespace Unity.Scenes.Editor
{
    [UnityEditor.Experimental.AssetImporters.ScriptedImporter(71, "extDontMatter")]
    [InitializeOnLoad]
    class SubSceneImporter : UnityEditor.Experimental.AssetImporters.ScriptedImporter
    {
        static SubSceneImporter()
        {
            EntityScenesPaths.SubSceneImporterType = typeof(SubSceneImporter);
        }

        static unsafe EntityScenesPaths.SceneWithBuildConfigurationGUIDs ReadSceneWithBuildConfiguration(string path)
        {
            EntityScenesPaths.SceneWithBuildConfigurationGUIDs sceneWithBuildConfiguration = default;
            using (var reader = new StreamBinaryReader(path, sizeof(EntityScenesPaths.SceneWithBuildConfigurationGUIDs)))
            {
                reader.ReadBytes(&sceneWithBuildConfiguration, sizeof(EntityScenesPaths.SceneWithBuildConfigurationGUIDs));
            }
            return sceneWithBuildConfiguration;
        }

        public static void ConvertToBuild(GUID buildConfigurationGUIDSceneGuid, UnityEditor.Build.Pipeline.Tasks.CalculateCustomDependencyData task)
        {
            var buildConfigurationScenePath = AssetDatabase.GUIDToAssetPath(buildConfigurationGUIDSceneGuid.ToString());
            var sceneWithBuildConfiguration = ReadSceneWithBuildConfiguration(buildConfigurationScenePath);

            var hash = UnityEditor.Experimental.AssetDatabaseExperimental.GetArtifactHash(buildConfigurationGUIDSceneGuid.ToString(), typeof(SubSceneImporter));
            string[] paths;
            if (!UnityEditor.Experimental.AssetDatabaseExperimental.GetArtifactPaths(hash, out paths))
                return;

            foreach (var path in paths)
            {
                var ext = System.IO.Path.GetExtension(path).Replace(".", "");
                if (ext == EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesHeader))
                {
                    var loadPath = EntityScenesPaths.GetLoadPath(sceneWithBuildConfiguration.SceneGUID, EntityScenesPaths.PathType.EntitiesHeader, -1);
                    System.IO.File.Copy(path, loadPath, true);
                    continue;
                }

                if (ext == EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesBinary))
                {
                    var sectionIndex = EntityScenesPaths.GetSectionIndexFromPath(path);
                    var loadPath = EntityScenesPaths.GetLoadPath(sceneWithBuildConfiguration.SceneGUID, EntityScenesPaths.PathType.EntitiesBinary, sectionIndex);
                    System.IO.File.Copy(path, loadPath, true);
                    continue;
                }

                if (ext == EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesUnityObjectReferences))
                {
                    var sectionIndex = EntityScenesPaths.GetSectionIndexFromPath(path);
                    task.GetObjectIdentifiersAndTypesForSerializedFile(path, out UnityEditor.Build.Content.ObjectIdentifier[] objectIds, out System.Type[] types);
                    var bundlePath = EntityScenesPaths.GetLoadPath(sceneWithBuildConfiguration.SceneGUID, EntityScenesPaths.PathType.EntitiesUnityObjectReferences, sectionIndex);
                    var bundleName = System.IO.Path.GetFileName(bundlePath);
                    task.CreateAssetEntryForObjectIdentifiers(objectIds, path, bundleName, bundleName, typeof(ReferencedUnityObjects));
                }
            }
        }
        
        public static unsafe NativeList<RuntimeGlobalObjectId> ReferencedUnityObjectsToRuntimeGlobalObjectIds(ReferencedUnityObjects referencedUnityObjects, Allocator allocator = Allocator.Temp)
        {
            var globalObjectIds = new GlobalObjectId[referencedUnityObjects.Array.Length];
            var runtimeGlobalObjIDs = new NativeList<RuntimeGlobalObjectId>(globalObjectIds.Length, allocator);
            
            GlobalObjectId.GetGlobalObjectIdsSlow(referencedUnityObjects.Array, globalObjectIds);
            
            for (int i = 0; i != globalObjectIds.Length; i++)
            {
                var globalObjectId = globalObjectIds[i];

                //@TODO: HACK (Object is a scene object)
                if (globalObjectId.identifierType == 2)
                {
                    Debug.LogWarning($"{referencedUnityObjects.Array[i]} is part of a scene, LiveLink can't transfer scene objects. (Note: LiveConvertSceneView currently triggers this)");
                    continue;
                }

                if (globalObjectId.assetGUID == new GUID())
                {
                    //@TODO: How do we handle this
                    Debug.LogWarning($"{referencedUnityObjects.Array[i]} has no valid GUID. LiveLink currently does not support built-in assets.");
                    continue;
                }

                var runtimeGlobalObjectId =
                    System.Runtime.CompilerServices.Unsafe.AsRef<RuntimeGlobalObjectId>(&globalObjectId);
                runtimeGlobalObjIDs.Add(runtimeGlobalObjectId);
            }

            return runtimeGlobalObjIDs;
        }

        public static void WriteRefGuids(List<ReferencedUnityObjects> referencedUnityObjects, AssetImportContext ctx)
        {
            for (var index = 0; index < referencedUnityObjects.Count; index++)
            {
                var objRefs = referencedUnityObjects[index];
                if (objRefs == null)
                    continue;

                var refGuidsPath = ctx.GetResultPath($"{index}.{EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesUnityObjectRefGuids)}");
                var runtimeGlobalObjectIds = ReferencedUnityObjectsToRuntimeGlobalObjectIds(objRefs);

                using (var refGuidWriter = new StreamBinaryWriter(refGuidsPath))
                {
                    refGuidWriter.Write(runtimeGlobalObjectIds.Length);
                    refGuidWriter.WriteArray(runtimeGlobalObjectIds.AsArray());
                }

                runtimeGlobalObjectIds.Dispose();
            }
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            try
            {
                ctx.DependsOnCustomDependency("EntityBinaryFileFormatVersion");

                var sceneWithBuildConfiguration = ReadSceneWithBuildConfiguration(ctx.assetPath);

                // Ensure we have as many dependencies as possible registered early in case an exception is thrown
                var scenePath = AssetDatabase.GUIDToAssetPath(sceneWithBuildConfiguration.SceneGUID.ToString());
                ctx.DependsOnSourceAsset(scenePath);

                if (sceneWithBuildConfiguration.BuildConfiguration.IsValid)
                {
                    var buildConfigurationPath = AssetDatabase.GUIDToAssetPath(sceneWithBuildConfiguration.BuildConfiguration.ToString());
                    ctx.DependsOnSourceAsset(buildConfigurationPath);
                    var buildConfigurationDependencies = AssetDatabase.GetDependencies(buildConfigurationPath);
                    foreach (var dependency in buildConfigurationDependencies)
                        ctx.DependsOnSourceAsset(dependency);
                }

                var dependencies = AssetDatabase.GetDependencies(scenePath);
                foreach (var dependency in dependencies)
                {
                    if (dependency.ToLower().EndsWith(".prefab"))
                        ctx.DependsOnSourceAsset(dependency);
                }

                var config = BuildConfiguration.LoadAsset(sceneWithBuildConfiguration.BuildConfiguration);

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                try
                {
                    var settings = new GameObjectConversionSettings();

                    settings.SceneGUID = sceneWithBuildConfiguration.SceneGUID;
                    settings.BuildConfiguration = config;
                    settings.AssetImportContext = ctx;

                    var sectionRefObjs = new List<ReferencedUnityObjects>();
                    EditorEntityScenes.WriteEntityScene(scene, settings, sectionRefObjs);
                    WriteRefGuids(sectionRefObjs, ctx);
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            // Currently it's not acceptable to let the asset database catch the exception since it will create a default asset without any dependencies
            // This means a reimport will not be triggered if the scene is subsequently modified
            catch(Exception e)
            {
                Debug.Log($"Exception thrown during SubScene import: {e}");
            }
        }
    }
}