using UnityEngine;

namespace Unity.Entities.Editor
{
    static class PackageResources
    {
        const string PackageId = "com.unity.entities";
        public static VisualElementTemplate LoadTemplate(string name) => new VisualElementTemplate(PackageId, name);
        public static Texture2D LoadIcon(string path) => EditorResources.LoadTexture<Texture2D>(PackageId, path, true);
    }
}
