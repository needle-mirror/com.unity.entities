using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
#if UNITY_DOTSRUNTIME
    /// <summary>
    /// GenerateComponentFieldInfo registers which component type should have field information generated during compilation.
    /// A NativeArray&lt;FieldInfo&gt; can be retrieved for this component (and, implicitly, its member types) via calls to
    /// GetFieldInfos(typeof(SomeType)).
    /// Example usage:
    /// [assembly: GenerateComponentFieldInfo(typeof(MyComponent))]
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class GenerateComponentFieldInfoAttribute : Attribute
    {
        public Type ComponentType;

        public GenerateComponentFieldInfoAttribute(Type type)
        {
            ComponentType = type;
        }
    }
#endif

    public static unsafe partial class TypeManager
    {
#if UNITY_DOTSRUNTIME
        static Dictionary<Type, TypeRegistry.FieldInfoLookup> s_TypeToFieldInfosMap;
        static List<Type> s_FieldTypes;
        static List<string> s_FieldNames;
        static NativeList<FieldInfo> s_FieldInfos;

        static void InitializeFieldInfoState()
        {
            s_FieldInfos = new NativeList<FieldInfo>(Allocator.Persistent);
            s_FieldTypes = new List<Type>();
            s_FieldNames = new List<string>();
            s_TypeToFieldInfosMap = new Dictionary<Type, TypeRegistry.FieldInfoLookup>();
        }

        static void ShutdownFieldInfoState()
        {
            s_TypeToFieldInfosMap.Clear();
            s_FieldNames.Clear();
            s_FieldTypes.Clear();
            s_FieldInfos.Dispose();
        }

        public struct FieldInfo
        {
            public FieldInfo(int offset, int fieldTypeIndex, int fieldNameIndex)
            {
                Offset = offset;
                FieldTypeIndex = fieldTypeIndex;
                FieldNameIndex = fieldNameIndex;
            }

            public int Offset;
            internal int FieldTypeIndex;
            internal int FieldNameIndex;

            public Type FieldType => s_FieldTypes[FieldTypeIndex];
            public string FieldName => s_FieldNames[FieldNameIndex];
        }

        /// <summary>
        /// Gets an array of FieldInfo for a given type, if it has field information 
        /// generated via the <see cref="GenerateComponentFieldInfo"/> attribute.
        /// </summary>
        /// <remarks>
        /// You can use the Types from the returned NativeArray's FieldInfo element's 
        /// FieldType property to call this method recursively.
        /// </remarks>
        /// <param name="type">The type to get FieldInfo for.</param>
        /// <returns>Returns a NativeArray of FieldInfo.</returns>
        public static NativeArray<FieldInfo> GetFieldInfos(Type type)
        {
            if (!s_TypeToFieldInfosMap.TryGetValue(type, out var lookup))
                throw new ArgumentException($"'{type}' is not a Component type or a nested field type of a component. We only generate FieldInfo for Components and their fields if the component was registered using [GenerateComponentFieldInfo].");

            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<FieldInfo>((FieldInfo*)s_FieldInfos.GetUnsafeReadOnlyPtr() + lookup.Index, lookup.Count, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // This handle isn't correct but collections makes this way more difficult than this needs to be
            // and we know s_TypeInfos has the same lifetime and readonly requirement as the fieldinfos
            var handle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(s_TypeInfos);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, handle);
#endif
            return array;
        }
#else
        static void InitializeFieldInfoState()
        {
        }

        static void ShutdownFieldInfoState()
        {
        }
#endif
    }
}
