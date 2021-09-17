using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Unity.Entities
{    
    // ReSharper disable once InconsistentNaming
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public struct WorldUpdateAllocatorResetSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        public void OnUpdate(ref SystemState state)
        {
            state.m_WorldUnmanaged.ResetUpdateAllocator();
        }
    }
}
