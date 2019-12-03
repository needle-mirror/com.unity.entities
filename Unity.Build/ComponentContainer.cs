using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Properties;
using UnityEngine;
using Property = Unity.Properties.PropertyAttribute;

namespace Unity.Build
{
    /// <summary>
    /// Base class that stores a set of hierarchical components by type.
    /// Other <typeparamref name="TContainer"/> can be added as dependencies to get inherited or overridden components.
    /// </summary>
    /// <typeparam name="TContainer">Type of the component container.</typeparam>
    /// <typeparam name="TComponent">Components base type.</typeparam>
    public class ComponentContainer<TContainer, TComponent> : ScriptableObjectPropertyContainer<TContainer>
        where TContainer : ComponentContainer<TContainer, TComponent>
    {
        [Property] internal readonly List<TContainer> Dependencies = new List<TContainer>();
        [Property] internal readonly List<TComponent> Components = new List<TComponent>();

        /// <summary>
        /// Determine if a <see cref="Type"/> component is stored in this container or its dependencies.
        /// </summary>
        /// <param name="type"><see cref="Type"/> of the component.</param>
        public bool HasComponent(Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            return HasComponentOnSelf(type) || HasComponentOnDependency(type);
        }

        /// <summary>
        /// Determine if a <typeparamref name="T"/> component is stored in this container or its dependencies.
        /// </summary>
        /// <typeparam name="T">Type of the component.</typeparam>
        public bool HasComponent<T>() where T : TComponent => HasComponent(typeof(T));

        /// <summary>
        /// Determine if a <see cref="Type"/> component is inherited from a dependency.
        /// </summary>
        /// <param name="type"><see cref="Type"/> of the component.</param>
        public bool IsComponentInherited(Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            return !HasComponentOnSelf(type) && HasComponentOnDependency(type);
        }

        /// <summary>
        /// Determine if a <typeparamref name="T"/> component is inherited from a dependency.
        /// </summary>
        /// <typeparam name="T">Type of the component.</typeparam>
        public bool IsComponentInherited<T>() where T : TComponent => IsComponentInherited(typeof(T));

        /// <summary>
        /// Determine if a <see cref="Type"/> component overrides a dependency.
        /// </summary>
        /// <param name="type"><see cref="Type"/> of the component.</param>
        public bool IsComponentOverridden(Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            return HasComponentOnSelf(type) && HasComponentOnDependency(type);
        }

        /// <summary>
        /// Determine if a <typeparamref name="T"/> component overrides a dependency.
        /// </summary>
        /// <typeparam name="T">Type of the component.</typeparam>
        public bool IsComponentOverridden<T>() where T : TComponent => IsComponentOverridden(typeof(T));

        /// <summary>
        /// Get the value of a <see cref="Type"/> component.
        /// </summary>
        /// <param name="type"><see cref="Type"/> of the component.</param>
        public TComponent GetComponent(Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            if (!TryGetComponent(type, out var value))
            {
                throw new InvalidOperationException($"Component type '{type.FullName}' not found.");
            }
            return value;
        }

        /// <summary>
        /// Get the value of a <typeparamref name="T"/> component.
        /// </summary>
        /// <typeparam name="T">Type of the component.</typeparam>
        public T GetComponent<T>() where T : TComponent => (T)GetComponent(typeof(T));

        /// <summary>
        /// Try to get the value of a <see cref="Type"/> component.
        /// </summary>
        /// <param name="type"><see cref="Type"/> of the component.</param>
        /// <param name="value">Out value of the component.</param>
        public bool TryGetComponent(Type type, out TComponent value)
        {
            if (!TryGetDerivedTypeFromBaseType(type, out type) ||
                !(HasComponentOnSelf(type) || HasComponentOnDependency(type)) ||
                !TypeConstruction.TryConstruct<TComponent>(type, out var result))
            {
                value = default;
                return false;
            }

            for (var i = 0; i < Dependencies.Count; ++i)
            {
                var dependency = Dependencies[i];
                if (dependency == null || !dependency)
                {
                    continue;
                }

                if (dependency.TryGetComponent(type, out var component))
                {
                    CopyComponent(ref result, ref component);
                }
            }

            for (var i = 0; i < Components.Count; ++i)
            {
                var component = Components[i];
                if (component.GetType() == type)
                {
                    CopyComponent(ref result, ref component);
                    break;
                }
            }

            value = result;
            return true;
        }

        /// <summary>
        /// Try to get the value of a <typeparamref name="T"/> component.
        /// </summary>
        /// <param name="value">Out value of the component.</param>
        /// <typeparam name="T">Type of the component.</typeparam>
        public bool TryGetComponent<T>(out T value) where T : TComponent
        {
            if (TryGetComponent(typeof(T), out var result))
            {
                value = (T)result;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Get a flatten list of all components recursively from this container and its dependencies.
        /// </summary>
        /// <returns>List of components.</returns>
        public IEnumerable<TComponent> GetComponents()
        {
            var lookup = new Dictionary<Type, TComponent>();
            foreach (var dependency in GetDependencies())
            {
                foreach (var component in dependency.Components)
                {
                    lookup[component.GetType()] = CopyComponent(component);
                }
            }

            foreach (var component in Components)
            {
                lookup[component.GetType()] = CopyComponent(component);
            }

            return lookup.Values;
        }

        /// <summary>
        /// Get a flatten list of all components recursively from this container and its dependencies, that matches <see cref="Type"/>.
        /// </summary>
        /// <typeparam name="T">Type of the components.</typeparam>
        /// <returns>List of components.</returns>
        public IEnumerable<TComponent> GetComponents(Type type)
        {
            CheckTypeAndThrowIfInvalid(type);

            var lookup = new Dictionary<Type, TComponent>();
            foreach (var dependency in GetDependencies())
            {
                foreach (var component in dependency.Components)
                {
                    var componentType = component.GetType();
                    if (type.IsAssignableFrom(componentType))
                    {
                        lookup[componentType] = CopyComponent(component);
                    }
                }
            }

            foreach (var component in Components)
            {
                var componentType = component.GetType();
                if (type.IsAssignableFrom(componentType))
                {
                    lookup[componentType] = CopyComponent(component);
                }
            }

            return lookup.Values;
        }

        /// <summary>
        /// Get a flatten list of all components recursively from this container and its dependencies, that matches <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Type of the components.</typeparam>
        /// <returns>List of components.</returns>
        public IEnumerable<T> GetComponents<T>() where T : TComponent => GetComponents(typeof(T)).Cast<T>();

        /// <summary>
        /// Set the value of a <see cref="Type"/> component on this container.
        /// </summary>
        /// <param name="type"><see cref="Type"/> of the component.</param>
        /// <param name="value">Value of the component to set.</param>
        public void SetComponent(Type type, TComponent value)
        {
            CheckTypeAndThrowIfInvalid(type);
            if (type.IsInterface || type.IsAbstract)
            {
                throw new InvalidOperationException($"{nameof(type)} cannot be interface or abstract.");
            }

            for (var i = 0; i < Components.Count; ++i)
            {
                if (Components[i].GetType() == type)
                {
                    Components[i] = CopyComponent(value);
                    return;
                }
            }

            Components.Add(CopyComponent(value));
        }

        /// <summary>
        /// Set the value of a <typeparamref name="T"/> component on this container.
        /// </summary>
        /// <param name="value">Value of the component to set.</param>
        /// <typeparam name="T">Type of the component.</typeparam>
        public void SetComponent<T>(T value) where T : TComponent => SetComponent(typeof(T), value);

        /// <summary>
        /// Remove a <see cref="Type"/> component from this container.
        /// </summary>
        /// <param name="type"><see cref="Type"/> of the component.</param>
        public bool RemoveComponent(Type type)
        {
            CheckTypeAndThrowIfInvalid(type);
            return Components.RemoveAll(c => type.IsAssignableFrom(c.GetType())) > 0;
        }

        /// <summary>
        /// Remove all <typeparamref name="T"/> components from this container.
        /// </summary>
        /// <typeparam name="T">Type of the component.</typeparam>
        public bool RemoveComponent<T>() where T : TComponent => RemoveComponent(typeof(T));

        /// <summary>
        /// Remove all components from this container.
        /// </summary>
        public void ClearComponents() => Components.Clear();

        /// <summary>
        /// Determine if a dependency exist in this container or its dependencies.
        /// </summary>
        /// <param name="dependency">The dependency to search.</param>
        /// <returns><see langword="true"/> if the dependency is found, <see langword="false"/> otherwise.</returns>
        public bool HasDependency(TContainer dependency)
        {
            if (dependency == null || !dependency)
            {
                return false;
            }

            foreach (var dep in Dependencies)
            {
                if (dep == null || !dep)
                {
                    continue;
                }

                if (dep == dependency || dep.HasDependency(dependency))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Add a dependency to this container.
        /// Circular dependencies or dependencies on self are not allowed.
        /// </summary>
        /// <param name="dependency">The dependency to add.</param>
        /// <returns><see langword="true"/> if the dependency was added, <see langword="false"/> otherwise.</returns>
        public bool AddDependency(TContainer dependency)
        {
            if (dependency == null || !dependency)
            {
                throw new ArgumentNullException(nameof(dependency));
            }

            if (dependency == this || HasDependency(dependency) || dependency.HasDependency(this as TContainer))
            {
                return false;
            }

            Dependencies.Add(dependency);
            return true;
        }

        /// <summary>
        /// Get a flatten list of all dependencies recursively from this container and its dependencies.
        /// </summary>
        /// <returns>List of dependencies.</returns>
        public IEnumerable<TContainer> GetDependencies()
        {
            var dependencies = new HashSet<TContainer>();
            foreach (var dependency in Dependencies)
            {
                if (dependency == null || !dependency || dependency == this)
                {
                    continue;
                }

                dependencies.Add(dependency);
                foreach (var childDependency in dependency.GetDependencies())
                {
                    dependencies.Add(childDependency);
                }
            }
            return dependencies;
        }

        /// <summary>
        /// Remove a dependency from this container.
        /// </summary>
        /// <param name="dependency">The dependency to remove.</param>
        public bool RemoveDependency(TContainer dependency)
        {
            if (dependency == null || !dependency)
            {
                throw new ArgumentNullException(nameof(dependency));
            }
            return Dependencies.Remove(dependency);
        }

        /// <summary>
        /// Remove all dependencies from this container.
        /// </summary>
        public void ClearDependencies() => Dependencies.Clear();

        protected override void Reset()
        {
            base.Reset();
            Dependencies.Clear();
            Components.Clear();
        }

        protected override void Sanitize()
        {
            base.Sanitize();
            Dependencies.RemoveAll(dependency => dependency == null);
            Components.RemoveAll(component => component == null);
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

            if (!typeof(TComponent).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"{nameof(type)} must derive from '{typeof(TComponent).FullName}'.");
            }
        }

        bool HasComponentOnSelf(Type type) => Components.Any(component => type.IsAssignableFrom(component.GetType()));

        bool HasComponentOnDependency(Type type) => GetDependencies().Any(dependency => dependency.HasComponentOnSelf(type));

        bool TryGetDerivedTypeFromBaseType(Type baseType, out Type value)
        {
            value = baseType;
            if (baseType == null || baseType == typeof(object) || !typeof(TComponent).IsAssignableFrom(baseType))
            {
                return false;
            }

            if (!baseType.IsInterface && !baseType.IsAbstract)
            {
                return true;
            }

            foreach (var dependency in Dependencies)
            {
                if (null == dependency && !dependency)
                {
                    continue;
                }

                if (dependency.TryGetDerivedTypeFromBaseType(baseType, out var type))
                {
                    value = type;
                }
            }

            foreach (var component in Components)
            {
                var type = component.GetType();
                if (baseType.IsAssignableFrom(type))
                {
                    value = type;
                    break;
                }
            }

            return true;
        }

        T CopyComponent<T>(T value) where T : TComponent
        {
            var result = TypeConstruction.Construct<T>(value.GetType());
            PropertyContainer.Construct(ref result, ref value).Dispose();
            PropertyContainer.Transfer(ref result, ref value).Dispose();
            return result;
        }

        void CopyComponent(ref TComponent dst, ref TComponent src)
        {
            PropertyContainer.Construct(ref dst, ref src).Dispose();
            PropertyContainer.Transfer(ref dst, ref src).Dispose();
        }
    }
}
