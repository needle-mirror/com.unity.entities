#if !UNITY_DOTSRUNTIME
using System;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    struct AspectTypeInfo : IDisposable
    {
        public UnsafeList<AspectType> AspectTypes;
        public UnsafeMultiHashMap<AspectType, ComponentType> AspectRequiredComponents;
        public UnsafeMultiHashMap<AspectType, ComponentType> AspectExcludedComponents;

        public void Dispose()
        {
            if (AspectTypes.IsCreated)
                AspectTypes.Dispose();

            if (AspectRequiredComponents.IsCreated)
                AspectRequiredComponents.Dispose();

            if (AspectExcludedComponents.IsCreated)
                AspectExcludedComponents.Dispose();
        }
    }
}
#endif
