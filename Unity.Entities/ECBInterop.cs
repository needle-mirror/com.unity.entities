using System;
using Unity.Burst;

namespace Unity.Entities
{
    [GenerateBurstMonoInterop("ECBInterop")]
    [BurstCompile]
    internal partial struct ECBInterop
    {
        [BurstMonoInteropMethod]
        [BurstDiscard]
        internal unsafe static void _RemoveManagedReferences(EntityDataAccess* mgr, int* sharedIndex, int count)
        {
            try
            {
                for (var i = 0; i < count; i++)
                {
                    mgr->ManagedComponentStore.RemoveSharedComponentReference_Managed(sharedIndex[i]);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
