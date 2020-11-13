using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
#if !NET_DOTS
using Unity.Properties;
#endif
using EntityOffsetInfo = Unity.Entities.TypeManager.EntityOffsetInfo;

namespace Unity.Entities
{
    public static unsafe class EntityRemapUtility
    {
        public struct EntityRemapInfo
        {
            public int SourceVersion;
            public Entity Target;
        }

        public static void GetTargets(out NativeArray<Entity> output, NativeArray<EntityRemapInfo> remapping)
        {
            NativeArray<Entity> temp = new NativeArray<Entity>(remapping.Length, Allocator.TempJob);
            var outputs = 0;
            for (var i = 0; i < remapping.Length; ++i)
                if (remapping[i].Target != Entity.Null)
                    temp[outputs++] = remapping[i].Target;
            output = new NativeArray<Entity>(outputs, Allocator.Persistent);
            UnsafeUtility.MemCpy(output.GetUnsafePtr(), temp.GetUnsafePtr(), sizeof(Entity) * outputs);
            temp.Dispose();
        }

        public static void AddEntityRemapping(ref NativeArray<EntityRemapInfo> remapping, Entity source, Entity target)
        {
            remapping[source.Index] = new EntityRemapInfo { SourceVersion = source.Version, Target = target };
        }

        public static Entity RemapEntity(ref NativeArray<EntityRemapInfo> remapping, Entity source)
        {
            return RemapEntity((EntityRemapInfo*)remapping.GetUnsafeReadOnlyPtr(), source);
        }

        public static Entity RemapEntity(EntityRemapInfo* remapping, Entity source)
        {
            if (source.Version == remapping[source.Index].SourceVersion)
                return remapping[source.Index].Target;
            else
            {
                // When moving whole worlds, we do not allow any references that aren't in the new world
                // to avoid any kind of accidental references
                return Entity.Null;
            }
        }

        public static Entity RemapEntityForPrefab(Entity* remapSrc, Entity* remapDst, int remappingCount, Entity source)
        {
            // When instantiating prefabs,
            // internal references are remapped.
            for (int i = 0; i != remappingCount; i++)
            {
                if (source == remapSrc[i])
                    return remapDst[i];
            }
            // And external references are kept.
            return source;
        }

        public struct EntityPatchInfo
        {
            public int Offset;
            public int Stride;
        }

        public struct BufferEntityPatchInfo
        {
            // Offset within chunk where first buffer header can be found
            public int BufferOffset;
            // Stride between adjacent buffers that need patching
            public int BufferStride;
            // Offset (from base pointer of array) where entities live
            public int ElementOffset;
            // Stride between adjacent buffer elements
            public int ElementStride;
        }

#if !UNITY_DOTSRUNTIME

        public static void CalculateEntityAndBlobOffsetsUnmanaged(Type type,
            out bool hasEntityRefs,
            out bool hasBlobRefs,
            ref NativeList<EntityOffsetInfo> entityOffsets,
            ref NativeList<EntityOffsetInfo> blobOffsets)
        {
            int entityOffsetsCount = entityOffsets.Length;
            int blobOffsetsCount = blobOffsets.Length;
            CalculateOffsetsRecurse(ref entityOffsets, ref blobOffsets, type, 0);

            hasEntityRefs = entityOffsets.Length != entityOffsetsCount;
            hasBlobRefs = blobOffsets.Length != blobOffsetsCount;
        }

        static void CalculateOffsetsRecurse(ref NativeList<EntityOffsetInfo> entityOffsets, ref NativeList<EntityOffsetInfo> blobOffsets, Type type, int baseOffset)
        {
            if (type == typeof(Entity))
            {
                entityOffsets.Add(new EntityOffsetInfo { Offset = baseOffset });
            }
            else if (type == typeof(BlobAssetReferenceData))
            {
                blobOffsets.Add(new EntityOffsetInfo { Offset = baseOffset });
            }
            else
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                foreach (var field in fields)
                {
                    if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive)
                        CalculateOffsetsRecurse(ref entityOffsets, ref blobOffsets, field.FieldType, baseOffset + UnsafeUtility.GetFieldOffset(field));
                }
            }
        }

        public static void HasEntityReferencesManaged(Type type, out bool hasEntityReferences, out bool hasBlobReferences)
        {
            hasEntityReferences = false;
            hasBlobReferences = false;

            ProcessEntityOrBlobReferencesRecursiveManaged(type, ref hasEntityReferences, ref hasBlobReferences, 0);
        }


        static bool ProcessEntityOrBlobReferencesRecursiveManaged(Type type, ref bool hasEntityReferences, ref bool hasBlobReferences, int depth)
        {
            // Avoid deep / infinite recursion
            if (depth > 10)
                return true;

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var field in fields)
            {
                var fieldType = field.FieldType;
                if (fieldType.IsArray)
                    fieldType = fieldType.GetElementType();

                if (fieldType.IsPrimitive)
                { }
                else if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
                { }
                else if (fieldType == typeof(Entity))
                {
                    hasEntityReferences = true;
                    if (hasBlobReferences && hasEntityReferences)
                        return true;
                }
                else if (type == typeof(BlobAssetReferenceData))
                {
                    hasBlobReferences = true;
                    if (hasBlobReferences && hasEntityReferences)
                        return true;
                }
                else if (fieldType.IsValueType || fieldType.IsSealed)
                {
                    if (ProcessEntityOrBlobReferencesRecursiveManaged(fieldType, ref hasEntityReferences, ref hasBlobReferences, depth + 1))
                        return true;
                }
                // It is not possible to determine if there are entity references in a polymorphic non-sealed class type
                else
                {
                    hasEntityReferences = true;
                    hasBlobReferences = true;
                    return true;
                }
            }

            return false;
        }

#endif

        public static EntityPatchInfo* AppendEntityPatches(EntityPatchInfo* patches, EntityOffsetInfo* offsets, int offsetCount, int baseOffset, int stride)
        {
            if (offsets == null)
                return patches;

            for (int i = 0; i < offsetCount; i++)
                patches[i] = new EntityPatchInfo { Offset = baseOffset + offsets[i].Offset, Stride = stride };
            return patches + offsetCount;
        }

        public static BufferEntityPatchInfo* AppendBufferEntityPatches(BufferEntityPatchInfo* patches, EntityOffsetInfo* offsets, int offsetCount, int bufferBaseOffset, int bufferStride, int elementStride)
        {
            if (offsets == null)
                return patches;

            for (int i = 0; i < offsetCount; i++)
            {
                patches[i] = new BufferEntityPatchInfo
                {
                    BufferOffset = bufferBaseOffset,
                    BufferStride = bufferStride,
                    ElementOffset = offsets[i].Offset,
                    ElementStride = elementStride,
                };
            }

            return patches + offsetCount;
        }

        public static void PatchEntities(EntityPatchInfo* scalarPatches, int scalarPatchCount,
            BufferEntityPatchInfo* bufferPatches, int bufferPatchCount,
            byte* chunkBuffer, int entityCount, ref NativeArray<EntityRemapInfo> remapping)
        {
            // Patch scalars (single components) with entity references.
            for (int p = 0; p < scalarPatchCount; p++)
            {
                byte* entityData = chunkBuffer + scalarPatches[p].Offset;
                for (int i = 0; i != entityCount; i++)
                {
                    Entity* entity = (Entity*)entityData;
                    *entity = RemapEntity(ref remapping, *entity);
                    entityData += scalarPatches[p].Stride;
                }
            }

            // Patch buffers that contain entity references
            for (int p = 0; p < bufferPatchCount; ++p)
            {
                byte* bufferData = chunkBuffer + bufferPatches[p].BufferOffset;

                for (int i = 0; i != entityCount; ++i)
                {
                    BufferHeader* header = (BufferHeader*)bufferData;

                    byte* elemsBase = BufferHeader.GetElementPointer(header) + bufferPatches[p].ElementOffset;
                    int elemCount = header->Length;

                    for (int k = 0; k != elemCount; ++k)
                    {
                        Entity* entityPtr = (Entity*)elemsBase;
                        *entityPtr = RemapEntity(ref remapping, *entityPtr);
                        elemsBase += bufferPatches[p].ElementStride;
                    }

                    bufferData += bufferPatches[p].BufferStride;
                }
            }
        }

        public static void PatchEntitiesForPrefab(EntityPatchInfo* scalarPatches, int scalarPatchCount,
            BufferEntityPatchInfo* bufferPatches, int bufferPatchCount,
            byte* chunkBuffer, int indexInChunk, int entityCount, Entity* remapSrc, Entity* remapDst, int remappingCount)
        {
            // Patch scalars (single components) with entity references.
            for (int p = 0; p < scalarPatchCount; p++)
            {
                byte* entityData = chunkBuffer + scalarPatches[p].Offset;
                for (int e = 0; e != entityCount; e++)
                {
                    Entity* entity = (Entity*)(entityData + scalarPatches[p].Stride * (e + indexInChunk));
                    *entity = RemapEntityForPrefab(remapSrc, remapDst + e * remappingCount, remappingCount, *entity);
                }
            }

            // Patch buffers that contain entity references
            for (int p = 0; p < bufferPatchCount; ++p)
            {
                byte* bufferData = chunkBuffer + bufferPatches[p].BufferOffset;

                for (int e = 0; e != entityCount; e++)
                {
                    BufferHeader* header = (BufferHeader*)(bufferData + bufferPatches[p].BufferStride * (e + indexInChunk));

                    byte* elemsBase = BufferHeader.GetElementPointer(header) + bufferPatches[p].ElementOffset;
                    int elemCount = header->Length;

                    for (int k = 0; k != elemCount; ++k)
                    {
                        Entity* entityPtr = (Entity*)elemsBase;
                        *entityPtr = RemapEntityForPrefab(remapSrc, remapDst + e * remappingCount, remappingCount, *entityPtr);
                        elemsBase += bufferPatches[p].ElementStride;
                    }
                }
            }
        }
    }
}
