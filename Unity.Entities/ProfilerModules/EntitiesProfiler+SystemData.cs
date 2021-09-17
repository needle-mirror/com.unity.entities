#if ENABLE_PROFILER
using System;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace Unity.Entities
{
    partial class EntitiesProfiler
    {
        [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct SystemData : IEquatable<SystemData>
        {
            readonly SystemHandleUntyped m_System;
            readonly FixedString128Bytes m_Name;

            public SystemHandleUntyped System => m_System;

            [NotBurstCompatible]
            public string Name => m_Name.ToString();

            [NotBurstCompatible]
            public SystemData(World world, ComponentSystemBase system)
            {
                var systemType = system.GetType();
                m_System = world.Unmanaged.GetExistingUnmanagedSystem(systemType);
                m_Name = TypeManager.GetSystemName(systemType).ToFixedString128();
            }

            public bool Equals(SystemData other)
            {
                return m_System.Equals(other.m_System);
            }

            [NotBurstCompatible]
            public override bool Equals(object obj)
            {
                return obj is SystemData systemData ? Equals(systemData) : false;
            }

            public override int GetHashCode()
            {
                return m_System.GetHashCode();
            }

            public static bool operator ==(SystemData lhs, SystemData rhs)
            {
                return lhs.Equals(rhs);
            }

            public static bool operator !=(SystemData lhs, SystemData rhs)
            {
                return !lhs.Equals(rhs);
            }
        }
    }
}
#endif
