#if !UNITY_DOTSRUNTIME
using System;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.Content
{
    /// <summary>
    /// System responsible for initializing and updating the <seealso cref="RuntimeContentManager"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class RuntimeContentSystem : SystemBase
    {
        /// <summary>Initializes the <seealso cref="RuntimeContentManager"/>.</summary>
        protected override void OnCreate()
        {
#if !UNITY_EDITOR && !ENABLE_CONTENT_DELIVERY
            LoadContentCatalog(null, null, null, false);
#endif
        }

        /// <summary>
        /// Loads the content catalog data.
        /// </summary>
        /// <remarks>
        /// By default, this loads the catalog from the StreamingAssets folder.
        /// However, if you've set the 'ENABLE_CONTENT_DELIVERY' define, this initiates the content delivery system and updates the content before loading the catalog.
        ///</remarks>
        /// <param name="remoteUrlRoot">The remote URL root for the content. Set null or leave empty, to load the catalog from the local StreamingAssets path.</param>
        /// <param name="localCachePath">Optional path for the local cache. Set null or leave empty to create a folder named 'ContentCache' in the device's Application.persistentDataPath.</param>
        /// <param name="initialContentSet">Initial content set to download.  'all' is generally used to denote the entire content set.</param>
        /// <param name="allowOverrideArgs">Set to true, to use application command line arguments to override the passed in values.</param>
        public static void LoadContentCatalog(string remoteUrlRoot, string localCachePath, string initialContentSet, bool allowOverrideArgs = false)
        {
#if ENABLE_CONTENT_DELIVERY
            if (allowOverrideArgs)
            {
                if (TryGetAppArg("remoteRoot", ref remoteUrlRoot))
                    ContentDeliveryGlobalState.LogFunc?.Invoke($"Overwrote remoteRoot to '{remoteUrlRoot}'");
                if (TryGetAppArg("cachePath", ref localCachePath))
                    ContentDeliveryGlobalState.LogFunc?.Invoke($"Overwrote cachePath to '{localCachePath}'");
                if (TryGetAppArg("contentSet", ref initialContentSet))
                    ContentDeliveryGlobalState.LogFunc?.Invoke($"Overwrote contentSet '{initialContentSet}'");
            }

            if (string.IsNullOrEmpty(remoteUrlRoot))
            {
                if (!string.IsNullOrEmpty(localCachePath))
                {
                    ContentDeliveryGlobalState.Initialize("", localCachePath, null, s =>
                    {
                        if (s >= ContentDeliveryGlobalState.ContentUpdateState.ContentReady)
                            LoadCatalogFunc(ContentDeliveryGlobalState.PathRemapFunc);
                    });
                }
                else
                {
                    //even though ENABLE_CONTENT_DELIVERY is enabled, still allow the local catalog to be used without checking for updates.
                    LoadCatalogFunc(ContentDeliveryGlobalState.PathRemapFunc = p => $"{Application.streamingAssetsPath}/{p}");
                }
            }
            else
            {
                //if no cache specified, set to default
                if (string.IsNullOrEmpty(localCachePath))
                    localCachePath = System.IO.Path.Combine(Application.persistentDataPath, "ContentCache");
                //start content update process
                ContentDeliveryGlobalState.Initialize(remoteUrlRoot, localCachePath, initialContentSet, s =>
                {
                    if (s >= ContentDeliveryGlobalState.ContentUpdateState.ContentReady)
                        LoadCatalogFunc(ContentDeliveryGlobalState.PathRemapFunc);
                });
            }

#else
            LoadCatalogFunc(p => $"{Application.streamingAssetsPath}/{p}");
#endif
        }

        /// <summary>
        /// Processes the <seealso cref="RuntimeContentManager"/>.
        /// </summary>
        protected override void OnUpdate()
        {
            if (World.Flags == WorldFlags.Game || World.Flags == WorldFlags.GameServer || World.Flags == WorldFlags.GameClient || World.Flags == WorldFlags.GameThinClient || World.Flags == WorldFlags.Editor)
            {
                //always update the CDGS in the player so that the catalog can load
#if ENABLE_CONTENT_DELIVERY && !UNITY_EDITOR
                ContentDeliveryGlobalState.Update();
#endif

#if !UNITY_EDITOR  //only update RCM in the player if the catalog has been loaded
                if(RuntimeContentManager.IsReady)
#endif
                    RuntimeContentManager.ProcessQueuedCommands();
            }
        }

        static void LoadCatalogFunc(Func<string, string> remapFunc)
        {
            var catalogPath = remapFunc(RuntimeContentManager.RelativeCatalogPath);
            if (ContentDeliveryGlobalState.FileExists(catalogPath))
                RuntimeContentManager.LoadLocalCatalogData(catalogPath,
                    RuntimeContentManager.DefaultContentFileNameFunc,
                    p => remapFunc(RuntimeContentManager.DefaultArchivePathFunc(p)));
        }

        static bool TryGetAppArg(string name, ref string value)
        {
            if (!Application.HasARGV(name))
                return false;

            value = Application.GetValueForARGV(name);
            return true;
        }
    }
}
#endif
