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
    public interface ISystem
    {
        void OnCreate(ref SystemState state);
        void OnDestroy(ref SystemState state);
        void OnUpdate(ref SystemState state);
    }

    /// <summary>
    /// OLD Interface implemented by unmanaged component systems.
    /// Use <see cref="ISystem"/> instead.
    /// </summary>
    ///
    [Obsolete("ISystemBase has been renamed. Use ISystem instead (RemovedAfter 2022-30-08) (UnityUpgradable) -> ISystem", true)]
    public interface ISystemBase : ISystem {}

    public static class SystemBaseDelegates
    {
        public delegate void Function(ref SystemState state);
    }

    /// <summary>
    /// Optional interface for start/stop notifications on systems.
    /// </summary>
    internal interface ISystemStartStop
    {
        void OnStartRunning(ref SystemState state);
        void OnStopRunning(ref SystemState state);
    }

    /// <summary>
    /// Interface for methods only used by compiler
    /// </summary>
    public interface ISystemCompilerGenerated
    {
        void OnCreateForCompiler(ref SystemState state);
    }

}
