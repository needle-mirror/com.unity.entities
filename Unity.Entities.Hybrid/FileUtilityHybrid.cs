
using Unity.IO.LowLevel.Unsafe;

namespace Unity.Entities.Hybrid
{
    internal static unsafe class FileUtilityHybrid
    {
        public static bool FileExists(string path)
        {
            var readHandle = AsyncReadManager.Read(path, null, 0);
            readHandle.JobHandle.Complete();
            if (readHandle.Status == ReadStatus.Failed)
                return false;

            return true;
        }
    }
}
