using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Hybrid;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Unity.Build.Common;
using Unity.Build.Classic;
using Unity.Build;
using Unity.Properties;
using Unity.Platforms;

namespace Unity.Scenes.Editor
{
    [BuildStep(Name = "Build Resource Catalog", Description = "Build Resource Catalog", Category = "Classic")]
    sealed class BuildStepCreateResourceCatalog : BuildStep
    {
        const string k_Description = "Build Resource Catalog";
        string SceneInfoPath => $"Assets/StreamingAssets/{SceneSystem.k_SceneInfoFileName}";

        TemporaryFileTracker m_TemporaryFileTracker;

        public override Type[] RequiredComponents => new[]
        {
            typeof(SceneList)
        };

        unsafe public override BuildStepResult RunBuildStep(BuildContext context)
        {
            m_TemporaryFileTracker = new TemporaryFileTracker();

            var sceneList = GetRequiredComponent<SceneList>(context);
            var sceneInfos = sceneList.GetSceneInfosForBuild();
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ResourceCatalogData>();
            var metas = builder.Allocate(ref root.resources, sceneInfos.Length);
            for (int i = 0; i < sceneInfos.Length; i++)
            {
                metas[i] = new ResourceMetaData()
                {
                    ResourceId = sceneInfos[i].Scene.assetGUID,
                    ResourceFlags = sceneInfos[i].AutoLoad ? ResourceMetaData.Flags.AutoLoad : ResourceMetaData.Flags.None,
                    ResourceType = ResourceMetaData.Type.Scene,
                };
            }

            var componentData = builder.Allocate(ref root.paths, sceneInfos.Length);
            for (int i = 0; i < sceneInfos.Length; i++)
                builder.AllocateString(ref componentData[i], sceneInfos[i].Path);

            BlobAssetReference<ResourceCatalogData>.Write(builder, m_TemporaryFileTracker.TrackFile(SceneInfoPath), ResourceCatalogData.CurrentFileFormatVersion);
            builder.Dispose();

            return Success();
        }

        public override BuildStepResult CleanupBuildStep(BuildContext context)
        {
            m_TemporaryFileTracker.Dispose();
            return Success();
        }
    }
}
