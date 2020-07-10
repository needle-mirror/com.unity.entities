using System;

namespace Unity.Entities
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class GenerateBurstMonoInteropAttribute : Attribute
    {
        public string AssetName { get; private set; }

        public GenerateBurstMonoInteropAttribute(string assetName)
        {
            AssetName = assetName;
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BurstMonoInteropMethodAttribute : Attribute
    {
        /// <summary>
        /// If set, the generated method will be exposed as public. Otherwise it will have the same access modifier as
        /// the original method.
        /// </summary>
        public bool MakePublic;

        public BurstMonoInteropMethodAttribute(bool makePublic = false)
        {
            MakePublic = makePublic;
        }
    }
}
