#if !NET_DOTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Burst;
using UnityEngine.Assertions;

namespace Unity.Entities
{
    public class TypeHash
    {
        // http://www.isthe.com/chongo/src/fnv/hash_64a.c
        // with basis and prime:
        const ulong kFNV1A64OffsetBasis = 14695981039346656037;
        const ulong kFNV1A64Prime = 1099511628211;

        public static ulong FNV1A64(string text)
        {
            ulong result = kFNV1A64OffsetBasis;
            foreach (var c in text)
            {
                result = kFNV1A64Prime * (result ^ (byte)(c & 255));
                result = kFNV1A64Prime * (result ^ (byte)(c >> 8));
            }
            return result;
        }

        [BurstCompile]
        public static ulong FNV1A64(int val)
        {
            ulong result = kFNV1A64OffsetBasis;
            unchecked
            {
                result = (((ulong)(val & 0x000000FF) >>  0) ^ result) * kFNV1A64Prime;
                result = (((ulong)(val & 0x0000FF00) >>  8) ^ result) * kFNV1A64Prime;
                result = (((ulong)(val & 0x00FF0000) >> 16) ^ result) * kFNV1A64Prime;
                result = (((ulong)(val & 0xFF000000) >> 24) ^ result) * kFNV1A64Prime;
            }

            return result;
        }

        [BurstCompile]
        public static ulong CombineFNV1A64(ulong hash, params ulong[] values)
        {
            foreach (var value in values)
            {
                hash ^= value;
                hash *= kFNV1A64Prime;
            }

            return hash;
        }

        // Todo: Remove this. DOTS Runtime currently doesn't conform to these system types so don't inspect their fields
        private static readonly Type[] WorkaroundTypes = new Type[] { typeof(System.Guid) };

        private static ulong HashType(Type type, Dictionary<Type, ulong> cache)
        {
            var hash = HashTypeName(type);

#if !UNITY_DOTSRUNTIME
            // UnityEngine objects have their own serialization mechanism so exclude hashing the type's
            // internals and just hash its name which is stable and important to how Entities will serialize
            if (TypeManager.UnityEngineObjectType?.IsAssignableFrom(type) == true)
                return hash;
#endif

            if (type.IsGenericParameter || type.IsArray || type.IsPointer || type.IsPrimitive || type.IsEnum || WorkaroundTypes.Contains(type))
                return hash;

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (!field.IsStatic) // statics have no effect on data layout
                {
                    var fieldType = field.FieldType;

                    if (!cache.TryGetValue(fieldType, out ulong fieldTypeHash))
                    {
                        // Classes can have cyclical type definitions so to prevent a potential stackoverflow
                        // we make all future occurence of fieldType resolve to the hash of its field type name
                        cache.Add(fieldType, HashTypeName(fieldType));
                        fieldTypeHash = HashType(fieldType, cache);
                        cache[fieldType] = fieldTypeHash;
                    }

                    var fieldOffsetAttrs = field.GetCustomAttributes(typeof(FieldOffsetAttribute));
                    if (fieldOffsetAttrs.Any())
                    {
                        var offset = ((FieldOffsetAttribute)fieldOffsetAttrs.First()).Value;
                        hash = CombineFNV1A64(hash, (ulong)offset);
                    }

                    hash = CombineFNV1A64(hash, fieldTypeHash);
                }
            }

            // TODO: Enable this. Currently IL2CPP gives totally inconsistent results to Mono.
            /*
            if (type.StructLayoutAttribute != null && !type.StructLayoutAttribute.IsDefaultAttribute())
            {
                var explicitSize = type.StructLayoutAttribute.Size;
                if (explicitSize > 0)
                    hash = CombineFNV1A64(hash, (ulong)explicitSize);

                // Todo: Enable this. We cannot support Pack at the moment since a type's Packing will
                // change based on its field's explicit packing which will fail for Tiny mscorlib
                // as it's not in sync with dotnet
                // var packingSize = type.StructLayoutAttribute.Pack;
                // if (packingSize > 0)
                //     hash = CombineFNV1A64(hash, (ulong)packingSize);
            }
            */

            return hash;
        }

        private static ulong HashVersionAttribute(Type type)
        {
            int version = 0;
            if (type.CustomAttributes.Any())
            {
                var versionAttribute = type.CustomAttributes.FirstOrDefault(ca => ca.Constructor.DeclaringType.Name == "TypeVersionAttribute");
                if (versionAttribute != null)
                {
                    version = (int)versionAttribute.ConstructorArguments
                        .First(arg => arg.ArgumentType.Name == "Int32")
                        .Value;
                }
            }

            return FNV1A64(version);
        }

        private static ulong HashNamespace(Type type)
        {
            var hash = kFNV1A64OffsetBasis;

            // System.Reflection and Cecil don't report namespaces the same way so do an alternative:
            // Find the namespace of an un-nested parent type, then hash each of the nested children names
            if (type.IsNested)
            {
                hash = CombineFNV1A64(hash, HashNamespace(type.DeclaringType));
                hash = CombineFNV1A64(hash, FNV1A64(type.DeclaringType.Name));
            }
            else if (!string.IsNullOrEmpty(type.Namespace))
                hash = CombineFNV1A64(hash, FNV1A64(type.Namespace));

            return hash;
        }

        private static ulong HashTypeName(Type type)
        {
            ulong hash = HashNamespace(type);
            hash = CombineFNV1A64(hash, FNV1A64(type.Name));
            foreach (var ga in type.GenericTypeArguments)
            {
                Assert.IsTrue(!ga.IsGenericParameter);
                hash = CombineFNV1A64(hash, HashTypeName(ga));
            }

            return hash;
        }

        // Our StableTypeHashes purpose is to provide a version linking serialized component data to its runtime representation
        // As such the type hash has a few requirements:
        // R0 - TypeHashes apply to types serialized by Unity.Entities. The internals of a UnityEngine.Object reference
        //      contained in a component must not have an effect on the type hash as the internals of the
        //      UnityEngine.Object reference are fine to change safely since they are serialized outside of Unity.Entities
        // R1 - Types with the same data layout, but are still different types, should have different hashes
        // R2 - If a type's data layout changes, so should the type hash. This includes:
        //      - Nested field's data layout changes (e.g. new member added)
        //      - FieldOffsets, explicit size, or pack alignment are changed
        //      - Different types of the same width swap places (e.g. uint <-> int)
        //      - NOTE: we cannot detect if fields of the same type swap (e.g. mInt1 <-> mInt2) This would be a semantic
        //        difference which the user should increase their component [TypeVersion(1)] attribute. We explicitly do
        //        not want to try to handle this case by hashing field names, as users do not control all field names,
        //        and field names have no effect on how we serialize and should not effect our hashes.
        // R3 - The hash should be user versioned should the semantics of a type change, but the data layout is unchanged
        // R4 - DOTS Runtime will rely on hashes generated from the editor (used in serialized data) and hashes generated
        //      during compilation. These hashes must match. This rule exists due to the non-obvious gotchas:
        //      - DOTS Runtime will swap out assemblies for 'tiny' versions, this means any hashing using the AssemblyName
        //        should be avoided or handled specially for the known swapped assemblies.
        //        - Of course this means Type.AssemblyQualifiedName should be avoided in hashes, but as well, closed-form
        //          generic types will include the assembly qualified name for GenericArguments in Type.FullName which will
        //          cause issues. e.g. typeof(ComponentWithGeneric.GenericField).FullName ==
        //          Unity.Entities.ComponentWithGeneric.GenericField`1[[System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]
        //      - System.Reflection and Mono.Cecil will provide different Type names (they use a different format).
        //        Generating hashes from System.Reflection and to match hashes using Mono.Cecil must account for this difference
        public static ulong CalculateStableTypeHash(Type type)
        {
            ulong versionHash = HashVersionAttribute(type);
            ulong typeHash = HashType(type, new Dictionary<Type, ulong>());

            return CombineFNV1A64(versionHash, typeHash);
        }

        public static ulong CalculateMemoryOrdering(Type type)
        {
            if (type == null || type.FullName == "Unity.Entities.Entity")
            {
                return 0;
            }

            if (type.CustomAttributes.Any())
            {
                var forcedMemoryOrderAttribute = type.CustomAttributes.FirstOrDefault(ca => ca.Constructor.DeclaringType.Name == "ForcedMemoryOrderingAttribute");
                if (forcedMemoryOrderAttribute != null)
                {
                    ulong memoryOrder = (ulong)forcedMemoryOrderAttribute.ConstructorArguments
                        .First(arg => arg.ArgumentType.Name == "UInt64" || arg.ArgumentType.Name == "ulong")
                        .Value;

                    return memoryOrder;
                }
            }

            return CalculateStableTypeHash(type);
        }
    }
}
#endif
