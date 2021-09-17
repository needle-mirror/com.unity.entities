using Unity.Properties.UI;

namespace Unity.Entities.Editor
{
    class TabViewAttribute : InspectorAttribute
    {
        public string Id;
        public TabViewAttribute(string id)
        {
            Id = id;
        }
    }
}
