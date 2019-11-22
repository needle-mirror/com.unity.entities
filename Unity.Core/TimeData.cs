using System;
using System.ComponentModel;

namespace Unity.Core
{
    public readonly struct TimeData
    {
        /// <summary>
        /// The total cumulative elapsed time in seconds.
        /// </summary>
        public readonly double ElapsedTime;

        /// <summary>
        /// The time in seconds since the last time-updating event occurred. (For example, a frame.)
        /// </summary>
        public readonly float DeltaTime;

        /// <summary>
        /// Create a new TimeData struct with the given values.
        /// </summary>
        /// <param name="elapsedTime">Time since the start of time collection.</param>
        /// <param name="deltaTime">Elapsed time since the last time-updating event occurred.</param>
        public TimeData(double elapsedTime, float deltaTime)
        {
            ElapsedTime = elapsedTime;
            DeltaTime = deltaTime;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
    #if UNITY_SKIP_UPDATES_WITH_VALIDATION_SUITE
        [Obsolete("deltaTime has been renamed to DeltaTime and will be (RemovedAfter 2020-02-20). If you see this message in a user project, remove the UNITY_SKIP_UPDATES_WITH_VALIDATION_SUITE define from the Entities assembly definition file.", true)]
    #else
        [Obsolete("deltaTime has been renamed to DeltaTime and will be (RemovedAfter 2020-02-20). (UnityUpgradable) -> DeltaTime")]
    #endif
        public float deltaTime => DeltaTime;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("time has been replaced by ElapsedTime with double precision. Please use `(float)ElapsedTime` explicitly when you want to use single precision. (RemovedAfter 2020-02-20)")]
        public float time => (float)ElapsedTime;
        
    #if !UNITY_DOTSPLAYER

        // This member will be deprecated once a native fixed delta time is introduced in dots.
        [EditorBrowsable(EditorBrowsableState.Never)]
        public float fixedDeltaTime => UnityEngine.Time.fixedDeltaTime;


        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("timeSinceLevelLoad is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float timeSinceLevelLoad => UnityEngine.Time.timeSinceLevelLoad;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("captureFramerate is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public int captureFramerate => UnityEngine.Time.captureFramerate;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("fixedTime is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float fixedTime => UnityEngine.Time.fixedTime;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("frameCount is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public int frameCount => UnityEngine.Time.frameCount;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("timeScale is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float timeScale => UnityEngine.Time.timeScale;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("unscaledTime is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float unscaledTime => UnityEngine.Time.unscaledTime;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("captureDeltaTime is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float captureDeltaTime => UnityEngine.Time.captureDeltaTime;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("fixedUnscaledTime is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float fixedUnscaledTime => UnityEngine.Time.fixedUnscaledTime;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("maximumDeltaTime is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float maximumDeltaTime => UnityEngine.Time.maximumDeltaTime;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("realtimeSinceStartup is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float realtimeSinceStartup => UnityEngine.Time.realtimeSinceStartup;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("renderedFrameCount is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public int renderedFrameCount => UnityEngine.Time.renderedFrameCount;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("smoothDeltaTime is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float smoothDeltaTime => UnityEngine.Time.smoothDeltaTime;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("unscaledDeltaTime is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float unscaledDeltaTime => UnityEngine.Time.unscaledDeltaTime;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("fixedUnscaledDeltaTime is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float fixedUnscaledDeltaTime => UnityEngine.Time.fixedUnscaledDeltaTime;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("maximumParticleDeltaTime is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public float maximumParticleDeltaTime => UnityEngine.Time.maximumParticleDeltaTime;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("inFixedTimeStep is only available in UnityEngine.Time. Please use the explicit UnityEngine prefix. This property will be (RemovedAfter 2020-02-20)")]
        public bool inFixedTimeStep => UnityEngine.Time.inFixedTimeStep;
    #endif

    }
}
