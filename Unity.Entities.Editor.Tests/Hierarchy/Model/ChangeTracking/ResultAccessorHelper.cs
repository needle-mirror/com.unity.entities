using Unity.Collections;

namespace Unity.Entities.Editor.Tests
{
    static class ResultAccessorHelper
    {
        public static (Entity, T) GetAddedComponent<T>(this ComponentDataDiffer.ComponentChanges result, int index) where T : struct
        {
            var (entities, componentData) = result.GetAddedComponents<T>(Allocator.Temp);
            var r = (entities[index], componentData[index]);
            entities.Dispose();
            componentData.Dispose();
            return r;
        }

        public static (Entity, T) GetRemovedComponent<T>(this ComponentDataDiffer.ComponentChanges result, int index) where T : struct
        {
            var (entities, componentData) = result.GetRemovedComponents<T>(Allocator.Temp);
            var r = (entities[index], componentData[index]);
            entities.Dispose();
            componentData.Dispose();
            return r;
        }

        public static (Entity, T) GetAddedEntities<T>(this SharedComponentDataDiffer.ComponentChanges result, int index) where T : struct, ISharedComponentData
        {
            var entity = result.GetAddedEntity(index);
            var t = result.GetAddedComponent<T>(index);
            return (entity, t);
        }

        public static (Entity, T) GetRemovedEntities<T>(this SharedComponentDataDiffer.ComponentChanges result, int index) where T : struct, ISharedComponentData
        {
            var entity = result.GetRemovedEntity(index);
            var t = result.GetRemovedComponent<T>(index);
            return (entity, t);
        }
    }
}
