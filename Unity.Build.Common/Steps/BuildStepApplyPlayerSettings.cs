using System;
using UnityEditor;

namespace Unity.Build.Common
{
    [BuildStep(description = k_Description, category = "Classic")]
    public sealed class BuildStepApplyPlayerSettings : BuildStep
    {
        const string k_Description = "Apply Player Settings";

        public override string Description => k_Description;

        public override Type[] RequiredComponents => new[]
        {
            typeof(ClassicBuildProfile),
            typeof(GeneralSettings),
            typeof(ClassicScriptingSettings)
        };

        class PlayerSettingsState
        {
            public string Contents { set; get; }
            public bool IsDirty { set; get; }
            public static PlayerSettings Target => AssetDatabase.LoadAssetAtPath<PlayerSettings>("ProjectSettings/ProjectSettings.asset");
        }

        private BuildStepResult FindProperty(SerializedObject serializedObject, string name, out SerializedProperty serializedProperty)
        {
            serializedProperty = serializedObject.FindProperty(name);
            if (serializedProperty == null)
            {
                return Failure($"Failed to find: {name}");
            }
            return Success();
        }

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            context.SetValue(new PlayerSettingsState()
            {
                Contents = EditorJsonUtility.ToJson(PlayerSettingsState.Target),
                IsDirty = EditorUtility.GetDirtyCount(PlayerSettingsState.Target) > 0
            });

            var serializedObject = new SerializedObject(PlayerSettingsState.Target);
            var profile = GetRequiredComponent<ClassicBuildProfile>(context);
            var generalSettings = GetRequiredComponent<GeneralSettings>(context);
            var scriptingSettings = GetRequiredComponent<ClassicScriptingSettings>(context);
            var targetGroup = UnityEditor.BuildPipeline.GetBuildTargetGroup(profile.Target);

            // Get serialized properties for things which don't have API exposed
            SerializedProperty gcIncremental;
            var result = FindProperty(serializedObject, nameof(gcIncremental), out gcIncremental);
            if (result.Failed)
                return result;

            PlayerSettings.productName = generalSettings.ProductName;
            PlayerSettings.companyName = generalSettings.CompanyName;

            // Scripting Settings
            PlayerSettings.SetScriptingBackend(targetGroup, scriptingSettings.ScriptingBackend);
            PlayerSettings.SetIl2CppCompilerConfiguration(targetGroup, scriptingSettings.Il2CppCompilerConfiguration);
            gcIncremental.boolValue = scriptingSettings.UseIncrementalGC;

            EditorUtility.ClearDirty(PlayerSettingsState.Target);

            return Success();
        }
        public override BuildStepResult CleanupBuildStep(BuildContext context)
        {
            var savedState = context.GetValue<PlayerSettingsState>();
            // Note: EditorJsonUtility.FromJsonOverwrite doesn't dirty PlayerSettings
            EditorJsonUtility.FromJsonOverwrite(savedState.Contents, PlayerSettingsState.Target);
            if (savedState.IsDirty)
                EditorUtility.SetDirty(PlayerSettingsState.Target);

            return Success();
        }
    }

}
