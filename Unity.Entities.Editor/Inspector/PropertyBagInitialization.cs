using System;
using System.Threading;
using Unity.Profiling;
using Unity.Properties.Internal;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Editor
{
    static class PropertyBagInitialization
    {
        [InitializeOnLoadMethod]
        static void Initialize()
        {
            TypeManager.Initialize();
            ThreadPool.QueueUserWorkItem(state =>
            {
                using (new ProfilerMarker("PropertyBagInitialization").Auto())
                {
                    var types = (TypeManager.TypeInfo[])state;
                    foreach (var type in types)
                    {
                        if (type.Type != null)
                        {
                            PropertyBagStore.GetPropertyBag(type.Type);
                        }
                    }
                }

            }, TypeManager.GetAllTypes());
        }
    }
}
