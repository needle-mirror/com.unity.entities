#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
using Unity.Burst;

namespace Unity.Entities
{
    partial class EntitiesJournaling
    {
        [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
        partial class FrameCountSystem : SystemBase
        {
            sealed class SharedFrameCount { internal static readonly SharedStatic<int> Ref = SharedStatic<int>.GetOrCreate<SharedFrameCount>(); }

            public static int FrameCount
            {
                get => SharedFrameCount.Ref.Data;
                private set => SharedFrameCount.Ref.Data = value;
            }

            protected override void OnCreate() => FrameCount = 0;
            protected override void OnDestroy() => FrameCount = 0;
            protected override void OnUpdate() => FrameCount++;
        }
    }
}
#endif
