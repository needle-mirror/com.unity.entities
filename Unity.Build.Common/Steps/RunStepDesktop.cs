using System.Diagnostics;
using System.IO;

namespace Unity.Build.Common
{
    public sealed class RunStepDesktop : RunStep
    {
        public override bool CanRun(BuildSettings settings, out string reason)
        {
            var artifact = BuildArtifacts.GetBuildArtifact<BuildArtifactDesktop>(settings);
            if (artifact == null)
            {
                reason = $"Could not retrieve build artifact '{nameof(BuildArtifactDesktop)}'.";
                return false;
            }

            if (artifact.OutputTargetFile == null)
            {
                reason = $"{nameof(BuildArtifactDesktop.OutputTargetFile)} is null.";
                return false;
            }

#if UNITY_EDITOR_OSX
            // On macOS, the output target is a .app directory structure
            if (!Directory.Exists(artifact.OutputTargetFile.FullName))
#else
            if (!File.Exists(artifact.OutputTargetFile.FullName))
#endif
            {
                reason = $"Output target file '{artifact.OutputTargetFile.FullName}' not found.";
                return false;
            }

            reason = null;
            return true;
        }

        public override RunStepResult Start(BuildSettings settings)
        {
            var artifact = BuildArtifacts.GetBuildArtifact<BuildArtifactDesktop>(settings);
            var process = new Process();
#if UNITY_EDITOR_OSX
            process.StartInfo.FileName = "open";
            process.StartInfo.Arguments = '\"' + artifact.OutputTargetFile.FullName.Trim('\"') + '\"';
#else
            process.StartInfo.FileName = artifact.OutputTargetFile.FullName;
#endif
            process.StartInfo.WorkingDirectory = artifact.OutputTargetFile.Directory?.FullName ?? string.Empty;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = true;

            if (!process.Start())
            {
                return Failure(settings, $"Failed to start process at '{process.StartInfo.FileName}'.");
            }

            return Success(settings, new RunInstanceDesktop(process));
        }
    }
}
