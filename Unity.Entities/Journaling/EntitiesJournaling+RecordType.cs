#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
namespace Unity.Entities
{
    partial class EntitiesJournaling
    {
        /// <summary>
        /// Record type enumeration.
        /// </summary>
        public enum RecordType : int
        {
            WorldCreated,
            WorldDestroyed,
            SystemAdded,
            SystemRemoved,
            CreateEntity,
            DestroyEntity,
            AddComponent,
            RemoveComponent,
            SetComponentData,
            SetSharedComponentData,
            SetComponentObject,
            SetBuffer
        }
    }
}
#endif
