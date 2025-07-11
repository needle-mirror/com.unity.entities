using System;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.Content
{
    /// <summary>Contains methods for the content update process.</summary>
    public static class ContentDeliveryGlobalState
    {
        /// <summary>
        /// Action to log content update progress.
        /// </summary>
        public static Action<string> LogFunc = null;

        /// <summary>
        /// The name of the content set for remote catalogs.
        /// </summary>
        public const string kCatalogLocations = "catalogs";

        /// <summary>
        /// The name of the content set for local catalogs.
        /// </summary>
        public const string kLocalCatalogsContentSet = "local_catalogs";

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
            /// Downloading remote catalogs.  This consists of any remote catalogs contained in the catalog info file.
            /// </summary>
            DownloadingCatalogs,
            /// <summary>
            /// Downloading local catalogs.  During the publish process, all .bin files are added to this content set.
            /// </summary>
            DownloadingLocalCatalogs,
            /// <summary>
            /// Downloading initial content sets.  This can be used to specify an initial set of content to download before initialization completes.
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

        /// <summary>
        /// Functor used to remap content into the local cache.  The first parameter is the original path, usually relative to the StreamingAssets folder.
        /// The boolean parameter indicates if the remapped path should be checked before using it.  The return value should be the remapped path and it should exist on device
        /// if the bool parameter is true.  If the bool parameter is false, this can return a remapped path that does not exist yet.  
        /// </summary>
        public static Func<string, bool, string> PathRemapFuncWithFileCheck;

        /// <summary>
        /// Functor used to remap content to cached path.  
        /// </summary>
        [Obsolete("Use PathRemapFuncWithFileCheck instead as it allows for checking the local device for files before remapping.")]
        public static Func<string, string> PathRemapFunc => s => PathRemapFuncWithFileCheck(s, false);

        /// <summary>
        /// The current state of the content update process.
        /// </summary>
        public static ContentUpdateState CurrentContentUpdateState => contentUpdateState;

        static Action<ContentUpdateState> OnContentReady;
        static ContentUpdateState contentUpdateState = ContentUpdateState.None;
        static ContentUpdateContext contentUpdateContext;
        static ContentDeliveryService contentDeliveryService;


        /// <summary>
        /// Gets the delivery service used to download remote content.  This can be used to initiate the download of additional files or content sets.
        /// If initialization fails to download remote content, this will be null;
        /// </summary>
        public static ContentDeliveryService DeliveryService
        {
            get { return contentDeliveryService; }
        }


        /// <summary>
        /// Release resources from the content delivery initialization process and delivery service.
        /// </summary>
        public static void Cleanup()
        {
            PathRemapFuncWithFileCheck = null;
            OnContentReady = null;
            contentUpdateContext = null;
            contentUpdateState = ContentUpdateState.None;
            contentDeliveryService?.Dispose();
        }


        /// <summary>
        /// Register for content update state changes.
        /// </summary>
        /// <param name="updateStateFunc">The action that will be called for each content update state change.</param>
        public static void RegisterForContentUpdateCompletion(Action<ContentUpdateState> updateStateFunc)
        {
            if (updateStateFunc != null)
            {
                if (contentUpdateState >= ContentUpdateState.ContentReady)
                    updateStateFunc(contentUpdateState);
                else
                    OnContentReady += updateStateFunc;
            }
        }

        internal static bool IsValidURLRoot(string remoteUrlRoot)
        {
            if (string.IsNullOrEmpty(remoteUrlRoot))
                return false;

            if (!Uri.TryCreate(remoteUrlRoot, UriKind.Absolute, out var uri))
            {
                LogFunc?.Invoke($"Invalid uri used for remoteUrlRoot: '{remoteUrlRoot}'.");
                return false;
            }
            if(uri.Scheme == Uri.UriSchemeHttps ||
                uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeFile ||
                uri.Scheme == Uri.UriSchemeFtp)
            {
                return true;
            }
            LogFunc?.Invoke($"Uri scheme {uri.Scheme} is not supported with specified remoteUrlRoot: '{remoteUrlRoot}'.");
            return false;
        }

        /// <summary>
        /// Initialize the content delivery system.
        /// </summary>
        /// <param name="remoteUrlRoot">The remote url root in the form of "https://hostname.com/pathToContent/".  If this parameter is not specified, content is assumed to be local.</param>
        /// <param name="cachePath">The full local path of the content cache.  This must be a directory that the application has read and write access to.</param>
        /// <param name="initialContentSet">The initial set of content to download.  The content sets are given names during the publish process and by default everything is added to the "all" set.</param>
        /// <param name="updateStateFunc">Callback action that will get called whenever the content update state changes.</param>
        public static void Initialize(string remoteUrlRoot, string cachePath, string initialContentSet, Action<ContentUpdateState> updateStateFunc)
        {
            LogFunc?.Invoke($"ContentDeliveryGlobalState.Initialize({remoteUrlRoot}, {cachePath}, {initialContentSet})");
            if (!IsValidURLRoot(remoteUrlRoot))
            {
                contentUpdateState = ContentUpdateState.UsingContentFromStreamingAssets;
                PathRemapFuncWithFileCheck = (p,d) => $"{Application.streamingAssetsPath}/{p}";
            }
            else
            {
                contentUpdateState = ContentUpdateState.None;
                contentUpdateContext = new ContentUpdateContext()
                {
                    remoteUrlRoot = remoteUrlRoot,
                    cachePath = cachePath,
                    initialContentSet = initialContentSet
                };

                contentDeliveryService = new ContentDeliveryService();
                PathRemapFuncWithFileCheck = contentDeliveryService.RemapContentPath;
                contentDeliveryService.AddDownloadService(new ContentDownloadService("default", cachePath, 1, 5, null));
            }
            RegisterForContentUpdateCompletion(updateStateFunc);
        }

        internal static void Update()
        {
            // Update only during play mode in the Editor or when running in the Player
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            if (contentDeliveryService == null)
                return;
            contentDeliveryService.Process();

            if (contentUpdateContext == null)
                return;

            ContentUpdateState state = contentUpdateState;
            if (contentUpdateContext.Update(contentDeliveryService, ref state))
            {
                //update context is not needed anymore
                contentUpdateContext = null;

                //no connection and no cached data, use local data in streaming assets
                if (state == ContentUpdateState.UsingContentFromStreamingAssets)
                {
                    contentDeliveryService.Dispose();
                    contentDeliveryService = null;
                    PathRemapFuncWithFileCheck = (p,d) => $"{Application.streamingAssetsPath}/{p}";
                }
            }

            //if the state changes, save new state and invoke callbacks
            if (state != contentUpdateState)
            {
                contentUpdateState = state;
                if (OnContentReady != null)
                {
                    OnContentReady(contentUpdateState);
                    if (contentUpdateState >= ContentUpdateState.ContentReady)
                        OnContentReady = null;
                }
            }
        }

        internal static string CreateCatalogLocationPath(string streamingAssetsPath) => Path.Combine(streamingAssetsPath, $"{kCatalogLocations}.bin");

        internal unsafe static bool FileExists(string path)
        {
#if UNITY_EDITOR
            return System.IO.File.Exists(path);
#else
                IO.LowLevel.Unsafe.FileInfoResult result;
                var readHandle = IO.LowLevel.Unsafe.AsyncReadManager.GetFileInfo(path, &result);
                readHandle.JobHandle.Complete();
                return result.FileState == IO.LowLevel.Unsafe.FileState.Exists;
#endif
        }


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
                            if (!currentRemoteId.IsValid)
                            {
                                LogFunc?.Invoke($"Unable to resolve content for remote catalogs, name = '{kCatalogLocations}'");
                                currentUpdateState = AttemptToSetContentPathsToStreamingAssets();
                            }
                            else
                            {
                                currentUpdateState = ContentUpdateState.DownloadingCatalogs;
                            }
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

                        LogFunc?.Invoke("Downloading local catalogs.");
                        currentRemoteId = contentDeliveryService.DeliverContent(kLocalCatalogsContentSet);
                        if (currentRemoteId.IsValid)
                        {
                            currentUpdateState = ContentUpdateState.DownloadingLocalCatalogs;
                        }
                        else
                        {
                            LogFunc?.Invoke($"Failed to resolve local catalog content set with name '{kLocalCatalogsContentSet}'");
                            currentUpdateState = DownloadInitialContentSet(contentDeliveryService, ref currentRemoteId, initialContentSet);
                        }
                    }
                    else if (status.State == ContentDeliveryService.DeliveryState.Failed ||
                             status.State == ContentDeliveryService.DeliveryState.None)
                    {
                        currentUpdateState = AttemptToSetContentPathsToStreamingAssets();
                    }
                }

                if (currentUpdateState == ContentUpdateState.DownloadingLocalCatalogs)
                {
                    var status = contentDeliveryService.GetDeliveryStatus(currentRemoteId);
                    if (status.State >= ContentDeliveryService.DeliveryState.ContentDownloaded)
                    {
                        LogFunc?.Invoke($"Successfully downloaded local catalogs.");
                        currentUpdateState = DownloadInitialContentSet(contentDeliveryService, ref currentRemoteId, initialContentSet);
                    }
                    else if (status.State == ContentDeliveryService.DeliveryState.Failed)
                    {
                        currentRemoteId = default;
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
                    else if (status.State == ContentDeliveryService.DeliveryState.Failed ||
                        status.State == ContentDeliveryService.DeliveryState.None)
                    {
                        currentUpdateState = AttemptToSetContentPathsToStreamingAssets();
                    }
                }
                return (currentUpdateState >= ContentUpdateState.NoContentAvailable);
            }

            private static ContentUpdateState DownloadInitialContentSet(ContentDeliveryService contentDeliveryService, ref RemoteContentId remoteId, string contentSet)
            {
                if (!string.IsNullOrEmpty(contentSet))
                {
                    LogFunc?.Invoke($"Downloading initial content set '{contentSet}'");
                    remoteId = contentDeliveryService.DeliverContent(contentSet);
                    if (remoteId.IsValid)
                        return ContentUpdateState.DownloadingContentSet;
                    else
                        LogFunc?.Invoke($"Unable to resolve content set '{contentSet}'");
                }
                else
                {
                    LogFunc?.Invoke($"No initial content set, initialization complete.");
                }

                remoteId = default;
                return ContentUpdateState.ContentUpdatedFromRemote;
            }

            internal ContentUpdateState AttemptToSetContentPathsToStreamingAssets()
            {
                var catalogPathInStreamingAssets = $"{Application.streamingAssetsPath}/{RuntimeContentManager.RelativeCatalogPath}";
                if (FileExists(catalogPathInStreamingAssets))
                {
                    LogFunc?.Invoke($"No connection, but catalog found at {catalogPathInStreamingAssets}.  Attempting to use streaming assets for content.");
                    return ContentUpdateState.UsingContentFromStreamingAssets;
                }
                var remoteCatalogPathInStreamingAssets = CreateCatalogLocationPath(Application.streamingAssetsPath);
                if (!FileExists(remoteCatalogPathInStreamingAssets))
                {
                    LogFunc?.Invoke($"No connection, no catalog found at {catalogPathInStreamingAssets}, but no remote catalog at {remoteCatalogPathInStreamingAssets}.  Attempting to use streaming assets for content.");
                    return ContentUpdateState.UsingContentFromStreamingAssets;
                }
                LogFunc?.Invoke($"No connection, no cached data, no catalog found at {catalogPathInStreamingAssets}. Remote catalog found at {remoteCatalogPathInStreamingAssets}.  Content is not available.");
                return ContentUpdateState.NoContentAvailable;
            }
        }
    }
}
