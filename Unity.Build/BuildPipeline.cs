using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;
using PropertyAttribute = Unity.Properties.PropertyAttribute;

namespace Unity.Build
{
    /// <summary>
    /// Defines a list of <see cref="IBuildStep"/> to run in order.
    /// </summary>
    [BuildStep(flags = BuildStepAttribute.Flags.Hidden)]
    public sealed class BuildPipeline : ScriptableObjectPropertyContainer<BuildPipeline>, IBuildStep
    {
        [Property] public List<IBuildStep> BuildSteps = new List<IBuildStep>();
        [Property] public IRunStep RunStep;

        internal static event Action<BuildPipeline, BuildSettings> BuildStarted;
        internal static event Action<BuildPipelineResult> BuildCompleted;

        /// <summary>
        /// File extension for <see cref="BuildPipeline"/> assets.
        /// </summary>
        public const string AssetExtension = ".buildpipeline";

        /// <summary>
        /// Description of this <see cref="BuildPipeline"/> displayed in build progress reporting.
        /// </summary>
        public string Description => $"Build pipeline {name}";

        /// <summary>
        /// List of <see cref="IBuildSettingsComponent"/> derived types that are required for this <see cref="BuildPipeline"/>.
        /// </summary>
        public Type[] RequiredComponents => BuildSteps.Where(step => step.RequiredComponents != null).SelectMany(step => step.RequiredComponents).Distinct().ToArray();

        /// <summary>
        /// List of <see cref="IBuildSettingsComponent"/> derived types that are optional for this <see cref="BuildPipeline"/>.
        /// </summary>
        public Type[] OptionalComponents => BuildSteps.Where(step => step.OptionalComponents != null).SelectMany(step => step.OptionalComponents).Distinct().ToArray();

        /// <summary>
        /// Determine if this <see cref="BuildPipeline"/> will be executed or not.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildPipeline"/>.</param>
        /// <returns><see langword="true"/> if enabled, <see langword="false"/> otherwise.</returns>
        public bool IsEnabled(BuildContext context) => true;

        /// <summary>
        /// Run the <see cref="IBuildStep"/> list of this <see cref="BuildPipeline"/>.
        /// If a <see cref="IBuildStep"/> fails, subsequent <see cref="IBuildStep"/> are not run.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildPipeline"/>.</param>
        /// <returns>The result of the execution of this <see cref="BuildPipeline"/>.</returns>
        public BuildStepResult RunBuildStep(BuildContext context) => RunBuildSteps(context);

        /// <summary>
        /// Cleanup the <see cref="IBuildStep"/> list of this <see cref="BuildPipeline"/>.
        /// Cleanup will only be called for <see cref="IBuildStep"/> that ran.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildPipeline"/>.</param>
        public BuildStepResult CleanupBuildStep(BuildContext context) => BuildStepResult.Success(this);

        /// <summary>
        /// Determine if this <see cref="BuildPipeline"/> can build.
        /// </summary>
        /// <param name="settings">The <see cref="BuildSettings"/> used for the build.</param>
        /// <param name="reason">If <see cref="CanBuild"/> returns <see langword="false"/>, the reason why it fails.</param>
        /// <returns><see langword="true"/> if this <see cref="BuildPipeline"/> can build, otherwise <see langword="false"/>.</returns>
        public bool CanBuild(BuildSettings settings, out string reason)
        {
            foreach (var step in BuildSteps)
            {
                if (step.RequiredComponents == null)
                {
                    continue;
                }

                foreach (var type in step.RequiredComponents)
                {
                    if (!typeof(IBuildSettingsComponent).IsAssignableFrom(type))
                    {
                        reason = $"Type '{type.Name}' is not a valid required component type. It must derive from {nameof(IBuildSettingsComponent)}.";
                        return false;
                    }

                    if (!settings.HasComponent(type))
                    {
                        reason = $"Build step {step.GetType().Name} is missing required component '{type.Name}'.";
                        return false;
                    }
                }
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// Build this <see cref="BuildPipeline"/>.
        /// </summary>
        /// <param name="settings">The <see cref="BuildSettings"/> used for the build.</param>
        /// <param name="progress">Optional build progress that will be displayed when executing the build.</param>
        /// <param name="mutator">Optional mutator that can be used to modify the <see cref="BuildContext"/> before building.</param>
        /// <returns>The result of building this <see cref="BuildPipeline"/>.</returns>
        public BuildPipelineResult Build(BuildSettings settings, BuildProgress progress = null, Action<BuildContext> mutator = null)
        {
            if (EditorApplication.isCompiling)
            {
                throw new InvalidOperationException("Building is not allowed while Unity is compiling.");
            }

            if (!CanBuild(settings, out var reason))
            {
                return BuildPipelineResult.Failure(this, settings, reason);
            }

            BuildStarted?.Invoke(this, settings);
            using (var context = new BuildContext(this, settings, progress, mutator))
            {
                var timer = Stopwatch.StartNew();
                var result = RunBuildSteps(context);
                timer.Stop();

                result.Duration = timer.Elapsed;

                var firstFailedBuildStep = result.BuildStepsResults.FirstOrDefault(r => r.Failed);
                if (firstFailedBuildStep != null)
                {
                    result.Succeeded = false;
                    result.Message = firstFailedBuildStep.Message;
                }

                BuildArtifacts.Store(result, context.Values.OfType<IBuildArtifact>().ToArray());
                BuildCompleted?.Invoke(result);
                return result;
            }
        }

        /// <summary>
        /// Determine if this <see cref="BuildPipeline"/> can run.
        /// </summary>
        /// <param name="settings">The <see cref="BuildSettings"/> used for the build.</param>
        /// <param name="reason">If <see cref="CanRun"/> returns <see langword="false"/>, the reason why it fails.</param>
        /// <returns>The result of running this <see cref="BuildPipeline"/>.</returns>
        public bool CanRun(BuildSettings settings, out string reason)
        {
            var result = BuildArtifacts.GetBuildResult(settings);
            if (result == null)
            {
                reason = $"No build result found for {settings.name.ToHyperLink()}.";
                return false;
            }

            if (result.Failed)
            {
                reason = $"Last build failed with error:\n{result.Message}";
                return false;
            }

            if (RunStep == null)
            {
                reason = $"No run step provided for {name.ToHyperLink()}.";
                return false;
            }

            if (!RunStep.CanRun(settings, out reason))
            {
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// Run this <see cref="BuildPipeline"/>.
        /// This will attempt to run the build target produced from building this <see cref="BuildPipeline"/>.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns>The result of running this <see cref="BuildPipeline"/>.</returns>
        public RunStepResult Run(BuildSettings settings)
        {
            if (!CanRun(settings, out var reason))
            {
                return RunStepResult.Failure(settings, RunStep, reason);
            }

            try
            {
                return RunStep.Start(settings);
            }
            catch (Exception exception)
            {
                return RunStepResult.Exception(settings, RunStep, exception);
            }
        }

        protected override void Reset()
        {
            base.Reset();
            BuildSteps.Clear();
            RunStep = null;
        }

        protected override void Sanitize()
        {
            base.Sanitize();
            BuildSteps.RemoveAll(step => step == null);
        }

        BuildPipelineResult RunBuildSteps(BuildContext context)
        {
            var timer = new Stopwatch();
            var status = context.BuildPipelineStatus;
            var title = context.BuildProgress?.Title ?? string.Empty;

            // Setup build step actions to perform
            var cleanupSteps = new Stack<IBuildStep>();
            var enabledSteps = BuildSteps.Where(step => step.IsEnabled(context)).ToArray();

            // Execute build step actions (Stop executing on first failure - of any kind)
            for (var i = 0; i < enabledSteps.Length; ++i)
            {
                var step = enabledSteps[i];

                var cancelled = context.BuildProgress?.Update($"{title} (Step {i + 1} of {enabledSteps.Length})", step.Description + "...", (float)i / enabledSteps.Length) ?? false;
                if (cancelled)
                {
                    status.Succeeded = false;
                    status.Message = $"{title} was cancelled.";
                    break;
                }

                cleanupSteps.Push(step);

                try
                {
                    timer.Restart();
                    var result = step.RunBuildStep(context);
                    timer.Stop();

                    result.Duration = timer.Elapsed;

                    status.BuildStepsResults.Add(result);

                    // Stop execution for normal build  steps after failure
                    if (!result.Succeeded)
                    {
                        break;
                    }
                }
                catch (Exception exception)
                {
                    // Stop execution for normal build steps after failure
                    status.BuildStepsResults.Add(BuildStepResult.Exception(step, exception));
                    break;
                }
            }

            // Execute Cleanup (Even if there are failures)
            // * In opposite order of the run steps (Only run the cleanup steps, for steps that ran)
            // * can't be cancelled, cleanup must always run
            foreach (var step in cleanupSteps)
            {
                context.BuildProgress?.Update($"{title} (Cleanup)", step.Description + "...", 1.0F);

                try
                {
                    timer.Restart();
                    var result = step.CleanupBuildStep(context);
                    timer.Stop();

                    result.Duration = timer.Elapsed;

                    // All clean steps must run even if there are failures
                    status.BuildStepsResults.Add(result);
                }
                catch (Exception exception)
                {
                    // All clean steps must run even if there are failures
                    status.BuildStepsResults.Add(BuildStepResult.Exception(step, exception));
                }
            }

            return status;
        }
    }
}
