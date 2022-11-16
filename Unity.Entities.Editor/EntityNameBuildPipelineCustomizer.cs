#if USING_PLATFORMS_PACKAGE
using System;
using Unity.Build;
using Unity.Build.Classic;

namespace Unity.Entities.Editor
{
    class EntityNameBuildPipelineCustomizer: ClassicBuildPipelineCustomizer
    {
        public override Type[] UsedComponents { get; } =
        {
            typeof(EnableEntityNames)
        };
        public override string[] ProvidePlayerScriptingDefines()
        {
            if (Context.HasComponent<EnableEntityNames>())
            {
                return Array.Empty<string>();
            }

            var classicBuildProfile = Context.GetComponentOrDefault<ClassicBuildProfile>();
            if (classicBuildProfile.Configuration == BuildType.Release)
            {
                string[] ret = {"DOTS_DISABLE_DEBUG_NAMES"};
                return ret;
            }

            return Array.Empty<string>();
        }
    }
}
#endif
