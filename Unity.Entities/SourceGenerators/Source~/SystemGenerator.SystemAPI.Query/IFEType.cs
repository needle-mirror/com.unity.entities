using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGenerator.SystemAPI.Query
{
    struct IFEType
    {
        IReadOnlyCollection<ReturnedTupleElementDuringEnumeration> _returnedTupleElementsDuringEnumeration;

        internal IReadOnlyCollection<ReturnedTupleElementDuringEnumeration> ReturnedTupleElementsDuringEnumeration
        {
            get => _returnedTupleElementsDuringEnumeration;
            set
            {
                _returnedTupleElementsDuringEnumeration = value;
                StructDeclarationSyntax = Generate();
            }
        }

        public StructDeclarationSyntax StructDeclarationSyntax { get; private set; }
        public string TypeName { get; set; }
        public string FullyQualifiedTypeName { get; set; }
        public bool MustReturnEntityDuringIteration { get; set; }
        public AttributeData BurstCompileAttribute { get; set; }
        public bool PerformsCollectionChecks { get; set; }

        private bool UseBurst => BurstCompileAttribute != null;

        (string FullName, string Creation) GetResultType(IEnumerable<string> queryResultConstructorArgs)
        {
            string queryReturnTypeFullName;

            if (MustReturnEntityDuringIteration)
            {
                var typeParameterFullNames = ReturnedTupleElementsDuringEnumeration.Select(f => f.TypeSymbolFullName).SeparateByCommaAndSpace();
                queryReturnTypeFullName = $"Unity.Entities.QueryEnumerableWithEntity<{typeParameterFullNames}>";
                return
                (
                    queryReturnTypeFullName,
                    $"new {queryReturnTypeFullName}({queryResultConstructorArgs.SeparateByComma()})"
                );
            }

            if (ReturnedTupleElementsDuringEnumeration.Count > 1)
            {
                queryReturnTypeFullName =
                    $"({ReturnedTupleElementsDuringEnumeration.Select(fieldInfo => fieldInfo.TypeSymbolFullName).SeparateByCommaAndSpace()})";

                return (queryReturnTypeFullName, $"({queryResultConstructorArgs.SeparateByComma()})");
            }

            return (ReturnedTupleElementsDuringEnumeration.Single().TypeSymbolFullName, queryResultConstructorArgs.Single());
        }

        string GenerateQueryMethod() =>
            "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]" +
            $"{Environment.NewLine}public static Enumerator Query(global::Unity.Entities.EntityQuery entityQuery, TypeHandle typeHandle) => new Enumerator(entityQuery, typeHandle);";

        string GenerateEnumerator(string queryResultTypeName) =>
            $@"{(UseBurst ? $"[global::Unity.Burst.NoAlias]{Environment.NewLine}[{BurstCompileAttribute}]" : string.Empty)}
                public struct Enumerator : global::System.Collections.Generic.IEnumerator<{queryResultTypeName}>
                {{
                    global::Unity.Entities.Internal.InternalEntityQueryEnumerator _entityQueryEnumerator;
                    TypeHandle _typeHandle;
                    ResolvedChunk _resolvedChunk;

                    int _currentEntityIndex;
                    int _endEntityIndex;

                    public Enumerator(global::Unity.Entities.EntityQuery entityQuery, TypeHandle typeHandle)
                    {{
                        _entityQueryEnumerator = new global::Unity.Entities.Internal.InternalEntityQueryEnumerator(entityQuery);

                        _currentEntityIndex = -1;
                        _endEntityIndex = -1;

                        _typeHandle = typeHandle;
                        _resolvedChunk = default;
                    }}

                    public void Dispose() => _entityQueryEnumerator.Dispose();

                    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                    public bool MoveNext()
                    {{
                        _currentEntityIndex++;

                        {(UseBurst ? "if (global::Unity.Burst.CompilerServices.Hint.Unlikely(_currentEntityIndex >= _endEntityIndex))" : "if (_currentEntityIndex >= _endEntityIndex)")}
                        {{
                            {(UseBurst
                                ? "if (global::Unity.Burst.CompilerServices.Hint.Likely(_entityQueryEnumerator.MoveNextEntityRange(out bool movedToNewChunk, out global::Unity.Entities.ArchetypeChunk chunk, out int entityStartIndex, out int entityEndIndex)))"
                                : "if (_entityQueryEnumerator.MoveNextEntityRange(out bool movedToNewChunk, out global::Unity.Entities.ArchetypeChunk chunk, out int entityStartIndex, out int entityEndIndex))")}
                            {{
                                if (movedToNewChunk)
                                    _resolvedChunk = _typeHandle.Resolve(chunk);

                                _currentEntityIndex = entityStartIndex;
                                _endEntityIndex = entityEndIndex;
                                return true;
                            }}
                            return false;
                        }}
                        return true;
                    }}

                    public {queryResultTypeName} Current => _resolvedChunk.Get(_currentEntityIndex);

                    public Enumerator GetEnumerator() => this;
                    public void Reset() => throw new global::System.NotImplementedException();
                    object global::System.Collections.IEnumerator.Current => throw new global::System.NotImplementedException();
                }}";

        string GenerateCompleteDependenciesMethod()
        {
            var builder = new StringBuilder();
            builder.AppendLine("public static void CompleteDependencyBeforeRW(ref SystemState state) {");

            foreach (var element in ReturnedTupleElementsDuringEnumeration)
            {
                builder.AppendLine(element.Type switch
                {
                    QueryType.ManagedComponent         => $"                                state.EntityManager.CompleteDependencyBeforeRW<{element.TypeSymbolFullName}>();",
                    QueryType.UnityEngineComponent     => $"                                state.EntityManager.CompleteDependencyBeforeRW<{element.TypeArgumentFullName}>();",
                    QueryType.RefRW                    => $"                                state.EntityManager.CompleteDependencyBeforeRW<{element.TypeArgumentFullName}>();",
                    QueryType.RefRO                    => $"                                state.EntityManager.CompleteDependencyBeforeRO<{element.TypeArgumentFullName}>();",
                    QueryType.RefRO_TagComponent          => $"                             state.EntityManager.CompleteDependencyBeforeRO<{element.TypeArgumentFullName}>();",
                    QueryType.RefRW_TagComponent          => $"                             state.EntityManager.CompleteDependencyBeforeRW<{element.TypeArgumentFullName}>();",
                    QueryType.UnmanagedSharedComponent => $"                                state.EntityManager.CompleteDependencyBeforeRO<{element.TypeSymbolFullName}>();",
                    QueryType.ManagedSharedComponent   => $"                                state.EntityManager.CompleteDependencyBeforeRW<{element.TypeSymbolFullName}>();",
                    QueryType.Aspect                   => $"                                {element.TypeSymbolFullName}.CompleteDependencyBeforeRW(ref state);",
                    QueryType.DynamicBuffer            => $"                                state.EntityManager.CompleteDependencyBeforeRW<{element.TypeArgumentFullName}>();",
                    QueryType.ValueTypeComponent       => $"                                state.EntityManager.CompleteDependencyBeforeRO<{element.TypeSymbolFullName}>();",
                    QueryType.EnabledRefRW             => $"                                state.EntityManager.CompleteDependencyBeforeRW<{element.TypeArgumentFullName}>();",
                    QueryType.EnabledRefRO             => $"                                state.EntityManager.CompleteDependencyBeforeRO<{element.TypeArgumentFullName}>();",
                    QueryType.TagComponent             => "",
                    _ => throw new ArgumentOutOfRangeException()
                });
            }

            builder.AppendLine("                            }");
            return builder.ToString();
        }

        StructDeclarationSyntax Generate()
        {
            var resolvedChunk =
                NestedStruct.ResolvedChunk(ReturnedTupleElementsDuringEnumeration, MustReturnEntityDuringIteration, PerformsCollectionChecks).ToArray();
            var typeHandle =
                NestedStruct.TypeHandle(ReturnedTupleElementsDuringEnumeration, MustReturnEntityDuringIteration, PerformsCollectionChecks).ToArray();

            (NestedStruct.Field ResolvedChunkField, NestedStruct.ArgumentInReturnedType TypeHandleArgument)[] pairedFields =
                resolvedChunk.Zip(typeHandle, (e1, e2) =>
                    (ResolvedChunkField: e1.Field, TypeHandleField: e2.ArgumentWhenInitializingResolvedChunk)).ToArray();

            var resultType =
                GetResultType(queryResultConstructorArgs:
                    resolvedChunk
                        .Where(f => !f.ArgumentInReturnedTupleDuringIndexAccess.IsEmpty)
                        .Select(f => f.ArgumentInReturnedTupleDuringIndexAccess.Value));

            string generatedType =
                $@"
                   {TypeCreationHelpers.GeneratedLineTriviaToGeneratedSource}
                   {(UseBurst ? $"[global::Unity.Burst.NoAlias]{Environment.NewLine}[{BurstCompileAttribute}]" : string.Empty)}
                   readonly struct {TypeName}
                   {{
                        {(UseBurst ? $"[global::Unity.Burst.NoAlias]{Environment.NewLine}[{BurstCompileAttribute}]" : string.Empty)}
                        public struct ResolvedChunk
                        {{
                            {resolvedChunk.Select(field => field.Field.Declaration).SeparateByNewLine()}

                            [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                            public {resultType.FullName} Get(int index) {{ return {resultType.Creation}; }}
                        }}

                        {(UseBurst ? $"[global::Unity.Burst.NoAlias]{Environment.NewLine}[{BurstCompileAttribute}]" : string.Empty)}
                        public struct TypeHandle
                        {{
                            {"public global::Unity.Entities.EntityManager _entityManager;".EmitIfTrue(typeHandle.Any(f => f.Field.DependsOnEntityManagerField))}
                            {typeHandle.Select(field => field.Field.Declaration).Distinct().SeparateByNewLine()}

                            public TypeHandle(ref global::Unity.Entities.SystemState systemState, bool isReadOnly)
                            {{
                                {"_entityManager = systemState.EntityManager;".EmitIfTrue(typeHandle.Any(f => f.Field.DependsOnEntityManagerField))}
                                {typeHandle.Select(f => f.Field.AssignmentInNestedStructConstructor).Distinct().SeparateByNewLine()}
                            }}

                            public void Update(ref global::Unity.Entities.SystemState systemState)
                            {{
                                {typeHandle.Select(kvp => kvp.Field.Name)
                                    .Where(name => !string.IsNullOrEmpty(name))
                                    .Distinct()
                                    .Select(name => $"{name}.Update(ref systemState);").SeparateByNewLine()}
                            }}

                            [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                            public ResolvedChunk Resolve(global::Unity.Entities.ArchetypeChunk archetypeChunk)
                            {{
                                var resolvedChunk = new ResolvedChunk();
                                {NestedStruct.InitializeResolvedChunkInstanceInTypeHandle(pairedFields).SeparateByNewLine()}
                                return resolvedChunk;
                            }}
                        }}
                        {GenerateQueryMethod()}
                        {GenerateEnumerator(resultType.FullName)}
                        {GenerateCompleteDependenciesMethod()}
                    }}";

            return (StructDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(generatedType);
        }
    }
}
