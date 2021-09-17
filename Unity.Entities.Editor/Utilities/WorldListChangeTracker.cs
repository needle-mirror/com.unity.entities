using System;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// Detect world list changes when new worlds are created or old worlds are destroyed.
    /// </summary>
    class WorldListChangeTracker
    {
        static int s_Version;
        int m_LastVersion = -1;

        static WorldListChangeTracker()
        {
            World.WorldCreated += OnWorldCreatedOrDestroyed;
            World.WorldDestroyed += OnWorldCreatedOrDestroyed;
        }

        static void OnWorldCreatedOrDestroyed(World world)
        {
            s_Version++;
        }

        public bool HasChanged()
        {
            if (m_LastVersion == s_Version)
                return false;

            m_LastVersion = s_Version;
            return true;
        }
    }
}
