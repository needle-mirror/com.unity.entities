using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using EntityOffsetInfo = Unity.Entities.TypeManager.EntityOffsetInfo;

namespace Unity.Entities
{
    internal static unsafe class EntityRemapUtility
    {
        public struct EntityRemapInfo
        {
            public int SourceVersion;
            public Entity Target;
        }

        public static void AddEntityRemapping(ref NativeArray<EntityRemapInfo> remapping, Entity source, Entity target)
        {
            remapping[source.Index] = new EntityRemapInfo { SourceVersion = source.Version, Target = target };
        }

        public static Entity RemapEntity(ref NativeArray<EntityRemapInfo> remapping, Entity source)
        {
            if (source.Version == remapping[source.Index].SourceVersion)
                return remapping[source.Index].Target;
            else
                return Entity.Null;
        }

        public struct EntityPatchInfo
        {
            public int Offset;
            public int Stride;
        }

        public static EntityOffsetInfo[] CalculateEntityOffsets(Type type)
        {
            var offsets = new List<EntityOffsetInfo>();
            CalculateEntityOffsetsRecurse(ref offsets, type, 0);
            if (offsets.Count > 0)
                return offsets.ToArray();
            else
                return null;
        }

        static void CalculateEntityOffsetsRecurse(ref List<EntityOffsetInfo> offsets, Type type, int baseOffset)
        {
            if (type == typeof(Entity))
            {
                offsets.Add(new EntityOffsetInfo { Offset = baseOffset });
            }
            else
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                foreach (var field in fields)
                {
                    if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive)
                        CalculateEntityOffsetsRecurse(ref offsets, field.FieldType, baseOffset + UnsafeUtility.GetFieldOffset(field));
                }
            }
        }

        public static void AppendEntityPatches(ref NativeList<EntityPatchInfo> patches, EntityOffsetInfo[] offsets, int baseOffset, int stride)
        {
            if (offsets == null)
                return;

            for (int i = 0; i < offsets.Length; i++)
                patches.Add(new EntityPatchInfo { Offset = baseOffset + offsets[i].Offset, Stride = stride });
        }

        public static void PatchEntities(ref NativeList<EntityPatchInfo> patches, byte* data, int count, ref NativeArray<EntityRemapInfo> remapping)
        {
            for (int i = 0; i < patches.Length; i++)
            {
                byte* entityData = data + patches[i].Offset;
                for (int j = 0; j != count; j++)
                {
                    Entity* entity = (Entity*)entityData;
                    *entity = RemapEntity(ref remapping, *entity);
                    entityData += patches[i].Stride;
                }
            }
        }
    }
}
