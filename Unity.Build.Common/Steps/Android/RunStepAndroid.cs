using System.Diagnostics;
using System.IO;
using System;
using UnityEditor;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif

namespace Unity.Build.Common
{
    // This is just a placeholder until we implement RunStepAndroid in com.unity.platforms
    sealed class RunStepAndroid : RunStep
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

            reason = null;
            return true;
        }

        public override RunStepResult Start(BuildSettings settings)
        {
            var artifact = BuildArtifacts.GetBuildArtifact<BuildArtifactDesktop>(settings);
            var fileName = artifact.OutputTargetFile.FullName;
            if (Path.GetExtension(fileName) != ".apk")
                return Failure(settings, $"Expected .apk in path, but got '{fileName}'.");

            var path = $"\"{Path.GetFullPath(fileName)}\"";
#if UNITY_ANDROID
            EditorUtility.DisplayProgressBar("Installing", $"Installing {path}", 0.3f);
            var adb = ADB.GetInstance();
            try
            {
                adb.Run(new[] {"install", "-r", "-d", path}, $"Failed to install '{fileName}'");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                return Failure(settings, ex.Message);
            }

            UnityEngine.Debug.Log($"{path} successfully installed.");
            var runTarget = $"\"{PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android)}/com.unity3d.player.UnityPlayerActivity\"";
            EditorUtility.DisplayProgressBar("Launching", $"Launching {runTarget}", 0.6f);
            try
            {
                adb.Run(new[]
                    {
                        "shell", "am", "start",
                        "-a", "android.intent.action.MAIN",
                        "-c", "android.intent.category.LAUNCHER",
                        "-f", "0x10200000",
                        "-S",
                        "-n", runTarget
                    },
                    $"Failed to launch {runTarget}");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                return Failure(settings, ex.Message);
            }

            UnityEngine.Debug.Log($"{runTarget} successfully launched.");
            EditorUtility.ClearProgressBar();
            return Success(settings, new RunInstanceAndroid());
#else
            return Failure(settings, $"Active Editor platform has to be set to Android.");
#endif
        }
    }
}
