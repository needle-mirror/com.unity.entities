using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Build;
using Unity.Build.DotsRuntime;
using Unity.Core.Compression;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.SceneManagement;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

namespace Unity.Scenes.Editor
{
    [ScriptedImporter(90, "extDontMatter")]
    [InitializeOnLoad]
    class SubSceneImporter : ScriptedImporter
    {
        static SubSceneImporter()
        {
            EntityScenesPaths.SubSceneImporterType = typeof(SubSceneImporter);
        }

        static unsafe NativeList<Hash128> ReferencedUnityObjectsToGUIDs(ReferencedUnityObjects referencedUnityObjects, AssetImportContext ctx)
        {
            var globalObjectIds = new GlobalObjectId[referencedUnityObjects.Array.Length];
            var guids = new NativeList<Hash128>(globalObjectIds.Length, Allocator.Temp);

            GlobalObjectId.GetGlobalObjectIdsSlow(referencedUnityObjects.Array, globalObjectIds);

            for (int i = 0; i != globalObjectIds.Length; i++)
            {
                var assetGUID = globalObjectIds[i].assetGUID;
                // Skip most built-ins, except for BuiltInExtra which we need to depend on
                if (GUIDHelper.IsBuiltin(assetGUID))
                {
                    if (GUIDHelper.IsBuiltinExtraResources(assetGUID))
                    {
                        var objectIdentifiers = ContentBuildInterface.GetPlayerObjectIdentifiersInAsset(assetGUID, ctx.selectedBuildTarget);

                        foreach (var objectIdentifier in objectIdentifiers)
                        {
                            var packedGUID = assetGUID;
                            GUIDHelper.PackBuiltinExtraWithFileIdent(ref packedGUID, objectIdentifier.localIdentifierInFile);

                            guids.Add(packedGUID);
                        }
                    }
                    else if (GUIDHelper.IsBuiltinResources(assetGUID))
                    {
                        guids.Add(assetGUID);
                    }
                }
                else if(!assetGUID.Empty())
                {
                    guids.Add(assetGUID);
                }
            }

            return guids;
        }

        static void WriteAssetDependencyGUIDs(List<ReferencedUnityObjects> referencedUnityObjects, SceneSectionData[] sectionData, AssetImportContext ctx)
        {
            for (var index = 0; index < referencedUnityObjects.Count; index++)
            {
                var sectionIndex = sectionData[index].SubSectionIndex;

                var objRefs = referencedUnityObjects[index];
                if (objRefs == null)
                    continue;

                var path = ctx.GetResultPath($"{sectionIndex}.{EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesAssetDependencyGUIDs)}");
                var assetDependencyGUIDs = ReferencedUnityObjectsToGUIDs(objRefs, ctx);

                using (var writer = new StreamBinaryWriter(path))
                {
                    writer.Write(assetDependencyGUIDs.Length);
                    writer.WriteArray(assetDependencyGUIDs.AsArray());
                }

                assetDependencyGUIDs.Dispose();
            }
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            try
            {
                var sceneWithBuildConfiguration = SceneWithBuildConfigurationGUIDs.ReadFromFile(ctx.assetPath);

                // Ensure we have as many dependencies as possible registered early in case an exception is thrown
                EditorEntityScenes.AddEntityBinaryFileDependencies(ctx, sceneWithBuildConfiguration.BuildConfiguration);
                EditorEntityScenes.DependOnSceneGameObjects(sceneWithBuildConfiguration.SceneGUID, ctx);

                var config = BuildConfiguration.LoadAsset(sceneWithBuildConfiguration.BuildConfiguration);

                var scenePath = AssetDatabaseCompatibility.GuidToPath(sceneWithBuildConfiguration.SceneGUID);
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                try
                {
                    EditorSceneManager.SetActiveScene(scene);

                    var settings = new GameObjectConversionSettings();

                    settings.SceneGUID = sceneWithBuildConfiguration.SceneGUID;
                    if (!sceneWithBuildConfiguration.IsBuildingForEditor)
                        settings.ConversionFlags |= GameObjectConversionUtility.ConversionFlags.IsBuildingForPlayer;

                    settings.BuildConfiguration = config;
                    settings.AssetImportContext = ctx;
                    settings.FilterFlags = WorldSystemFilterFlags.HybridGameObjectConversion;

                    WriteEntitySceneSettings writeEntitySettings = new WriteEntitySceneSettings();
                    if (config != null && config.TryGetComponent<DotsRuntimeBuildProfile>(out var profile))
                    {
                        if (config.TryGetComponent<DotsRuntimeRootAssembly>(out var rootAssembly))
                        {
                            writeEntitySettings.Codec = Codec.LZ4;
                            writeEntitySettings.IsDotsRuntime = true;
                            writeEntitySettings.BuildAssemblyCache = new BuildAssemblyCache()
                            {
                                BaseAssemblies = rootAssembly.RootAssembly.asset,
                                PlatformName = profile.Target.UnityPlatformName
                            };
                            settings.FilterFlags = WorldSystemFilterFlags.DotsRuntimeGameObjectConversion;

                            //Updating the root asmdef references or its references should re-trigger conversion
                            ctx.DependsOnArtifact(AssetDatabase.GetAssetPath(rootAssembly.RootAssembly.asset));
                            foreach (var assemblyPath in writeEntitySettings.BuildAssemblyCache.AssembliesPath)
                            {
                                ctx.DependsOnArtifact(assemblyPath);
                            }
                        }
                    }

                    var sectionRefObjs = new List<ReferencedUnityObjects>();
                    var sectionData = EditorEntityScenes.ConvertAndWriteEntityScene(scene, settings, sectionRefObjs, writeEntitySettings);

                    WriteAssetDependencyGUIDs(sectionRefObjs, sectionData, ctx);

                    foreach(var objRefs in sectionRefObjs)
                        DestroyImmediate(objRefs);
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            // Currently it's not acceptable to let the asset database catch the exception since it will create a default asset without any dependencies
            // This means a reimport will not be triggered if the scene is subsequently modified
            catch (Exception e)
            {
                Debug.Log($"Exception thrown during SubScene import: {e}");
            }
        }
    }
}
