using System;
using Unity.Properties;
using UnityEditor;

namespace Unity.Build
{
    /// <summary>
    /// Holds the result of the execution of a <see cref="IBuildStep"/>.
    /// </summary>
    public class BuildStepResult
    {
        /// <summary>
        /// Determine if the execution of the <see cref="IBuildStep"/> succeeded.
        /// </summary>
        [Property] public bool Succeeded { get; internal set; }

        /// <summary>
        /// Determine if the execution of the <see cref="IBuildStep"/> failed.
        /// </summary>
        public bool Failed { get => !Succeeded; }

        /// <summary>
        /// The message resulting from the execution of this <see cref="IBuildStep"/>.
        /// </summary>
        [Property] public string Message { get; internal set; }

        /// <summary>
        /// Duration of the execution of this <see cref="IBuildStep"/>.
        /// </summary>
        [Property] public TimeSpan Duration { get; internal set; }

        /// <summary>
        /// The <see cref="IBuildStep"/> that was executed.
        /// </summary>
        [Property] public IBuildStep BuildStep { get; internal set; }

        /// <summary>
        /// Description of the <see cref="IBuildStep"/>.
        /// </summary>
        [Property] public string Description => BuildStep.Description;

        /// <summary>
        /// Implicit conversion to <see cref="bool"/>.
        /// </summary>
        /// <param name="result">Instance of <see cref="BuildStepResult"/>.</param>
        public static implicit operator bool(BuildStepResult result) => result.Succeeded;

        /// <summary>
        /// Create a new instance of <see cref="BuildStepResult"/> from a <see cref="UnityEditor.Build.Reporting.BuildReport"/>.
        /// </summary>
        /// <param name="step">The <see cref="IBuildStep"/> that was executed.</param>
        /// <param name="report">The report that was generated.</param>
        public BuildStepResult(IBuildStep step, UnityEditor.Build.Reporting.BuildReport report)
        {
            Succeeded = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            Message = Failed ? report.summary.ToString() : null;
            BuildStep = step;
        }

        /// <summary>
        /// Create a new instance of <see cref="BuildStepResult"/> that represent a successful execution.
        /// </summary>
        /// <param name="step">The <see cref="IBuildStep"/> that was executed.</param>
        /// <returns>A new <see cref="BuildStepResult"/> instance.</returns>
        public static BuildStepResult Success(IBuildStep step) => new BuildStepResult
        {
            Succeeded = true,
            BuildStep = step
        };

        /// <summary>
        /// Create a new instance of <see cref="BuildStepResult"/> that represent a failed execution.
        /// </summary>
        /// <param name="step">The <see cref="IBuildStep"/> that was executed.</param>
        /// <param name="message">The failure message.</param>
        /// <returns>A new <see cref="BuildStepResult"/> instance.</returns>
        public static BuildStepResult Failure(IBuildStep step, string message) => new BuildStepResult
        {
            Succeeded = false,
            Message = message,
            BuildStep = step
        };

        internal static BuildStepResult Exception(IBuildStep step, Exception exception) => new BuildStepResult
        {
            Succeeded = false,
            Message = exception.Message + "\n" + exception.StackTrace,
            BuildStep = step
        };

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            TypeConstruction.SetExplicitConstructionMethod(() => { return new BuildStepResult(); });
        }

        internal BuildStepResult() { }
    }
}
