using System;
using Unity.Entities.CodeGeneratedJobForEach;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities
{
    /// <summary>
    /// An abstract class to implement in order to create a system that uses ECS-specific Jobs.
    /// </summary>
    public abstract class SystemBase : ComponentSystemBase
    {
        bool      _GetDependencyFromSafetyManager;
        JobHandle _JobHandle;

        unsafe protected JobHandle Dependency
        {
            get
            {
                if (_GetDependencyFromSafetyManager)
                {
                    _GetDependencyFromSafetyManager = false;
                    _JobHandle = m_DependencyManager->GetDependency(m_JobDependencyForReadingSystems.Ptr,
                        m_JobDependencyForReadingSystems.Length, m_JobDependencyForWritingSystems.Ptr,
                        m_JobDependencyForWritingSystems.Length);
                }

                return _JobHandle;
            }
            set
            {
                _GetDependencyFromSafetyManager = false;
                _JobHandle = value;
            }
        }

        unsafe protected void CompleteDependency()
        {
            // Previous frame job
            _JobHandle.Complete();

            // We need to get more job handles from other systems
            if (_GetDependencyFromSafetyManager)
            {
                _GetDependencyFromSafetyManager = false;
                CompleteDependencyInternal();
            }
        }

        /// <summary>
        /// Use Entities.ForEach((ref Translation translation, in Velocity velocity) => { translation.Value += velocity.Value * dt; }).Schedule(inputDependencies);
        /// </summary>
        protected internal ForEachLambdaJobDescription Entities => new ForEachLambdaJobDescription();

#if ENABLE_DOTS_COMPILER_CHUNKS
            /// <summary>
            /// Use query.Chunks.ForEach((ArchetypeChunk chunk, int chunkIndex, int indexInQueryOfFirstEntity) => { YourCodeGoesHere(); }).Schedule();
            /// </summary>
            public LambdaJobChunkDescription Chunks
            {
                get
                {
                    return new LambdaJobChunkDescription();
                }
            }
#endif

        /// <summary>
        /// Use Job.WithCode(() => { YourCodeGoesHere(); }).Schedule(inputDependencies);
        /// </summary>
        protected internal LambdaSingleJobDescription Job
        {
            get { return new LambdaSingleJobDescription(); }
        }

        void BeforeOnUpdate()
        {
            BeforeUpdateVersioning();

            // We need to wait on all previous frame dependencies, otherwise it is possible that we create infinitely long dependency chains
            // without anyone ever waiting on it
            _JobHandle.Complete();
            _GetDependencyFromSafetyManager = true;
        }
#pragma warning disable 649
        private unsafe struct JobHandleData
        {
            public void* jobGroup;
            public int version;
        }
#pragma warning restore 649

        unsafe void AfterOnUpdate(bool throwException)
        {
            AfterUpdateVersioning();

            // If outputJob says no relevant jobs were scheduled,
            // then no need to batch them up or register them.
            // This is a big optimization if we only Run methods on main thread...
            var outputJob = _JobHandle;
            if (((JobHandleData*) &outputJob)->jobGroup != null)
            {
                JobHandle.ScheduleBatchedJobs();
                _JobHandle = m_DependencyManager->AddDependency(m_JobDependencyForReadingSystems.Ptr,
                    m_JobDependencyForReadingSystems.Length, m_JobDependencyForWritingSystems.Ptr,
                    m_JobDependencyForWritingSystems.Length, outputJob);
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (JobsUtility.JobDebuggerEnabled)
            {
                var dependencyError = SystemDependencySafetyUtility.CheckSafetyAfterUpdate(this, ref m_JobDependencyForReadingSystems, ref m_JobDependencyForWritingSystems, m_DependencyManager);
                if (throwException && dependencyError != null)
                    throw new InvalidOperationException(dependencyError);
            }
#endif
        }

        public sealed override void Update()
        {
#if ENABLE_PROFILER
            using (m_ProfilerMarker.Auto())
#endif
            {
                if (Enabled && ShouldRunSystem())
                {
                    if (!m_PreviouslyEnabled)
                    {
                        m_PreviouslyEnabled = true;
                        OnStartRunning();
                    }

                    BeforeOnUpdate();

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    var oldExecutingSystem = ms_ExecutingSystem;
                    ms_ExecutingSystem = this;
    #endif
                    try
                    {
                        OnUpdate();
                    }
                    catch
                    {
    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                        ms_ExecutingSystem = oldExecutingSystem;
    #endif

                        AfterOnUpdate(false);
                        throw;
                    }

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    ms_ExecutingSystem = oldExecutingSystem;
    #endif

                    AfterOnUpdate(true);
                }
                else if (m_PreviouslyEnabled)
                {
                    m_PreviouslyEnabled = false;
                    OnStopRunning();
                }                
            }
        }

        internal sealed override void OnBeforeCreateInternal(World world)
        {
            base.OnBeforeCreateInternal(world);
        }

        internal sealed override void OnBeforeDestroyInternal()
        {
            base.OnBeforeDestroyInternal();
            _JobHandle.Complete();
        }

        /// <summary>Implement OnUpdate to perform the major work of this system.</summary>
        /// <remarks>
        /// The system invokes OnUpdate once per frame on the main thread when any of this system's
        /// EntityQueries match existing entities, or if the system has the AlwaysUpdate
        /// attribute.
        /// </remarks>
        protected abstract void OnUpdate();
    }
}