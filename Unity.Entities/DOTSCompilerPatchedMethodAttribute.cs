#if ROSLYN_SOURCEGEN_ENABLED
using System;

namespace Unity.Entities
{
    // Used by Source Generators to target method patching.
    // Only used by internal Unity codegen.
    [AttributeUsage(AttributeTargets.Method)]
    public class DOTSCompilerPatchedMethodAttribute : Attribute
    {
        public DOTSCompilerPatchedMethodAttribute(string targetMethodName) { }
    }
}
#endif
