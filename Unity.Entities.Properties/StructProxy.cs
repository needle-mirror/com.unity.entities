using System;
using Unity.Properties;

namespace Unity.Entities.Properties
{
    public unsafe struct StructProxy : IPropertyContainer
    {
        public IVersionStorage VersionStorage => PassthroughVersionStorage.Instance;
        public IPropertyBag PropertyBag => bag;

        public byte* data;
        public PropertyBag bag;
        public Type type;
    }
}
