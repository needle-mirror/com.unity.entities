using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    internal static class CalculateReaderWriterDependency
    {
        public static bool Add(ComponentType type, ref UnsafeList reading, ref UnsafeList writing)
        {
            if (!type.RequiresJobDependency)
                return false;

            // If any other dependency is added the Entity type dependency is removed to avoid the overhead of having all jobs
            // depend on this.
            if ((reading.m_size == 1) && reading.Contains(TypeManager.GetTypeIndex<Entity>()))
                reading.m_size = 0;

            if (type.AccessModeType == ComponentType.AccessMode.ReadOnly)
            {
                if (reading.Contains(type.TypeIndex))
                    return false;
                if (writing.Contains(type.TypeIndex))
                    return false;

                reading.Add(type.TypeIndex);
                return true;
            }
            else
            {
                if (writing.Contains(type.TypeIndex))
                    return false;

                var readingIndex = reading.IndexOf(type.TypeIndex);
                if (readingIndex != -1)
                    reading.RemoveAtSwapBack<int>(readingIndex);

                writing.Add(type.TypeIndex);
                return true;
            }
        }
    }
}
