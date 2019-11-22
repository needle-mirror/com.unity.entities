using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Properties;
using UnityEditor;

namespace Unity.Build
{
    /// <summary>
    /// Holds contextual information while a <see cref="Build.BuildPipeline"/> is executing.
    /// </summary>
    public sealed class BuildContext : IDisposable
    {
        readonly Dictionary<Type, object> m_Values = new Dictionary<Type, object>();

        internal BuildPipeline BuildPipeline { get; }
        internal BuildSettings BuildSettings { get; }

        /// <summary>
        /// List of all values stored.
        /// </summary>
        public object[] Values => m_Values.Values.ToArray();

        /// <summary>
        /// Current <see cref="Build.BuildPipeline"/> execution status.
        /// </summary>
        public BuildPipelineResult BuildPipelineStatus { get; }

        /// <summary>
        /// The <see cref="BuildProgress"/> object used througout the build.
        /// </summary>
        public BuildProgress BuildProgress { get; }

        /// <summary>
        /// Quick access to <see cref="Build.BuildManifest"/> value.
        /// </summary>
        public BuildManifest BuildManifest => GetOrCreateValue<BuildManifest>();

        /// <summary>
        /// Determine if the value of type <typeparamref name="T"/> exists.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <returns><see langword="true"/> if value is found, <see langword="false"/> otherwise.</returns>
        public bool HasValue<T>() where T : class => m_Values.Keys.Any(type => typeof(T).IsAssignableFrom(type));

        /// <summary>
        /// Get value of type <typeparamref name="T"/> if found, otherwise <see langword="null"/>.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <returns>The value of type <typeparamref name="T"/> if found, otherwise <see langword="null"/>.</returns>
        public T GetValue<T>() where T : class => m_Values.FirstOrDefault(pair => typeof(T).IsAssignableFrom(pair.Key)).Value as T;

        /// <summary>
        /// Get value of type <typeparamref name="T"/> if found, otherwise a new instance of type <typeparamref name="T"/> constructed with <see cref="TypeConstruction"/>.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <returns>The value or new instance of type <typeparamref name="T"/>.</returns>
        public T GetOrCreateValue<T>() where T : class
        {
            var value = GetValue<T>();
            if (value == null)
            {
                value = TypeConstruction.Construct<T>();
                SetValue(value);
            }
            return value;
        }

        /// <summary>
        /// Set value of type <typeparamref name="T"/> to this <see cref="BuildContext"/>.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="value">The value to set.</param>
        public void SetValue<T>(T value) where T : class
        {
            if (value == null)
            {
                return;
            }

            var type = value.GetType();
            if (type == typeof(object))
            {
                return;
            }

            m_Values[type] = value;
        }

        /// <summary>
        /// Remove value of type <typeparamref name="T"/> from this <see cref="BuildContext"/>.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <returns><see langword="true"/> if the value was removed, otherwise <see langword="false"/>.</returns>
        public bool RemoveValue<T>() where T : class => m_Values.Remove(typeof(T));

        internal BuildContext() { }

        internal BuildContext(BuildPipeline pipeline, BuildSettings settings, BuildProgress progress, Action<BuildContext> mutator = null)
        {
            BuildPipeline = pipeline ?? throw new NullReferenceException(nameof(pipeline));
            BuildSettings = settings ?? BuildSettings.CreateInstance();
            BuildProgress = progress;
            BuildPipelineStatus = BuildPipelineResult.Success(pipeline, BuildSettings);

            mutator?.Invoke(this);

            // Work-around for assets that can be garbage collected during a build
            BuildSettings.GCPin();
            BuildPipeline.GCPin();
        }

        public void Dispose()
        {
            // Work-around for assets that can be garbage collected during a build
            BuildSettings.GCUnPin();
            BuildPipeline.GCUnPin();
        }
    }
}
