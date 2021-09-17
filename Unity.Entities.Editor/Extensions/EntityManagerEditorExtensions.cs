namespace Unity.Entities.Editor
{
    static class EntityManagerEditorExtensions
    {
        public static bool SafeExists(this EntityManager entityManager, Entity entity)
        {
            return entity.Index >= 0 && (uint)entity.Index < (uint)entityManager.EntityCapacity
                   && entityManager.Exists(entity);
        }
    }
}
