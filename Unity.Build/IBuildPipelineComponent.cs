namespace Unity.Build
{
    /// <summary>
    /// Base interface for <see cref="BuildSettings"/> components that provides the <see cref="BuildPipeline"/>.
    /// </summary>
    public interface IBuildPipelineComponent : IBuildSettingsComponent
    {
        BuildPipeline Pipeline { get; set; }
    }
}
