using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Properties;

namespace Unity.Build.Internals
{
    internal static class BuildSettingsInternals
    {
        internal static IEnumerable<IBuildSettingsComponent> GetComponents(BuildSettings settings, Type type)
        {
            var lookup = new Dictionary<Type, IBuildSettingsComponent>();
            foreach (var dependency in settings.GetDependencies())
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

            foreach (var component in settings.Components)
            {
                var componentType = component.GetType();
                if (type.IsAssignableFrom(componentType))
                {
                    lookup[componentType] = CopyComponent(component);
                }
            }

            return lookup.Values;
        }

        internal static IEnumerable<T> GetComponents<T>(BuildSettings settings) => GetComponents(settings, typeof(T)).Cast<T>();

        static T CopyComponent<T>(T value)
        {
            var result = TypeConstruction.Construct<T>(value.GetType());
            PropertyContainer.Construct(ref result, ref value).Dispose();
            PropertyContainer.Transfer(ref result, ref value).Dispose();
            return result;
        }
    }
}
