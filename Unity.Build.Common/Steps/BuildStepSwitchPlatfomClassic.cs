using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Unity.Build.Common
{
    sealed class BuildStepSwitchPlatfomClassic : BuildStep
    {
        public override string Description => "Switch Active Platform (Classic)";

        public override Type[] RequiredComponents => new[]
        {
            typeof(ClassicBuildProfile)
        };

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            var profile = GetRequiredComponent<ClassicBuildProfile>(context);
            if (profile.Target == BuildTarget.NoTarget)
            {
                return Failure($"Invalid build target '{profile.Target.ToString()}'.");
            }

            if (EditorUserBuildSettings.activeBuildTarget == profile.Target)
            {
                return Success();
            }

            if (EditorUserBuildSettings.SwitchActiveBuildTarget(UnityEditor.BuildPipeline.GetBuildTargetGroup(profile.Target), profile.Target))
                return Failure("Editor's active Build Target needed to be switched. Please wait for switch to complete and then build again.");
            else
                return Failure($"Editor's active Build Target could not be switched. Look in the console or the editor log for additional errors.");
        }
    }

    sealed class SaveScenesAndAssets : BuildStep
    {
        public override string Description => "Save Scenes and Assets";

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return Failure($"All Scenes and Assets must be saved before a build can be started");

            return Success();
        }
    }
}
