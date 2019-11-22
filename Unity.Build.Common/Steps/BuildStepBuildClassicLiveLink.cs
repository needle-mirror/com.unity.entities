using System;
using UnityEditor;

namespace Unity.Build.Common
{
    [BuildStep(description = k_Description, category = "Classic")]
    public sealed class BuildStepBuildClassicLiveLink : BuildStep
    {
        const string k_Description = "Build LiveLink Player";

        TemporaryFileTracker m_TemporaryFileTracker;

        public override string Description => k_Description;

        public override Type[] RequiredComponents => new[]
        {
            typeof(ClassicBuildProfile),
            typeof(SceneList),
            typeof(GeneralSettings)
        };

        public override Type[] OptionalComponents => new[]
        {
            typeof(OutputBuildDirectory),
            typeof(InternalSourceBuildConfiguration)
        };

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            m_TemporaryFileTracker = new TemporaryFileTracker();
            if (!BuildStepBuildClassicPlayer.Prepare(context, this, true, m_TemporaryFileTracker, out var failure, out var buildPlayerOptions))
            {
                return failure;
            }

            //@TODO: Allow debugging should be based on profile...
            buildPlayerOptions.options = BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectToHost;

            var report = UnityEditor.BuildPipeline.BuildPlayer(buildPlayerOptions);
            return BuildStepBuildClassicPlayer.ReturnBuildPlayerResult(context, this, report);
        }

        public override BuildStepResult CleanupBuildStep(BuildContext context)
        {
            m_TemporaryFileTracker.Dispose();
            return Success();
        }
    }
}
