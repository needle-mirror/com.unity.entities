using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEditor;

namespace Unity.Build
{
    public abstract class RunStep : IRunStep
    {
        public virtual bool CanRun(BuildSettings settings, out string reason)
        {
            reason = null;
            return true;
        }

        public abstract RunStepResult Start(BuildSettings settings);

        public RunStepResult Success(BuildSettings settings, IRunInstance instance) => RunStepResult.Success(settings, this, instance);

        public RunStepResult Failure(BuildSettings settings, string message) => RunStepResult.Failure(settings, this, message);

        internal static string Serialize(IRunStep step)
        {
            if (step == null)
            {
                return null;
            }

            var type = step.GetType();
            return $"{type}, {type.Assembly.GetName().Name}";
        }

        internal static IRunStep Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var type = Type.GetType(json);
            if (TypeConstruction.TryConstruct<IRunStep>(type, out var step))
            {
                return step;
            }

            return null;
        }

        public static IReadOnlyCollection<Type> GetAvailableTypes(Func<Type, bool> filter = null)
        {
            var types = new HashSet<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IRunStep>())
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
    }
}
