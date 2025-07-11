using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Unity.Cecil.Awesome;
using Unity.Cecil.Awesome.Comparers;
using Unity.Entities.BuildUtils;
using static Unity.Entities.BuildUtils.MonoExtensions;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Unity.Entities.CodeGen
{
    public class TypeUtils
    {
        public struct AlignAndSize
        {
            public readonly int align;
            public readonly int size;
            public readonly int offset;
            public readonly bool empty;

            public AlignAndSize(int single)
            {
                align = size = single;
                offset = 0;
                empty = false;
            }

            public AlignAndSize(int a, int s)
            {
                align = a;
                size = s;
                offset = 0;
                empty = false;
            }

            public AlignAndSize(int a, int s, int o)
            {
                align = a;
                size = s;
                offset = o;
                empty = false;
            }

            public AlignAndSize(int a, int s, int o, bool e)
            {
                align = a;
                size = s;
                offset = o;
                empty = e;
            }

            public static readonly AlignAndSize Zero = new AlignAndSize(0);
            public static readonly AlignAndSize One = new AlignAndSize(1);
            public static readonly AlignAndSize Two = new AlignAndSize(2);
            public static readonly AlignAndSize Four = new AlignAndSize(4);
            public static readonly AlignAndSize Eight = new AlignAndSize(8);
            public static readonly AlignAndSize Pointer2_32 = new AlignAndSize(4, 8);
            public static readonly AlignAndSize Pointer2_64 = new AlignAndSize(8, 16);
            public static readonly AlignAndSize Pointer3_32 = new AlignAndSize(4, 12);
            public static readonly AlignAndSize Pointer3_64 = new AlignAndSize(8, 24);
            public static readonly AlignAndSize Pointer4_32 = new AlignAndSize(4, 16);
            public static readonly AlignAndSize Pointer4_64 = new AlignAndSize(8, 32);
            public static readonly AlignAndSize Sentinel = new AlignAndSize(-1);

            public static AlignAndSize Pointer(int bits) => (bits == 32 || bits == 0) ? Four : Eight;
            public static AlignAndSize DynamicArray(int bits) => (bits == 32 || bits == 0) ? new AlignAndSize(4, 12) : new AlignAndSize(8, 16);
            public static AlignAndSize NativeString(int bits) => (bits == 32 || bits == 0) ? new AlignAndSize(4, 8) : new AlignAndSize(8, 16); // 64-bit has 4 bytes of wasted space to make the alignment work

            public bool IsSentinel => size == -1;

            public override string ToString()
            {
                return String.Format("[{0};{1}]", align, size);
            }
        }

        private Dictionary<TypeReference, AlignAndSize>[] ValueTypeAlignment =
        {
            new Dictionary<TypeReference, AlignAndSize>(new TypeReferenceEqualityComparer()), new Dictionary<TypeReference, AlignAndSize>(new TypeReferenceEqualityComparer())
        };

        private Dictionary<FieldReference, AlignAndSize>[] StructFieldAlignment =
        {
            new Dictionary<FieldReference, AlignAndSize>(new FieldReferenceComparer()), new Dictionary<FieldReference, AlignAndSize>(new FieldReferenceComparer())
        };

        internal Dictionary<TypeReference, bool>[] ValueTypeIsComplex =
        {
            new Dictionary<TypeReference, bool>(new TypeReferenceEqualityComparer()), new Dictionary<TypeReference, bool>(new TypeReferenceEqualityComparer())
        };

        public static AlignAndSize AlignAndSizeOfType(MetadataType mtype, int bits)
        {
            if (mtype == MetadataType.Boolean || mtype == MetadataType.Byte || mtype == MetadataType.SByte) return AlignAndSize.One;
            if (mtype == MetadataType.Int16 || mtype == MetadataType.UInt16 || mtype == MetadataType.Char) return AlignAndSize.Two;
            if (mtype == MetadataType.Int32 || mtype == MetadataType.UInt32 || mtype == MetadataType.Single) return AlignAndSize.Four;
            if (mtype == MetadataType.Int64 || mtype == MetadataType.UInt64 || mtype == MetadataType.Double) return AlignAndSize.Eight;
            if (mtype == MetadataType.IntPtr || mtype == MetadataType.UIntPtr) return AlignAndSize.Pointer(bits);
            if (mtype == MetadataType.String) return AlignAndSize.NativeString(bits);

            throw new ArgumentException($"Metadata type {mtype} is a special type which is not supported");
        }


        /*
         * Make sure exceptions refer to the top-level component type in error messages
         */
        public AlignAndSize AlignAndSizeOfType(TypeReference typeref, int bits)
        {
            try
            {
                return AlignAndSizeOfTypeInternal(typeref, bits, 0);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Caught exception while processing type {typeref.FullName}: {e.Message}");
            }
        }

        public AlignAndSize AlignAndSizeOfTypeInternal(TypeReference typeRef, int bits, int depth)
        {
            //rely on outer function to add type context
            if (depth > 100)
                throw new InvalidOperationException("Recursive depth limit exceeded.");

            depth++;

            // This is a gross hack and i'm not proud of it; we use bits as an array index,
            // and we call this method recursively.
            if (bits == 32) bits = 0;
            else if (bits == 64) bits = 1;

            if (typeRef.IsPointer)
                return AlignAndSize.Pointer(bits);

            TypeDefinition fixedSpecialType = typeRef.FixedSpecialType();
            if (fixedSpecialType != null)
            {
                return AlignAndSizeOfType(fixedSpecialType.MetadataType, bits);
            }

            var type = typeRef.Resolve();

            // Handle the case where we have a fixed buffer. Cecil will name it: "<MyMemberName>e_FixedBuffer"
            if (type.ClassSize != -1 && typeRef.Name.Contains(">e__FixedBuffer"))
            {
                // Fixed buffers can only be of primitive types so inspect the fields of the buffer (there should only be one)
                // and determine the packing requirement for the type
                if (type.Fields.Count() != 1)
                    throw new ArgumentException("A FixedBuffer type contains more than one field, this should not happen");

                var fieldAlignAndSize = AlignAndSizeOfType(type.Fields[0].FieldType.MetadataType, bits);
                return new AlignAndSize(fieldAlignAndSize.align, type.ClassSize);
            }
            else if (type.IsExplicitLayout && type.ClassSize > 0 && type.PackingSize > 0)
            {
                return new AlignAndSize(type.PackingSize, type.ClassSize);
            }

            if (ValueTypeAlignment[bits].ContainsKey(typeRef))
            {
                var sz = ValueTypeAlignment[bits][typeRef];

                if (sz.IsSentinel)
                    throw new ArgumentException($"Type {typeRef} triggered sentinel; recursive value type definition");

                return sz;
            }

            if (type.IsArray)
            {
                throw new ArgumentException($"Can't represent {typeRef}: C# array types cannot be represented directly, use DynamicArray<T>");
            }

            if (IsDynamicArray(typeRef))
            {
                var elementType = GetDynamicArrayElementType(typeRef);
                //Console.WriteLine($"{nts.TypeArguments[0]}");
                // call this just for the side effect checks
                if (AlignAndSizeOfTypeInternal(elementType, bits, depth).size == 0)
                    throw new Exception("Unexpected type with size 0: " + elementType);

                // arrays match std::array, for emscripten at least
                return AlignAndSize.DynamicArray(bits);
            }

            if (type.IsEnum)
            {
                // Inspect the __value member to determine the underlying type size
                var enumBaseType = type.Fields.First(f => f.Name == "value__").FieldType;
                return AlignAndSizeOfTypeInternal(enumBaseType, bits, depth);
            }

            if (!type.IsValueType())
            {
                // Why not throw? Really should expect this:
                //throw new ArgumentException($"Type {type} ({type.Name}) was expected to be a value type");
                // However, the DisposeSentinel is a ManagedType that sits in the NativeContainers.
                // Treat it as an opaque pointer and skip over it.
                return AlignAndSizeOfType(MetadataType.IntPtr, bits);
            }

            if (TypeReferenceEqualityComparer.AreEqual(typeRef, runnerOfMe._System_DecimalDef))
                throw new InvalidOperationException("Decimal types in unmanaged component data are not supported.");

            if (!ValueTypeAlignment[bits].ContainsKey(typeRef))
            {
                ValueTypeAlignment[bits].Add(typeRef, AlignAndSize.Sentinel);
                PreprocessTypeFields(typeRef, bits, depth);
            }

            return ValueTypeAlignment[bits][typeRef];
        }

        public AlignAndSize AlignAndSizeOfField(FieldReference fieldRef, int bits)
        {
            if (bits == 32) bits = 0;
            else if (bits == 64) bits = 1;

            if (!StructFieldAlignment[bits].ContainsKey(fieldRef))
            {
                PreprocessTypeFields(fieldRef.DeclaringType, bits, 0);
            }
            return StructFieldAlignment[bits][fieldRef];
        }

        public static int AlignUp(int sz, int align)
        {
            if (align == 0)
                return sz;
            int k = (sz + align - 1);
            return k - k % align;
        }

        public static bool HasNestedDynamicArrayType(TypeReference type)
        {
            if (type.IsPrimitive || type.Resolve().IsEnum || type.MetadataType == MetadataType.String) return false;
            if (IsDynamicArray(type)) return true;

            foreach (var field in type.Resolve().Fields)
            {
                if (field.IsNotSerialized) continue;
                if (HasNestedDynamicArrayType(field.FieldType))
                    return true;
            }

            return false;
        }

        internal EntitiesILPostProcessors runnerOfMe;

        public static bool IsEntityType(TypeReference typeRef)
        {
            return (typeRef.FullName == "Unity.Entities.Entity");
        }


        public static bool IsBlobAssetReferenceType(TypeReference typeRef)
        {
            //XXX THIS IS WRONG USE CECIL AWESOME
            return typeRef.Name == "BlobAssetReferenceData" && typeRef.Namespace == "Unity.Entities";
        }

        public static bool IsDynamicArray(TypeReference type)
        {
            //wrong, use cecil awesome
            return type.Name.StartsWith("DynamicArray`", StringComparison.Ordinal);
        }

        public static bool IsComponentType(TypeReference typeRef)
        {
            if (!typeRef.IsValueType || typeRef.IsPrimitive)
                return false;

            return typeRef.Resolve().Interfaces.Any(i => i.InterfaceType.Name == "IComponentData") ||
                IsSharedComponentType(typeRef) ||
                IsCleanupComponentData(typeRef) ||
                IsBufferElementComponentType(typeRef);
        }

        public static bool IsBufferElementComponentType(TypeReference typeRef)
        {
            if (!typeRef.IsValueType || typeRef.IsPrimitive)
                return false;

            return typeRef.Resolve().Interfaces.Any(i => i.InterfaceType.Name == "IBufferElementData");
        }

        public static TypeReference GetDynamicArrayElementType(TypeReference typeRef)
        {
            var type = typeRef.Resolve();

            if (!IsDynamicArray(type))
                throw new ArgumentException("Expected DynamicArray type reference.");

            GenericInstanceType genericInstance = (GenericInstanceType)typeRef;
            return genericInstance.GenericArguments[0];
        }

        public static bool IsStructValueType(TypeReference type)
        {
            if (!type.IsValueType)
                return false;
            if (type.Resolve().IsEnum || type.IsPrimitive)
                return false;
            if (IsDynamicArray(type))
                return false;
            if (type.FixedSpecialType() != null)
                return false;
            if (type.MetadataType == MetadataType.IntPtr)
                return false;
            return true;
        }

        public static bool IsStructValueType(TypeDefinition type)
        {
            if (!type.IsValueType)
                return false;
            if (type.IsEnum || type.IsPrimitive)
                return false;
            if (type.FixedSpecialType() != null)
                return false;
            if (IsComponentType(type))
                return false;
            if (type.MetadataType == MetadataType.IntPtr)
                return false;
            return true;
        }

        public static bool IsStructWithInterface(TypeDefinition type, string fullName)
        {
            return IsStructValueType(type)
                && type.HasInterfaces
                && type.Interfaces.FirstOrDefault(i => i.InterfaceType.FullName == fullName) != null;
        }

        public static bool IsSharedComponentType(TypeReference typeRef)
        {
            if (!typeRef.IsValueType || typeRef.IsPrimitive)
                return false;

            //XXX sucks
            return typeRef.Resolve().Interfaces.Any(i => i.InterfaceType.Name == "ISharedComponentData");
        }

        public static bool IsCleanupComponentData(TypeReference typeRef)
        {
            if (!typeRef.IsValueType || typeRef.IsPrimitive)
                return false;

            //XXX sucks
            return typeRef.Resolve().Interfaces.Any(i => i.InterfaceType.Name == "ICleanupComponentData");
        }


        Dictionary<TypeReference, bool> m_IsManagedTypeCache = new Dictionary<TypeReference, bool>();
        public bool IsManagedType(TypeReference typeRef, int depth)
        {
            if (m_IsManagedTypeCache.TryGetValue(typeRef, out var isManagedType))
                return isManagedType;

            isManagedType = IsManagedTypeInternal(typeRef, depth);

            m_IsManagedTypeCache[typeRef] = isManagedType;

            return isManagedType;
        }

        public bool IsManagedTypeInternal(TypeReference typeRef, int depth)
        {
            if (depth > 100)
                throw new InvalidOperationException("Recursive depth limit exceeded.");

            depth++;

            // We must check this before calling Resolve() as cecil loses this property otherwise
            if (typeRef.IsPointer)
                return false;

            if (typeRef.IsArray || typeRef.IsGenericParameter)
                return true;

            var type = typeRef.Resolve();

            if (IsDynamicArray(type))
                return true;

            TypeDefinition fixedSpecialType = type.FixedSpecialType();
            if (fixedSpecialType != null)
            {
                if (fixedSpecialType.MetadataType == MetadataType.String)
                    return true;
                return false;
            }

            if (type.IsEnum)
                return false;

            if (type.IsValueType())
            {
                // if none of the above check the type's fields
                var typeResolver = TypeResolver.For(typeRef);
                foreach (var field in type.Fields)
                {
                    if (field.IsStatic)
                        continue;

                    var fieldType = typeResolver.Resolve(field.FieldType);
                    if (IsManagedType(fieldType, depth))
                        return true;
                }

                return false;
            }

            return true;
        }

        public static bool IsPodType(TypeReference typeRef)
        {
            TypeDefinition type = typeRef.Resolve();
            if (type.IsCppBasicType() || type.IsEnum) return true;

            var typeResolver = TypeResolver.For(typeRef);
            foreach (var f in type.Fields)
            {
                var fieldType = typeResolver.Resolve(f.FieldType);
                if (fieldType.MetadataType == MetadataType.String || IsDynamicArray(fieldType))
                    return false;
                bool recursiveIsPodType = IsPodType(fieldType);
                if (!recursiveIsPodType)
                    return false;
            }

            return true;
        }


        public static TypeDefinition[] GetSystemRunsBefore(TypeDefinition type)
        {
            var deps = new List<TypeDefinition>();
            foreach (var attr in type.CustomAttributes)
            {
                //XXX sucks
                if (attr.AttributeType.Name == "UpdateBeforeAttribute")
                {
                    TypeReference reference = (TypeReference)attr.ConstructorArguments[0].Value;
                    deps.Add(reference.Resolve());
                }
            }

            return deps.ToArray();
        }

        public static TypeDefinition[] GetSystemRunsAfter(TypeDefinition type)
        {
            var deps = new List<TypeDefinition>();
            foreach (var attr in type.CustomAttributes)
            {
                //XXX sucks
                if (attr.AttributeType.Name == "UpdateAfterAttribute")
                {
                    TypeReference reference = (TypeReference)attr.ConstructorArguments[0].Value;
                    deps.Add(reference.Resolve());
                }
            }

            return deps.ToArray();
        }

        public bool IsComplex(TypeReference typeRef, int depth)
        {
            if (depth > 100)
                throw new InvalidOperationException("Recursive depth limit exceeded.");

            depth++;

            // We must check this before calling Resolve() as cecil loses this property otherwise
            if (typeRef.IsPointer)
                return false;


            if (ValueTypeIsComplex[0].ContainsKey(typeRef))
                return ValueTypeIsComplex[0][typeRef];

            if (IsDynamicArray(typeRef))
                return true;

            TypeDefinition fixedSpecialType = typeRef.FixedSpecialType();
            if (fixedSpecialType != null)
            {
                if (fixedSpecialType.MetadataType == MetadataType.String)
                    return true;
                return false;
            }

            if (typeRef.Resolve().IsEnum)
                return false;

            PreprocessTypeFields(typeRef, 0, depth);

            return ValueTypeIsComplex[0][typeRef];
        }

        public void PreprocessTypeFields(TypeReference valuetype, int bits, int depth)
        {
            if (bits == 32) bits = 0;
            else if (bits == 64) bits = 1;

            int size = 0;
            int highestFieldAlignment = 0;
            bool isComplex = false;

            // have we already preprocessed this?
            if (ValueTypeAlignment[bits].ContainsKey(valuetype) &&
                !ValueTypeAlignment[bits][valuetype].IsSentinel)
            {
                return;
            }

            // For each field, calculate its layout as if it was a C++ struct
            //Console.WriteLine($"Type {valuetype}");
            var typeResolver = TypeResolver.For(valuetype);
            var typeDef = valuetype.Resolve();

            var instanceFieldCount = 0;
            foreach (var fs in typeDef.Fields)
            {
                if (fs.IsStatic)
                    continue;

                instanceFieldCount++;

                var fieldType = typeResolver.Resolve(fs.FieldType);

                var sz = AlignAndSizeOfTypeInternal(fieldType, bits, depth);
                isComplex = isComplex || IsComplex(fieldType, depth);

                // In C++, all members of a struct must have their own address.
                // If we have a "struct {}" as a member, treat its size as at least one byte.
                sz = new AlignAndSize(sz.align, Math.Max(sz.size, 1));
                highestFieldAlignment = Math.Max(highestFieldAlignment, sz.align);
                size = AlignUp(size, sz.align);
                //Console.WriteLine($"  Field: {fs.Name} ({fs.GetType()}) - offset: {size} alignment {sz.align} sz {sz.size}");

                /*
                 * We tryadd here because if you have a generic component like UnityObjectRef<Mesh> and another one UnityObjectRef<Material>,
                 * the fields on those two will resolve to the same FieldReference (or at least the Unity.Cecil.Awesome FieldReferenceComparer
                 * can't tell them apart), and it'll yell at you. But it'll always resolve to the same alignandsize, so it's fine. I think.
		         * Also, we can't literally TryAdd because TryAdd is in netstandard 2.1, and we have to do 2.0 because
		         * visual studio sucks and can't run anything higher when running tasks in msbuild inside it.
                 */

		        var key = typeResolver.Resolve(fs);
		        if (!StructFieldAlignment[bits].ContainsKey(key))
		            StructFieldAlignment[bits].Add(key, new AlignAndSize(sz.align, sz.size, size));

                int offset = fs.Offset;
                if (offset >= 0)
                    size = offset + sz.size;
                else
                    size += sz.size;
            }
            // same min size for outer struct
            size = Math.Max(size, 1);

            // C++ aligns struct sizes up to the highest alignment required
            size = AlignUp(size, highestFieldAlignment);

            // If an explicit size have been provided use that instead
            if (typeDef.IsExplicitLayout && typeDef.ClassSize > 0)
                size = typeDef.ClassSize;

            // Alignment requirements are > 0
            highestFieldAlignment = Math.Max(highestFieldAlignment, 1);

            // If an explict alignment has been provided use that instead
            if (typeDef.IsExplicitLayout && typeDef.PackingSize > 0)
                size = typeDef.PackingSize;

            ValueTypeAlignment[bits].Remove(valuetype);
            ValueTypeAlignment[bits].Add(valuetype, new AlignAndSize(highestFieldAlignment, size, 0, instanceFieldCount == 0));
            //Console.WriteLine($"ValueType: {valuetype.Name} ({valuetype.GetType()}) - alignment {highestFieldAlignment} sz {size}");
            ValueTypeIsComplex[bits].Add(valuetype, isComplex);
        }

        internal void GetFieldOffsetsOfRecurse(Func<FieldReference, TypeReference, bool> match, int offset, TypeReference type, List<int> list, int bits)
        {
            int in_type_offset = 0;
            var typeResolver = TypeResolver.For(type);
            foreach (var f in type.Resolve().Fields)
            {
                if (f.IsStatic)
                    continue;

                uint alignUp(uint a, uint align) => (a + ((align - a) % align));
                int valueOr1(int v) => Math.Max(v, 1);

                TypeUtils.AlignAndSize resize(TypeUtils.AlignAndSize s) => new TypeUtils.AlignAndSize(
                    valueOr1(s.align),
                    valueOr1(s.size));

                var fieldReference = typeResolver.Resolve(f);
                var fieldType = typeResolver.Resolve(f.FieldType);

                var tinfo = resize(AlignAndSizeOfTypeInternal(fieldType, bits, 0));
                if (f.Offset != -1)
                    in_type_offset = f.Offset;
                else
                    in_type_offset = (int)alignUp((uint)in_type_offset, (uint)tinfo.align);

                if (IsDynamicArray(fieldType) && match(fieldReference, GetDynamicArrayElementType(fieldType)))
                {
                    // +1 so that we have a way to indicate an Array<Entity> at position 0
                    // fixup code subtracts 1
                    list.Add(-(offset + in_type_offset + 1));
                }
                else if (match(fieldReference, fieldType))
                {
                    list.Add(offset + in_type_offset);
                }
                else if (fieldType.IsValueType() && !fieldType.IsPrimitive)
                {
                    GetFieldOffsetsOfRecurse(match, offset + in_type_offset, fieldType, list, bits);
                }

                in_type_offset += tinfo.size;
            }
        }

        public List<int> GetFieldOffsetsOf(TypeDefinition typeToFind, TypeReference typeToLookIn, int archBits)
        {
            var offsets = new List<int>();

            if (typeToLookIn != null)
            {
                GetFieldOffsetsOfRecurse((_, typeRef) => TypeReferenceEqualityComparer.AreEqual(typeRef.Resolve(), typeToFind), 0, typeToLookIn, offsets, archBits);
            }

            return offsets;
        }

        /* match: 1st param is the Field, 2nd param is the FieldType */
        public List<int> GetFieldOffsetsOf(Func<FieldReference, TypeReference, bool> match, TypeReference typeToLookIn, int archBits)
        {
            var offsets = new List<int>();

            if (typeToLookIn != null)
            {
                GetFieldOffsetsOfRecurse(match, 0, typeToLookIn, offsets, archBits);
            }

            return offsets;
        }

        static string AdjustFieldNameForProperties(string fieldName, TypeDefinition typeDef)
        {
            // Some fieldNames may actually be Properties so replace them with the backing field name
            foreach (var property in typeDef.Properties)
            {
                if (fieldName == property.Name)
                    return $"<{fieldName}>k__BackingField";
            }

            return fieldName;
        }

        public int GetFieldOffsetByFieldPath(string fieldPath, TypeReference type, int archBits, out TypeReference fieldType)
        {
            fieldType = null;
            var resolvedType = type.Resolve();
            var fields = resolvedType.Fields;
            var properties = resolvedType.Properties;

            var fieldNames = fieldPath.Split('.');
            var currentFieldIndex = 0;
            var maxFieldIndex = fieldNames.Length - 1;
            var fieldName = AdjustFieldNameForProperties(fieldNames[currentFieldIndex], resolvedType);

            int offset = 0;
            for (int i = 0; i < fields.Count; ++i)
            {
                var f = fields[i];
                if (f.IsStatic)
                    continue;

                uint alignUp(uint a, uint align) => (a + ((align - a) % align));
                int valueOr1(int v) => Math.Max(v, 1);

                AlignAndSize resize(TypeUtils.AlignAndSize s) => new TypeUtils.AlignAndSize(
                    valueOr1(s.align),
                    valueOr1(s.size));

                var tinfo = resize(AlignAndSizeOfTypeInternal(f.FieldType, archBits, 0));
                if (f.Offset != -1)
                    offset = f.Offset;
                else
                    offset = (int)alignUp((uint)offset, (uint)tinfo.align);

                if (f.Name == fieldName)
                {
                    // We found a match to our first fieldName but we now need to look at the next type
                    if (currentFieldIndex < maxFieldIndex)
                    {
                        if (!(f.FieldType.IsValueType() && !f.FieldType.IsPrimitive))
                            throw new ArgumentException($"Trying to get the field offset of a primitive type from string '{fieldPath}'. Please confirm your field path string is correct.");

                        // Swap in the found field types field list and reset our iteration counter
                        resolvedType = f.FieldType.Resolve();
                        fields = resolvedType.Fields;
                        i = -1; // we will increment at the top of this loop (to bring us back to 0)

                        // We know we are looking for a new fieldName so set that up
                        fieldName = AdjustFieldNameForProperties(fieldNames[++currentFieldIndex], resolvedType);
                        continue;
                    }
                    else
                    {
                        // We found our last field type, all done!
                        fieldType = f.FieldType;
                        break;
                    }
                }

                offset += tinfo.size;
            }

            return offset;
        }

        public int GetFieldOffset(string fieldName, TypeReference typeRefToLookIn, int archBits)
        {
            int in_type_offset = 0;
            var typeResolver = TypeResolver.For(typeRefToLookIn);
            var typeDefToLookIn = typeRefToLookIn.Resolve();
            foreach (var f in typeDefToLookIn.Fields)
            {
                if (f.IsStatic)
                    continue;

                uint alignUp(uint a, uint align) => (a + ((align - a) % align));
                int valueOr1(int v) => Math.Max(v, 1);

                TypeUtils.AlignAndSize resize(TypeUtils.AlignAndSize s) => new TypeUtils.AlignAndSize(
                    valueOr1(s.align),
                    valueOr1(s.size));

                var fieldReference = typeResolver.Resolve(f);
                var fieldType = typeResolver.Resolve(f.FieldType);

                var tinfo = resize(AlignAndSizeOfTypeInternal(fieldType, archBits, 0));
                if (f.Offset != -1)
                    in_type_offset = f.Offset;
                else
                    in_type_offset = (int)alignUp((uint)in_type_offset, (uint)tinfo.align);

                if (fieldName == f.Name)
                    break;

                in_type_offset += tinfo.size;
            }

            return in_type_offset;
        }

    }
}
