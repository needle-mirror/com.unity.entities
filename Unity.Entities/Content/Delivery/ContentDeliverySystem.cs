#if !UNITY_DOTSRUNTIME
using System;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.Content
{
    /// <summary>
    /// System to process the <seealso cref="ContentDeliveryService"/>.  This system also will ensure that the content is properly updated./>
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    public partial class ContentDeliverySystem : SystemBase
    {
        /// <summary>
        /// Action to log content update progress.
        /// </summary>
        public static Action<string> LogFunc = null;

        /// <summary>
        /// The name of the content set for the catalogs.
        /// </summary>
        public const string kCatalogLocations = "catalogs";

        /// <summary>
        /// States of content update process.
        /// </summary>
        public enum ContentUpdateState
        {
            /// <summary>
            /// Content update process has not started.
            /// </summary>
            None,
            /// <summary>
            /// Downloading uncached catalog info - this happens each time the application is started to check for updated catalogs.
            /// </summary>
            DownloadingCatalogInfo,
            /// <summary>
            /// Downloading catalogs.  
            /// </summary>
            DownloadingCatalogs,
            /// <summary>
            /// Downloading initial content sets.
            /// </summary>
            DownloadingContentSet,
            /// <summary>
            /// The content update failed and there is no data in the cache.
            /// </summary>
            NoContentAvailable,
            /// <summary>
            /// Content is ready.  It may be local, updated from remote, or from the cache.  
            /// </summary>
            ContentReady,
            /// <summary>
            /// The content will be loaded from the streaming assets folder and no attempt to update will be made.
            /// </summary>
            UsingContentFromStreamingAssets,
            /// <summary>
            /// The content has successfully been updated from the remote content server.
            /// </summary>
            ContentUpdatedFromRemote,
            /// <summary>
            /// The content update failed, content will be loaded from the local cache if possible.
            /// </summary>
            UsingContentFromCache
        }

        static ContentDeliverySystem _instance;
        /// <summary>
        /// Static instance of this system. This needs to be a singleton since it is responsible for managing content delivery.
        /// </summary>
        public static ContentDeliverySystem Instance => _instance;

        /// <summary>
        /// Functor used to remap content into the local cache.
        /// </summary>
        public Func<string, string> PathRemapFunc => contentDeliveryService == null ? (p => $"{Application.streamingAssetsPath}/{p}") : contentDeliveryService.RemapContentPath;

        /// <summary>
        /// The current state of the content update process.
        /// </summary>
        public ContentUpdateState CurrentContentUpdateState => contentUpdateState;

        Action<ContentUpdateState> OnContentReady;
        ContentUpdateState contentUpdateState = ContentUpdateState.None;
        ContentUpdateContext contentUpdateContext;
        ContentDeliveryService contentDeliveryService;


        /// <summary>
        /// Register an action that will be called when ContentDeliveryService is ready for use.
        /// </summary>
        /// <param name="action">The action to invoke when the service is ready.  This may be called inline if the service has already updated.</param>
        public void RegisterForContentUpdateCompletion(Action<ContentUpdateState> action)
        {
            if (contentUpdateState < ContentUpdateState.NoContentAvailable)
            {
                OnContentReady += action;
            }
            else
            {
                action(contentUpdateState);
            }
        }

        static bool TryGetAppArg(string name, string defaultVal, out string value)
        {
            if (!Application.HasARGV(name))
            {
                value = defaultVal;
                return false;
            }
            value = Application.GetValueForARGV(name);
            if (string.IsNullOrEmpty(value))
            {
                value = defaultVal;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Starts the content delivery process.  Content updates will be applied here as well.
        /// </summary>
        /// <param name="remoteUrlRoot">The root url of all content files to be delivered.</param>
        /// <param name="initialContentSet">The initial content set to download.  This can be null if none are needed.</param>
        /// <param name="cachePath">The local cahe path for the content files.  If not specified this will default to Path.Combine(Application.persistentDataPath, "ContentCache");</param>
        public void UpdateContent(string remoteUrlRoot, string initialContentSet, string cachePath = null)
        {
            if (string.IsNullOrEmpty(cachePath))
                cachePath = Path.Combine(Application.persistentDataPath, "ContentCache");
            contentUpdateContext = new ContentUpdateContext()
            {
                remoteUrlRoot = remoteUrlRoot,
                cachePath = cachePath,
                initialContentSet = initialContentSet
            };

            contentDeliveryService = new ContentDeliveryService();
            contentDeliveryService.AddDownloadService(new ContentDownloadService("default", cachePath, 1, 5, null));
        }

        /// <summary>
        /// Called when the system is created.  The ContentDeliveryService is created and updated.
        /// </summary>
        protected override void OnCreate()
        {
            _instance = this;
#if ENABLE_CONTENT_DELIVERY
            if (TryGetAppArg("remoteRoot", null, out var remoteUrlRoot))
            {
                LogFunc?.Invoke($"Set remote root to '{remoteUrlRoot}'");
                TryGetAppArg("cachePath", Path.Combine(Application.persistentDataPath, "ContentCache"), out var cachePath);
                LogFunc?.Invoke($"Set local cache path to '{cachePath}'");
                TryGetAppArg("contentSet", "all", out var initialContentSet);
                LogFunc?.Invoke($"Downloading content set '{initialContentSet}'");
                UpdateContent(remoteUrlRoot, initialContentSet, cachePath);
            }
            else
#endif
            contentUpdateState = ContentUpdateState.UsingContentFromStreamingAssets;
        }

        /// <summary>
        /// Called when the system is destroyed.  The ContentDeliveryService will be destroyed.
        /// </summary>
        protected override void OnDestroy()
        {
#if ENABLE_CONTENT_DELIVERY
            contentDeliveryService?.Dispose();
#endif
        }

        /// <summary>
        /// The content delivery service is updated.
        /// </summary>
        protected override void OnUpdate()
        {
#if ENABLE_CONTENT_DELIVERY
            contentDeliveryService?.Process();
            if (contentUpdateContext != null && contentUpdateContext.Update(contentDeliveryService, ref contentUpdateState))
            {
                if (OnContentReady != null)
                {
                    OnContentReady(contentUpdateState);
                    OnContentReady = null;
                }
                contentUpdateContext = null;

                //no connection, remote delivery service
                if (contentUpdateState < ContentUpdateState.ContentUpdatedFromRemote)
                {
                    contentDeliveryService.Dispose();
                    contentDeliveryService = null;
                }
            }
#endif
        }

        internal static string CreateCatalogLocationPath(string streamingAssetsPath) => Path.Combine(streamingAssetsPath, $"{kCatalogLocations}.bin");


        internal class ContentUpdateContext
        {
            public string remoteUrlRoot;
            public string initialContentSet;
            public string cachePath;
            RemoteContentId currentRemoteId;

            public bool Update(ContentDeliveryService contentDeliveryService, ref ContentUpdateState currentUpdateState)
            {
                if (currentUpdateState == ContentUpdateState.None)
                {
                    LogFunc?.Invoke($"Downloading remote catalog info from {remoteUrlRoot}{kCatalogLocations}.bin");
                    currentRemoteId = contentDeliveryService.DeliverContent($"{remoteUrlRoot}{kCatalogLocations}.bin", default, 0, 0);
                    currentUpdateState = ContentUpdateState.DownloadingCatalogInfo;
                }

                if (currentUpdateState == ContentUpdateState.DownloadingCatalogInfo)
                {
                    var status = contentDeliveryService.GetDeliveryStatus(currentRemoteId);
                    if (status.State == ContentDeliveryService.DeliveryState.ContentDownloaded)
                    {
                        LogFunc?.Invoke($"Remote catalog info loaded, creating location service from {status.DownloadStatus.LocalPath}");
                        var catalogInfoPath = Path.Combine(cachePath, $"{kCatalogLocations}.bin");
                        File.Copy(status.DownloadStatus.LocalPath.ToString(), catalogInfoPath, true);
                        File.Delete(status.DownloadStatus.LocalPath.ToString());
                        var catLocSvc = new DefaultContentLocationService(kCatalogLocations, 1, catalogInfoPath, p => $"{remoteUrlRoot}{p}");
                        contentDeliveryService.AddLocationService(catLocSvc);
                        LogFunc?.Invoke($"Downloading content set '{kCatalogLocations}'");
                        currentRemoteId = contentDeliveryService.DeliverContent(kCatalogLocations);
                        currentUpdateState = ContentUpdateState.DownloadingCatalogs;
                    }
                    else if (status.State == ContentDeliveryService.DeliveryState.Failed)
                    {
                        LogFunc?.Invoke($"Failed to load remote catalog info.");
                        var catalogInfoPath = Path.Combine(cachePath, $"{kCatalogLocations}.bin");
                        if (File.Exists(catalogInfoPath))
                        {
                            LogFunc?.Invoke($"Cached catalog info file found at '{catalogInfoPath}', attempting to use cached content.");
                            var catLocSvc = new DefaultContentLocationService(kCatalogLocations, 1, catalogInfoPath, p => $"{remoteUrlRoot}{p}");
                            contentDeliveryService.AddLocationService(catLocSvc);
                            LogFunc?.Invoke($"Downloading content set '{kCatalogLocations}'");
                            currentRemoteId = contentDeliveryService.DeliverContent(kCatalogLocations);
                            currentUpdateState = ContentUpdateState.DownloadingCatalogs;
                        }
                        else
                        {
                            currentUpdateState = AttemptToSetContentPathsToStreamingAssets();
                        }
                    }
                }

                if (currentUpdateState == ContentUpdateState.DownloadingCatalogs)
                {
                    var status = contentDeliveryService.GetDeliveryStatus(currentRemoteId);
                    if (status.State == ContentDeliveryService.DeliveryState.ContentDownloaded)
                    {
                        var deliveryStatus = new NativeList<ContentDeliveryService.DeliveryStatus>(3, Allocator.Temp);
                        contentDeliveryService.GetDeliveryStatus(currentRemoteId, ref deliveryStatus);
                        LogFunc?.Invoke($"Content set '{kCatalogLocations}' loaded, found {deliveryStatus.Length} catalog locations.");
                        for (int i = 0; i < deliveryStatus.Length; i++)
                        {
                            LogFunc?.Invoke($"Loading catalog '{deliveryStatus[i].ContentId.Name}' from path '{deliveryStatus[i].DownloadStatus.LocalPath}'");
                            var locSvc = new DefaultContentLocationService(deliveryStatus[i].ContentId.Name.ToString(), 2 + (deliveryStatus.Length - i), deliveryStatus[i].DownloadStatus.LocalPath.ToString(), p => $"{remoteUrlRoot}{p}");
                            contentDeliveryService.AddLocationService(locSvc);
                        }
                        deliveryStatus.Dispose();
                        if (!string.IsNullOrEmpty(initialContentSet))
                        {
                            LogFunc?.Invoke($"Downloading content set '{initialContentSet}'");
                            currentRemoteId = contentDeliveryService.DeliverContent(initialContentSet);
                            currentUpdateState = ContentUpdateState.DownloadingContentSet;
                        }
                        else
                        {
                            LogFunc?.Invoke("No initial content set specified");
                            currentRemoteId = default;
                            currentUpdateState = ContentUpdateState.ContentUpdatedFromRemote;
                        }
                    }
                    else if (status.State == ContentDeliveryService.DeliveryState.Failed)
                    {
                        currentUpdateState = AttemptToSetContentPathsToStreamingAssets(); 
                    }
                }
                if (currentUpdateState == ContentUpdateState.DownloadingContentSet)
                {
                    var status = contentDeliveryService.GetDeliveryStatus(currentRemoteId);
                    if (status.State == ContentDeliveryService.DeliveryState.ContentDownloaded)
                    {
                        var deliveryStatus = new NativeList<ContentDeliveryService.DeliveryStatus>(128, Allocator.Temp);
                        contentDeliveryService.GetDeliveryStatus(currentRemoteId, ref deliveryStatus);
                        LogFunc?.Invoke($"Content set '{initialContentSet}' loaded, found {deliveryStatus.Length} content locations.");
                        deliveryStatus.Dispose();
                        currentRemoteId = default;
                        currentUpdateState = ContentUpdateState.ContentUpdatedFromRemote;
                    }
                    else if (status.State == ContentDeliveryService.DeliveryState.Failed)
                    {
                        currentUpdateState = AttemptToSetContentPathsToStreamingAssets();
                    }
                }
                return (currentUpdateState >= ContentUpdateState.NoContentAvailable);
            }

            private ContentUpdateState AttemptToSetContentPathsToStreamingAssets()
            {
                if (File.Exists($"{Application.streamingAssetsPath}/{RuntimeContentManager.RelativeCatalogPath}"))
                {
                    LogFunc?.Invoke($"No connection, attempting to use streaming assets.");
                    return ContentUpdateState.UsingContentFromStreamingAssets;
                }
                else
                {
                    LogFunc?.Invoke($"No connection, no cached data, not streaming assets.");
                    return ContentUpdateState.NoContentAvailable;
                }
            }

        }
    }
}
#endif
