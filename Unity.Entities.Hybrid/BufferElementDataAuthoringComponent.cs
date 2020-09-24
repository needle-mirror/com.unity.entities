using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.Entities.Hybrid
{
    [DisallowMultipleComponent]
    internal abstract class BufferElementAuthoring<TBufferElementData, TWrappedValue> :
        MonoBehaviour, IConvertGameObjectToEntity
        where TBufferElementData : struct, IBufferElementData
        where TWrappedValue : unmanaged
    {
        public TWrappedValue[] Values;

        public unsafe void Convert(
            Entity entity,
            EntityManager destinationManager,
            GameObjectConversionSystem _)
        {
            DynamicBuffer<TBufferElementData> dynamicBuffer = destinationManager.AddBuffer<TBufferElementData>(entity);

            if (Values == null || Values.Length == 0)
            {
                return;
            }

            dynamicBuffer.ResizeUninitialized(Values.Length);
            fixed(void* sourcePtr = &Values[0])
            {
                UnsafeUtility.MemCpy(
                    destination: dynamicBuffer.GetUnsafePtr(),
                    sourcePtr,
                    size: Values.Length * UnsafeUtility.SizeOf<TBufferElementData>());
            }
        }
    }
}
