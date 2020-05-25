using System;

namespace Unity.Entities
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    internal sealed class GenerateBurstMonoInteropAttribute : Attribute
    {
        public string AssetName { get; private set; }

        public GenerateBurstMonoInteropAttribute(string assetName)
        {
            AssetName = assetName;
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    internal sealed class BurstMonoInteropMethodAttribute : Attribute
    {
    }
}
