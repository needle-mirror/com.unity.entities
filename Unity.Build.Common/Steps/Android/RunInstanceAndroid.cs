using System.Diagnostics;

namespace Unity.Build.Common
{
    // This is just a placeholder until we implement RunStepAndroid in com.unity.platforms
    sealed class RunInstanceAndroid : IRunInstance
    {
        public bool IsRunning => true;

        public RunInstanceAndroid()
        {
        }

        public void Dispose()
        {
        }
    }
}
