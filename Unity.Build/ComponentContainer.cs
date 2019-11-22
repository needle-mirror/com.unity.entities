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
        public bool HasComponent(Type type) => HasComponentOnSelf(type) || HasComponentOnDependency(type);

        /// <summary>
        /// Determine if a <typeparamref name="T"/> component is stored in this container or its dependencies.
        /// </summary>
        /// <typeparam name="T">Type of the component.</typeparam>
        public bool HasComponent<T>() where T : TComponent => HasComponent(typeof(T));

        /// <summary>
        /// Determine if a <see cref="Type"/> component is inherited from a dependency.
        /// </summary>
        /// <param name="type"><see cref="Type"/> of the component.</param>
        public bool IsComponentInherited(Type type) => !HasComponentOnSelf(type) && HasComponentOnDependency(type);

        /// <summary>
        /// Determine if a <typeparamref name="T"/> component is inherited from a dependency.
        /// </summary>
        /// <typeparam name="T">Type of the component.</typeparam>
        public bool IsComponentInherited<T>() where T : TComponent => IsComponentInherited(typeof(T));

        /// <summary>
        /// Determine if a <see cref="Type"/> component overrides a dependency.
        /// </summary>
        /// <param name="type"><see cref="Type"/> of the component.</param>
        public bool IsComponentOverridden(Type type) => HasComponentOnSelf(type) && HasComponentOnDependency(type);

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
            if (type == null)
            {
                throw new NullReferenceException(nameof(type));
            }

            if (type == typeof(object))
            {
                throw new InvalidOperationException($"{nameof(type)} cannot be '{typeof(object).FullName}'.");
            }

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
            if (!TryGetDerivedTypeFromBaseType(type, out type) || !HasComponent(type))
            {
                value = default;
                return false;
            }

            TComponent result;
            try
            {
                result = TypeConstruction.Construct<TComponent>(type);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to construct type '{type.FullName}'.\n{e.Message}");
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
        /// Get a flatten list of all components from this container and its dependencies.
        /// </summary>
        public List<TComponent> GetComponents()
        {
            var lookup = new Dictionary<Type, TComponent>();

            foreach (var dependency in Dependencies)
            {
                if (dependency == null || !dependency)
                {
                    continue;
                }

                var components = dependency.GetComponents();
                foreach (var component in components)
                {
                    lookup[component.GetType()] = component;
                }
            }

            foreach (var component in Components)
            {
                lookup[component.GetType()] = CopyComponent(component);
            }

            return lookup.Values.ToList();
        }

        /// <summary>
        /// Set the value of a <see cref="Type"/> component on this container.
        /// </summary>
        /// <param name="type"><see cref="Type"/> of the component.</param>
        /// <param name="value">Value of the component to set.</param>
        public void SetComponent(Type type, TComponent value)
        {
            if (type == null)
            {
                throw new NullReferenceException(nameof(type));
            }

            if (type == typeof(object))
            {
                throw new InvalidOperationException($"{nameof(type)} cannot be '{typeof(object).FullName}'.");
            }

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
            if (type == null)
            {
                throw new NullReferenceException(nameof(type));
            }

            if (type == typeof(object))
            {
                throw new InvalidOperationException($"{nameof(type)} cannot be '{typeof(object).FullName}'.");
            }

            for (var i = 0; i < Components.Count; ++i)
            {
                if (type.IsAssignableFrom(Components[i].GetType()))
                {
                    Components.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Remove a <typeparamref name="T"/> component from this container.
        /// </summary>
        /// <typeparam name="T">Type of the component.</typeparam>
        public bool RemoveComponent<T>() where T : TComponent => RemoveComponent(typeof(T));

        /// <summary>
        /// Remove all components from this container.
        /// </summary>
        public void ClearComponents() => Components.Clear();

        /// <summary>
        /// Visit a flatten list of all components from this container and its dependencies.
        /// </summary>
        /// <param name="visitor">The visitor to use for visiting each component.</param>
        public void VisitComponents(IPropertyVisitor visitor)
        {
            var components = GetComponents();
            for (var i = 0; i < components.Count; ++i)
            {
                var component = components[i];
                PropertyContainer.Visit(ref component, visitor);
            }
        }

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
        /// Override the dependencies on this container.
        /// </summary>
        /// <param name="dependencies"></param>
        public void SetDependencies(TContainer[] dependencies)
        {
            Dependencies.Clear();
            Dependencies.AddRange(dependencies);
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
            Components.RemoveAll(component => component == null);
        }

        bool HasComponentOnSelf(Type type)
        {
            if (type == null || type == typeof(object))
            {
                return false;
            }
            return Components.Any(component => type.IsAssignableFrom(component.GetType()));
        }

        bool HasComponentOnDependency(Type type)
        {
            if (type == null || type == typeof(object))
            {
                return false;
            }
            return Dependencies.Any(dependency =>
            {
                if (dependency == null || !dependency)
                {
                    return false;
                }
                return dependency.HasComponent(type);
            });
        }

        bool TryGetDerivedTypeFromBaseType(Type baseType, out Type value)
        {
            value = baseType;

            if (baseType == null || baseType == typeof(object))
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
