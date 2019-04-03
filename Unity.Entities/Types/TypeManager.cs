using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.Entities
{
    public static unsafe class TypeManager
    {
        public enum TypeCategory
        {
            ComponentData,
            ISharedComponentData,
            OtherValueType,
            EntityData,
            Class
        }

        public const int MaximumTypesCount = 1024 * 10;
        private static ComponentType[] s_Types;
        private static volatile int s_Count;
        private static SpinLock s_CreateTypeLock;
        public static int ObjectOffset;
        internal static readonly Type UnityEngineComponentType = typeof(Component);

        private struct StaticTypeLookup<T>
        {
            public static int typeIndex;
        }

        public struct EntityOffsetInfo
        {
            public int Offset;
        }

        public struct ComponentType
        {
            public ComponentType(Type type, int size, TypeCategory category, FastEquality.Layout[] layout, EntityOffsetInfo[] entityOffsets, UInt64 memoryOrdering)
            {
                Type = type;
                SizeInChunk = size;
                Category = category;
                FastEqualityLayout = layout;
                EntityOffsets = entityOffsets;
                MemoryOrdering = memoryOrdering;
            }

            public readonly Type Type;
            public readonly int SizeInChunk;
            public readonly FastEquality.Layout[] FastEqualityLayout;
            public readonly TypeCategory Category;
            public readonly EntityOffsetInfo[] EntityOffsets;
            public readonly UInt64 MemoryOrdering;
        }

        // TODO: this creates a dependency on UnityEngine, but makes splitting code in separate assemblies easier. We need to remove it during the biggere refactor.
        private struct ObjectOffsetType
        {
#pragma warning disable 0169 // "never used" warning
            private void* v0;
            private void* v1;
#pragma warning restore 0169
        }

        public static void Initialize()
        {
            if (s_Types != null)
                return;

            ObjectOffset = UnsafeUtility.SizeOf<ObjectOffsetType>();
            s_CreateTypeLock = new SpinLock();
            s_Types = new ComponentType[MaximumTypesCount];
            s_Count = 0;

            s_Types[s_Count++] = new ComponentType(null, 0, TypeCategory.ComponentData, null, null, 0);
            // This must always be first so that Entity is always index 0 in the archetype
            s_Types[s_Count++] = new ComponentType(typeof(Entity), sizeof(Entity), TypeCategory.EntityData,
			    FastEquality.CreateLayout(typeof(Entity)), EntityRemapUtility.CalculateEntityOffsets(typeof(Entity)), 0);
        }


        public static int GetTypeIndex<T>()
        {
            var typeIndex = StaticTypeLookup<T>.typeIndex;
            if (typeIndex != 0)
                return typeIndex;

            typeIndex = GetTypeIndex(typeof(T));
            StaticTypeLookup<T>.typeIndex = typeIndex;
            return typeIndex;
        }

        public static int GetTypeIndex(Type type)
        {
            var index = FindTypeIndex(type, s_Count);
            return index != -1 ? index : CreateTypeIndexThreadSafe(type);
        }

        private static int FindTypeIndex(Type type, int count)
        {
            for (var i = 0; i != count; i++)
            {
                var c = s_Types[i];
                if (c.Type == type)
                    return i;
            }

            return -1;
        }

#if UNITY_EDITOR
        public static int TypesCount => s_Count;

        public static IEnumerable<ComponentType> AllTypes()
        {
            return Enumerable.Take(s_Types, s_Count);
        }
#endif //UNITY_EDITOR

        private static int CreateTypeIndexThreadSafe(Type type)
        {
            var lockTaken = false;
            try
            {
                s_CreateTypeLock.Enter(ref lockTaken);

                // After taking the lock, make sure the type hasn't been created
                // after doing the non-atomic FindTypeIndex
                var index = FindTypeIndex(type, s_Count);
                if (index != -1)
                    return index;

                var componentType = BuildComponentType(type);

                index = s_Count++;
                s_Types[index] = componentType;

                return index;
            }
            finally
            {
                if (lockTaken)
                    s_CreateTypeLock.Exit(true);
            }
        }

        static UInt64 CalculateMemoryOrdering(Type type)
        {
            if (type == typeof(Entity))
                return 0;

            var hash = new System.Security.Cryptography.SHA1Managed().ComputeHash(System.Text.Encoding.UTF8.GetBytes(type.AssemblyQualifiedName));
            var hash64 = new byte[8];
            Array.Copy(hash, 0, hash64, 0, 8);

            UInt64 result = 0;
            for (int i = 0; i < 8; ++i)
            {
                result = result * 256 + hash64[i];
            }
            return (result != 0) ? result : 1;
        }

        private static ComponentType BuildComponentType(Type type)
        {
            var componentSize = 0;
            TypeCategory category;
            FastEquality.Layout[] fastEqualityLayout = null;
            EntityOffsetInfo[] entityOffsets = null;
            var memoryOrdering = CalculateMemoryOrdering(type);
            if (typeof(IComponentData).IsAssignableFrom(type))
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (type.IsClass)
                    throw new ArgumentException($"{type} is an IComponentData, and thus must be a struct.");
                if (!UnsafeUtility.IsBlittable(type))
                    throw new ArgumentException(
                        $"{type} is an IComponentData, and thus must be blittable (No managed object is allowed on the struct).");
#endif

                category = TypeCategory.ComponentData;
                componentSize = UnsafeUtility.SizeOf(type);
                fastEqualityLayout = FastEquality.CreateLayout(type);
                entityOffsets = EntityRemapUtility.CalculateEntityOffsets(type);
             }
            else if (typeof(ISharedComponentData).IsAssignableFrom(type))
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (type.IsClass)
                    throw new ArgumentException($"{type} is an ISharedComponentData, and thus must be a struct.");
#endif

                category = TypeCategory.ISharedComponentData;
                fastEqualityLayout = FastEquality.CreateLayout(type);
            }
            else if (type.IsValueType)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!UnsafeUtility.IsBlittable(type))
                    throw new ArgumentException($"{type} is used for FixedArrays, and thus must be blittable.");
#endif
                category = TypeCategory.OtherValueType;
                componentSize = UnsafeUtility.SizeOf(type);
            }
            else if (type.IsClass)
            {
                category = TypeCategory.Class;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (type.FullName == "Unity.Entities.GameObjectEntity")
                    throw new ArgumentException(
                        "GameObjectEntity can not be used from EntityManager. The component is ignored when creating entities for a GameObject.");
#endif
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            else
            {
                throw new ArgumentException($"'{type}' is not a valid component");
            }
#else
            else
            {
                category = TypeCategory.OtherValueType;
            }
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (typeof(IComponentData).IsAssignableFrom(type) && typeof(ISharedComponentData).IsAssignableFrom(type))
                throw new ArgumentException($"Component {type} can not be both IComponentData & ISharedComponentData");
#endif
            return new ComponentType(type, componentSize, category, fastEqualityLayout, entityOffsets, memoryOrdering);
        }

        public static bool IsValidComponentTypeForArchetype(int typeIndex, bool isArray)
        {
            if (s_Types[typeIndex].Category == TypeCategory.OtherValueType)
                return isArray;
            return !isArray;
        }

        public static ComponentType GetComponentType(int typeIndex)
        {
            return s_Types[typeIndex];
        }

        public static ComponentType GetComponentType<T>()
        {
            return s_Types[GetTypeIndex<T>()];
        }

        public static Type GetType(int typeIndex)
        {
            return s_Types[typeIndex].Type;
        }

        public static int GetTypeCount()
        {
            return s_Count;
        }
    }
}
