#if ENABLE_PROFILER
namespace Unity.Entities
{
    static partial class StructuralChangesProfiler
    {
        public enum StructuralChangeType
        {
            CreateEntity,
            DestroyEntity,
            AddComponent,
            RemoveComponent,
        }
    }
}
#endif
