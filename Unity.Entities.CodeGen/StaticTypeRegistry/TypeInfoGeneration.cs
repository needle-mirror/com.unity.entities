#if !DISABLE_TYPEMANAGER_ILPP

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeGenInfoList = System.Collections.Generic.List<Unity.Entities.CodeGen.StaticTypeRegistryPostProcessor.TypeGenInfo>;
using SystemList = System.Collections.Generic.List<Unity.Entities.CodeGen.StaticTypeRegistryPostProcessor.SystemTypeGenInfo>;

using Unity.Cecil.Awesome;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using Unity.Entities.BuildUtils;
using static Unity.Entities.BuildUtils.MonoExtensions;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TypeReferenceEqualityComparer = Unity.Cecil.Awesome.Comparers.TypeReferenceEqualityComparer;
using static Unity.Entities.CodeGen.TypeUtils;
using static Unity.Entities.CodeGen.EntitiesILPostProcessors;
using static Unity.Entities.TypeManager;

namespace Unity.Entities.CodeGen
{
    internal partial class StaticTypeRegistryPostProcessor : EntitiesILPostProcessor
    {

	//These are automatically filled in from the actual values in the engine
        public static int MaximumTypesCount;
        public static int HasNoEntityReferencesFlag; // this flag is inverted to ensure the type id of Entity can still be 1
        public static int IsNotChunkSerializableTypeFlag;
        public static int HasNativeContainerFlag;
        public static int BakingOnlyTypeFlag;
        public static int TemporaryBakingTypeFlag;
        public static int IRefCountedComponentFlag;
        public static int IEquatableTypeFlag;
        public static int EnableableComponentFlag;
        public static int CleanupComponentTypeFlag;
        public static int BufferComponentTypeFlag;
        public static int SharedComponentTypeFlag;
        public static int ManagedComponentTypeFlag;
        public static int ChunkComponentTypeFlag;
        public static int ZeroSizeInChunkTypeFlag;
        public static int CleanupSharedComponentTypeFlag;
        public static int CleanupBufferComponentTypeFlag;
        public static int ManagedSharedComponentTypeFlag;

        public static int ClearFlagsMask;

        public static int MaximumChunkCapacity;
        public static int MaximumSupportedAlignment;
        public static int DefaultBufferCapacityNumerator;
        /* This must be kept in sync with TypeManager.TypeInfo in the engine,
	 * but we have a check enforcing that
	 */
        public readonly struct TypeInfo
        {
            public TypeInfo(
                int typeIndex,
                TypeCategory category,
                int entityOffsetCount,
                int entityOffsetStartIndex,
                ulong memoryOrdering,
                ulong stableTypeHash,
                int bufferCapacity,
                int sizeInChunk,
                int elementSize,
                int alignmentInBytes,
                int maximumChunkCapacity,
                int writeGroupCount,
                int writeGroupStartIndex,
                bool hasBlobRefs,
                bool hasUnityObjRefs,
                int blobAssetRefOffsetCount,
                int blobAssetRefOffsetStartIndex,
                int weakAssetRefOffsetCount,
                int weakAssetRefOffsetStartIndex,
                int unityObjectRefOffsetCount,
                int unityObjectRefOffsetStartIndex,
                int typeSize,
                ulong bloomFilterMask = 0L)
            {
                TypeIndex = typeIndex;
                Category = category;
                EntityOffsetCount = entityOffsetCount;
                EntityOffsetStartIndex = entityOffsetStartIndex;
                MemoryOrdering = memoryOrdering;
                StableTypeHash = stableTypeHash;
                BloomFilterMask = bloomFilterMask;
                BufferCapacity = bufferCapacity;
                SizeInChunk = sizeInChunk;
                ElementSize = elementSize;
                AlignmentInBytes = alignmentInBytes;
                MaximumChunkCapacity = maximumChunkCapacity;
                WriteGroupCount = writeGroupCount;
                WriteGroupStartIndex = writeGroupStartIndex;
                _HasBlobAssetRefs = hasBlobRefs ? 1 : 0;
                _HasUnityObjectRefs = hasUnityObjRefs ? 1 : 0;
                BlobAssetRefOffsetCount = blobAssetRefOffsetCount;
                BlobAssetRefOffsetStartIndex = blobAssetRefOffsetStartIndex;
                WeakAssetRefOffsetCount = weakAssetRefOffsetCount;
                WeakAssetRefOffsetStartIndex = weakAssetRefOffsetStartIndex;
                UnityObjectRefOffsetCount = unityObjectRefOffsetCount;
                UnityObjectRefOffsetStartIndex = unityObjectRefOffsetStartIndex;
                TypeSize = typeSize;
            }
            public readonly int TypeIndex;
            public readonly int SizeInChunk;
            public readonly int ElementSize;
            public readonly int BufferCapacity;
            public readonly ulong MemoryOrdering;
            public readonly ulong StableTypeHash;
            public readonly ulong BloomFilterMask;
            public readonly int AlignmentInBytes;
            public readonly TypeCategory Category;
            public readonly int EntityOffsetCount;

            internal readonly int EntityOffsetStartIndex;
            private readonly int _HasBlobAssetRefs;
            private readonly int _HasUnityObjectRefs;

            public readonly int BlobAssetRefOffsetCount;
            internal readonly int BlobAssetRefOffsetStartIndex;
            public readonly int WeakAssetRefOffsetCount;
            internal readonly int WeakAssetRefOffsetStartIndex;

            public readonly int UnityObjectRefOffsetCount;
            internal readonly int UnityObjectRefOffsetStartIndex;
            public readonly int WriteGroupCount;
            internal readonly int WriteGroupStartIndex;
            public readonly int MaximumChunkCapacity;
            public readonly int TypeSize;
        }

        internal struct TypeGenInfo
        {
            public TypeReference TypeReference;
            public TypeCategory TypeCategory;
            public List<int> EntityOffsets;
            public int EntityOffsetIndex;
            public List<int> BlobAssetRefOffsets;
            public int BlobAssetRefOffsetIndex;
            public List<int> WeakAssetRefOffsets;
            public int WeakAssetRefOffsetIndex;
            public List<int> UnityObjectRefOffsets;
            public int UnityObjectRefOffsetIndex;
            public HashSet<TypeReference> WriteGroupTypes;
            public int WriteGroupsIndex;
            public int TypeIndex;
            public bool IsManaged;
            public TypeUtils.AlignAndSize AlignAndSize;
            public int BufferCapacity;
            public int MaxChunkCapacity;
            public int ElementSize;
            public int SizeInChunk;
            public int Alignment;
            public ulong StableHash;
            public ulong MemoryOrdering;
            public bool MightHaveEntityReferences;
            public bool MightHaveWeakAssetReferences;
            public bool MightHaveBlobAssetReferences;
            internal bool MightHaveUnityObjectReferences;
        }

        internal struct SystemAttributeWithTypeReference
        {
            public SystemAttributeKind Kind;
            public TypeReference TargetSystemType;
            public int Flags;
        }

        internal struct SystemTypeGenInfo
        {
            public TypeReference TypeReference;
            public int Size;
            public long Hash;
            public int TypeFlags;
            public WorldSystemFilterFlags FilterFlags;
            public HashSet<SystemAttributeWithTypeReference> Attributes;
        }

        int m_TotalTypeCount;
        int m_TotalSystemCount;
        int m_TotalSystemAttributeCount;
        int m_TotalEntityOffsetCount;
        int m_TotalBlobAssetRefOffsetCount;
        int m_TotalWeakAssetRefOffsetCount;
        int m_TotalUnityObjectRefOffsetCount;
        int m_TotalWriteGroupCount;

        internal FieldReference GenerateConstantData(TypeDefinition constantStorageTypeDef, byte[] data)
        {
            const string kConstantDataFieldNamePrefix = "ConstantData";
            int constantDataCount = constantStorageTypeDef.NestedTypes.Count(t => t.Name.StartsWith(kConstantDataFieldNamePrefix, StringComparison.Ordinal));

            var constantDataTypeDef = new TypeDefinition(
                constantStorageTypeDef.Namespace,
                $"{kConstantDataFieldNamePrefix}{constantDataCount}",
                TypeAttributes.Class | TypeAttributes.NestedPublic | TypeAttributes.Sealed | TypeAttributes.AnsiClass,
                AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_ValueTypeDef));
            constantStorageTypeDef.NestedTypes.Add(constantDataTypeDef);
            constantDataTypeDef.IsExplicitLayout = true;
            constantDataTypeDef.PackingSize = 1;
            constantDataTypeDef.ClassSize = data.Length;

            var constantDataFieldDef = new FieldDefinition($"Value{constantDataCount}", FieldAttributes.Static | FieldAttributes.InitOnly | FieldAttributes.HasFieldRVA, constantDataTypeDef);
            constantDataFieldDef.InitialValue = data;
            constantStorageTypeDef.Fields.Add(constantDataFieldDef);

            return constantDataFieldDef;
        }

        /// <summary>
        /// Populates the registry's BlobAssetReferenceOffsets int array.
        /// Offsets are laid out contiguously in memory such that the memory layout for Types A (2 entities), B (3 entities), C (0 entities) D (2 entities) is as such: aabbbdd
        /// </summary>
        internal void GenerateBlobAssetReferenceArray(ILProcessor il, in TypeGenInfoList typeGenInfoList, FieldReference fieldRef, bool isStaticField)
        {
            var intRef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def);
            PushNewArray(il, intRef, m_TotalBlobAssetRefOffsetCount);

            int blobOffsetIndex = 0;
            foreach (var typeGenInfo in typeGenInfoList)
            {
                foreach (var offset in typeGenInfo.BlobAssetRefOffsets)
                {
                    PushNewArrayElement(il, blobOffsetIndex++);
                    EmitLoadConstant(il, offset);
                    il.Emit(OpCodes.Stelem_Any, intRef);
                }
            }

            StoreTopOfStackToField(il, fieldRef, isStaticField);
        }

        /// <summary>
        /// Populates the registry's entityOffset int array.
        /// Offsets are laid out contiguously in memory such that the memory layout for Types A (2 entites), B (3 entities), C (0 entities) D (2 entities) is as such: aabbbdd
        /// </summary>
        internal void GenerateEntityOffsetInfoArray(ILProcessor il, in TypeGenInfoList typeGenInfoList, FieldReference fieldRef, bool isStaticField)
        {
            PushNewArray(il, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def), m_TotalEntityOffsetCount);

            int entityOffsetIndex = 0;
            foreach (var typeGenInfo in typeGenInfoList)
            {
                foreach (var offset in typeGenInfo.EntityOffsets)
                {
                    PushNewArrayElement(il, entityOffsetIndex++);
                    EmitLoadConstant(il, offset);
                    il.Emit(OpCodes.Stelem_Any, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def));
                }
            }

            StoreTopOfStackToField(il, fieldRef, isStaticField);
        }

        /// <summary>
        ///  Populates the registry's System.Type array for all types in typeIndex order.
        /// </summary>
        internal void GenerateTypeArray(ILProcessor il, List<TypeReference> typeReferences, FieldReference fieldRef, bool isStaticField)
        {
            PushNewArray(il, AssemblyDefinition.MainModule.ImportReference(typeof(Type)), typeReferences.Count);

            for (int typeIndex = 0; typeIndex < typeReferences.Count; ++typeIndex)
            {
                TypeReference typeRef = EnsureImported(typeReferences[typeIndex]);

                PushNewArrayElement(il, typeIndex);
                il.Emit(OpCodes.Ldtoken, typeRef); // Push our meta-type onto the stack as it will be our arg to System.Type.GetTypeFromHandle
                il.Emit(OpCodes.Call, m_GetTypeFromHandleFnRef); // Call System.Type.GetTypeFromHandle with the above stack arg. Return value pushed on the stack
                il.Emit(OpCodes.Stelem_Ref);
            }

            StoreTopOfStackToField(il, fieldRef, isStaticField);
        }



        /// <summary>
        ///  Populates the registry's SystemTypeSizes array for all System types in typeIndex order.
        ///  Currently sets 0 for size of managed systems.
        /// </summary>
        internal void GenerateSystemTypeSizeArray(ILProcessor il, List<TypeReference> typeReferences, FieldReference fieldRef, bool isStaticField, int archbits)
        {
            PushNewArray(il, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def), typeReferences.Count);

            for (int typeIndex = 0; typeIndex < typeReferences.Count; ++typeIndex)
            {
                TypeReference typeRef = EnsureImported(typeReferences[typeIndex]);
                var size = TypeUtilsInstance.AlignAndSizeOfType(typeRef, archbits).size;

                PushNewArrayElement(il, typeIndex);
                il.Emit(OpCodes.Ldc_I4, size); // Push our size onto the stack
                il.Emit(OpCodes.Stelem_I4);
            }

            StoreTopOfStackToField(il, fieldRef, isStaticField);
        }

        /// <summary>
        ///  Populates the registry's SystemTypeHashes array for all System types in typeIndex order.
        /// </summary>
        internal void GenerateSystemTypeHashArray(ILProcessor il, List<TypeReference> typeReferences, FieldReference fieldRef, bool isStaticField)
        {
            PushNewArray(il, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int64Def), typeReferences.Count);

            for (int typeIndex = 0; typeIndex < typeReferences.Count; ++typeIndex)
            {
                TypeReference typeRef = EnsureImported(typeReferences[typeIndex]);

                PushNewArrayElement(il, typeIndex);
                il.Emit(OpCodes.Call, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._BurstRuntime_GetHashCode64Def.MakeGenericInstanceMethod(typeRef))); // Call BurstRuntime.GetHashCode64 with the above stack arg. Return value pushed on the stack
                il.Emit(OpCodes.Stelem_I8);
            }

            StoreTopOfStackToField(il, fieldRef, isStaticField);
        }

        /// <summary>
        /// Populates the registry's writeGroup int array.
        /// WriteGroup TypeIndices are laid out contiguously in memory such that the memory layout for Types A (2 writegroup elements),
        /// B (3 writegroup elements), C (0 writegroup elements) D (2 writegroup elements) is as such: aabbbdd
        /// </summary>
        internal void GenerateWriteGroupArray(ILProcessor il, TypeGenInfoList typeGenInfoList, FieldReference fieldRef, bool isStaticField)
        {
            PushNewArray(il, AssemblyDefinition.MainModule.ImportReference(typeof(Type)), m_TotalWriteGroupCount);

            int writeGroupIndex = 0;
            foreach (var typeGenInfo in typeGenInfoList)
            {
                foreach (var wgType in typeGenInfo.WriteGroupTypes)
                {
                    PushNewArrayElement(il, writeGroupIndex++);
                    il.Emit(OpCodes.Ldtoken, AssemblyDefinition.MainModule.ImportReference(wgType)); // Push our meta-type onto the stack as it will be our arg to System.Type.GetTypeFromHandle
                    il.Emit(OpCodes.Call, m_GetTypeFromHandleFnRef); // Call System.Type.GetTypeFromHandle with the above stack arg. Return value pushed on the stack
                    il.Emit(OpCodes.Stelem_Ref);
                }
            }

            StoreTopOfStackToField(il, fieldRef, isStaticField);
        }

        internal unsafe byte[] GenerateSystemTypeInfoBlobArray(SystemList typeGenInfoList)
        {
            var systemTypeInfoSize = sizeof(SystemTypeInfo);
            var blob = new byte[systemTypeInfoSize * typeGenInfoList.Count];
            if (typeGenInfoList.Count == 0)
                return blob;

            var attributesSoFar = 0;

            fixed (byte* pData = &blob[0])
            {
                for (int i = 0; i < typeGenInfoList.Count; i++)
                {
                    var typeGenInfo = typeGenInfoList[i];
                    var typeInfo = new SystemTypeInfo
                    {
                        TypeIndex = i,
                        FilterFlags = typeGenInfo.FilterFlags,
                        TypeFlags = typeGenInfo.TypeFlags,
                        SystemAttributeCount = typeGenInfo.Attributes.Count,
                        SystemAttributeStartIndex = attributesSoFar,

                    };
                    attributesSoFar += typeGenInfo.Attributes.Count;
                    *((SystemTypeInfo*)pData + i) = typeInfo;
                }
            }

            return blob;
        }


        /// <summary>
        /// Populates the registry's TypeInfo array in typeIndex order.
        /// </summary>
        internal unsafe byte[] GenerateTypeInfoBlobArray(TypeGenInfoList typeGenInfoList)
        {
            var typeInfoSize = sizeof(TypeInfo);
            var blob = new byte[typeInfoSize * typeGenInfoList.Count];
            if (typeGenInfoList.Count == 0)
                return blob;

            fixed (byte* pData = &blob[0])
            {
                for (int i = 0; i < typeGenInfoList.Count; ++i)
                {
                    var typeGenInfo = typeGenInfoList[i];
                    var typeInfo = new TypeInfo(
                        typeGenInfo.TypeIndex,
                        typeGenInfo.TypeCategory,
                        typeGenInfo.EntityOffsetIndex == -1 ? -1 : typeGenInfo.EntityOffsets.Count,
                        typeGenInfo.EntityOffsetIndex,
                        typeGenInfo.MemoryOrdering,
                        typeGenInfo.StableHash,
                        typeGenInfo.BufferCapacity,
                        typeGenInfo.SizeInChunk,
                        typeGenInfo.ElementSize,
                        typeGenInfo.Alignment,
                        typeGenInfo.MaxChunkCapacity,
                        typeGenInfo.WriteGroupTypes.Count,
                        typeGenInfo.WriteGroupsIndex,
                        typeGenInfo.MightHaveBlobAssetReferences,
                        typeGenInfo.MightHaveUnityObjectReferences,
                        typeGenInfo.BlobAssetRefOffsets.Count,
                        typeGenInfo.BlobAssetRefOffsetIndex,
                        typeGenInfo.WeakAssetRefOffsets.Count,
                        typeGenInfo.WeakAssetRefOffsetIndex,
                        typeGenInfo.UnityObjectRefOffsets.Count,
                        typeGenInfo.UnityObjectRefOffsetIndex,
                        typeGenInfo.AlignAndSize.size,
                        bloomFilterMask: 0L
                    );
                    *((TypeInfo*)pData + i) = typeInfo;
                }
            }

            return blob;
        }
        public static bool IsPowerOfTwo(int value)
        {
            return (value & (value - 1)) == 0;
        }

        internal static int CalculateAlignmentInChunk(int sizeOfTypeInBytes)
        {
            int alignmentInBytes = MaximumSupportedAlignment;
            if (sizeOfTypeInBytes < alignmentInBytes && IsPowerOfTwo(sizeOfTypeInBytes))
                alignmentInBytes = sizeOfTypeInBytes;

            return alignmentInBytes;
        }

        /// <summary>
        /// Populates the registry's TypeInfo array in typeIndex order.
        /// </summary>
        internal void GenerateTypeInfoArray(ILProcessor il, TypeGenInfoList typeGenInfoList, FieldReference fieldRef, bool isStaticField)
        {
            PushNewArray(il, runnerOfMe._TypeManager_TypeInfoDef, typeGenInfoList.Count);

            for (int i = 0; i < typeGenInfoList.Count; ++i)
            {
                var typeGenInfo = typeGenInfoList[i];

                if (i != (typeGenInfo.TypeIndex & ClearFlagsMask))
                    throw new ArgumentException("The typeGenInfo list is not in the correct order. This is a bug.");

                PushNewArrayElement(il, i);

                // Push constructor arguments on to the stack
                EmitLoadConstant(il, typeGenInfo.TypeIndex);
                EmitLoadConstant(il, (int)typeGenInfo.TypeCategory);
                EmitLoadConstant(il, typeGenInfo.EntityOffsetIndex == -1 ? -1 : typeGenInfo.EntityOffsets.Count);
                EmitLoadConstant(il, typeGenInfo.EntityOffsetIndex);
                EmitLoadConstant(il, typeGenInfo.MemoryOrdering);
                EmitLoadConstant(il, typeGenInfo.StableHash);
                EmitLoadConstant(il, typeGenInfo.BufferCapacity);
                EmitLoadConstant(il, typeGenInfo.SizeInChunk);
                EmitLoadConstant(il, typeGenInfo.ElementSize);
                EmitLoadConstant(il, typeGenInfo.Alignment);
                EmitLoadConstant(il, typeGenInfo.MaxChunkCapacity);
                EmitLoadConstant(il, typeGenInfo.WriteGroupTypes.Count);
                EmitLoadConstant(il, typeGenInfo.WriteGroupsIndex);
                EmitLoadConstant(il, typeGenInfo.BlobAssetRefOffsets.Count);
                EmitLoadConstant(il, typeGenInfo.BlobAssetRefOffsetIndex);
                EmitLoadConstant(il, typeGenInfo.WeakAssetRefOffsets.Count);
                EmitLoadConstant(il, typeGenInfo.WeakAssetRefOffsetIndex);
                EmitLoadConstant(il, typeGenInfo.AlignAndSize.size);

                il.Emit(OpCodes.Newobj, m_TypeInfoConstructorRef);

                il.Emit(OpCodes.Stelem_Any, runnerOfMe._TypeManager_TypeInfoDef);
            }

            StoreTopOfStackToField(il, fieldRef, isStaticField);
        }

        (MethodReference, MethodReference, MethodReference) InjectEqualityFunctions(TypeGenInfoList typeGenInfoList)
        {
            // Declares: static public bool Equals(object lhs, object rhs, int typeIndex)
            // This function is required to allow users to query for equality when a Generic <T> param isn't available but the 'int' typeIndex is
            var boxedEqualsFn = new MethodDefinition(
                "Equals",
                MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig,
                AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_BoolDef));
            boxedEqualsFn.Parameters.Add(new ParameterDefinition("lhs", Mono.Cecil.ParameterAttributes.None, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_ObjectDef)));
            boxedEqualsFn.Parameters.Add(new ParameterDefinition("rhs", Mono.Cecil.ParameterAttributes.None, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_ObjectDef)));
            boxedEqualsFn.Parameters.Add(new ParameterDefinition("typeIndex", Mono.Cecil.ParameterAttributes.None, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def)));
            GeneratedRegistryDef.Methods.Add(boxedEqualsFn);

            // Declares: static public bool Equals(object lhs, void* rhs, int typeIndex)
            // This function is required to allow users to query for equality when a Generic <T> param isn't available but the 'int' typeIndex is
            var boxedEqualsPtrFn = new MethodDefinition(
                "Equals",
                MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig,
                AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_BoolDef));
            boxedEqualsPtrFn.Parameters.Add(new ParameterDefinition("lhs", Mono.Cecil.ParameterAttributes.None, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_ObjectDef)));
            boxedEqualsPtrFn.Parameters.Add(
                new ParameterDefinition(
                    "rhs",
                    Mono.Cecil.ParameterAttributes.None,
                    AssemblyDefinition.MainModule.ImportReference(
                        runnerOfMe._voidStarRef)));
                        //AssemblyDefinition.MainModule.TypeSystem.Void.MakePointerType())));
            boxedEqualsPtrFn.Parameters.Add(new ParameterDefinition(
                "typeIndex",
                Mono.Cecil.ParameterAttributes.None,
                AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def)));
            GeneratedRegistryDef.Methods.Add(boxedEqualsPtrFn);

            // Declares: static public int GetHashCode(object val, int typeIndex)
            // This function is required to allow users to query for equality when a Generic <T> param isn't available but the 'int' typeIndex is
            var boxedGetHashCodeFn = new MethodDefinition(
                 "BoxedGetHashCode",
                 MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig,
                 AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def));
            boxedGetHashCodeFn.Parameters.Add(new ParameterDefinition("val", Mono.Cecil.ParameterAttributes.None, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_ObjectDef)));
            boxedGetHashCodeFn.Parameters.Add(new ParameterDefinition("typeIndex", Mono.Cecil.ParameterAttributes.None, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def)));
            GeneratedRegistryDef.Methods.Add(boxedGetHashCodeFn);

            // List of instructions in an array where index == typeIndex
            var boxedEqJumpTable = new List<Instruction>[typeGenInfoList.Count];
            var boxedPtrEqJumpTable = new List<Instruction>[typeGenInfoList.Count];
            var boxedHashJumpTable = new List<Instruction>[typeGenInfoList.Count];

            for (int i = 0; i < typeGenInfoList.Count; ++i)
            {
                var typeGenInfo = typeGenInfoList[i];
                var thisTypeRef = AssemblyDefinition.MainModule.ImportReference(typeGenInfo.TypeReference);
                var typeRef = AssemblyDefinition.MainModule.ImportReference(typeGenInfo.TypeReference);

                {
                    // Equals function for operating on (object lhs, object rhs, int typeIndex) where the type isn't known by the user
                    {
                        var eqIL = boxedEqualsFn.Body.GetILProcessor();

                        boxedEqJumpTable[i] = new List<Instruction>();
                        var instructionList = boxedEqJumpTable[i];

                        if (!typeGenInfo.IsManaged)
                        {
                            instructionList.Add(eqIL.Create(OpCodes.Ldarg_0));
                            instructionList.Add(eqIL.Create(OpCodes.Unbox, thisTypeRef));
                            instructionList.Add(eqIL.Create(OpCodes.Ldarg_1));
                            instructionList.Add(eqIL.Create(OpCodes.Unbox, thisTypeRef));
                            instructionList.Add(eqIL.Create(OpCodes.Ldc_I8, (long)typeGenInfo.AlignAndSize.size));
                            instructionList.Add(eqIL.Create(OpCodes.Call, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._UnsafeUtility_MemCmpFnDef)));
                            instructionList.Add(eqIL.Create(OpCodes.Ldc_I4_0));
                            instructionList.Add(eqIL.Create(OpCodes.Ceq));
                        }
                        else
                        {
                            if (!typeRef.IsValueType())
                            {
                                var equalsFn = GenerateEqualsFunction(GeneratedRegistryDef, typeGenInfo);
                                instructionList.Add(eqIL.Create(OpCodes.Ldarg_0));
                                instructionList.Add(eqIL.Create(OpCodes.Castclass, thisTypeRef));
                                instructionList.Add(eqIL.Create(OpCodes.Ldarg_1));
                                instructionList.Add(eqIL.Create(OpCodes.Castclass, thisTypeRef));
                                instructionList.Add(eqIL.Create(OpCodes.Call, equalsFn));
                            }
                            else
                            {
                                var userImpl = GetTypesEqualsMethodReference(typeGenInfo.TypeReference)?.Resolve();
                                if (userImpl == null)
                                    throw new ArgumentException($"Component type '{typeRef.FullName}' contains managed references. It must implement IEquatable<>");

                                var loc0 = new VariableDefinition(thisTypeRef);
                                boxedEqualsFn.Body.Variables.Add(loc0);

                                instructionList.Add(eqIL.Create(OpCodes.Ldarg_0));
                                instructionList.Add(eqIL.Create(OpCodes.Unbox_Any, thisTypeRef));
                                instructionList.Add(eqIL.Create(OpCodes.Stloc, loc0));
                                instructionList.Add(eqIL.Create(OpCodes.Ldloca, loc0));
                                instructionList.Add(eqIL.Create(OpCodes.Ldarg_1));
                                instructionList.Add(eqIL.Create(OpCodes.Unbox_Any, thisTypeRef));
                                instructionList.Add(eqIL.Create(OpCodes.Call, AssemblyDefinition.MainModule.ImportReference(userImpl)));
                            }
                        }

                        instructionList.Add(eqIL.Create(OpCodes.Ret));
                    }

                    // Equals function for operating on (object lhs, void* rhs, int typeIndex) where the type isn't known by the user
                    {
                        var eqIL = boxedEqualsPtrFn.Body.GetILProcessor();

                        boxedPtrEqJumpTable[i] = new List<Instruction>();
                        var instructionList = boxedPtrEqJumpTable[i];


                        if (!typeGenInfo.IsManaged)
                        {
                            instructionList.Add(eqIL.Create(OpCodes.Ldarg_0));
                            instructionList.Add(eqIL.Create(OpCodes.Unbox, thisTypeRef));
                            instructionList.Add(eqIL.Create(OpCodes.Ldarg_1));
                            instructionList.Add(eqIL.Create(OpCodes.Ldc_I8, (long)typeGenInfo.AlignAndSize.size));
                            instructionList.Add(eqIL.Create(OpCodes.Call, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._UnsafeUtility_MemCmpFnDef)));
                            instructionList.Add(eqIL.Create(OpCodes.Ldc_I4_0));
                            instructionList.Add(eqIL.Create(OpCodes.Ceq));
                            instructionList.Add(eqIL.Create(OpCodes.Ret));
                        }
                        else
                        {
                            instructionList.Add(eqIL.Create(OpCodes.Ldstr, "Equals(object, void*) is not supported for managed types in DOTSRuntime"));
                            var notSupportedExConstructor = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_NotSupportedExceptionDef).Resolve().GetConstructors()
                                .Single(c => c.Parameters.Count == 1 && c.Parameters[0].ParameterType.MetadataType == MetadataType.String);
                            instructionList.Add(eqIL.Create(OpCodes.Newobj, AssemblyDefinition.MainModule.ImportReference(notSupportedExConstructor)));
                            instructionList.Add(eqIL.Create(OpCodes.Throw));
                        }
                    }
                }

                // Store new Hash fn to Hash member
                {
                    // Hash function for operating on (object val, int typeIndex) where the type isn't known by the user
                    {
                        var hashIL = boxedGetHashCodeFn.Body.GetILProcessor();
                        boxedHashJumpTable[i] = new List<Instruction>();
                        var instructionList = boxedHashJumpTable[i];

                        if (!typeGenInfo.IsManaged)
                        {
                            instructionList.Add(hashIL.Create(OpCodes.Ldarg_0));
                            instructionList.Add(hashIL.Create(OpCodes.Unbox, thisTypeRef));
                            instructionList.Add(hashIL.Create(OpCodes.Ldc_I4, typeGenInfo.AlignAndSize.size));
                            instructionList.Add(hashIL.Create(OpCodes.Ldc_I4_0));
                            instructionList.Add(hashIL.Create(OpCodes.Call, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._XXHash_Hash32Def)));
                        }
                        else
                        {
                            if (!typeRef.IsValueType())
                            {
                                var hashFn = GenerateHashFunction(GeneratedRegistryDef, typeGenInfo.TypeReference);
                                instructionList.Add(hashIL.Create(OpCodes.Ldarg_0));
                                instructionList.Add(hashIL.Create(OpCodes.Castclass, thisTypeRef));
                                instructionList.Add(hashIL.Create(OpCodes.Call, hashFn));
                            }
                            else
                            {
                                var userImpl = GetTypesGetHashCodeMethodReference(typeRef)?.Resolve();
                                if (userImpl == null)
                                    throw new ArgumentException($"Component type '{typeRef.FullName}' contains managed references. It must implement IEquatable<>");

                                var loc0 = new VariableDefinition(thisTypeRef);
                                boxedGetHashCodeFn.Body.Variables.Add(loc0);

                                instructionList.Add(hashIL.Create(OpCodes.Ldarg_0));
                                instructionList.Add(hashIL.Create(OpCodes.Unbox_Any, thisTypeRef));
                                instructionList.Add(hashIL.Create(OpCodes.Stloc, loc0));
                                instructionList.Add(hashIL.Create(OpCodes.Ldloca, loc0));
                                instructionList.Add(hashIL.Create(OpCodes.Constrained, thisTypeRef));
                                instructionList.Add(hashIL.Create(OpCodes.Callvirt, AssemblyDefinition.MainModule.ImportReference(userImpl)));
                            }
                        }
                        instructionList.Add(hashIL.Create(OpCodes.Ret));
                    }
                }
            }

            // We now have a list of instructions for each type on how to invoke the correct Equals/Hash call.
            // Now generate the void* Equals and Hash functions by making a jump table to those instructions

            // object Equals
            {
                var eqIL = boxedEqualsFn.Body.GetILProcessor();
                List<Instruction> jumps = new List<Instruction>(boxedEqJumpTable.Length);
                Instruction loadTypeIndex = eqIL.Create(OpCodes.Ldarg_2);
                Instruction defaultCaseOp = Instruction.Create(OpCodes.Ldstr, "FATAL: Tried to call TypeManager.Equals() for a component type unknown to the static TypeRegistry");
                eqIL.Append(loadTypeIndex); // Load typeIndex

                foreach (var instructionList in boxedEqJumpTable)
                {
                    if (instructionList == null)
                    {
                        jumps.Add(defaultCaseOp);
                        continue;
                    }

                    // Add starting instruction to our jump table so we know which Equals IL block to execute
                    jumps.Add(instructionList[0]);

                    foreach (var instruction in instructionList)
                    {
                        eqIL.Append(instruction);
                    }
                }

                // default case
                eqIL.Append(defaultCaseOp);
                var notSupportedExConstructor = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_NotSupportedExceptionDef).Resolve().GetConstructors()
                    .Single(c => c.Parameters.Count == 1 && c.Parameters[0].ParameterType.MetadataType == MetadataType.String);
                eqIL.Emit(OpCodes.Newobj, AssemblyDefinition.MainModule.ImportReference(notSupportedExConstructor));
                eqIL.Emit(OpCodes.Throw);

                // Since we are using InsertAfter these instructions are appended in reverse order to how they will appear
                eqIL.InsertAfter(loadTypeIndex, eqIL.Create(OpCodes.Br, defaultCaseOp));
                eqIL.InsertAfter(loadTypeIndex, eqIL.Create(OpCodes.Switch, jumps.ToArray()));
            }

            // object, void* Equals
            {
                var eqIL = boxedEqualsPtrFn.Body.GetILProcessor();
                List<Instruction> jumps = new List<Instruction>(boxedPtrEqJumpTable.Length);
                Instruction loadTypeIndex = eqIL.Create(OpCodes.Ldarg_2);
                Instruction defaultCaseOp = Instruction.Create(OpCodes.Ldstr, "FATAL: Tried to call TypeManager.Equals() for a component type unknown to the static TypeRegistry");
                eqIL.Append(loadTypeIndex); // Load typeIndex

                foreach (var instructionList in boxedPtrEqJumpTable)
                {
                    if (instructionList == null)
                    {
                        jumps.Add(defaultCaseOp);
                        continue;
                    }

                    // Add starting instruction to our jump table so we know which Equals IL block to execute
                    jumps.Add(instructionList[0]);

                    foreach (var instruction in instructionList)
                    {
                        eqIL.Append(instruction);
                    }
                }

                // default case
                eqIL.Append(defaultCaseOp);
                var notSupportedExConstructor = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_NotSupportedExceptionDef).Resolve().GetConstructors()
                    .Single(c => c.Parameters.Count == 1 && c.Parameters[0].ParameterType.MetadataType == MetadataType.String);
                eqIL.Emit(OpCodes.Newobj, AssemblyDefinition.MainModule.ImportReference(notSupportedExConstructor));
                eqIL.Emit(OpCodes.Throw);

                // Since we are using InsertAfter these instructions are appended in reverse order to how they will appear
                eqIL.InsertAfter(loadTypeIndex, eqIL.Create(OpCodes.Br, defaultCaseOp));
                eqIL.InsertAfter(loadTypeIndex, eqIL.Create(OpCodes.Switch, jumps.ToArray()));
            }

            // object Hash
            {
                var hashIL = boxedGetHashCodeFn.Body.GetILProcessor();
                List<Instruction> jumps = new List<Instruction>(boxedHashJumpTable.Length);
                Instruction loadTypeIndex = hashIL.Create(OpCodes.Ldarg_1);
                Instruction defaultCaseOp = Instruction.Create(OpCodes.Ldstr, "FATAL: Tried to call TypeManager.GetHashCode() for a component type unknown to the static TypeRegistry");
                hashIL.Append(loadTypeIndex); // Load typeIndex

                foreach (var instructionList in boxedHashJumpTable)
                {
                    if (instructionList == null)
                    {
                        jumps.Add(defaultCaseOp);
                        continue;
                    }

                    // Add starting instruction to our jump table so we know which Equals IL block to execute
                    jumps.Add(instructionList[0]);

                    foreach (var instruction in instructionList)
                    {
                        hashIL.Append(instruction);
                    }
                }

                // default case
                hashIL.Append(defaultCaseOp);
                var notSupportedExConstructor = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_NotSupportedExceptionDef).Resolve().GetConstructors()
                    .Single(c => c.Parameters.Count == 1 && c.Parameters[0].ParameterType.MetadataType == MetadataType.String);
                hashIL.Emit(OpCodes.Newobj, AssemblyDefinition.MainModule.ImportReference(notSupportedExConstructor));
                hashIL.Emit(OpCodes.Throw);

                // Since we are using InsertAfter these instructions are appended in reverse order to how they will appear in generated code
                hashIL.InsertAfter(loadTypeIndex, hashIL.Create(OpCodes.Br, defaultCaseOp));
                hashIL.InsertAfter(loadTypeIndex, hashIL.Create(OpCodes.Switch, jumps.ToArray()));
            }

            return (boxedEqualsFn, boxedEqualsPtrFn, boxedGetHashCodeFn);
        }

        private static MethodReference GetTypesEqualsMethodReference(TypeReference typeRef)
        {
            return GetTypesEqualsMethodReference(typeRef.Resolve());
        }

        private static MethodDefinition GetTypesEqualsMethodReference(TypeDefinition typeDef)
        {
            var candidate = typeDef.Methods.FirstOrDefault(
                m => m.Name == "Equals"
                && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType == typeDef);

            if (candidate == null)
            {
                if (typeDef.BaseType != null)
                    candidate = GetTypesEqualsMethodReference(typeDef.BaseType.Resolve());
            }

            return candidate;
        }

        private static MethodReference GetTypesGetHashCodeMethodReference(TypeReference typeRef)
        {
            return GetTypesGetHashCodeMethodReference(typeRef.Resolve());
        }

        private static MethodReference GetTypesGetHashCodeMethodReference(TypeDefinition typeDef)
        {
            // This code is kind of weak. We actually want to confirm this function is overriding System.Object.GetHashCode however
            // as far as I can tell, cecil is not detecting legitimate overrides so we resort to this.
            return typeDef.Methods.FirstOrDefault(
                m => m.Name == "GetHashCode" && m.Parameters.Count == 0);
        }

        private TypeReference EnsureImported(TypeReference type)
        {
            var module = AssemblyDefinition.MainModule;
            if (type.IsGenericInstance)
            {
                var importedType = new GenericInstanceType(module.ImportReference(type.Resolve()));
                var genericType = type as GenericInstanceType;
                foreach (var ga in genericType.GenericArguments)
                    importedType.GenericArguments.Add(ga.IsGenericParameter ? ga : module.ImportReference(ga));
                return module.ImportReference(importedType);
            }
            return module.ImportReference(type);
        }

        internal MethodReference GenerateEqualsFunction(TypeDefinition registryDef, TypeGenInfo typeGenInfo)
        {
            var typeRef = AssemblyDefinition.MainModule.ImportReference(typeGenInfo.TypeReference);
            var equalsFn = new MethodDefinition("DoEquals", MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_BoolDef));
            var arg0 = new ParameterDefinition("i0", Mono.Cecil.ParameterAttributes.None, typeRef.IsValueType() ? new ByReferenceType(typeRef) : typeRef);
            var arg1 = new ParameterDefinition("i1", Mono.Cecil.ParameterAttributes.None, typeRef.IsValueType() ? new ByReferenceType(typeRef) : typeRef);
            equalsFn.Parameters.Add(arg0);
            equalsFn.Parameters.Add(arg1);
            registryDef.Methods.Add(equalsFn);

            var il = equalsFn.Body.GetILProcessor();

            var userImpl = GetTypesEqualsMethodReference(typeGenInfo.TypeReference)?.Resolve();
            if (userImpl != null)
            {
                if (typeRef.IsValueType())
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldobj, typeRef);
                    il.Emit(OpCodes.Call, AssemblyDefinition.MainModule.ImportReference(userImpl));
                }
                else
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Callvirt, AssemblyDefinition.MainModule.ImportReference(userImpl));
                    // Ret is called outside of this block
                }
            }
            else
            {
                GenerateEqualsFunctionRecurse(il, arg0, arg1, typeGenInfo);
            }

            il.Emit(OpCodes.Ret);

            return equalsFn;
        }

        internal void GenerateEqualsFunctionRecurse(ILProcessor il, ParameterDefinition arg0, ParameterDefinition arg1, TypeGenInfo typeGenInfo)
        {
            int typeSize = typeGenInfo.AlignAndSize.size;

            // Raw memcmp of the two types
            // May need to do something more clever if this doesn't pan out for all types
            il.Emit(OpCodes.Ldarg, arg0);
            il.Emit(OpCodes.Ldarg, arg1);
            il.Emit(OpCodes.Ldc_I8, (long)typeSize);
            il.Emit(OpCodes.Call, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._UnsafeUtility_MemCmpFnDef));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
        }

        internal MethodReference GenerateHashFunction(TypeDefinition registryDef, TypeReference typeRef)
        {
            // http://www.isthe.com/chongo/tech/comp/fnv/index.html#FNV-1a
            const int FNV1a_32_OFFSET = unchecked((int)0x811C9DC5);

            var importedTypeRef = AssemblyDefinition.MainModule.ImportReference(typeRef);
            var hashFn = new MethodDefinition(
                "DoHash",
                MethodAttributes.Static
                | MethodAttributes.Public
                | MethodAttributes.HideBySig,
                AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def));
            var arg0 = new ParameterDefinition("val", Mono.Cecil.ParameterAttributes.None, typeRef.IsValueType() ? new ByReferenceType(importedTypeRef) : importedTypeRef);
            hashFn.Parameters.Add(arg0);
            registryDef.Methods.Add(hashFn);

            var il = hashFn.Body.GetILProcessor();

            MethodDefinition userImpl = GetTypesGetHashCodeMethodReference(importedTypeRef)?.Resolve();
            if (userImpl != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                if (typeRef.IsValueType())
                {
                    // avoid boxing if we know the type is a value type
                    il.Emit(OpCodes.Constrained, importedTypeRef);
                }
                il.Emit(OpCodes.Callvirt, AssemblyDefinition.MainModule.ImportReference(userImpl));
            }
            else
            {
                List<Instruction> fieldLoadChain = new List<Instruction>();
                List<Instruction> hashInstructions = new List<Instruction>();

                GenerateHashFunctionRecurse(il, hashInstructions, fieldLoadChain, arg0, importedTypeRef);
                if (hashInstructions.Count == 0)
                {
                    // If the type doesn't contain any value types to hash we want to return 0 as the hash
                    il.Emit(OpCodes.Ldc_I4_0);
                }
                else
                {
                    EmitLoadConstant(il, FNV1a_32_OFFSET); // Initial Hash value

                    foreach (var instruction in hashInstructions)
                    {
                        il.Append(instruction);
                    }
                }
            }

            il.Emit(OpCodes.Ret);

            return hashFn;
        }

        internal void GenerateHashFunctionRecurse(ILProcessor il, List<Instruction> hashInstructions, List<Instruction> fieldLoadChain, ParameterDefinition val, TypeReference typeRef)
        {
            // http://www.isthe.com/chongo/tech/comp/fnv/index.html#FNV-1a
            const int FNV1a_32_PRIME = 16777619;
            var typeResolver = TypeResolver.For(typeRef);
            var typeDef = typeRef.Resolve();
            foreach (var f in typeDef.Fields)
            {
                if (!f.IsStatic)
                {
                    var field = typeResolver.Resolve(f);
                    var fieldTypeRef = typeResolver.Resolve(field.FieldType);
                    var fieldTypeDef = fieldTypeRef.Resolve();
                    fieldTypeDef.MakeTypeInternal();

                    // https://cecilifier.appspot.com/ outputs what you would expect here, that there is
                    // a bit in the attributes for 'Fixed.'. Specifically:
                    //     FieldAttributes.Fixed
                    // Haven't been able to find the actual numeric value. Until then, use this approach:
                    bool isFixed = fieldTypeDef.ClassSize != -1 && fieldTypeDef.Name.Contains(">e__FixedBuffer");
                    if (isFixed || fieldTypeRef.IsPrimitive || fieldTypeRef.IsPointer || fieldTypeDef.IsEnum)
                    {
                        /*
                         Equivalent to:
                            hash *= FNV1a_32_PRIME;
                            hash ^= value;
                        */
                        hashInstructions.Add(il.Create(OpCodes.Ldc_I4, FNV1a_32_PRIME));
                        hashInstructions.Add(il.Create(OpCodes.Mul));


                        hashInstructions.Add(il.Create(OpCodes.Ldarg, val));
                        // Since we need to find the offset to nested members we need to chain field loads
                        hashInstructions.AddRange(fieldLoadChain);

                        if (isFixed)
                        {
                            hashInstructions.Add(il.Create(OpCodes.Ldflda, AssemblyDefinition.MainModule.ImportReference(field)));
                            hashInstructions.Add(il.Create(OpCodes.Ldind_I4));
                        }
                        else
                        {
                            if (fieldTypeRef.IsPointer && ArchBits == 64)
                            {
                                // Xor top and bottom of pointer
                                //
                                // Bottom 32 Bits
                                hashInstructions.Add(il.Create(OpCodes.Ldfld, AssemblyDefinition.MainModule.ImportReference(field)));
                                hashInstructions.Add(il.Create(OpCodes.Conv_I8)); // do I need this if we know the ptr is 64-bit
                                hashInstructions.Add(il.Create(OpCodes.Ldc_I4_M1)); // 0x00000000FFFFFFFF
                                hashInstructions.Add(il.Create(OpCodes.Conv_I8));

                                hashInstructions.Add(il.Create(OpCodes.And));

                                // Top 32 bits
                                hashInstructions.Add(il.Create(OpCodes.Ldarg, val));
                                hashInstructions.AddRange(fieldLoadChain);
                                hashInstructions.Add(il.Create(OpCodes.Ldfld, AssemblyDefinition.MainModule.ImportReference(field)));
                                hashInstructions.Add(il.Create(OpCodes.Conv_I8)); // do I need this if we know the ptr is 64-bit
                                hashInstructions.Add(il.Create(OpCodes.Ldc_I4, 32));
                                hashInstructions.Add(il.Create(OpCodes.Shr_Un));
                                hashInstructions.Add(il.Create(OpCodes.Ldc_I4_M1)); // 0x00000000FFFFFFFF
                                hashInstructions.Add(il.Create(OpCodes.Conv_I8));
                                hashInstructions.Add(il.Create(OpCodes.And));

                                hashInstructions.Add(il.Create(OpCodes.Xor));
                            }
                            else
                            {
                                hashInstructions.Add(il.Create(OpCodes.Ldfld, AssemblyDefinition.MainModule.ImportReference(field)));
                            }
                        }

                        // Subtle behavior. Aside from pointer types, we only load the first 4 bytes of the field.
                        // Makes hashing fast and simple, at the cost of more hash collisions.
                        hashInstructions.Add(il.Create(OpCodes.Conv_I4));
                        hashInstructions.Add(il.Create(OpCodes.Xor));
                    }
                    else if (fieldTypeRef.IsValueType())
                    {
                        // Workaround: We shouldn't need to special case for System.Guid however accessing the private members of types in mscorlib
                        // is problematic as eventhough we may elevate the field permissions in a new mscorlib assembly, Windows may load the assembly from the
                        // Global Assembly Cache regardless resulting in us throwing FieldAccessExceptions
                        if (fieldTypeRef.FullName == "System.Guid")
                        {
                            /*
                             Equivalent to:
                                hash *= FNV1a_32_PRIME;
                                hash ^= value;
                            */
                            hashInstructions.Add(il.Create(OpCodes.Ldc_I4, FNV1a_32_PRIME));
                            hashInstructions.Add(il.Create(OpCodes.Mul));

                            hashInstructions.Add(il.Create(OpCodes.Ldarg, val));
                            // Since we need to find the offset to nested members we need to chain field loads
                            hashInstructions.AddRange(fieldLoadChain);

                            hashInstructions.Add(il.Create(OpCodes.Ldflda, AssemblyDefinition.MainModule.ImportReference(field)));
                            hashInstructions.Add(il.Create(OpCodes.Call, AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Guid_GetHashCode)));
                            hashInstructions.Add(il.Create(OpCodes.Xor));
                        }
                        else
                        {
                            fieldLoadChain.Add(Instruction.Create(OpCodes.Ldfld, AssemblyDefinition.MainModule.ImportReference(field)));
                            GenerateHashFunctionRecurse(il, hashInstructions, fieldLoadChain, val, fieldTypeRef);
                            fieldLoadChain.RemoveAt(fieldLoadChain.Count - 1);
                        }
                    }
                }
            }
        }

        void CalculateMemoryOrderingAndStableHash(TypeReference typeRef, out ulong memoryOrder, out ulong stableHash)
        {
            if (typeRef == null)
            {
                memoryOrder = 0;
                stableHash = 0;
                return;
            }
            var typeDef = typeRef.Resolve();

            stableHash = typeRef.CalculateStableTypeHash();
            memoryOrder = stableHash; // They are equivalent unless overridden below

            if (typeDef.CustomAttributes.Count > 0)
            {
                var forcedMemoryOrderAttribute = typeDef.CustomAttributes.FirstOrDefault(ca => ca.Constructor.DeclaringType.Name == "ForcedMemoryOrderingAttribute");
                if (forcedMemoryOrderAttribute != null)
                {
                    memoryOrder = (ulong)forcedMemoryOrderAttribute.ConstructorArguments
                        .First(arg => arg.Type.MetadataType == MetadataType.UInt64)
                        .Value;
                }
            }
        }

        bool HasNativeContainer(TypeReference type)
        {
            if (type.IsPrimitive)
                return false;
            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                if (elementType != null)
                    return HasNativeContainer(elementType);
                return false;
            }

            var typeDef = type.Resolve();
            //XXX LETS NOT COMPARE STRINGS AMONG FRIENDS
            if (typeDef.HasAttribute(runnerOfMe._NativeContainerAttributeDef.FullName))
                return true;

            // The incoming `type` should be a full generic instance.  This genericResolver
            // will help us resolve the generic parameters of any of its fields
            var genericResolver = TypeResolver.For(type);

            foreach (var typeField in typeDef.Fields)
            {
                // 1) enums which infinitely recurse because the values in the enum are of the same enum type
                // 2) statics which infinitely recurse themselves (Such as vector3.zero.zero.zero.zero)
                if (typeField.IsStatic)
                    continue;

                var genericResolvedFieldType = genericResolver.ResolveFieldType(typeField);
                if (genericResolvedFieldType.IsGenericParameter)
                    continue;

                // only recurse into things that are straight values; no pointers or byref values
                if (genericResolvedFieldType.IsValueType() && !genericResolvedFieldType.IsPrimitive)
                {
                    // make sure we iterate the FieldType with all generic params resolved
                    if (HasNativeContainer(genericResolvedFieldType))
                        return true;
                }
            }

            return false;
        }

        // True when a component is valid to using in world serialization. A component IsSerializable when it is valid to blit
        // the data across storage media. Thus components containing pointers have an IsSerializable of false as the component
        // is blittable but no longer valid upon deserialization.
        private (bool isSerializable, bool hasChunkSerializableAttribute) IsTypeValidForSerialization(TypeReference type)
        {
            var result = (false, false);
            if (m_ChunkSerializableCache.TryGetValue(type, out result))
                return result;

            var typeDef = type.Resolve();
            if (typeDef.HasAttribute("Unity.Entities.ChunkSerializableAttribute"))
            {
                result = (true, true);
                m_ChunkSerializableCache.Add(type, result);
                return result;
            }

            var genericResolver = TypeResolver.For(type);
            foreach (var typeField in typeDef.Fields)
            {
                if (typeField.IsStatic)
                    continue;

                var fieldType = genericResolver.ResolveFieldType(typeField);
                if (fieldType.IsGenericParameter)
                    continue;

                if (fieldType.IsPointer
                    || fieldType.Name == nameof(IntPtr)
                    || fieldType.Name == nameof(UIntPtr))
                {
                    m_ChunkSerializableCache.Add(type, result);
                    return result;
                }
                else if (fieldType.IsValueType() && !fieldType.IsPrimitive && !fieldType.IsEnum())
                {
                    if (!IsTypeValidForSerialization(fieldType).isSerializable)
                    {
                        m_ChunkSerializableCache.Add(type, result);
                        return result;
                    }
                }
            }

            result = (true, false);
            m_ChunkSerializableCache.Add(type, result);
            return result;
        }

        // A component type is "chunk serializable" if it meets the following rules:
        // - It is decorated with [ChunkSerializable] attribute (this is an override for all other rules)
        // - The type is blittable AND
        // - The type does not contain any pointer types (including IntPtr) AND
        // - If the type is a shared component, it does not contain an entity reference
        private bool IsComponentChunkSerializable(TypeReference type, TypeCategory category, bool hasEntityReference)
        {
            var isSerializable = IsTypeValidForSerialization(type);

            // Shared Components are expected to be handled specially when serializing and are not required to be blittable.
            // They cannot contain an entity reference today as they are not patched however for unmanaged components
            // we should be able to correct this behaviour DOTS-7613
            if (!isSerializable.hasChunkSerializableAttribute && category == TypeCategory.ISharedComponentData && hasEntityReference)
            {
                isSerializable.isSerializable = false;
            }

            return isSerializable.isSerializable;
        }

        /*
         * copied and translated from reflection from EntityRemapUtility.cs
         */
        public void HasEntityReferencesManaged(
            TypeReference type,
            out bool hasEntityReferences,
            out bool hasBlobReferences,
            out bool hasUnityObjReferences,
            out bool hasWeakAssetReferences,
            int maxDepth = 128)
        {
            try
            {
                ProcessReferencesRecursiveManaged(type, out hasEntityReferences, out hasBlobReferences, out hasUnityObjReferences, out hasWeakAssetReferences, 0, maxDepth);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"While processing type {type}, caught exception {e}; this is always a bug in an ILPostProcessor. Please report with Help->Report a bug... or to the DOTS team directly. Thanks!");
            }
        }

        /*
         * copied and translated from reflection from EntityRemapUtility.cs
         */
        void ProcessReferencesRecursiveManaged(
            TypeReference type,
            out bool hasEntityReferences,
            out bool hasBlobReferences,
            out bool hasUnityObjReferences,
            out bool hasWeakAssetReferences,
            int depth,
            int maxDepth = 10)
        {
            hasEntityReferences = false;
            hasBlobReferences = false;
            hasUnityObjReferences = false;
            hasWeakAssetReferences = false;

            //we don't follow pointers for patching anyway, so don't follow them for looking for
            //entity or blob references either
            if (type.IsPointer)
                return;

            var typeDef = type.Resolve();

            // If the type explicitly overrides entity/blob reference attributes account for that now
            if (typeDef.CustomAttributes.Count > 0)
            {
                // Force true
                var forceReference = typeDef.CustomAttributes.FirstOrDefault(ca =>
                    ca.Constructor.DeclaringType.Name == "ForceReferenceAttribute");

                if (forceReference != null)
                {
                    hasEntityReferences = (bool) forceReference.ConstructorArguments[0].Value;
                    hasBlobReferences = (bool) forceReference.ConstructorArguments[1].Value;
                    hasUnityObjReferences = (bool) forceReference.ConstructorArguments[2].Value;
                }
            }

            // Avoid deep / infinite recursion
            if (depth > maxDepth)
            {
                // The max depth is reached on searching for nested Entity References, Blob References or Unity Object References
                // It will ignore any Entity, Blob or Unity Object References. If you are certain that there is any of these types
                // somewhere in the nesting structure, please add the [{nameof(TypeManager.ForceReferenceSearchAttribute)}] attribute to the type.
                return;
            }

            var fields = typeDef.Fields.Where(f=>!f.IsStatic);

            var typeResolver = TypeResolver.For(type);

            foreach (var f in fields)
            {
                var field = typeResolver.Resolve(f);
                if (hasEntityReferences && hasBlobReferences && hasUnityObjReferences && hasWeakAssetReferences)
                {
                    break;
                }

                //if we don't do this, if we're on a MyGenericThing<int> and we get to a field of type T,
                //it won't figure out that the type here is actually int.
                var fieldRef = typeResolver.Resolve(field.FieldType);
                var fieldType = fieldRef.Resolve();

                // Get underlying type for Array or List
                if (fieldRef.IsArray)
                {
                    fieldRef = ((TypeSpecification)fieldRef).ElementType;

                }
                else if (fieldRef is GenericInstanceType ginst)
                {
                    fieldType = fieldRef.Resolve();
                    if (TypeReferenceEqualityComparer.AreEqual(
                        fieldType,
                        AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Collections_Generic_List_T_Def)))
                    {
                        fieldRef = ginst.GenericArguments[0];
                    }
                }

                fieldType = typeResolver.Resolve(fieldRef).Resolve();

                if (fieldType.IsPrimitive)
                {

                }
                else if (fieldType.IsChildTypeOf(runnerOfMe._UnityEngine_ObjectDef))
                {

                }
                else if (TypeReferenceEqualityComparer.AreEqual(fieldType, runnerOfMe._EntityDef))
                {
                    hasEntityReferences = true;
                }
                else if (TypeReferenceEqualityComparer.AreEqual(fieldType, runnerOfMe._BlobAssetReferenceDataDef))
                {
                    hasBlobReferences = true;
                }
                else if (TypeReferenceEqualityComparer.AreEqual(fieldType, runnerOfMe._UntypedUnityObjectRefDef))
                {
                    hasUnityObjReferences = true;
                }
                else if (TypeReferenceEqualityComparer.AreEqual(fieldType, runnerOfMe._UntypedWeakReferenceIdDef))
                {
                    hasWeakAssetReferences = true;
                }
                else if (fieldType.IsValueType || fieldType.IsSealed)
                {
                    ProcessReferencesRecursiveManaged(
                        fieldRef,
                        out var recursiveHasEntityRefs,
                        out var recursiveHasBlobRefs,
                        out var recursiveHasUnityObjRefs,
                        out var recursiveHasWeakAssetRefs,
                        depth + 1,
                        maxDepth);

                    hasEntityReferences |= recursiveHasEntityRefs;
                    hasBlobReferences |= recursiveHasBlobRefs;
                    hasUnityObjReferences |= recursiveHasUnityObjRefs;
                    hasWeakAssetReferences |= recursiveHasWeakAssetRefs;
                }
                else
                {
                    // We can't determine if there are references in a polymorphic non-sealed class type in the TypeManager.
                    // It will ignore any Entity, Blob or Unity Object References. If you are certain that there is any of these types
                    // somewhere in the nesting structure, please add the [{nameof(TypeManager.ForceReferenceSearchAttribute)}] attribute to the type.
                }
            }
        }

        /*
         * copied and translated from reflection from TypeManager.cs
         */
        internal static void ThrowOnDisallowedManagedComponentData(Type type, string baseTypeDesc)
        {
            // Validate the class component data type is usable:
            // - Has a default constructor
            if (type.GetConstructor(Type.EmptyTypes) == null)
                throw new ArgumentException($"{type} is a class based {baseTypeDesc}. Class based {baseTypeDesc} must implement a default constructor.");
        }
        /*
         * copied and translated from reflection from TypeManager.cs
         */
        internal static void ThrowOnDisallowedComponentData(TypeReference typeRef, TypeReference baseType, string baseTypeDesc)
        {
            if (typeRef.IsPrimitive)
                return;

            // if it's a pointer, we assume you know what you're doing
            if (typeRef.IsPointer)
                return;

            var typeResolver = TypeResolver.For(typeRef);
            var typeDef = typeRef.Resolve();

            if (!typeRef.IsValueType || typeDef.IsByReference || typeDef.IsInterface || typeRef.IsArray)
            {
                if (typeRef == baseType)
                    throw new ArgumentException(
                        $"{typeRef} is a {baseTypeDesc} and thus must be a struct containing only primitive or blittable members.");

                throw new ArgumentException($"{baseType} contains a field of {typeRef}, which is neither primitive nor blittable.");
            }

            foreach (var f in typeDef.Fields)
            {
                if (!f.IsStatic)
                {
                    var field = typeResolver.Resolve(f);
                    ThrowOnDisallowedComponentData(field.FieldType, baseType, baseTypeDesc);
                }
            }
        }
        /*
         * this is inefficient: it traverses through the type twice, once inside IsManagedType, and once in ThrowOnWhatever.
         */
        internal void CheckIsAllowedAsComponentData(TypeReference type, string baseTypeDesc)
        {
            if (!TypeUtilsInstance.IsManagedType(type, 0))
                return;

            // it can't be used -- so we expect this to find and throw
            ThrowOnDisallowedComponentData(type, type, baseTypeDesc);//, allowedComponentCache);

            // if something went wrong and the above didn't throw, then throw
            throw new ArgumentException($"{type} cannot be used as component data for unknown reasons (BUG)");
        }


        /*
         * copied and translated from reflection from TypeManager.cs
         */
        private static ulong ComputeBloomFilterMask(ulong typeHash)
        {
            // This function effectively computes k different hashes from the input typeHash to a single bit in the output
            // mask. If k is too low, the odds increase that multiple types will have the same bloomFilterMask. If k is
            // too high, the odds increase that bitwise-or'ing multiple masks together will have so many bits set that a
            // missing type is "hidden". Either way, the net result is a higher false positive rate, reducing
            // the effectiveness of the Bloom filter early-out check.
            // k=5 seems to strike a reasonable balance, given the number of unique component types and the number
            // of types per archetype in typical DOTS applications.
            const int k = 5;
            const int maxShift = 8 * sizeof(ulong);
            uint seed = (uint)((typeHash & 0xFFFFFFFF) ^ (typeHash >> 32));
            var rng = new System.Random((int)(seed != 0 ? seed : 17)); //new Unity.Mathematics.Random(seed != 0 ? seed : 17);
            ulong mask = 0;
            for (int i = 0; i < k; i++)
            {
                mask |= 1UL << rng.Next(maxShift);
            }
            return mask;
        }

        bool AnybodyUpTheChainImplements(TypeDefinition type, TypeDefinition iface)
        {
            return type.TypeImplements(iface) || (type.BaseType != null && AnybodyUpTheChainImplements(type.BaseType.Resolve(), iface));
        }

        /*
         * copied and translated from reflection from TypeManager.cs
         */
        internal TypeGenInfo BuildComponentType(TypeReference typeRef)//, BuildComponentCache caches)
        {
            var typeDef = typeRef.Resolve();

            var sizeInChunk = 0;
            TypeCategory category;
            int bufferCapacity = -1;

            //let's deal with caching if things are in fact slow
            var memoryOrdering = TypeHash.CalculateMemoryOrdering(typeRef, out var hasCustomMemoryOrder);
            // The stable type hash is the same as the memory order if the user hasn't provided a custom memory ordering
            var stableTypeHash = !hasCustomMemoryOrder ? memoryOrdering : TypeHash.CalculateStableTypeHash(typeRef);
            var bloomFilterMask = ComputeBloomFilterMask(stableTypeHash);
            bool isManaged = !typeDef.IsValueType();
            var isRefCounted = typeDef.TypeImplements(runnerOfMe._IRefCountedDef);
            var maxChunkCapacity = MaximumChunkCapacity;
            var valueTypeSize = 0;

            var entityOffsets = new List<int>();
            var blobAssetRefOffsets = new List<int>();
            var weakAssetRefOffsets = new List<int>();
            var unityObjectRefOffsets = new List<int>();

            //xxx don't use a string
            var maxCapacityAttribute = typeDef.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "MaximumChunkCapacityAttribute");
            if (typeDef.CustomAttributes.Count > 0)
            {
                var maxChunkCapacityAttribute = typeDef.CustomAttributes.FirstOrDefault(ca => ca.AttributeType.Name == "MaximumChunkCapacityAttribute");
                if (maxChunkCapacityAttribute != null)
                {
                    var chunkCapacityFromAttribute = (int)maxChunkCapacityAttribute.ConstructorArguments
                        .First(arg => arg.Type.MetadataType == MetadataType.Int32)
                        .Value;
                    if (chunkCapacityFromAttribute < maxChunkCapacity)
                        maxChunkCapacity = chunkCapacityFromAttribute;
                }
            }

            int elementSize = 0;
            int alignmentInBytes = 0;

            if (typeDef.IsInterface)
                throw new ArgumentException($"{typeRef} is an interface. It must be a concrete type.");

            //important to use typeRef and not typeDef here for generic purposes
            //this traverses the type, but all the cases below are also going to traverse the type, so this is dumb
            //also, maybe we could cache something mumble, but maybe it wouldn't help

            bool hasNativeContainer = !isManaged && HasNativeContainer(typeRef);
            bool hasEntityReferences = false;
            bool hasBlobReferences = false;
            bool hasWeakAssetReferences = false;
            bool hasUnityObjectReferences = false;

            bool implementsICD = AnybodyUpTheChainImplements(typeDef, runnerOfMe._IComponentDataDef);
            if (implementsICD && !isManaged)
            {
                CheckIsAllowedAsComponentData(typeRef, "IComponentData");//, caches.AllowedComponentCache);

                category = TypeCategory.ComponentData;

                var alignAndSize = TypeUtilsInstance.AlignAndSizeOfType(typeRef, 64);
                valueTypeSize = alignAndSize.size;//UnsafeUtility.SizeOf(typeRef);
                //elliotc xxx todo: figure out the relationship between the alignment from TypeUtils.AlignAndSizeOfType and that from CalculateAlignmentInChunk
                alignmentInBytes = CalculateAlignmentInChunk(valueTypeSize);

                if (alignAndSize.empty)
                    sizeInChunk = 0;
                else
                    sizeInChunk = valueTypeSize;

                entityOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._EntityDef, typeRef, 64);
                if (entityOffsets.Count > 0)
                    hasEntityReferences = true;
                blobAssetRefOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._BlobAssetReferenceDataDef, typeRef, 64);
                if (blobAssetRefOffsets.Count > 0)
                    hasBlobReferences = true;
                weakAssetRefOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._UntypedWeakReferenceIdDef, typeRef, ArchBits);
                if (weakAssetRefOffsets.Count > 0)
                    hasWeakAssetReferences = true;
                unityObjectRefOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._UntypedUnityObjectRefDef, typeRef, ArchBits);
                if (unityObjectRefOffsets.Count > 0)
                    hasUnityObjectReferences = true;
            }
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            else if (implementsICD && isManaged)
            {
                if (!typeDef.GetConstructors().Any(c => c.Parameters.Count == 0))
                    throw new ArgumentException($"{typeDef} is a class based IComponentData. Class based IComponentData must implement a default constructor.");

                category = TypeCategory.ComponentData;
                sizeInChunk = sizeof(int);
                HasEntityReferencesManaged(typeRef, out var entityRefResult, out var blobRefResult, out var unityObjRefResult, out var weakAssetRefResult);

                hasEntityReferences = entityRefResult;
                hasBlobReferences = blobRefResult;
                hasUnityObjectReferences = unityObjRefResult;
                hasWeakAssetReferences = weakAssetRefResult;
            }
#endif
            else if (typeDef.TypeImplements(runnerOfMe._IBufferElementDataDef))
            {
                CheckIsAllowedAsComponentData(typeRef, "IBufferElementData");

                category = TypeCategory.BufferData;


                var alignAndSize = TypeUtilsInstance.AlignAndSizeOfType(typeRef, 64);
                valueTypeSize = alignAndSize.size;

                alignmentInBytes = CalculateAlignmentInChunk(valueTypeSize);

                elementSize = valueTypeSize;

                if (typeDef.CustomAttributes.Count > 0)
                {
                    var forcedCapacityAttribute = typeDef.CustomAttributes.FirstOrDefault(ca => ca.Constructor.DeclaringType.Name == "InternalBufferCapacityAttribute");
                    if (forcedCapacityAttribute != null)
                    {
                        bufferCapacity = (int)forcedCapacityAttribute.ConstructorArguments
                            .First(arg => arg.Type.MetadataType == MetadataType.Int32)
                            .Value;
                    }
                }
                if (bufferCapacity == -1)
                    bufferCapacity = DefaultBufferCapacityNumerator / elementSize; // Rather than 2*cachelinesize, to make it cross platform deterministic

                sizeInChunk = TypeUtilsInstance.AlignAndSizeOfType(runnerOfMe._BufferHeaderDef, 64).size + bufferCapacity * elementSize;
                entityOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._EntityDef, typeRef, 64);
                if (entityOffsets.Count > 0)
                    hasEntityReferences = true;
                blobAssetRefOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._BlobAssetReferenceDataDef, typeRef, 64);
                if (blobAssetRefOffsets.Count > 0)
                    hasBlobReferences = true;
                weakAssetRefOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._UntypedWeakReferenceIdDef, typeRef, ArchBits);
                if (weakAssetRefOffsets.Count > 0)
                    hasWeakAssetReferences = true;
                unityObjectRefOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._UntypedUnityObjectRefDef, typeRef, ArchBits);
                if (unityObjectRefOffsets.Count > 0)
                    hasUnityObjectReferences = true;
            }
            else if (typeDef.TypeImplements(runnerOfMe._ISharedComponentDataDef))
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                if (!typeRef.IsValueType)
                    throw new ArgumentException($"{typeRef} is an ISharedComponentData, and thus must be a struct.");
#endif

                isManaged = TypeUtilsInstance.IsManagedType(typeRef, 0);

                AlignAndSize alignAndSize = default;

                //do not traverse managed types, because they can have cycles and blow us up
                if (!isManaged)
                    alignAndSize = TypeUtilsInstance.AlignAndSizeOfType(typeRef, 64);

                valueTypeSize = alignAndSize.size;//UnsafeUtility.SizeOf(typeRef);

                category = TypeCategory.ISharedComponentData;


                if (isManaged)
                {
                    HasEntityReferencesManaged(typeRef, out _, out var blobRefResult, out var unityObjRefResult, out var weakAssetRefResult);//, caches.HasEntityOrBlobAssetReferenceCache);

                    // Managed shared components explicitly do not allow patching of entity references
                    hasEntityReferences = false;
                    hasBlobReferences = blobRefResult;
                    hasUnityObjectReferences = unityObjRefResult;
                    hasWeakAssetReferences = weakAssetRefResult;
                }
                else
                {
                    //xxx elliotc todo: commonize this shit, obviously, between this and buffers and icd
                    entityOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._EntityDef, typeRef, 64);
                    if (entityOffsets.Count > 0)
                        hasEntityReferences = true;
                    blobAssetRefOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._BlobAssetReferenceDataDef, typeRef, 64);
                    if (blobAssetRefOffsets.Count > 0)
                        hasBlobReferences = true;
                    weakAssetRefOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._UntypedWeakReferenceIdDef, typeRef, ArchBits);
                    if (weakAssetRefOffsets.Count > 0)
                        hasWeakAssetReferences = true;
                    unityObjectRefOffsets = TypeUtilsInstance.GetFieldOffsetsOf(runnerOfMe._UntypedUnityObjectRefDef, typeRef, ArchBits);
                    if (unityObjectRefOffsets.Count > 0)
                        hasUnityObjectReferences = true;
                }
            }
            else if (TypeUtilsInstance.IsManagedType(typeDef, 0))
            {
                category = TypeCategory.UnityEngineObject;
                sizeInChunk = sizeof(int);
                alignmentInBytes = sizeof(int);
                hasEntityReferences = false;
                hasBlobReferences = false;

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
               if (!typeDef.IsChildTypeOf(runnerOfMe._UnityEngine_ObjectDef))
                    throw new ArgumentException($"{typeRef} must inherit from {runnerOfMe._UnityEngine_ObjectDef}.");
#endif
            }
            else
            {
                throw new ArgumentException($"{typeRef} is not a valid component.");
            }

            // If the type explicitly overrides entity/blob reference attributes account for that now
            if (typeDef.CustomAttributes.Count > 0)
            {
                var overrideAttribute = typeDef.CustomAttributes.FirstOrDefault(ca =>
                    ca.Constructor.DeclaringType.Name == "TypeOverridesAttribute");

                var hasOverrideAttribute = overrideAttribute != null;

                if (hasOverrideAttribute)
                {
                    if ((bool)overrideAttribute.ConstructorArguments[0].Value)
                    {
                        hasEntityReferences = false;
                    }
                    if ((bool)overrideAttribute.ConstructorArguments[1].Value)
                    {
                        hasBlobReferences = false;
                    }
                    if ((bool)overrideAttribute.ConstructorArguments[2].Value)
                    {
                        hasUnityObjectReferences = false;
                    }
                }
            }

            //fastequality is supposed to be handled later by generating all the equals fns in InjectEqualityFunctions
            //AddFastEqualityInfo(typeRef, category == TypeCategory.UnityEngineObject, caches.FastEqualityLayoutInfoCache);

            int typeIndex = m_TotalTypeCount++;
            bool isCleanupBufferElement = typeDef.Interfaces.Select(i => i.InterfaceType.Name).Contains("ICleanupBufferElementData");
            bool isCleanupSharedComponent = typeDef.Interfaces.Select(i => i.InterfaceType.Name).Contains("ICleanupSharedComponentData");
            bool isCleanupComponent = typeDef.Interfaces.Select(i => i.InterfaceType.Name).Contains("ICleanupComponentData") || isCleanupSharedComponent || isCleanupBufferElement;

            bool isEnableable = typeDef.Interfaces.Any(i => i.InterfaceType.Name.Contains("IEnableableComponent"));

            // if it's a managed component and it's not already obviously enableable, look for IEnableableComponent on
            // its base classes.
            if (isManaged && !isEnableable)
            {
                var mytype = typeDef.BaseType?.Resolve();
                while (mytype != null)
                {
                    // should switch to real checking for the interface, probably
                    if (mytype.Interfaces.Any(i => i.InterfaceType.Name.Contains("IEnableableComponent")))
                    {
                        isEnableable = true;
                        break;
                    }

                    mytype = mytype.BaseType?.Resolve();
                }
            }

            bool isChunkSerializable = isManaged || IsComponentChunkSerializable(typeRef, category, hasEntityReferences);

            // Cleanup shared components are also considered cleanup components
            if (isEnableable)
            {
                if (!(category == TypeCategory.ComponentData || category == TypeCategory.BufferData) || isCleanupComponent)
                    throw new ArgumentException($"IEnableableComponent is not supported for type {typeRef}. Only IComponentData and IBufferElementData can be disabled. Cleanup components are not supported.");
            }

            bool isTemporaryBakingType = false;
            bool isBakingOnlyType = false;
            foreach (var c in typeDef.CustomAttributes)
            {
                if (c.AttributeType.Name == "TemporaryBakingTypeAttribute")
                    isTemporaryBakingType = true;
                else if (c.AttributeType.Name == "BakingTypeAttribute")
                    isBakingOnlyType = true;
            }
            var isIEquatable = typeDef.TypeImplements(AssemblyDefinition.MainModule.ImportReference(typeof(IEquatable<>)));

            if (sizeInChunk == 0)
                typeIndex |= ZeroSizeInChunkTypeFlag;

            if (category == TypeCategory.ISharedComponentData)
                typeIndex |= SharedComponentTypeFlag;

            if (isCleanupComponent)
                typeIndex |= CleanupComponentTypeFlag;

            if (isCleanupSharedComponent)
                typeIndex |= CleanupSharedComponentTypeFlag;

            if (bufferCapacity >= 0)
                typeIndex |= BufferComponentTypeFlag;

            if (!hasEntityReferences)
                typeIndex |= HasNoEntityReferencesFlag;

            if (hasNativeContainer)
                typeIndex |= HasNativeContainerFlag;

            if (isManaged)
                typeIndex |= ManagedComponentTypeFlag;

            if (isEnableable)
                typeIndex |= EnableableComponentFlag;

            if (isRefCounted)
                typeIndex |= IRefCountedComponentFlag;

            if (isTemporaryBakingType)
                typeIndex |= TemporaryBakingTypeFlag;

            if (isBakingOnlyType)
                typeIndex |= BakingOnlyTypeFlag;

            if (isIEquatable)
                typeIndex |= IEquatableTypeFlag;

            if (!isChunkSerializable)
                typeIndex |= IsNotChunkSerializableTypeFlag;


            /* note: typegeninfo has the actual list of offsets for all the kinds of offsets,
             * whereas typeinfo just has indices into the giant array, because typeinfo is global
             * and typegeninfo is local
            */

            typeDef.MakeTypeInternal();
            var ret = new TypeGenInfo() {
                TypeReference = TypeReferenceExtensions.LaunderTypeRef(typeRef, AssemblyDefinition.MainModule),
                TypeIndex = typeIndex,
                TypeCategory = category,

                MemoryOrdering = memoryOrdering,
                StableHash = stableTypeHash,
                BufferCapacity = bufferCapacity,
                SizeInChunk = sizeInChunk,
                ElementSize = elementSize > 0 ? elementSize : sizeInChunk,
                Alignment = alignmentInBytes,
                MaxChunkCapacity = maxChunkCapacity,
                WriteGroupTypes = new HashSet<TypeReference>(),
                WriteGroupsIndex = 0, //writeGroupIndex,
                MightHaveWeakAssetReferences = hasWeakAssetReferences,
                MightHaveEntityReferences = hasEntityReferences,
                MightHaveBlobAssetReferences = hasBlobReferences,
                MightHaveUnityObjectReferences = hasUnityObjectReferences,
                EntityOffsets = entityOffsets,
                EntityOffsetIndex = m_TotalEntityOffsetCount,
                BlobAssetRefOffsets = blobAssetRefOffsets,
                BlobAssetRefOffsetIndex = m_TotalBlobAssetRefOffsetCount,
                WeakAssetRefOffsets = weakAssetRefOffsets,
                WeakAssetRefOffsetIndex = m_TotalWeakAssetRefOffsetCount,
                UnityObjectRefOffsets = unityObjectRefOffsets,
                UnityObjectRefOffsetIndex = m_TotalUnityObjectRefOffsetCount,
                AlignAndSize = isManaged ? new AlignAndSize() : TypeUtilsInstance.AlignAndSizeOfType(typeRef, 64),
            };

            m_TotalEntityOffsetCount += entityOffsets.Count;
            m_TotalBlobAssetRefOffsetCount += blobAssetRefOffsets.Count;
            m_TotalWeakAssetRefOffsetCount += weakAssetRefOffsets.Count;
            m_TotalUnityObjectRefOffsetCount += unityObjectRefOffsets.Count;

            return ret;
        }

        internal void PopulateWriteGroups(TypeGenInfoList typeGenInfoList)
        {
            var writeGroupMap = new Dictionary<TypeReference, HashSet<TypeReference>>();

            foreach (var typeGenInfo in typeGenInfoList)
            {
                var typeRef = typeGenInfo.TypeReference;
                var typeDef = typeRef.Resolve();
                foreach (var attribute in typeDef.CustomAttributes.Where(a => a.AttributeType.Name == "WriteGroupAttribute" && a.AttributeType.Namespace == "Unity.Entities"))
                {
                    if (!writeGroupMap.ContainsKey(typeRef))
                    {
                        var targetList = new HashSet<TypeReference>();
                        writeGroupMap.Add(typeRef, targetList);
                    }
                    var targetType = attribute.ConstructorArguments[0].Value as TypeReference;
                    writeGroupMap[typeRef].Add(targetType);
                }
            }

            m_TotalWriteGroupCount = 0;
            for (int i = 0; i < typeGenInfoList.Count; ++i)
            {
                var typeGenInfo = typeGenInfoList[i];

                if (writeGroupMap.TryGetValue(typeGenInfo.TypeReference, out var writeGroups))
                {
                    typeGenInfo.WriteGroupTypes = writeGroups;
                    typeGenInfo.WriteGroupsIndex = m_TotalWriteGroupCount;
                    typeGenInfoList[i] = typeGenInfo;
                    m_TotalWriteGroupCount += writeGroups.Count;
                }
            }
        }
    }
}
#endif // !DISABLE_TYPEMANAGER_ILPP
