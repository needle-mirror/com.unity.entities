#if USING_PLATFORMS_PACKAGE
using Unity.Build.Common;
#endif
using Unity.Collections;
using Unity.Entities;

namespace Unity.Scenes.Editor
{
    static class ResourceCatalogBuildCode
    {
#if USING_PLATFORMS_PACKAGE
        public static void WriteCatalogFile(SceneList sceneList, string sceneInfoPath)
        {
            var dir = System.IO.Path.GetDirectoryName(sceneInfoPath);
            System.IO.Directory.CreateDirectory(dir);
            var sceneInfos = sceneList.GetSceneInfosForBuild();
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ResourceCatalogData>();
            var metas = builder.Allocate(ref root.resources, sceneInfos.Length);
            for (int i = 0; i < sceneInfos.Length; i++)
            {
                metas[i] = new ResourceMetaData()
                {
                    ResourceId = sceneInfos[i].Scene.assetGUID,
                    ResourceType = ResourceMetaData.Type.Scene,
                };
            }

            var strings = builder.Allocate(ref root.paths, sceneInfos.Length);
            for (int i = 0; i < sceneInfos.Length; i++)
                builder.AllocateString(ref strings[i], sceneInfos[i].Path);

            BlobAssetReference<ResourceCatalogData>.Write(builder, sceneInfoPath, ResourceCatalogData.CurrentFileFormatVersion);
            builder.Dispose();
        }
#endif

        public static void WriteCatalogFile(RootSceneInfo[] sceneArray, string sceneInfoPath)
        {
            var dir = System.IO.Path.GetDirectoryName(sceneInfoPath);
            System.IO.Directory.CreateDirectory(dir);
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ResourceCatalogData>();
            var metas = builder.Allocate(ref root.resources, sceneArray.Length);
            for (int i = 0; i < sceneArray.Length; i++)
            {
                metas[i] = new ResourceMetaData()
                {
                    ResourceId = sceneArray[i].Guid,
                    ResourceType = ResourceMetaData.Type.Scene,
                };
            }

            var strings = builder.Allocate(ref root.paths, sceneArray.Length);
            for (int i = 0; i < sceneArray.Length; i++)
                builder.AllocateString(ref strings[i], sceneArray[i].Path);

            BlobAssetReference<ResourceCatalogData>.Write(builder, sceneInfoPath, ResourceCatalogData.CurrentFileFormatVersion);
            builder.Dispose();
        }
    }
}
