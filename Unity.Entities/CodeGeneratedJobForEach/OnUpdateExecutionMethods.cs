// The OnUpdateExecutionMethods class is meant for scheduling a new IJobEntity interface that is coming soon.

#if ROSLYN_SOURCEGEN_ENABLED
using Unity.Entities.CodeGeneratedJobForEach;

namespace Unity.Entities
{
    public static class OnUpdateExecutionMethods
    {
        public static EntitiesOnUpdateMethod OnUpdate<T>(
            this ForEachLambdaJobDescription _, T iJobEntity) where T : struct, IJobEntity
        {
            return new EntitiesOnUpdateMethod();
        }

        public struct EntitiesOnUpdateMethod : ILambdaJobExecutionDescription
        {
        }
    }
}
#endif
