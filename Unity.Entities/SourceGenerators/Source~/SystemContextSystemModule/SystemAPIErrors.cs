﻿using System;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.SystemCodegenContext {
    public static class SystemAPIErrors {
        private const string k_SystemAPIGenericAccess = "SystemAPI usage with Generic Access";
        const string k_SystemAPIComponentAccess = "SystemAPI usage with Component Access";
        public static void SGSA0001(SystemDescription systemDescription, SystemContextSystemModule.CandidateSyntax candidate) {
            systemDescription.LogError(nameof(SGSA0001), k_SystemAPIGenericAccess,
                "SystemAPI usage with generic parameter not supported", candidate.Node.GetLocation());
        }

        public static void SGSA0002(SystemDescription systemDescription, SystemContextSystemModule.CandidateSyntax candidate) {
            var dataLookupName = candidate.Type.ToString();
            systemDescription.LogError(nameof(SGSA0002), k_SystemAPIComponentAccess,
                $"{dataLookupName} usage with variable read permission not supported outside OnCreate of your system. To fix, instead call it inside OnCreate of your calling system",
                candidate.Node.GetLocation());
        }
    }
}
