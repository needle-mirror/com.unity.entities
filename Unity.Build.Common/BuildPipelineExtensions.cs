using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine.Events;

namespace Unity.Build.Common
{
    static class BuildPipelineExtensions
    {
        /// <summary>
        /// Queues and builds multiple builds. For builds requiring explicit active Editor build target, this function also switches Editor build target before starting the build.
        /// That's why there's no return result here, because the build won't be executed immediately in some cases
        /// </summary>
        internal static void BuildAsync(BuildBatchDescription buildBatchDescription)
        {
            var buildEntities = buildBatchDescription.BuildItems;
            // ToDo: when running multiple builds, should we stop at first failure?
            var buildPipelineResults = new BuildPipelineResult[buildEntities.Length];

            for (int i = 0; i < buildEntities.Length; i++)
            {
                var settings = buildEntities[i].BuildSettings;
                var pipeline = settings.GetBuildPipeline();
                if (!settings.CanBuild(out var reason))
                {
                    buildPipelineResults[i] = BuildPipelineResult.Failure(pipeline, settings, reason);
                }
                else
                {
                    buildPipelineResults[i] = null;
                }
            }


            var queue = BuildQueue.instance;
            for (int i = 0; i < buildEntities.Length; i++)
            {
                var settings = buildEntities[i].BuildSettings;
                var pipeline = settings.GetBuildPipeline();
                queue.QueueBuild(settings, buildPipelineResults[i]);
            }

            queue.FlushBuilds(buildBatchDescription.OnBuildCompleted);
        }

        /// <summary>
        /// Cancels and clear the build queue. It also stops switching editor targets, so the target which was set last will remain.
        /// </summary>
        internal static void CancelBuildAsync()
        {
            BuildQueue.instance.Clear();
        }
    }
}
