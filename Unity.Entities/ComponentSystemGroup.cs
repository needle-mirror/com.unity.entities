using System;
using System.Collections.Generic;
using Unity.Assertions;
using Unity.Core;
#if !NET_DOTS
using System.Linq;
#endif

namespace Unity.Entities
{

    public abstract class ComponentSystemGroup : ComponentSystem
    {
        private bool m_systemSortDirty = false;
        protected List<ComponentSystemBase> m_systemsToUpdate = new List<ComponentSystemBase>();
        protected List<ComponentSystemBase> m_systemsToRemove = new List<ComponentSystemBase>();

        public virtual IEnumerable<ComponentSystemBase> Systems => m_systemsToUpdate;

        // Fixed-Rate Support
        // internal for now, until we figure out proper API
        internal bool FixedTimeStepEnabled { get; /*protected*/ set; }
        internal float FixedTimeStep { get; /*protected*/ set; }
        protected double m_LastFixedUpdateTime;
        protected int m_FixedUpdateCount;

        public void AddSystemToUpdateList(ComponentSystemBase sys)
        {
            if (sys != null)
            {
                if (this == sys)
                    throw new ArgumentException($"Can't add {TypeManager.SystemName(GetType())} to its own update list");

                // Check for duplicate Systems. Also see issue #1792
                if (m_systemsToUpdate.IndexOf(sys) >= 0)
                    return;

                m_systemsToUpdate.Add(sys);
                m_systemSortDirty = true;
            }
        }

        public void RemoveSystemFromUpdateList(ComponentSystemBase sys)
        {
            m_systemSortDirty = true;
            m_systemsToRemove.Add(sys);
        }

        public virtual void SortSystemUpdateList()
        {
            if (!m_systemSortDirty)
                return;
            m_systemSortDirty = false;

            if (m_systemsToRemove.Count > 0)
            {
                foreach (var sys in m_systemsToRemove)
                    m_systemsToUpdate.Remove(sys);
                m_systemsToRemove.Clear();
            }
            
            foreach (var sys in m_systemsToUpdate)
            {
                if (TypeManager.IsSystemAGroup(sys.GetType()))
                {
                    ((ComponentSystemGroup)sys).SortSystemUpdateList();
                }
            }

            ComponentSystemSorter.Sort(m_systemsToUpdate, x => x.GetType(), this.GetType());
        }

#if UNITY_DOTSPLAYER
        public void RecursiveLogToConsole()
        {
            foreach (var sys in m_systemsToUpdate)
            {
                if (sys is ComponentSystemGroup)
                {
                    (sys as ComponentSystemGroup).RecursiveLogToConsole();
                }

                var index = TypeManager.GetSystemTypeIndex(sys.GetType());
                var names = TypeManager.SystemNames;
                var name = names[index];
                Debug.Log(name);
            }
        }

#endif

        protected override void OnStopRunning()
        {
        }

        internal override void OnStopRunningInternal()
        {
            OnStopRunning();

            foreach (var sys in m_systemsToUpdate)
            {
                if ((sys == null) || (!sys.m_PreviouslyEnabled)) continue;

                sys.m_PreviouslyEnabled = false;
                sys.OnStopRunningInternal();
            }
        }

        // Fixed-Rate Support
        // internal for now, until we figure out proper API
        internal void SetFixedTimeStep(float step)
        {
            Assert.IsTrue(step > 0.0f, "Fixed time step must be > 0.0f");

            if (!FixedTimeStepEnabled)
            {
                m_LastFixedUpdateTime = 0.0;
                m_FixedUpdateCount = 0;
            }

            FixedTimeStepEnabled = true;
            FixedTimeStep = step;
        }

        internal void DisableFixedTimeStep()
        {
            FixedTimeStepEnabled = false;
            FixedTimeStep = 0.0f;
        }

        protected override void OnUpdate()
        {
            if (FixedTimeStepEnabled)
            {
                // First trigger after enabling always ticks at the current time
                if (m_FixedUpdateCount == 0)
                {
                    m_LastFixedUpdateTime = Time.ElapsedTime;
                    m_FixedUpdateCount = 1;

                    World.PushTime(new TimeData(
                        elapsedTime: m_LastFixedUpdateTime,
                        deltaTime: FixedTimeStep));  // fudge the initial delta time to be what's expected, even though it's really 0

                    UpdateAllSystems();

                    World.PopTime();

                    return;
                }

                // Note that FixedTimeStep of 0.0f will never update
                while (Time.ElapsedTime - m_LastFixedUpdateTime > FixedTimeStep)
                {
                    m_LastFixedUpdateTime += FixedTimeStep;
                    m_FixedUpdateCount++;

                    World.PushTime(new TimeData(
                        elapsedTime: m_LastFixedUpdateTime,
                        deltaTime: FixedTimeStep));

                    UpdateAllSystems();

                    World.PopTime();
                }
            }
            else
            {
                UpdateAllSystems();
            }
        }

        void UpdateAllSystems()
        {
            if (m_systemSortDirty)
                SortSystemUpdateList();

            foreach (var sys in m_systemsToUpdate)
            {
                try
                {
                    sys.Update();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
#if UNITY_DOTSPLAYER
                    // When in a DOTS Runtime build, throw this upstream -- continuing after silently eating an exception
                    // is not what you'll want, except maybe once we have LiveLink.  If you're looking at this code
                    // because your LiveLink dots runtime build is exiting when you don't want it to, feel free
                    // to remove this block, or guard it with something to indicate the player is not for live link.
                    throw;
#endif
                }

                if (World.QuitUpdate)
                    break;
            }
        }
    }
}
