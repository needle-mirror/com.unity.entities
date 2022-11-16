#if !UNITY_DOTSRUNTIME
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.Content
{
    /// <summary>
    /// System responsible for initializing and updating the <seealso cref="RuntimeContentManager"/>.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct RuntimeContentSystem : ISystem
    {
        /// <summary>
        /// Initializes the <seealso cref="RuntimeContentManager"/> with the local catalog.
        /// </summary>
        /// <param name="state">System state.</param>
        [ExcludeFromBurstCompatTesting("Loading catalog data not burstable")]
        public void OnCreate(ref SystemState state)
        {
#if !UNITY_EDITOR
#if ENABLE_CONTENT_DELIVERY
            //local catalog data will be loaded once content update has completed.
#else
            var catalogPath = $"{Application.streamingAssetsPath}/{RuntimeContentManager.RelativeCatalogPath}";
            if (FileExists(catalogPath)) {
                RuntimeContentManager.LoadLocalCatalogData(catalogPath,
                    RuntimeContentManager.DefaultContentFileNameFunc,
                    p => $"{Application.streamingAssetsPath}/{RuntimeContentManager.DefaultArchivePathFunc(p)}");
            }
#endif
#endif
        }

        /// <summary>
        /// Processes the <seealso cref="RuntimeContentManager"/>.
        /// </summary>
        /// <param name="state">System state.</param>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RuntimeContentManager.ProcessQueuedCommands();
        }

        /// <summary>
        /// Cleanup resources from the <seealso cref="RuntimeContentManager"/>.
        /// </summary>
        /// <param name="state">System state.</param>
        public void OnDestroy(ref SystemState state)
        {
        }

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

    }
}
#endif
