# Content management

You can use the [`RuntimeContentManger`](xref:Unity.Entities.Content.RuntimeContentManager) API to load and release Unity engine objects, and GameObject scene from [systems](systems-intro.md) or MonoBehaviour code.

## Main content APIs

The primary APIs for objects are:
* [`LoadObjectAsync`](xref:Unity.Entities.Content.RuntimeContentManager.LoadObjectAsync*)
* [`GetObjectLoadingStatus`](xref:Unity.Entities.Content.RuntimeContentManager.GetObjectLoadingStatus*)
* [`GetObjectValue`](Unity.Entities.Content.RuntimeContentManager.GetObjectValue*)
* [`ReleaseObjectAsync`](xref:Unity.Entities.Content.RuntimeContentManager.ReleaseObjectAsync*)

The primary APIs for scenes are:
* [`LoadSceneAsync`](xref:Unity.Entities.Content.RuntimeContentManager.LoadSceneAsync*)
* [`GetSceneLoadingStatus`](xref:Unity.Entities.Content.RuntimeContentManager.GetSceneLoadingStatus*)
* [`GetSceneValue`](xref:Unity.Entities.Content.RuntimeContentManager.GetSceneValue*)
* [`ReleaseScene`](xref:Unity.Entities.Content.RuntimeContentManager.ReleaseScene*) 

Both scenes and objects are reference counted and released once there are no longer any references to them.  

This example illustrates using `RuntimeContentManger` to load and render a mesh with a material from an ECS system:

```c#
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Transforms;
using UnityEngine;

public struct DecorationVisualComponentData : IComponentData
{
    public bool startedLoad;
    public WeakObjectReference<Mesh> mesh;
    public WeakObjectReference<Material> material;
}

[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct DecorationRenderSystem : ISystem
{
    public void OnCreate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (transform, dec) in SystemAPI.Query<RefRW<LocalToWorld>, RefRW<DecorationVisualComponentData>>())
        {
            if (!dec.ValueRW.startedLoad)
            {
                dec.ValueRW.mesh.LoadAsync();
                dec.ValueRW.material.LoadAsync();
            }

            if (dec.ValueRW.mesh.LoadingStatus == ObjectLoadingStatus.Completed &&
                dec.ValueRW.material.LoadingStatus == ObjectLoadingStatus.Completed)
            {
                Graphics.DrawMesh(dec.ValueRO.mesh.Result, 
                    transform.ValueRO.Value, dec.ValueRO.material.Result, 0);
            }
        }
    }
}
```

## Content building

Referenced engine objects are automatically collected for each [subscene](conversion-subscenes.md) during the import process. During a build, Unity combines these object references from the scenes included in the build and builds them into [`ContentArchives`](xref:Unity.Entities.Content.ContentArchivesBuildUtility). Any `UntypedWeakReferenceId` that's serialized into entity data is included in the build. 

For convenience and better Editor workflows, you can use the [`WeakObjectReference<TObject>`](xref:Unity.Entities.Content.WeakObjectReference`1) and `WeakSceneReference` types in a `MonoBehaviour` and copy them to the `ComponentData` from the [`Baker`](xref:Unity.Entities.Baker`1).  

Here is an example of [baking](baking.md) the reference from a `MonoBehavior` to the `ComponentData`:

```c#
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

public class MeshRefSample : MonoBehaviour
{
    public WeakObjectReference<Mesh> mesh;
    class MeshRefSampleBaker : Baker<MeshRefSample>
    {
        public override void Bake(MeshRefSample authoring)
        {
            AddComponent(new MeshComponentData { mesh = authoring.mesh });
        }
    }
}
public struct MeshComponentData : IComponentData
{
    public WeakObjectReference<Mesh> mesh;
}
```

### Content delivery
The `RuntimeContentManger` APIs rely on content files to be on the device.  [`ContentDeliveryService`](xref:Unity.Entities.Content.ContentDeliveryService) can be used to download these files on demand.  Any file in the streaming assets folder can be delivered by the `ContentDeliveryService` but code that relies on these files must wait until they have been delivered.  


The primary APIs are:

* [`DeliverContent`](xref:Unity.Entities.Content.ContentDeliveryService.DeliverContent*)
* [`GetDeliveryStatus`](xref:Unity.Entities.Content.ContentDeliveryService.GetDeliveryStatus*)
* [`CancelDelivery`](xref:Unity.Entities.Content.ContentDeliveryService.CancelDelivery*)

Here is an example of how to wait for content delivery to complete before continuing with the game logic:
```C#
using System;
using Unity.Entities.Content;
using UnityEngine;
public class GameStarter : MonoBehaviour
{
    public string remoteUrlRoot;
    public string initialContentSet;
    void Start()
    {
#if ENABLE_CONTENT_DELIVERY
        ContentDeliverySystem.Instance.UpdateContent(remoteUrlRoot, initialContentSet);
        ContentDeliverySystem.Instance.RegisterForContentUpdateCompletion(s =>
        {
            LoadMainScene();
        });
#else
        LoadMainScene();
#endif
    }
    
    void LoadMainScene()
    {
        //content is ready here...
    }
}
```

To prepare content for remote delivery, you must "publish" a build using the APIs found in [`RemoteContentCatalogBuildUtility`](xref:Unity.Entities.Content.RemoteContentCatalogBuildUtility).  Any files in the streaming assets folder of the build can be published.  These files will be renamed with the content hash and put into a folder structure that mirrors the structure of the local device cache.  This data can then be put on a server for the players to download.

Here is an example of how to create a content build and update:
```C#
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Build.Classic;
using Unity.Collections;
using Unity.Scenes.Editor;
using UnityEditor.Experimental;
using UnityEngine;
using UnityEditor;
using System.Linq;
using Unity.Build;
using Unity.Build.Common;
using Unity.Entities.Build;
using Unity.Entities.Content;

static class BuildUtilities
{
    //prepares the content files for publish.  The original files can be deleted or retained during this process by changing the last parameter of the PublishContent call.
     static void PublishExistingBuild()
    {
        var buildFolder = EditorUtility.OpenFolderPanel("Select Build To Publish",
                            Path.GetDirectoryName(Application.dataPath), "Builds");
        if (!string.IsNullOrEmpty(buildFolder))
        {
            var streamingAssetsPath = $"{buildFolder}/{PlayerSettings.productName}_Data/StreamingAssets";
            //the content sets are defined by the functor passed in here.  
            RemoteContentCatalogBuildUtility.PublishContent(streamingAssetsPath, 
                $"{buildFolder}-RemoteContent", 
                f => new string[] { "all" }, true);
        }
}

    //This method is somewhat complicated because it will build the scenes from a player build but without fully building the player.
    static void CreateContentUpdate()
    {
       var buildFolder = EditorUtility.OpenFolderPanel("Select Build To Publish",
                    Path.GetDirectoryName(Application.dataPath), "Builds");
        if (!string.IsNullOrEmpty(buildFolder))
        {
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var tmpBuildFolder = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                        $"/Library/ContentUpdateBuildDir/{PlayerSettings.productName}");
    
            var instance = DotsGlobalSettings.Instance;
            var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client ? instance.GetClientGUID() : instance.GetServerGUID();
            if (!playerGuid.IsValid)
                throw new Exception("Invalid Player GUID");
    
            var subSceneGuids = new HashSet<Hash128>();
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            {
                var ssGuids = EditorEntityScenes.GetSubScenes(EditorBuildSettings.scenes[i].guid);
                foreach (var ss in ssGuids)
                    subSceneGuids.Add(ss);
            }
            var artifactKeys = new Dictionary<Hash128, ArtifactKey>();
            var binaryFiles = new EntitySectionBundlesInBuild();
    
            EntitySceneBuildUtility.PrepareEntityBinaryArtifacts(playerGuid, subSceneGuids, artifactKeys);
            binaryFiles.Add(artifactKeys.Keys, artifactKeys.Values);
            var entitySceneGUIDs = binaryFiles.SceneGUIDs.ToArray();
    
            EntitySceneBuildUtility.PrepareAdditionalFiles(default, artifactKeys.Keys.ToArray(), 
                    artifactKeys.Values.ToArray(), buildTarget, (s, d) => DoCopy(s, Path.Combine(tmpBuildFolder, d)));
    
            var publishFolder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", $"{buildFolder}-RemoteContent");
            PublishContent(tmpBuildFolder, publishFolder, f => new string[] { "all" });
        }
    }
}
```
