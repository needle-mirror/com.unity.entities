using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Unity.Entities.UI;
using Unity.Properties;
using Unity.Serialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    [DOTSEditorPreferencesSetting(Constants.Settings.Migration)]
    class MigrationPrefsSection : ISetting
    {
        const string k_SymbolForAddingPartialKeyword = "DOTS_ADD_PARTIAL_KEYWORD";

        private const string k_Tooltip = "When set, the define DOTS_ADD_PARTIAL_KEYWORD is added to " +
                                         "the scripting defines for the current platform. This signals to codegeneration to add the partial keywords. " +
                                         "Note: this will cause the generators to write over user scripts.  Make sure your changes are saved and backed up before using." +
                                         "Uncheck this to remove the define afterwards if you wish.";

        public void OnSettingChanged(PropertyPath path)
        {
        }

        [CreateProperty, DontSerialize, Tooltip(k_Tooltip)]
        public bool AddMissingPartialKeywordsToSystems
        {
            get => PlayerSettings
                .GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup)
                .Contains(k_SymbolForAddingPartialKeyword);
            set
            {
                var defines = PlayerSettings
                    .GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
                var hasPartialDefine = defines
                    .Contains(k_SymbolForAddingPartialKeyword);
                if (value && !hasPartialDefine)
                {
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup,
                        $"{defines};{k_SymbolForAddingPartialKeyword}");
                }
                else if (!value && hasPartialDefine)
                {
                    defines = defines.Remove(defines.IndexOf(k_SymbolForAddingPartialKeyword, StringComparison.Ordinal),
                        k_SymbolForAddingPartialKeyword.Length);
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup,
                        defines);
                }

            }
        }
    }
}
