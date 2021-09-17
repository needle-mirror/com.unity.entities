using System;
using UnityEditor;

namespace Unity.Entities.Editor
{
    static class AddPartialsToSystemMenuItem
    {
        const string k_Name = "DOTS/DOTS Compiler/Add missing partial keywords to systems";
        const string k_SymbolForAddingPartialKeyword = "DOTS_ADD_PARTIAL_KEYWORD";

        [MenuItem(k_Name)]
        static void ToggleEntitiesJournaling()
        {
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            if (defines.Contains(k_SymbolForAddingPartialKeyword))
            {
                defines = defines.Remove(defines.IndexOf(k_SymbolForAddingPartialKeyword, StringComparison.Ordinal), k_SymbolForAddingPartialKeyword.Length);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, defines);
            }
            else
                PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, $"{defines};{k_SymbolForAddingPartialKeyword}");
        }

        [MenuItem(k_Name, true)]
        static bool ValidateToggleEntitiesJournaling()
        {
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            Menu.SetChecked(k_Name, defines.Contains(k_SymbolForAddingPartialKeyword));
            return true;
        }
    }
}
