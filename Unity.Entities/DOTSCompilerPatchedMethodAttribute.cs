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

    // Used by Source Generators to target property patching.
    // Only used by internal Unity codegen.
    [AttributeUsage(AttributeTargets.Property)]
    public class DOTSCompilerPatchedPropertyAttribute : Attribute
    {
        public DOTSCompilerPatchedPropertyAttribute(string targetPropertyName) { }
    }
}
