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
    /// <summary>
    /// A system that resets the world update allocator by rewinding its memories.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ThinClientSimulation | WorldSystemFilterFlags.Editor)]
    public struct WorldUpdateAllocatorResetSystem : ISystem
    {
        /// <summary>
        /// Initializes this world update allocator reset system for rewinding memories of the world update allocator.
        /// </summary>
        /// <param name="state">Reference to the SystemState of the system.</param>
        public void OnCreate(ref SystemState state)
        {
        }

        /// <summary>
        /// Destroys this world update allocator reset system.
        /// </summary>
        /// <param name="state">The SystemState of the system.</param>
        public void OnDestroy(ref SystemState state)
        {
        }

        /// <summary>
        /// Executes world update allocator reset system to rewind memories of the world update allocator.
        /// </summary>
        /// <param name="state">The SystemState of the system.</param>
        public void OnUpdate(ref SystemState state)
        {
            state.m_WorldUnmanaged.ResetUpdateAllocator();
        }
    }
}
