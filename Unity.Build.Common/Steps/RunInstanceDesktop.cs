using System.Diagnostics;

namespace Unity.Build.Common
{
    public sealed class RunInstanceDesktop : IRunInstance
    {
        Process m_Process;

        public bool IsRunning => !m_Process.HasExited;

        public RunInstanceDesktop(Process process)
        {
            m_Process = process;
        }

        public void Dispose()
        {
            m_Process.Dispose();
        }
    }
}
