using System.Diagnostics;
using System.IO;

namespace Unity.Build.Common
{
    /// <summary>
    /// Dummy run step, which instructs BuildStepBuildClassicPlayer to use BuildOptions.AutoRunPlayer since there's no run step implementation
    /// </summary>
    sealed class RunStepNotImplemented : RunStep
    {
        public override bool CanRun(BuildSettings settings, out string reason)
        {
            reason = null;
            return true;
        }

        public override RunStepResult Start(BuildSettings settings)
        {
            return Success(settings, null);
        }
    }
}
