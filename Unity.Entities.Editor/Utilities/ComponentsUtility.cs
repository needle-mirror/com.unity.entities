using System.Collections.Generic;
using System.Linq;
using Unity.Properties;
using UnityEditor;

namespace Unity.Entities.Editor
{
    static class ComponentsUtility
    {
        static readonly Dictionary<string, string> s_ComponentsDisplayNames = new Dictionary<string, string>();

        public static IEnumerable<ComponentViewData> GetComponentDataFromQuery(this EntityQuery query)
        {
            // TODO(ECSB-387): This method does not support queries with >1 query descriptions, each of which should be represented individually.
            var desc = query.GetEntityQueryDescs()[0];
            return desc.All
                .Concat(desc.Any)
                .Select(t => new ComponentViewData(t.GetManagedType(), TypeUtility.GetTypeDisplayName(t.GetManagedType()), t.AccessModeType, GetComponentKind(t)))
                .Concat(desc.None.Select(t => new ComponentViewData(t.GetManagedType(), TypeUtility.GetTypeDisplayName(t.GetManagedType()), ComponentType.AccessMode.Exclude, GetComponentKind(t))))
                .Concat(desc.Disabled.Select(t => new ComponentViewData(t.GetManagedType(), TypeUtility.GetTypeDisplayName(t.GetManagedType()), ComponentType.AccessMode.ReadWrite, GetComponentKind(t), ComponentViewData.QueryOptions.Disabled)))
                .Concat(desc.Present.Select(t => new ComponentViewData(t.GetManagedType(), TypeUtility.GetTypeDisplayName(t.GetManagedType()), ComponentType.AccessMode.ReadWrite, GetComponentKind(t), ComponentViewData.QueryOptions.Present)))
                .Concat(desc.Absent.Select(t => new ComponentViewData(t.GetManagedType(), TypeUtility.GetTypeDisplayName(t.GetManagedType()), ComponentType.AccessMode.ReadWrite, GetComponentKind(t), ComponentViewData.QueryOptions.Absent)))
                .OrderBy(x => x);
        }

        public static ComponentViewData.ComponentKind GetComponentKind(ComponentType componentType) => componentType switch
        {
            { IsZeroSized: true } => ComponentViewData.ComponentKind.Tag,
            { IsBuffer: true } => ComponentViewData.ComponentKind.Buffer,
            { IsSharedComponent: true } => ComponentViewData.ComponentKind.Shared,
            { IsChunkComponent: true } => ComponentViewData.ComponentKind.Chunk,
            { IsManagedComponent: true } => ComponentViewData.ComponentKind.Managed,
            _ => ComponentViewData.ComponentKind.Default
        };

        public static string GetComponentDisplayName(string typeName)
        {
            if (!s_ComponentsDisplayNames.TryGetValue(typeName, out var displayName))
            {
                s_ComponentsDisplayNames[typeName] = displayName = ContentUtilities.NicifyTypeName(typeName);
            }

            return displayName;
        }
    }
}
