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

        [BurstMonoInteropMethod]
        internal static unsafe void _ProcessChainChunk(void* walker, int processorType,
            ECBChainPlaybackState* chainStates, int currentChain, int nextChain)
        {
            if (processorType == (int)EntityCommandBuffer.ECBProcessorType.PlaybackProcessor)
            {
                var playbackWalker = (EntityCommandBuffer.EcbWalker<EntityCommandBuffer.PlaybackProcessor>*) walker;
                playbackWalker->ProcessChain(chainStates, currentChain, nextChain);
            }
            else if (processorType == (int)EntityCommandBuffer.ECBProcessorType.DebugViewProcessor)
            {
                var debugViewWalker = (EntityCommandBuffer.EcbWalker<EntityCommandBuffer.DebugViewProcessor>*) walker;
                debugViewWalker->ProcessChain(chainStates, currentChain, nextChain);
            }
        }


    }
}
