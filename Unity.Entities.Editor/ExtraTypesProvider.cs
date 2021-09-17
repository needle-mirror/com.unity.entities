using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Build.Player;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities.Editor
{
    [InitializeOnLoad]
    public sealed class ExtraTypesProvider
    {
        static ExtraTypesProvider()
        {
            PlayerBuildInterface.ExtraTypesProvider += () =>
            {
                var extraTypes = new HashSet<string>();

                TypeManager.Initialize();

                foreach (var typeInfo in TypeManager.AllTypes)
                {
                    Type type = TypeManager.GetType(typeInfo.TypeIndex);
                    if (type != null)
                    {
                        FastEquality.AddExtraAOTTypes(type, extraTypes);
                    }
                }

                return extraTypes;
            };
        }
    }
}
