using System;
using System.Collections.Generic;

namespace Unity.Entities.SourceGen.IdiomaticCSharpForEach
{
    static class NestedStruct
    {
        public readonly struct Field
        {
            public readonly string Declaration;
            public readonly string AssignmentInNestedStructConstructor;
            public readonly string Name;
            public readonly bool DependsOnEntityManagerField;

            public Field(
                string declaration,
                string name,
                string assignmentInNestedStructConstructor = null,
                bool dependsOnEntityManagerField = false)
            {
                Declaration = declaration;
                AssignmentInNestedStructConstructor = assignmentInNestedStructConstructor;
                Name = name;
                DependsOnEntityManagerField = dependsOnEntityManagerField;
            }
        }

        public readonly struct ArgumentInReturnedType
        {
            public readonly string Setup;
            public readonly string Value;
            public bool RequiresSetup => !string.IsNullOrEmpty(Setup);

            public ArgumentInReturnedType(string value, string setup = null)
            {
                Setup = setup;
                Value = value;
            }
        }

        internal static IEnumerable<(Field Field, ArgumentInReturnedType ArgumentInReturnedTupleDuringIndexAccess)>
            ResolvedChunk(IReadOnlyCollection<ReturnedTupleElementDuringEnumeration> elements, bool provideEntityAccess)
        {
            foreach (var arg in elements)
            {
                string fieldName;
                string fieldDeclaration;
                string elementInReturnedTuple;

                switch (arg.Type)
                {
                    case QueryType.Aspect:
                        fieldName = $"{arg.PreferredName}_ResolvedChunk";
                        fieldDeclaration = $"public {arg.TypeSymbolFullName}.ResolvedChunk {fieldName};";
                        elementInReturnedTuple = $"{fieldName}[index]";
                        break;
                    case QueryType.TagComponent:
                        fieldName = "";
                        fieldDeclaration = "";
                        elementInReturnedTuple = $"default({arg.TypeSymbolFullName})";
                        break;
                    case QueryType.ValueTypeComponent:
                        fieldName = $"{arg.PreferredName}_NativeArray";
                        fieldDeclaration = $"public Unity.Collections.NativeArray<{arg.TypeSymbolFullName}> {fieldName};";
                        elementInReturnedTuple = $"{fieldName}[index]";
                        break;
                    case QueryType.RefRW:
                        fieldName = $"{arg.PreferredName}_NativeArray";
                        fieldDeclaration = $"public Unity.Collections.NativeArray<{arg.TypeArgumentFullName}> {fieldName};";
                        elementInReturnedTuple = $"new RefRW<{arg.TypeArgumentFullName}>({fieldName}, index)";
                        break;
                    case QueryType.EnabledRefRW:
                        fieldName = $"{arg.PreferredName}_EnabledMask";
                        fieldDeclaration = $"public Unity.Entities.EnabledMask {fieldName};";
                        elementInReturnedTuple = $"{fieldName}.GetEnabledRefRW<{arg.TypeArgumentFullName}>(index)";
                        break;
                    case QueryType.RefRO:
                        fieldName = $"{arg.PreferredName}_NativeArray";
                        fieldDeclaration = $"public Unity.Collections.NativeArray<{arg.TypeArgumentFullName}> {fieldName};";
                        elementInReturnedTuple = $"new RefRO<{arg.TypeArgumentFullName}>({fieldName}, index)";
                        break;
                    case QueryType.EnabledRefRO:
                        fieldName = $"{arg.PreferredName}_EnabledMask";
                        fieldDeclaration = $"public Unity.Entities.EnabledMask {fieldName};";
                        elementInReturnedTuple = $"{fieldName}.GetEnabledRefRO<{arg.TypeArgumentFullName}>(index)";
                        break;
                    case QueryType.UnmanagedSharedComponent:
                    case QueryType.ManagedSharedComponent:
                        fieldName = $"{arg.PreferredName}";
                        fieldDeclaration = $"public {arg.TypeSymbolFullName} {fieldName};";
                        elementInReturnedTuple = fieldName;
                        break;
                    case QueryType.DynamicBuffer:
                        fieldName = $"{arg.PreferredName}_BufferAccessor";
                        fieldDeclaration = $"public Unity.Entities.BufferAccessor<{arg.TypeArgumentFullName}> {fieldName};";
                        elementInReturnedTuple = $"{fieldName}[index]";
                        break;
                    case QueryType.ManagedComponent:
                        fieldName = $"{arg.PreferredName}_ManagedComponentAccessor";
                        fieldDeclaration = $"public Unity.Entities.ManagedComponentAccessor<{arg.TypeSymbolFullName}> {fieldName};";
                        elementInReturnedTuple = $"{fieldName}[index]";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                yield return (new Field(fieldDeclaration, fieldName), new ArgumentInReturnedType(elementInReturnedTuple));
            }

            if (provideEntityAccess)
                yield return
                    (
                        new Field("public Unity.Collections.NativeArray<Unity.Entities.Entity> Entity_NativeArray;", "Entity_NativeArray"),
                        new ArgumentInReturnedType("Entity_NativeArray[index]")
                    );
        }

        public static IEnumerable<(Field Field, ArgumentInReturnedType ArgumentWhenInitializingResolvedChunk)>
            TypeHandle(IReadOnlyCollection<ReturnedTupleElementDuringEnumeration> elements, bool provideEntityAccess)
        {
            foreach (var arg in elements)
            {
                string fieldName;
                string fieldDeclaration;
                string fieldAssignment;
                string resolvedChunkInitializerArgument;
                string initializerArgumentSetUp = default;

                switch (arg.Type)
                {
                    case QueryType.Aspect:
                        fieldName = $"{arg.PreferredName}_AspectTypeHandle";
                        fieldDeclaration = $"{arg.TypeSymbolFullName}.TypeHandle {fieldName};";
                        fieldAssignment = $"{fieldName} = new {arg.TypeSymbolFullName}.TypeHandle(ref systemState, isReadOnly);";
                        resolvedChunkInitializerArgument = $"{fieldName}.Resolve(archetypeChunk);";
                        break;
                    case QueryType.TagComponent:
                        fieldName = "";
                        fieldDeclaration = "";
                        fieldAssignment = "";
                        resolvedChunkInitializerArgument = "";
                        break;
                    case QueryType.ValueTypeComponent:
                        fieldName = $"{arg.PreferredName}_ComponentTypeHandle_RO";
                        fieldDeclaration = $"[Unity.Collections.ReadOnly] Unity.Entities.ComponentTypeHandle<{arg.TypeSymbolFullName}> {fieldName};";
                        fieldAssignment = $"{fieldName} = systemState.GetComponentTypeHandle<{arg.TypeSymbolFullName}>(isReadOnly: true);";
                        resolvedChunkInitializerArgument = $"archetypeChunk.GetNativeArray({fieldName});";
                        break;
                    case QueryType.RefRW:
                        fieldName = $"{arg.PreferredName}_ComponentTypeHandle_RW";
                        fieldDeclaration = $"Unity.Entities.ComponentTypeHandle<{arg.TypeArgumentFullName}> {fieldName};";
                        fieldAssignment = $"{fieldName} = systemState.GetComponentTypeHandle<{arg.TypeArgumentFullName}>(isReadOnly);";
                        resolvedChunkInitializerArgument = $"archetypeChunk.GetNativeArray({fieldName});";
                        break;
                    case QueryType.EnabledRefRW:
                        fieldName = $"{arg.PreferredName}_ComponentTypeHandle_RW";
                        fieldDeclaration = $"Unity.Entities.ComponentTypeHandle<{arg.TypeArgumentFullName}> {fieldName};";
                        fieldAssignment = $"{fieldName} = systemState.GetComponentTypeHandle<{arg.TypeArgumentFullName}>(isReadOnly);";
                        resolvedChunkInitializerArgument = $"archetypeChunk.GetEnabledMask(ref {fieldName});";
                        break;
                    case QueryType.RefRO:
                        fieldName = $"{arg.PreferredName}_ComponentTypeHandle_RO";
                        fieldDeclaration = $"[Unity.Collections.ReadOnly] Unity.Entities.ComponentTypeHandle<{arg.TypeArgumentFullName}> {fieldName};";
                        fieldAssignment = $"{fieldName} = systemState.GetComponentTypeHandle<{arg.TypeArgumentFullName}>(isReadOnly: true);";
                        resolvedChunkInitializerArgument = $"archetypeChunk.GetNativeArray({fieldName});";
                        break;
                    case QueryType.EnabledRefRO:
                        fieldName = $"{arg.PreferredName}_ComponentTypeHandle_RO";
                        fieldDeclaration = $"[Unity.Collections.ReadOnly] Unity.Entities.ComponentTypeHandle<{arg.TypeArgumentFullName}> {fieldName};";
                        fieldAssignment = $"{fieldName} = systemState.GetComponentTypeHandle<{arg.TypeArgumentFullName}>(isReadOnly: true);";
                        resolvedChunkInitializerArgument = $"archetypeChunk.GetEnabledMask(ref {fieldName});";
                        break;
                    case QueryType.UnmanagedSharedComponent:
                        fieldName = $"{arg.PreferredName}_SharedComponentTypeHandle_RO";
                        fieldDeclaration = $"[Unity.Collections.ReadOnly] Unity.Entities.SharedComponentTypeHandle<{arg.TypeSymbolFullName}> {fieldName};";
                        fieldAssignment = $"{fieldName} = systemState.GetSharedComponentTypeHandle<{arg.TypeSymbolFullName}>();";
                        resolvedChunkInitializerArgument = $"{arg.PreferredName};";
                        initializerArgumentSetUp = $"var {arg.PreferredName} = archetypeChunk.GetSharedComponent({fieldName}, _entityManager);";
                        break;
                    case QueryType.ManagedSharedComponent:
                        fieldName = $"{arg.PreferredName}_SharedComponentTypeHandle_RO";
                        fieldDeclaration = $"[Unity.Collections.ReadOnly] Unity.Entities.SharedComponentTypeHandle<{arg.TypeSymbolFullName}> {fieldName};";
                        fieldAssignment = $"{fieldName} = systemState.GetSharedComponentTypeHandle<{arg.TypeSymbolFullName}>();";
                        resolvedChunkInitializerArgument = $"{arg.PreferredName};";
                        initializerArgumentSetUp = $"var {arg.PreferredName} = archetypeChunk.GetSharedComponentManaged({fieldName}, _entityManager);";
                        break;
                    case QueryType.ManagedComponent:
                        fieldName = $"{arg.PreferredName}_ManagedComponentTypeHandle_RO";
                        fieldDeclaration = $"[Unity.Collections.ReadOnly] Unity.Entities.ComponentTypeHandle<{arg.TypeSymbolFullName}> {fieldName};";
                        fieldAssignment = $"{fieldName} = systemState.EntityManager.GetComponentTypeHandle<{arg.TypeSymbolFullName}>(true);";
                        resolvedChunkInitializerArgument = $"archetypeChunk.GetManagedComponentAccessor({fieldName}, _entityManager);";
                        break;
                    case QueryType.DynamicBuffer:
                        fieldName = $"{arg.PreferredName}_BufferTypeHandle_RW";
                        fieldDeclaration = $"Unity.Entities.BufferTypeHandle<{arg.TypeArgumentFullName}> {fieldName};";
                        fieldAssignment = $"{fieldName} = systemState.GetBufferTypeHandle<{arg.TypeArgumentFullName}>(isReadOnly);";
                        resolvedChunkInitializerArgument = $"archetypeChunk.GetBufferAccessor({fieldName});";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                var nestedStructField =
                    new Field(
                        fieldDeclaration,
                        fieldName,
                        fieldAssignment,
                        dependsOnEntityManagerField: arg.Type == QueryType.ManagedSharedComponent
                                                     || arg.Type == QueryType.UnmanagedSharedComponent
                                                     || arg.Type == QueryType.ManagedComponent);

                yield return
                (
                    nestedStructField,
                    new ArgumentInReturnedType(resolvedChunkInitializerArgument, initializerArgumentSetUp)
                );
            }

            if (provideEntityAccess)
            {
                var entityField =
                    new Field(
                        "Unity.Entities.EntityTypeHandle Entity_TypeHandle;",
                        "Entity_TypeHandle",
                        "Entity_TypeHandle = systemState.GetEntityTypeHandle();");
                yield return (entityField, new ArgumentInReturnedType("archetypeChunk.GetNativeArray(Entity_TypeHandle);"));
            }
        }

        internal static IEnumerable<string> InitializeResolvedChunkInstanceInTypeHandle(
            IEnumerable<(Field ResolvedChunkField, ArgumentInReturnedType Argument)> pairedFields)
        {
            foreach (var pair in pairedFields)
            {
                if (string.IsNullOrEmpty(pair.ResolvedChunkField.Name))
                    continue;

                if (pair.Argument.RequiresSetup)
                    yield return pair.Argument.Setup;

                yield return $"resolvedChunk.{pair.ResolvedChunkField.Name} = {pair.Argument.Value}";
            }
        }
    }
}
