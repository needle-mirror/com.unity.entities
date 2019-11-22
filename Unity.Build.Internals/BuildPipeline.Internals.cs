using System;
using UnityEditor;

namespace Unity.Build.Internals
{
    internal static class BuildPipelineInternals
    {
        internal static event Action<BuildPipeline, BuildSettings> BuildStarted;
        internal static event Action<BuildPipelineResult> BuildCompleted;

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            BuildPipeline.BuildStarted += (pipeline, settings) => BuildStarted?.Invoke(pipeline, settings);
            BuildPipeline.BuildCompleted += (result) => BuildCompleted?.Invoke(result);
        }
    }
}
