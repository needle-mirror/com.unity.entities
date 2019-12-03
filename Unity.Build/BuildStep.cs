using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Properties;
using UnityEditor;

namespace Unity.Build
{
    /// <summary>
    /// Base class for build steps that are executed througout a <see cref="BuildPipeline"/>.
    /// </summary>
    public abstract class BuildStep : IBuildStep
    {
        /// <summary>
        /// Description of this <see cref="BuildStep"/> displayed in build progress reporting.
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// List of <see cref="IBuildSettingsComponent"/> derived types that are required for this <see cref="BuildStep"/>.
        /// </summary>
        public virtual Type[] RequiredComponents { get; }

        /// <summary>
        /// List of <see cref="IBuildSettingsComponent"/> derived types that are optional for this <see cref="BuildStep"/>.
        /// </summary>
        public virtual Type[] OptionalComponents { get; }

        /// <summary>
        /// Determine if this <see cref="BuildStep"/> will be executed or not.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <returns><see langword="true"/> if enabled, <see langword="false"/> otherwise.</returns>
        public virtual bool IsEnabled(BuildContext context) => true;

        /// <summary>
        /// Run this <see cref="BuildStep"/>.
        /// If a previous <see cref="BuildStep"/> fails, this <see cref="BuildStep"/> will not run.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <returns>The result of running this <see cref="BuildStep"/>.</returns>
        public abstract BuildStepResult RunBuildStep(BuildContext context);

        /// <summary>
        /// Cleanup this <see cref="BuildStep"/>.
        /// Cleanup will only be called if this <see cref="BuildStep"/> ran.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        public virtual BuildStepResult CleanupBuildStep(BuildContext context) => Success();

        /// <summary>
        /// Determine if a required <see cref="Type"/> component is stored in <see cref="BuildSettings"/>.
        /// The component <see cref="Type"/> must exist in the <see cref="RequiredComponents"/> list.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <param name="type">Type of the required component.</param>
        /// <returns><see langword="true"/> if the required component type is found, <see langword="false"/> otherwise.</returns>
        public bool HasRequiredComponent(BuildContext context, Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            if (RequiredComponents == null || !RequiredComponents.Contains(type))
            {
                throw new InvalidOperationException($"Component type '{type.FullName}' is not in the {nameof(RequiredComponents)} list.");
            }
            return context.BuildSettings.HasComponent(type);
        }

        /// <summary>
        /// Determine if a required <typeparamref name="T"/> component is stored in <see cref="BuildSettings"/>.
        /// The component <see cref="Type"/> must exist in the <see cref="RequiredComponents"/> list.
        /// </summary>
        /// <typeparam name="T">Type of the required component.</typeparam>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <returns><see langword="true"/> if the required component type is found, <see langword="false"/> otherwise.</returns>
        public bool HasRequiredComponent<T>(BuildContext context) where T : IBuildSettingsComponent => HasRequiredComponent(context, typeof(T));

        /// <summary>
        /// Get the value of a required <see cref="Type"/> component from <see cref="BuildSettings"/>.
        /// The component <see cref="Type"/> must exist in the <see cref="RequiredComponents"/> list.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <param name="type">Type of the required component.</param>
        /// <returns>The value of the required component.</returns>
        public IBuildSettingsComponent GetRequiredComponent(BuildContext context, Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            if (RequiredComponents == null || !RequiredComponents.Contains(type))
            {
                throw new InvalidOperationException($"Component type '{type.FullName}' is not in the {nameof(RequiredComponents)} list.");
            }
            return context.BuildSettings.GetComponent(type);
        }

        /// <summary>
        /// Get the value of a required <typeparamref name="T"/> component from <see cref="BuildSettings"/>.
        /// The component <see cref="Type"/> must exist in the <see cref="RequiredComponents"/> list.
        /// </summary>
        /// <typeparam name="T">Type of the required component.</typeparam>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <returns>The value of the required component.</returns>
        public T GetRequiredComponent<T>(BuildContext context) where T : IBuildSettingsComponent => (T)GetRequiredComponent(context, typeof(T));

        /// <summary>
        /// Get all required components from <see cref="BuildSettings"/>.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <returns>List of required components.</returns>
        public IEnumerable<IBuildSettingsComponent> GetRequiredComponents(BuildContext context)
        {
            if (RequiredComponents == null)
            {
                return Enumerable.Empty<IBuildSettingsComponent>();
            }

            var lookup = new Dictionary<Type, IBuildSettingsComponent>();
            foreach (var requiredComponent in RequiredComponents)
            {
                lookup[requiredComponent] = context.BuildSettings.GetComponent(requiredComponent);
            }
            return lookup.Values;
        }

        /// <summary>
        /// Get all required components from <see cref="BuildSettings"/>, that matches <see cref="Type"/>.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <param name="type">Type of the components.</param>
        /// <returns>List of required components.</returns>
        public IEnumerable<IBuildSettingsComponent> GetRequiredComponents(BuildContext context, Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            if (RequiredComponents == null || !RequiredComponents.Contains(type))
            {
                throw new InvalidOperationException($"Component type '{type.FullName}' is not in the {nameof(RequiredComponents)} list.");
            }

            var lookup = new Dictionary<Type, IBuildSettingsComponent>();
            foreach (var requiredComponent in RequiredComponents)
            {
                if (!type.IsAssignableFrom(requiredComponent))
                {
                    continue;
                }
                lookup[requiredComponent] = context.BuildSettings.GetComponent(requiredComponent);
            }
            return lookup.Values;
        }

        /// <summary>
        /// Get all required components from <see cref="BuildSettings"/>, that matches <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Type of the components.</typeparam>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <returns>List of required components.</returns>
        public IEnumerable<T> GetRequiredComponents<T>(BuildContext context) where T : IBuildSettingsComponent => GetRequiredComponents(context, typeof(T)).Cast<T>();

        /// <summary>
        /// Determine if an optional <see cref="Type"/> component is stored in <see cref="BuildSettings"/>.
        /// The component <see cref="Type"/> must exist in the <see cref="OptionalComponents"/> list.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <param name="type">Type of the optional component.</param>
        /// <returns><see langword="true"/> if the optional component type is found, <see langword="false"/> otherwise.</returns>
        public bool HasOptionalComponent(BuildContext context, Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            if (OptionalComponents == null || !OptionalComponents.Contains(type))
            {
                throw new InvalidOperationException($"Component type '{type.FullName}' is not in the {nameof(OptionalComponents)} list.");
            }
            return context.BuildSettings.HasComponent(type);
        }

        /// <summary>
        /// Determine if an optional <typeparamref name="T"/> component is stored in <see cref="BuildSettings"/>.
        /// The component <see cref="Type"/> must exist in the <see cref="OptionalComponents"/> list.
        /// </summary>
        /// <typeparam name="T">Type of the optional component.</typeparam>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <returns><see langword="true"/> if the optional component type is found, <see langword="false"/> otherwise.</returns>
        public bool HasOptionalComponent<T>(BuildContext context) where T : IBuildSettingsComponent => HasOptionalComponent(context, typeof(T));

        /// <summary>
        /// Get the value of an optional <see cref="Type"/> component from <see cref="BuildSettings"/>.
        /// The component <see cref="Type"/> must exist in the <see cref="OptionalComponents"/> list.
        /// If the component is not found in <see cref="BuildSettings"/>, a new instance of type <see cref="Type"/> is returned.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <param name="type">Type of the optional component.</param>
        /// <returns>The value of the optional component.</returns>
        public IBuildSettingsComponent GetOptionalComponent(BuildContext context, Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            if (OptionalComponents == null || !OptionalComponents.Contains(type))
            {
                throw new InvalidOperationException($"Component type '{type.FullName}' is not in the {nameof(OptionalComponents)} list.");
            }

            if (context.BuildSettings.HasComponent(type))
            {
                return context.BuildSettings.GetComponent(type);
            }

            return TypeConstruction.Construct<IBuildSettingsComponent>(type);
        }

        /// <summary>
        /// Get the value of an optional <typeparamref name="T"/> component from <see cref="BuildSettings"/>.
        /// The component <see cref="Type"/> must exist in the <see cref="OptionalComponents"/> list.
        /// If the component is not found in <see cref="BuildSettings"/>, a new instance of type <typeparamref name="T"/> is returned.
        /// </summary>
        /// <typeparam name="T">Type of the optional component.</typeparam>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <returns>The value of the optional component.</returns>
        public T GetOptionalComponent<T>(BuildContext context) where T : IBuildSettingsComponent => (T)GetOptionalComponent(context, typeof(T));

        /// <summary>
        /// Get all optional components from <see cref="BuildSettings"/>.
        /// Optional component types not found in <see cref="BuildSettings"/> will be set to a new instance of that type.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <returns>List of optional components.</returns>
        public IEnumerable<IBuildSettingsComponent> GetOptionalComponents(BuildContext context)
        {
            if (OptionalComponents == null)
            {
                return Enumerable.Empty<IBuildSettingsComponent>();
            }

            var lookup = new Dictionary<Type, IBuildSettingsComponent>();
            foreach (var type in OptionalComponents)
            {
                if (!context.BuildSettings.TryGetComponent(type, out var component))
                {
                    component = TypeConstruction.Construct<IBuildSettingsComponent>(type);
                }
                lookup[type] = component;
            }
            return lookup.Values;
        }

        /// <summary>
        /// Get all optional components from <see cref="BuildSettings"/>, that matches <see cref="Type"/>.
        /// Optional component types not found in <see cref="BuildSettings"/> will be set to a new instance of that type.
        /// </summary>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <param name="type">Type of the components.</param>
        /// <returns>List of optional components.</returns>
        public IEnumerable<IBuildSettingsComponent> GetOptionalComponents(BuildContext context, Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            if (OptionalComponents == null || !OptionalComponents.Contains(type))
            {
                throw new InvalidOperationException($"Component type '{type.FullName}' is not in the {nameof(OptionalComponents)} list.");
            }

            var lookup = new Dictionary<Type, IBuildSettingsComponent>();
            foreach (var optionalComponentType in OptionalComponents)
            {
                if (!type.IsAssignableFrom(optionalComponentType))
                {
                    continue;
                }

                if (!context.BuildSettings.TryGetComponent(optionalComponentType, out var component))
                {
                    component = TypeConstruction.Construct<IBuildSettingsComponent>(optionalComponentType);
                }
                lookup[optionalComponentType] = component;
            }
            return lookup.Values;
        }

        /// <summary>
        /// Get all optional components from <see cref="BuildSettings"/>, that matches <typeparamref name="T"/>.
        /// Optional component types not found in <see cref="BuildSettings"/> will be set to a new instance of that type.
        /// </summary>
        /// <typeparam name="T">Type of the components.</typeparam>
        /// <param name="context">The <see cref="BuildContext"/> used by the execution of this <see cref="BuildStep"/>.</param>
        /// <returns>List of optional components.</returns>
        public IEnumerable<T> GetOptionalComponents<T>(BuildContext context) => GetOptionalComponents(context, typeof(T)).Cast<T>();

        /// <summary>
        /// Retrieves a list of valid types for build steps.
        /// </summary>
        /// <param name="filter">Optional filter function for types.</param>
        /// <returns>List of available build step types.</returns>
        public static IReadOnlyCollection<Type> GetAvailableTypes(Func<Type, bool> filter = null)
        {
            var types = new HashSet<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IBuildStep>())
            {
                if (type.IsAbstract || type.IsInterface)
                {
                    continue;
                }
                if (filter != null && !filter(type))
                {
                    continue;
                }
                types.Add(type);
            }
            return types;
        }

        /// <summary>
        /// Construct <see cref="BuildStepResult"/> from this <see cref="IBuildStep"/> that represent a successful execution.
        /// </summary>
        /// <returns>A new <see cref="BuildStepResult"/> instance.</returns>
        public BuildStepResult Success() => BuildStepResult.Success(this);

        /// <summary>
        /// Construct <see cref="BuildStepResult"/> from this <see cref="IBuildStep"/> that represent a failed execution.
        /// </summary>
        /// <param name="message">Message that explain why the <see cref="IBuildStep"/> execution failed.</param>
        /// <returns>A new <see cref="BuildStepResult"/> instance.</returns>
        public BuildStepResult Failure(string message) => BuildStepResult.Failure(this, message);

        internal static string Serialize(IBuildStep step)
        {
            if (step == null)
            {
                return null;
            }

            if (step is BuildPipeline pipeline)
            {
                return GlobalObjectId.GetGlobalObjectIdSlow(pipeline).ToString();
            }
            else
            {
                var type = step.GetType();
                return $"{type}, {type.Assembly.GetName().Name}";
            }
        }

        internal static IBuildStep Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            if (GlobalObjectId.TryParse(json, out var id))
            {
                if (GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is BuildPipeline pipeline)
                {
                    return pipeline;
                }
            }
            else
            {
                var type = Type.GetType(json);
                if (TypeConstruction.TryConstruct<IBuildStep>(type, out var step))
                {
                    return step;
                }
            }

            return null;
        }

        void CheckTypeAndThrowIfInvalid(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (type == typeof(object))
            {
                throw new InvalidOperationException($"{nameof(type)} cannot be 'object'.");
            }

            if (!typeof(IBuildSettingsComponent).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"{nameof(type)} must derive from '{typeof(IBuildSettingsComponent).FullName}'.");
            }
        }
    }
}
