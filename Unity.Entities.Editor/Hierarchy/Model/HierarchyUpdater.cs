using System;
using System.Collections;
using System.IO;
using Unity.Collections;

namespace Unity.Entities.Editor
{
    class HierarchyUpdater : IDisposable, IEnumerator
    {
        public enum UpdateStep
        {
            Start,
            GetGameObjectChanges,
            GetEntityChanges,
            IntegrateGameObjectChanges,
            IntegrateEntityChanges,
            ExportImmutable,
            End
        }

        readonly HierarchyNodeStore m_HierarchyNodeStore;
        readonly HierarchyNodeImmutableStore m_HierarchyNodeImmutableStore;
        readonly HierarchyNameStore m_HierarchyNameStore;

        readonly Allocator m_Allocator;

        World m_World;
        HierarchyEntityChangeTracker m_HierarchyEntityChangeTracker;
        HierarchyGameObjectChangeTracker m_HierarchyGameObjectChangeTracker;
        UpdateStep m_Step;

        uint m_Version;

        /// <summary>
        /// Re-usable data structure to store results from the <see cref="HierarchyEntityChangeTracker"/>.
        /// </summary>
        readonly HierarchyEntityChanges m_HierarchyEntityChanges;

        /// <summary>
        /// Re-usable data structure to store results from the <see cref="HierarchyGameObjectChangeTracker"/>.
        /// </summary>
        readonly HierarchyGameObjectChanges m_HierarchyGameObjectChanges;

        /// <summary>
        /// A nested 'Enumerator' used to handle the implementation details of integrating entity changes over several ticks.
        /// </summary>
        HierarchyNodeStore.IntegrateEntityChangesEnumerator m_IntegrateEntityChangesEnumerator;

        /// <summary>
        /// A nested 'Enumerator' used to handle the implementation details of building the immutable data set over several ticks.
        /// </summary>
        HierarchyNodeStore.ExportImmutableEnumerator m_ExportImmutableEnumerator;

        /// <summary>
        /// A shared state object used to drive the <see cref="m_ExportImmutableEnumerator"/>.
        /// </summary>
        HierarchyNodeStore.ExportImmutableState m_ExportImmutableState;

        public int EntityChangeIntegrationBatchSize { get; set; } = 1000;
        public int ExportImmutableBatchSize { get; set; } = 1000;

        public UpdateStep Step => m_Step;

        /// <summary>
        /// Returns the updater version. This is the total number of full update cycles that have been completed.
        /// </summary>
        public uint Version => m_Version;

        /// <summary>
        /// Returns the estimated progress for the updater.
        /// </summary>
        /// <remarks>
        /// This value is only an estimate. It should NOT be used for any meaningful logic.
        /// </remarks>
        public float EstimatedProgress
        {
            get
            {
                const float kEntityChangeIntegrationWeight = 1;
                const float kBuildImmutableWeight = 1;
                const float kEntityChangeProgress = kEntityChangeIntegrationWeight / (kEntityChangeIntegrationWeight + kBuildImmutableWeight);
                const float kBuildImmutableProgress = kBuildImmutableWeight / (kEntityChangeIntegrationWeight + kBuildImmutableWeight);

                switch (m_Step)
                {
                    case UpdateStep.GetGameObjectChanges:
                    case UpdateStep.Start:
                    case UpdateStep.GetEntityChanges:
                    case UpdateStep.IntegrateGameObjectChanges:
                        return 0f;
                    case UpdateStep.IntegrateEntityChanges:
                        return m_IntegrateEntityChangesEnumerator.Progress * kEntityChangeProgress;
                    case UpdateStep.ExportImmutable:
                        return m_ExportImmutableEnumerator.Progress * kBuildImmutableProgress + kEntityChangeProgress;
                    case UpdateStep.End:
                        return 1f;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public object Current => null;

        public HierarchyUpdater(HierarchyNodeStore hierarchyNodeStore, HierarchyNodeImmutableStore hierarchyNodeImmutableStore, Allocator allocator)
            : this(hierarchyNodeStore, hierarchyNodeImmutableStore, null, allocator)
        {
        }

        public HierarchyUpdater(HierarchyNodeStore hierarchyNodeStore, HierarchyNodeImmutableStore hierarchyNodeImmutableStore, HierarchyNameStore hierarchyNameStore, Allocator allocator)
        {
            m_HierarchyNodeStore = hierarchyNodeStore;
            m_HierarchyNodeImmutableStore = hierarchyNodeImmutableStore;
            m_HierarchyNameStore = hierarchyNameStore;
            m_Allocator = allocator;
            m_HierarchyEntityChanges = new HierarchyEntityChanges(allocator);
            m_HierarchyGameObjectChanges = new HierarchyGameObjectChanges(allocator);
            m_ExportImmutableState = new HierarchyNodeStore.ExportImmutableState(allocator);

            SetState(UpdateStep.Start);
        }

        public void Dispose()
        {
            m_HierarchyEntityChanges.Dispose();
            m_HierarchyGameObjectChanges.Dispose();
            m_ExportImmutableState.Dispose();
            m_HierarchyEntityChangeTracker?.Dispose();
            m_HierarchyGameObjectChangeTracker?.Dispose();

            m_HierarchyEntityChangeTracker = null;
            m_HierarchyGameObjectChangeTracker = null;
        }

        public void SetWorld(World world)
        {
            if (world == m_World)
                return;

            m_World = world;

            m_HierarchyEntityChangeTracker?.Dispose();
            m_HierarchyGameObjectChangeTracker?.Dispose();

            if (null != world)
            {
                // Re-initialize a new change tracker for the specified world.
                m_HierarchyEntityChangeTracker = new HierarchyEntityChangeTracker(m_World, m_Allocator);
                m_HierarchyGameObjectChangeTracker = new HierarchyGameObjectChangeTracker(m_Allocator);
            }
            else
            {
                m_HierarchyEntityChangeTracker = null;
                m_HierarchyGameObjectChangeTracker = null;
            }

            Reset();
        }

        /// <summary>
        /// This method is invoked when entering a new state and should be used to initialize any
        /// state specific variables. The state itself will not be run until the next frame. 
        /// </summary>
        /// <param name="state">The state to switch to.</param>
        void SetState(UpdateStep state)
        {
            while (true)
            {
                m_Step = state;

                switch (state)
                {
                    case UpdateStep.IntegrateEntityChanges:
                    {
                        if (m_HierarchyEntityChanges.HasChanges())
                        {
                            // Delegate the implementation to another enumerator.
                            m_IntegrateEntityChangesEnumerator = m_HierarchyNodeStore.CreateIntegrateEntityChangesEnumerator(m_World, m_HierarchyEntityChanges, EntityChangeIntegrationBatchSize);
                        }
                        else
                        {
                            // No changes to integrate move on to linear packing.
                            state = UpdateStep.ExportImmutable;
                            continue;
                        }

                        break;
                    }

                    case UpdateStep.ExportImmutable:
                    {
                        if (m_HierarchyNodeStore.GetRootChangeVersion() != m_HierarchyNodeImmutableStore.GetReadBuffer().ChangeVersion)
                        {
                            m_ExportImmutableEnumerator = m_HierarchyNodeStore.CreateBuildImmutableEnumerator(m_World, m_ExportImmutableState,m_HierarchyNodeImmutableStore.GetWriteBuffer(), m_HierarchyNodeImmutableStore.GetReadBuffer(), ExportImmutableBatchSize);
                        }
                        else
                        {
                            // The topology has not changed, skip packing and end.
                            state = UpdateStep.End;
                            continue;
                        }

                        break;
                    }

                    case UpdateStep.End:
                    {
                        unchecked
                        {
                            ++m_Version;
                        }
                        break;
                    }
                }

                break;
            }
        }

        public bool MoveNext()
        {
            if (null == m_World)
                return false;

            switch (m_Step)
            {
                case UpdateStep.Start:
                {
                    SetState(UpdateStep.GetGameObjectChanges);
                    return true;
                }

                case UpdateStep.GetGameObjectChanges:
                {
                    m_HierarchyGameObjectChangeTracker.GetChanges(m_HierarchyGameObjectChanges);
                    SetState(UpdateStep.GetEntityChanges);
                    return true;
                }

                case UpdateStep.GetEntityChanges:
                {
                    m_HierarchyEntityChangeTracker.GetChanges(m_HierarchyEntityChanges);
                    SetState(UpdateStep.IntegrateGameObjectChanges);
                    return true;
                }

                case UpdateStep.IntegrateGameObjectChanges:
                {
                    m_HierarchyNodeStore.IntegrateGameObjectChanges(m_HierarchyGameObjectChanges);
                    m_HierarchyNameStore?.IntegrateGameObjectChanges(m_HierarchyGameObjectChanges);
                    SetState(UpdateStep.IntegrateEntityChanges);
                    return true;
                }

                case UpdateStep.IntegrateEntityChanges:
                {
                    if (m_IntegrateEntityChangesEnumerator.MoveNext())
                        return true;

                    m_IntegrateEntityChangesEnumerator = default;
                    m_HierarchyNameStore?.IntegrateEntityChanges(m_HierarchyEntityChanges);
                    SetState(UpdateStep.ExportImmutable);
                    return true;
                }

                case UpdateStep.ExportImmutable:
                {
                    if (m_ExportImmutableEnumerator.MoveNext())
                        return true;

                    m_HierarchyNodeImmutableStore.SwapBuffers();
                    m_ExportImmutableEnumerator = default;
                    SetState(UpdateStep.End);
                    return true;
                }
            }

            return false;
        }

        public void Execute()
        {
            if (m_Step != UpdateStep.Start && m_Step != UpdateStep.End)
                throw new InvalidDataException("Failed to execute the enumerator. The state must be reset before running.");

            Reset();
            Flush();
        }

        public void Reset()
        {
            SetState(UpdateStep.Start);
        }

        public void Flush()
        {
            while (MoveNext())
            {
            }
        }
    }
}