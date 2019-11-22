using System;

namespace Unity.Build
{
    /// <summary>
    /// Base interface for <see cref="BuildStep"/>.
    /// <b>Note:</b> When writing a new build step, derive from <see cref="BuildStep"/> instead of this interface.
    /// </summary>
    public interface IBuildStep
    {
        /// <summary>
        /// Description of this <see cref="IBuildStep"/> displayed in build progress reporting.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// List of <see cref="IBuildSettingsComponent"/> derived types that are required for this <see cref="IBuildStep"/>.
        /// </summary>
        Type[] RequiredComponents { get; }

        /// <summary>
        /// List of <see cref="IBuildSettingsComponent"/> derived types that are optional for this <see cref="IBuildStep"/>.
        /// </summary>
        Type[] OptionalComponents { get; }

        /// <summary>
        /// Determine if this <see cref="IBuildStep"/> will be executed or not.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="IBuildStep"/>.</param>
        /// <returns><see langword="true"/> if enabled, <see langword="false"/> otherwise.</returns>
        bool IsEnabled(BuildContext context);

        /// <summary>
        /// Run this <see cref="IBuildStep"/>.
        /// If a previous <see cref="IBuildStep"/> fails, this <see cref="IBuildStep"/> will not run.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="IBuildStep"/>.</param>
        /// <returns>The result of running this <see cref="IBuildStep"/>.</returns>
        BuildStepResult RunBuildStep(BuildContext context);

        /// <summary>
        /// Cleanup this <see cref="IBuildStep"/>.
        /// Cleanup will only be called if this <see cref="IBuildStep"/> ran.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="IBuildStep"/>.</param>
        BuildStepResult CleanupBuildStep(BuildContext context);
    }
}
