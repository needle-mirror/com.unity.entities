using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Jobs;

namespace Unity.Entities
{
    /// <summary>
    /// Interface implemented by unmanaged component systems.
    /// </summary>
    public interface ISystemBase
    {
        void OnCreate(ref SystemState state);
        void OnDestroy(ref SystemState state);
        void OnUpdate(ref SystemState state);
    }

    public static class SystemBaseDelegates
    {
        public delegate void Function(ref SystemState state);
    }

    /// <summary>
    /// Optional interface for start/stop notifications on systems.
    /// </summary>
    internal interface ISystemBaseStartStop
    {
        void OnStartRunning(ref SystemState state);
        void OnStopRunning(ref SystemState state);
    }
}
