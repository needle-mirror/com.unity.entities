#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.Entities
{
    /* Builds a static registry of bindings between a runtime field with an authoring field. Multiple different runtime
     fields can be associated with a same authoring field.

     Only primitive types of int, bool, and float, in addition to Unity.Mathematics variants of these primitives
     (e.g. int2, float4) will be added to the BindingRegistry. Other types will be silently ignored.
     */

    // TODO: Support IBufferElementData

    [InitializeOnLoad]
    public static class BindingRegistry
    {
        const BindingFlags k_FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        //can't use NativeArray since Type is nullable. Would using Dots typemanager work here?
        private static readonly Type[] kBindingSupportedTypes =
        {
            typeof(float), typeof(bool), typeof(int),
            typeof(float2),typeof(int2),typeof(bool2),
            typeof(float3),typeof(int3),typeof(bool3),
            typeof(float4),typeof(int4),typeof(bool4),
            typeof(Vector3), typeof(quaternion)
        };

        internal struct RuntimeFieldProperties
        {
            public int FieldOffset;
            public int FieldSize;
            public Type FieldType;
        }

        // Binds a runtime field property to an authoring field name
        internal struct ReverseBinding
        {
            public string AuthoringFieldName;
            public TypeIndex ComponentTypeIndex;
            public RuntimeFieldProperties FieldProperties;
        }

        /// <summary>
        /// Maps a runtime (ComponentData) field type and name to an authoring (MonoBehaviour) field type and name.
        /// </summary>
        /// <remarks>
        /// An authoring field can map to several runtime fields. But a runtime field only reads from one authoring field.
        /// </remarks>
        static Dictionary<(Type, string), (Type, string)> s_RuntimeToAuthoringFieldMap =
            new Dictionary<(Type, string), (Type, string)>();

        /// <summary>
        /// Keeps a set of the names of all the fields that are bound for a runtime type.
        /// </summary>
        /// <remarks>
        /// A runtime type can be partially bound.
        /// </remarks>
        static Dictionary<Type, HashSet<string>> s_RuntimeFieldNames =
            new Dictionary<Type, HashSet<string>>();

        /// <summary>
        /// Field offset and size associated with the type and name of a runtime ComponentData.
        /// </summary>
        static Dictionary<(Type, string), RuntimeFieldProperties> s_RuntimeFieldProperties =
            new Dictionary<(Type, string), RuntimeFieldProperties>();

        /// <summary>
        /// Maps an association of runtime property fields with their authoring field name to an Authoring Type to be able to support live properties in the Editor
        /// </summary>
        internal static Dictionary<Type, List<ReverseBinding>> s_AuthoringToRuntimeBinding = new Dictionary<Type, List<ReverseBinding>>();

        static BindingRegistry() =>
            Initialize();

        static void Initialize()
        {
            // Function is called at DomainReload

            // Make sure the TypeManager is initialized
            TypeManager.Initialize();

            // Clear reverse binding mapping to clean old fields
            s_AuthoringToRuntimeBinding.Clear();

            // Gather all registered runtime fields
            var registeredFields = TypeCache.GetFieldsWithAttribute<RegisterBindingAttribute>();
            foreach (var field in registeredFields)
            {
                if(field.IsPrivate || !Array.Exists(kBindingSupportedTypes,type => type == field.FieldType))
                    continue;

                // We need to do that because an authoring field can be bound to several runtime fields.
                var attrs = field.GetCustomAttributes<RegisterBindingAttribute>();
                foreach (var attr in attrs)
                {
                    var authoringField = field.Name;
                    //fragile: assumption that the only generated support types are bool/int/float 2/3/4

                    if (attr.Generated)
                        authoringField += attr.ComponentField.Substring(attr.ComponentField.IndexOf('.'));

                    Register(attr.ComponentType, attr.ComponentField, field.DeclaringType, authoringField);
                }
            }
        }

        // Register binding of a runtime field with an authoring field
        public static void Register(Type runtimeComponent, string runtimeField, Type authoringComponent,
            string authoringField)
        {
            Assert.IsTrue(typeof(Component).IsAssignableFrom(authoringComponent));
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(runtimeComponent));

            var typeIndex = TypeManager.GetTypeIndex(runtimeComponent);
            Assert.IsTrue(typeIndex.Value != 1);

            (Type, string) runtimeKey = (runtimeComponent, runtimeField);
            (Type, string) authoringValue = (authoringComponent, authoringField);

            // TODO: Validation checks on authoringComponent type and authoringField
            // - Check if field is public, serialized and not decorated with a NotKeyable attribute
            // - Are validation checks even necessary? If these are not animatable field there's no way they could be put in the
            //   rig definition. However we risk having users declare stuff that just isn't valid and they would not know about it.
            // - Would be great to have an API such as AnimationUtility.GetAnimatableBindings(ComponentType) without the
            //   need to instantiate a GameObject. We don't care about the path, only animatable fields of Component type.
            //   Having this would minimize the amount of reflection we'd have to do in C# altogether for validation.

            bool extractedProperties = ExtractFieldProperties(runtimeKey, out var fieldProps);
            if (!extractedProperties) // Should we keep continuing?
                Debug.LogError($"No compatible binding for {authoringComponent}. ('{runtimeComponent}'.'{runtimeField}' => {authoringField}");

            if (!s_RuntimeFieldProperties.ContainsKey(runtimeKey))
            {
                if (extractedProperties)
                    s_RuntimeFieldProperties.Add(runtimeKey, fieldProps);
            }

            var isValueType = runtimeKey.Item1.IsValueType;
            if (!isValueType)
                Debug.LogWarning(
                    $"Field [{runtimeKey.Item2}] part of runtime component type [{runtimeKey.Item1}] could not be found.");
            else
            {
                if (!s_RuntimeToAuthoringFieldMap.TryGetValue(runtimeKey, out var registeredAuthoring))
                {
                    s_RuntimeToAuthoringFieldMap.Add(runtimeKey, authoringValue);
                }
                else
                {
                    if (registeredAuthoring != authoringValue)
                        throw new InvalidOperationException(
                            $"Field [{runtimeKey.Item2}] part of runtime component type [{runtimeKey.Item1}] is already bound to field [{registeredAuthoring.Item2}] of authoring component type [{registeredAuthoring.Item1}] ");
                }

                if (s_RuntimeFieldNames.TryGetValue(runtimeComponent, out var fieldNames))
                    fieldNames.Add(runtimeField);
                else
                    s_RuntimeFieldNames[runtimeComponent] = new HashSet<string>() {runtimeField};
            }

            if (!s_AuthoringToRuntimeBinding.TryGetValue(authoringComponent, out var lookup))
            {
                if (extractedProperties)
                    s_AuthoringToRuntimeBinding[authoringComponent] = lookup = new List<ReverseBinding>();
            }

            if (extractedProperties && lookup.Where(x => x.AuthoringFieldName == authoringField).ToArray().Length == 0)
                lookup.Add(new ReverseBinding() {
                    AuthoringFieldName = authoringField,
                    ComponentTypeIndex = typeIndex,
                    FieldProperties = fieldProps
                });
        }

        // Checks if a runtime type has any authoring bindings
        public static bool HasBindings(Type componentType)
        {
            if (!typeof(IComponentData).IsAssignableFrom(componentType))
                return false;

            return s_RuntimeFieldNames.ContainsKey(componentType);
        }

        // Return authoring binding for a given runtime type and field name
        public static (Type, string) GetBinding(Type type, string fieldName)
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(type));
            if (s_RuntimeToAuthoringFieldMap.TryGetValue((type, fieldName), out var authoringValue))
                return authoringValue;

            return (null, string.Empty);
        }

        internal static List<ReverseBinding> GetReverseBindings(Type authoringType)
        {
            Assert.IsTrue(typeof(Component).IsAssignableFrom(authoringType));

            if (s_AuthoringToRuntimeBinding.TryGetValue(authoringType, out var reverseBindings))
                return reverseBindings;
            return null;
        }


        // Get all registered fields of a runtime type
        public static HashSet<string> GetFields(Type type)
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(type));

            s_RuntimeFieldNames.TryGetValue(type, out var fieldSet);

            return fieldSet;
        }

        // Get runtime field size in bytes
        public static int GetFieldSize(Type type, string fieldName)
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(type));
            (Type, string) key = (type, fieldName);
            if (s_RuntimeFieldProperties.TryGetValue(key, out var fieldData))
                return fieldData.FieldSize;

            throw new InvalidOperationException(
                $"Field [{fieldName}] from component type [{type}] has not been registered.");
        }

        // Get runtime field offset in bytes
        public static int GetFieldOffset(Type type, string fieldName)
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(type));
            (Type, string) key = (type, fieldName);
            if (s_RuntimeFieldProperties.TryGetValue(key, out var fieldData))
                return fieldData.FieldOffset;

            throw new InvalidOperationException(
                $"Field [{fieldName}] from component type [{type}] has not been registered.");
        }

        static bool ExtractFieldProperties((Type, string) field, out RuntimeFieldProperties data)
        {
            data = default;

            Type type = field.Item1;

            if (!type.IsValueType)
                return false;

            foreach (var name in field.Item2.Split('.'))
            {
                var fieldInfo = type.GetField(name, k_FieldFlags);
                if (fieldInfo == null)
                    return false;

                data.FieldOffset += UnsafeUtility.GetFieldOffset(fieldInfo);
                type = fieldInfo.FieldType;
            }

            data.FieldSize = UnsafeUtility.SizeOf(type);
            data.FieldType = type;

            return true;
        }
    }
}

#endif
