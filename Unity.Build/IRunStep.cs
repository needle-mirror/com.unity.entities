namespace Unity.Build
{
    public interface IRunStep
    {
        bool CanRun(BuildSettings settings, out string reason);
        RunStepResult Start(BuildSettings settings);
    }
}
