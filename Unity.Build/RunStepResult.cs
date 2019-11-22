using System;
using Unity.Properties;
using UnityEditor;
using UnityEngine;

namespace Unity.Build
{
    /// <summary>
    /// Holds the result of the execution of a <see cref="IRunStep"/>.
    /// </summary>
    public class RunStepResult : IDisposable
    {
        /// <summary>
        /// Determine if the execution of the <see cref="IRunStep"/> succeeded.
        /// </summary>
        public bool Succeeded { get; internal set; }

        /// <summary>
        /// Determine if the execution of the <see cref="IRunStep"/> failed.
        /// </summary>
        public bool Failed { get => !Succeeded; }

        /// <summary>
        /// The message resulting from the execution of this <see cref="IRunStep"/>.
        /// </summary>
        public string Message { get; internal set; }

        /// <summary>
        /// The <see cref="Build.BuildSettings"/> used to run this <see cref="IRunStep"/>.
        /// </summary>
        public BuildSettings BuildSettings { get; internal set; }

        /// <summary>
        /// The <see cref="IRunStep"/> that was executed.
        /// </summary>
        public IRunStep RunStep { get; internal set; }

        /// <summary>
        /// The running process resulting from running the <see cref="IRunStep"/>.
        /// </summary>
        public IRunInstance RunInstance { get; internal set; }

        /// <summary>
        /// Implicit conversion to <see cref="bool"/>.
        /// </summary>
        /// <param name="result">Instance of <see cref="RunStepResult"/>.</param>
        public static implicit operator bool(RunStepResult result) => result.Succeeded;

        /// <summary>
        /// Create a new instance of <see cref="RunStepResult"/> that represent a successful execution.
        /// </summary>
        /// <param name="settings">The <see cref="BuildSettings"/> used by the <see cref="IRunStep"/>.</param>
        /// <param name="step">The <see cref="IRunStep"/> that was executed.</param>
        /// <param name="instance">The <see cref="IRunInstance"/> resulting from running this <see cref="IRunStep"/>.</param>
        /// <returns>A new <see cref="RunStepResult"/> instance.</returns>
        public static RunStepResult Success(BuildSettings settings, IRunStep step, IRunInstance instance) => new RunStepResult
        {
            Succeeded = true,
            BuildSettings = settings,
            RunStep = step,
            RunInstance = instance
        };

        /// <summary>
        /// Create a new instance of <see cref="RunStepResult"/> that represent a failed execution.
        /// </summary>
        /// <param name="settings">The <see cref="BuildSettings"/> used by the <see cref="IRunStep"/>.</param>
        /// <param name="step">The <see cref="IRunStep"/> that was executed.</param>
        /// <param name="message">The failure message.</param>
        /// <returns>A new <see cref="RunStepResult"/> instance.</returns>
        public static RunStepResult Failure(BuildSettings settings, IRunStep step, string message) => new RunStepResult
        {
            Succeeded = false,
            Message = message,
            BuildSettings = settings,
            RunStep = step,
            RunInstance = null
        };

        internal static RunStepResult Exception(BuildSettings settings, IRunStep step, Exception exception) => new RunStepResult
        {
            Succeeded = false,
            Message = exception.Message + "\n" + exception.StackTrace,
            BuildSettings = settings,
            RunStep = step,
            RunInstance = null
        };

        public void LogResult()
        {
            if (Succeeded)
            {
                // Disabled logging successful run result until we decide if its useful
                //Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, BuildSettings, ToString());
            }
            else
            {
                Debug.LogFormat(LogType.Error, LogOption.None, BuildSettings, ToString());
            }
        }

        public override string ToString()
        {
            var name = BuildSettings.name;
            var what = !string.IsNullOrEmpty(name) ? $" {name.ToHyperLink()}" : string.Empty;

            if (Succeeded)
            {
                return $"Run{what} successful.";
            }
            else
            {
                return $"Run{what} failed.\n{Message}";
            }
        }

        public void Dispose()
        {
            if (RunInstance != null)
            {
                RunInstance.Dispose();
            }
        }

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            TypeConstruction.SetExplicitConstructionMethod(() => { return new RunStepResult(); });
        }

        internal RunStepResult() { }
    }
}
