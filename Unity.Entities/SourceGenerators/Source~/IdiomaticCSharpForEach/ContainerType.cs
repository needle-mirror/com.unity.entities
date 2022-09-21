using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.IdiomaticCSharpForEach
{
    struct ContainerType
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
        public bool PerformsCollectionChecks { get; set; }
        public bool RequiresAspectLookupField { get; set; }
        public bool AllowsDebugging { get; set; }
        public bool MustReturnEntityDuringIteration { get; set; }

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
                    $"global::System.ValueTuple<{ReturnedTupleElementsDuringEnumeration.Select(fieldInfo => fieldInfo.TypeSymbolFullName).SeparateByCommaAndSpace()}>";

                return (queryReturnTypeFullName, $"new {queryReturnTypeFullName}({queryResultConstructorArgs.SeparateByComma()})");
            }

            return (ReturnedTupleElementsDuringEnumeration.Single().TypeSymbolFullName, queryResultConstructorArgs.Single());
        }

        string GenerateQueryMethod() => @"public static Enumerator Query(Unity.Entities.EntityQuery entityQuery, TypeHandle typeHandle) => new Enumerator(entityQuery, typeHandle);";

        string GenerateEnumerator(string queryResultTypeName)
        {
            return $@"
                public struct Enumerator : global::System.Collections.Generic.IEnumerator<{queryResultTypeName}>
                {{
                    Unity.Entities.EntityQueryEnumerator _entityQueryEnumerator;
                    TypeHandle _typeHandle;
                    ResolvedChunk _resolvedChunk;

                    public Enumerator(Unity.Entities.EntityQuery entityQuery, TypeHandle typeHandle)
                    {{
                        _entityQueryEnumerator = new Unity.Entities.EntityQueryEnumerator(entityQuery);
                        _typeHandle = typeHandle;
                        _resolvedChunk = default;
                    }}

                    public void Dispose() => _entityQueryEnumerator.Dispose();

                    public bool MoveNext()
                    {{
                        if (_entityQueryEnumerator.MoveNextHotLoop()) return true;
                        return MoveNextCold();
                    }}

                    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                    bool MoveNextCold()
                    {{
                        var didMove = _entityQueryEnumerator.MoveNextColdLoop(out ArchetypeChunk chunk);
                        if (didMove)
                            _resolvedChunk = _typeHandle.Resolve(chunk);
                        return didMove;
                    }}

                    public {queryResultTypeName} Current
                    {{
                        get
                        {{
                            {"_entityQueryEnumerator.CheckDisposed();\n".EmitIfTrue(PerformsCollectionChecks || AllowsDebugging)}
                            return _resolvedChunk[_entityQueryEnumerator.IndexInChunk];
                        }}
                    }}
                public Enumerator GetEnumerator() => this;
                public void Reset() => throw new global::System.NotImplementedException();
                object global::System.Collections.IEnumerator.Current => throw new global::System.NotImplementedException();
            }}";
        }

        string GenerateCompleteDependenciesMethod()
        {
            var builder = new StringBuilder();
            builder.AppendLine("public static void CompleteDependencyBeforeRW(ref SystemState state) {");
            foreach (var typeFieldInfo in ReturnedTupleElementsDuringEnumeration)
            {
                builder.AppendLine(typeFieldInfo.Type switch
                {
                    QueryType.ManagedComponent         => $"                                state.EntityManager.CompleteDependencyBeforeRW<{typeFieldInfo.TypeSymbolFullName}>();",
                    QueryType.RefRW                    => $"                                state.EntityManager.CompleteDependencyBeforeRW<{typeFieldInfo.TypeArgumentFullName}>();",
                    QueryType.RefRO                    => $"                                state.EntityManager.CompleteDependencyBeforeRO<{typeFieldInfo.TypeArgumentFullName}>();",
                    QueryType.UnmanagedSharedComponent => $"                                state.EntityManager.CompleteDependencyBeforeRO<{typeFieldInfo.TypeSymbolFullName}>();",
                    QueryType.ManagedSharedComponent   => $"                                state.EntityManager.CompleteDependencyBeforeRW<{typeFieldInfo.TypeSymbolFullName}>();",
                    QueryType.Aspect                   => $"                                {typeFieldInfo.TypeSymbolFullName}.CompleteDependencyBeforeRW(ref state);",
                    QueryType.DynamicBuffer            => $"                                state.EntityManager.CompleteDependencyBeforeRW<{typeFieldInfo.TypeArgumentFullName}>();",
                    QueryType.ValueTypeComponent       => $"                                state.EntityManager.CompleteDependencyBeforeRO<{typeFieldInfo.TypeSymbolFullName}>();",
                    QueryType.EnabledRefRW             => $"                                state.EntityManager.CompleteDependencyBeforeRW<{typeFieldInfo.TypeArgumentFullName}>();",
                    QueryType.EnabledRefRO             => $"                                state.EntityManager.CompleteDependencyBeforeRO<{typeFieldInfo.TypeArgumentFullName}>();",
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
                NestedStruct.ResolvedChunk(ReturnedTupleElementsDuringEnumeration, MustReturnEntityDuringIteration).ToArray();
            var typeHandle =
                NestedStruct.TypeHandle(ReturnedTupleElementsDuringEnumeration, MustReturnEntityDuringIteration).ToArray();

            (NestedStruct.Field ResolvedChunkField, NestedStruct.ArgumentInReturnedType TypeHandleArgument)[] pairedFields =
                resolvedChunk.Zip(typeHandle, (e1, e2) =>
                    (ResolvedChunkField: e1.Field, TypeHandleField: e2.ArgumentWhenInitializingResolvedChunk)).ToArray();

            var resultType =
                GetResultType(queryResultConstructorArgs: resolvedChunk.Select(f => f.ArgumentInReturnedTupleDuringIndexAccess.Value));

            string generatedType =
                $@"
                   {TypeCreationHelpers.GeneratedLineTriviaToGeneratedSource}
                   readonly struct {TypeName}
                   {{
                        public struct ResolvedChunk
                        {{
                            {resolvedChunk.Select(field => field.Field.Declaration).SeparateByNewLine()}
                            public {resultType.FullName} this[int index] => {resultType.Creation};
                        }}

                        public struct TypeHandle
                        {{
                            {"public Unity.Entities.EntityManager _entityManager;".EmitIfTrue(typeHandle.Any(f => f.Field.DependsOnEntityManagerField))}
                            {typeHandle.Select(field => field.Field.Declaration).SeparateByNewLine()}

                            public TypeHandle(ref Unity.Entities.SystemState systemState, bool isReadOnly)
                            {{
                                {"_entityManager = systemState.EntityManager;".EmitIfTrue(typeHandle.Any(f => f.Field.DependsOnEntityManagerField))}
                                {typeHandle.Select(f => f.Field.AssignmentInNestedStructConstructor).SeparateByNewLine()}
                            }}

                            public void Update(ref Unity.Entities.SystemState systemState)
                            {{
                                {typeHandle.Where(
                                    kvp => !string.IsNullOrEmpty(kvp.Field.Name))
                                    .Select(kvp => $"{kvp.Field.Name}.Update(ref systemState);").SeparateByNewLine()}
                            }}

                            public ResolvedChunk Resolve(Unity.Entities.ArchetypeChunk archetypeChunk)
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
