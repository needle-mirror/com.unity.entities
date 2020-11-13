using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    // Factory methods to generate LambdaJobs source
    // All actual source code should get generated from this file
    static class EntitiesSourceFactory
    {
        // This later gets replaced with #line directives to correct place in generated source for debugging
        static string GeneratedLineTriviaToGeneratedSource() => "// __generatedline__";

        static string QueryTypeForType(this INamedTypeSymbol type) => $@"ComponentType.ReadOnly<{type.ToFullName()}>()";

        static string NoAliasAttribute(LambdaJobDescription description) =>
            description.UsesBurst ? $@"[Unity.Burst.NoAlias]" : string.Empty;

        static string BurstCompileAttribute(LambdaJobDescription description) =>
            description.UsesBurst ? $@"
                [Unity.Burst.BurstCompile(FloatMode=Unity.Burst.FloatMode.{description.FloatMode.ToString()}, FloatPrecision=Unity.Burst.FloatPrecision.{description.FloatPrecision.ToString()}, CompileSynchronously={description.BurstSynchronousCompilation.ToString().ToLower()})]" : string.Empty;

        static IEnumerable<string> DistinctQueryTypesFor(LambdaJobDescription description)
        {
            var readOnlyTypeNames = new HashSet<string>();
            var readWriteTypeNames = new HashSet<string>();

            void AddQueryType(ITypeSymbol queryType, bool isReadOnly)
            {
                if (queryType != null)
                {
                    if (!isReadOnly)
                    {
                        readOnlyTypeNames.Remove(queryType.ToFullName());
                        readWriteTypeNames.Add(queryType.ToFullName());
                    }
                    else
                    {
                        if (!readWriteTypeNames.Contains(queryType.ToFullName()) &&
                            !readOnlyTypeNames.Contains(queryType.ToFullName()))
                            readOnlyTypeNames.Add(queryType.ToFullName());
                    }
                }
            }

            foreach (var param in description.LambdaParameters)
            {
                var queryType = param.QueryType();
                if (queryType != null)
                    AddQueryType(queryType, param.QueryTypeIsReadOnly());
            }

            foreach (var allComponentType in description.WithAllTypes)
                AddQueryType(allComponentType, true);

            foreach (var sharedComponentType in description.WithSharedComponentFilterTypes)
                AddQueryType(sharedComponentType, true);

            foreach (var changeFilterType in description.WithChangeFilterTypes)
                AddQueryType(changeFilterType, true);

            return readOnlyTypeNames.Select(type => $@"ComponentType.ReadOnly<{type}>()").Concat(
                readWriteTypeNames.Select(type => $@"ComponentType.ReadWrite<{type}>()"));
        }

        static string EntityQuerySetupStatementFor(LambdaJobDescription description)
        {
            return
                $@"GetEntityQuery(
                    new EntityQueryDesc
                    {{
                        All = new ComponentType[] {{
                            {DistinctQueryTypesFor(description).Distinct().SeparateByCommaAndNewLine()}
                        }},
                        Any = new ComponentType[] {{
                            {description.WithAnyTypes.Select(type => type.QueryTypeForType()).Distinct().SeparateByCommaAndNewLine()}
                        }},
                        None = new ComponentType[] {{
                            {description.WithNoneTypes.Select(type => type.QueryTypeForType()).Distinct().SeparateByCommaAndNewLine()}
                        }},
                        Options = {description.EntityQueryOptions.GetFlags().Select(flag => $"EntityQueryOptions.{flag.ToString()}").SeparateByBinaryOr()}
                    }});";
        }

        internal static MethodDeclarationSyntax OnCreateForCompilerMethodFor(IEnumerable<LambdaJobDescription> lambdaJobDescriptions)
        {
            string EntityQuerySetup()
            {
                string StoreEntityQueryInFieldAssignments(LambdaJobDescription description)
                {
                    var assignmentsBuilder = new StringBuilder();
                    foreach (var storeInQuery in description.WithStoreEntityQueryInFieldArgumentSyntaxes)
                        assignmentsBuilder.Append($"{(IdentifierNameSyntax)storeInQuery.Expression} = ");
                    return assignmentsBuilder.ToString();
                }

                string FullEntityQuerySetup(LambdaJobDescription description)
                {
                    var entityQuerySetup = $@"{StoreEntityQueryInFieldAssignments(description)}{description.QueryName} = {EntityQuerySetupStatementFor(description)}";
                    if (description.WithChangeFilterTypes.Any())
                    {
                        entityQuerySetup +=
                            $@"{description.QueryName}.SetChangedVersionFilter(new ComponentType[{description.WithChangeFilterTypes.Count}]
				            {{
                                {description.WithChangeFilterTypes.Select(WithChangeFilterType => $"ComponentType.ReadWrite<{WithChangeFilterType}>()").SeparateByComma()}
                            }});";
                    }
                    return entityQuerySetup;
                }

                var querySetupStrings = new List<string>();
                foreach (var description in lambdaJobDescriptions)
                {
                    if (!description.HasGenericParameters && description.LambdaJobKind != LambdaJobKind.Job)
                        querySetupStrings.Add(FullEntityQuerySetup(description));
                }
                return querySetupStrings.SeparateByNewLine();
            }

            string RunWithoutJobSystemDelegateSetup()
            {
                string runWithoutJobSystemDelegateSetup = default;
                foreach (var description in lambdaJobDescriptions)
                {
                    string BurstDelegateFieldAssignment()
                    {
                        if (description.UsesBurst)
                            return $"{description.JobStructName}.s_RunWithoutJobSystemDelegateFieldBurst = InternalCompilerInterface.BurstCompile({description.JobStructName}.s_RunWithoutJobSystemDelegateFieldNoBurst);";
                        return string.Empty;
                    }
                    if (description.NeedsJobDelegateFields)
                    {
                        runWithoutJobSystemDelegateSetup += $@"
                        unsafe
                        {{
                            {description.JobStructName}.s_RunWithoutJobSystemDelegateFieldNoBurst = {description.JobStructName}.RunWithoutJobSystem;
                            {BurstDelegateFieldAssignment()}
                        }}";
                    }
                }

                return runWithoutJobSystemDelegateSetup;
            }

            var accessKeywords = "protected";

            // Access must be protected unless this assembly has InternalsVisibleTo access to Unity.Entities (in which case it should be `protected internal`)
            var currentAssembly = lambdaJobDescriptions.First().Context.Compilation.Assembly;
            var entitiesAssembly = currentAssembly.Modules.First().ReferencedAssemblySymbols.First(asm => asm.Name == "Unity.Entities");
            if (entitiesAssembly.GivesAccessTo(currentAssembly))
                accessKeywords += " internal";
            var template = $@"
            {accessKeywords} override void OnCreateForCompiler()
            {{
                base.OnCreateForCompiler();
                {EntityQuerySetup()}
                {RunWithoutJobSystemDelegateSetup()}
            }}";
            return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(template);
        }

#if GENERIC_ENTITIES_FOREACH_SUPPORT
        internal static ClassDeclarationSyntax GenericEntityQueryWrapperFor(LambdaJobDescription description)
        {
            // TODO: Need to handle multiple generic parameters
            var template = $@"
                public static class EntityQueryWrapper{description.Name}<T> {GenericParameterConstraints(description)}
                {{
                    public static bool _created = false;
                    public static EntityQuery {description.QueryName};

                    static EntityQueryWrapper{description.Name}()
                    {{
                        _created = false;
                    }}

                    public static EntityQuery GetQuery(SystemBase system)
                    {{
                        if (!_created)
                        {{
                            {description.QueryName} = system.{EntityQuerySetupStatementFor(description)}
                            _created = true;
                        }}
                        return {description.QueryName};
                    }}
                }}";

            return (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(template);
        }
#endif

        // var componentArray1 = (ComponentType1*)chunk.GetNativeArray(_componentTypeAccessor1).GetUnsafePtr();
        static string GetChunkNativeArrays(LambdaJobDescription description) =>
            description.LambdaParameters.Select(param => param.GetNativeArray()).SeparateByNewLine();

        //var rotationTypeIndex = TypeManager.GetTypeIndex<Rotation>();
        static string StructuralChanges_GetTypeIndices(LambdaJobDescription description) =>
            description.LambdaParameters.Select(param => param.StructuralChanges_GetTypeIndex()).SeparateByNewLine();

        // var rotationOriginal = _rotationFromEntity[entity]; var rotation = rotationOriginal;
        static string StructuralChanges_ReadLambdaParams(LambdaJobDescription description) =>
            description.LambdaParameters.Select(param => param.StructuralChanges_ReadLambdaParam()).SeparateByNewLine();

        // WriteComponentData<Rotation>(__this.EntityManager, entity, rotationTypeIndex, ref rotation, ref T originalrotation);";
        static string StructuralChanges_WriteBackLambdaParams(LambdaJobDescription description) =>
            description.LambdaParameters.Select(param => param.StructuralChanges_WriteBackLambdaParam()).SeparateByNewLine();

#if !GENERIC_ENTITIES_FOREACH_SUPPORT
        static string GenericArguments(LambdaJobDescription description) => string.Empty;
        static string GenericParameterConstraints(LambdaJobDescription description) => string.Empty;
#else
        // TODO: Fix to allow for more than one
        static string GenericArguments(LambdaJobDescription description) => description.HasGenericParameters ? "<T>" : "";
        static string GenericParameterConstraints(LambdaJobDescription description)
        {
            string ParamConstraintFor(ITypeSymbol symbol) => $@"{symbol.Name}";
            string GenericParameterFor(LambdaParamDescription_Generic param) =>
                $@" where T : unmanaged, {param.Constraints.Select(constraint => ParamConstraintFor(constraint)).SeparateByComma()}";
            return description.LambdaParameters.OfType<LambdaParamDescription_Generic>()
                .Select(GenericParameterFor).SeparateByComma();
        }
#endif

        internal static StructDeclarationSyntax JobStructFor(LambdaJobDescription description)
        {
            // public [ReadOnly] CapturedFieldType capturedFieldName;
            // Need to also declare these for variables used by local methods
            string CapturedVariableFields()
            {
                string FieldForCapturedVariable(LambdaCapturedVariableDescription variable) =>
                    $@"{variable.Attributes.JoinAttributes()}public {variable.Symbol.GetSymbolTypeName()} {variable.VariableFieldName};";
                return description.VariablesCaptured.Concat(description.VariablesCapturedOnlyByLocals).Select(FieldForCapturedVariable).SeparateByNewLine();
            }

            // public ComponentTypeHandle<ComponentType> _rotationTypeAccessor;
            string TypeHandleFields() => description.LambdaParameters.Select(param => param.TypeHandleField()).SeparateByNewLine();

            // public ComponentDataFromEntity<ComponentType> _rotationDataFromEntity;
            string AdditionalDataFromEntityFields() => description.AdditionalFields.Select(dataFromEntityField => dataFromEntityField.ToFieldDeclaration().ToString()).SeparateByNewLine();

            // void OriginalLambdaBody(ref ComponentType1 component1, in ComponentType2 component2) {}";
            string OriginalLambdaBody() => $@"
            {"[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]".EmitIfTrue(description.UsesBurst)}
            void OriginalLambdaBody({description.LambdaParameters.Select(param => param.LambdaBodyMethodParameter(description.UsesBurst)).SeparateByComma()}) {{}}
            {GeneratedLineTriviaToGeneratedSource()}";

            // OriginalLambdaBody(ref Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AsRef<ComponentType1>(componentArray1 + i), *(componentArray2 + i));
            string PerformLambda()
            {
                var result = string.Empty;

                result += description.LambdaParameters.Select(param => param.LambdaBodyParameterSetup()).SeparateBySemicolonAndNewLine();

                if (description.WithStructuralChangesAndLambdaBodyInSystem)
                    result += $@"__this.{description.LambdaBodyMethodName}({description.LambdaParameters.Select(param => param.StructuralChanges_LambdaBodyParameter()).SeparateByComma()});";
                else if (description.WithStructuralChanges)
                    result += $@"OriginalLambdaBody({description.LambdaParameters.Select(param => param.StructuralChanges_LambdaBodyParameter()).SeparateByComma()});";
                else
                    result += $@"OriginalLambdaBody({description.LambdaParameters.Select(param => param.LambdaBodyParameter()).SeparateByComma()});";

                return result;
            }

            string MethodsForLocalFunctions() => description.MethodsForLocalFunctions.Select(method => method.ToString()).SeparateByNewLine();

            string ExecuteMethodForJob() => $@"
            public unsafe void Execute()
            {{
                {PerformLambda()}
            }}";

            string ExecuteMethodDefault() => $@"
            public unsafe void Execute(ArchetypeChunk chunk, int batchIndex)
            {{
                {GetChunkNativeArrays(description)}

                int count = chunk.Count;
                for (int entityIndex = 0; entityIndex != count; entityIndex++)
                {{
                    {PerformLambda()}
                }}
            }}";

            string ExecuteMethodWithEntityInQueryIndex() => $@"
            public unsafe void Execute(ArchetypeChunk chunk, int batchIndex, int indexOfFirstEntityInQuery)
            {{
                {GetChunkNativeArrays(description)}

                int count = chunk.Count;
                for (int entityIndex = 0; entityIndex != count; entityIndex++)
                {{
                    int entityInQueryIndex = indexOfFirstEntityInQuery + entityIndex;
                    {PerformLambda()}
                }}
            }}";

            string ExecuteMethodForStructuralChanges() => $@"
            public unsafe void RunWithStructuralChange(EntityQuery query)
            {{
                {GeneratedLineTriviaToGeneratedSource()}
                var mask = __this.EntityManager.GetEntityQueryMask(query);
                Unity.Entities.InternalCompilerInterface.UnsafeCreateGatherEntitiesResult(ref query, out var gatherEntitiesResult);
                {StructuralChanges_GetTypeIndices(description)}

                try
                {{
                    int entityCount = gatherEntitiesResult.EntityCount;
                    for (int entityIndex = 0; entityIndex != entityCount; entityIndex++)
                    {{
                        var entity = gatherEntitiesResult.EntityBuffer[entityIndex];
                        if (mask.Matches(entity))
                        {{
                            {StructuralChanges_ReadLambdaParams(description)}
                            {PerformLambda()}
                            {StructuralChanges_WriteBackLambdaParams(description)}
                        }}
                    }}
                }}
                finally
                {{
                    Unity.Entities.InternalCompilerInterface.UnsafeReleaseGatheredEntities(ref query, ref gatherEntitiesResult);
                }}
            }}";

            string ExecuteMethodForStructuralChangesWithEntities() => $@"
            public unsafe void RunWithStructuralChange(EntityQuery query, NativeArray<Entity> withEntities)
            {{
                {GeneratedLineTriviaToGeneratedSource()}
                var mask = __this.EntityManager.GetEntityQueryMask(query);
                {StructuralChanges_GetTypeIndices(description)}

                int entityCount = withEntities.Length;
                for (int entityIndex = 0; entityIndex != entityCount; entityIndex++)
                {{
                    var entity = withEntities[entityIndex];
                    if (mask.Matches(entity))
                    {{
                        {StructuralChanges_ReadLambdaParams(description)}
                        {PerformLambda()}
                        {StructuralChanges_WriteBackLambdaParams(description)}
                    }}
                }}
            }}";

            string ExecuteMethod()
            {
                if (description.LambdaJobKind == LambdaJobKind.Job)
                    return ExecuteMethodForJob();
                else if (description.WithStructuralChanges && description.WithFilter_EntityArray == null)
                    return ExecuteMethodForStructuralChanges();
                else if (description.WithStructuralChanges && description.WithFilter_EntityArray != null)
                    return ExecuteMethodForStructuralChangesWithEntities();
                else if (description.NeedsEntityInQueryIndex)
                    return ExecuteMethodWithEntityInQueryIndex();
                else
                    return ExecuteMethodDefault();
            }

            string DisposeOnCompletionMethod()
            {
                if (!description.DisposeOnJobCompletionVariables.Any())
                    return string.Empty;

                var allDisposableFieldsAndChildren = new List<string>();
                foreach (var variable in description.DisposeOnJobCompletionVariables)
                    allDisposableFieldsAndChildren.AddRange(variable.NamesOfAllDisposableMembersIncludingOurselves());

                if (description.ScheduleMode == ScheduleMode.Run)
                {
                    return $@"
                    public void DisposeOnCompletion()
				    {{
                        {allDisposableFieldsAndChildren.Select(disposable => $"{disposable}.Dispose();").SeparateByNewLine()}
                    }}";
                }
                else
                {
                    return $@"
                    public Unity.Jobs.JobHandle DisposeOnCompletion(Unity.Jobs.JobHandle jobHandle)
				    {{
                        {allDisposableFieldsAndChildren.Select(disposable => $"jobHandle = {disposable}.Dispose(jobHandle);").SeparateByNewLine()}
                        return jobHandle;
                    }}";

                }
            }

            string JobInterface()
            {
                if (description.LambdaJobKind == LambdaJobKind.Job)
                    return " : Unity.Jobs.IJob";
                else
                {
                    if (!description.WithStructuralChanges)
                        return description.NeedsEntityInQueryIndex ? " : Unity.Entities.IJobEntityBatchWithIndex" : " : Unity.Entities.IJobEntityBatch";
                }
                return string.Empty;
            }

            var template = $@"
			{GeneratedLineTriviaToGeneratedSource()}
            {NoAliasAttribute(description)}
            {BurstCompileAttribute(description)}
            struct {description.JobStructName}{GenericArguments(description)}
            {JobInterface()}
            {GenericParameterConstraints(description)}
            {{
                {RunWithoutJobSystemDelegateFields(description).EmitIfTrue(description.NeedsJobDelegateFields)}
                {CapturedVariableFields()}
                {TypeHandleFields().EmitIfTrue(!description.WithStructuralChanges)}
                {AdditionalDataFromEntityFields()}
                {MethodsForLocalFunctions()}

                {(!description.WithStructuralChangesAndLambdaBodyInSystem ? OriginalLambdaBody() : string.Empty)}

                {ExecuteMethod()}
                {DisposeOnCompletionMethod()}

                {RunWithoutJobSystemMethod(description).EmitIfTrue(description.NeedsJobDelegateFields)}
            }}";

            var jobStructDeclaration = (StructDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(template);

            // Find lambda body in job struct template and replace rewritten lambda body into method
            if (!description.WithStructuralChangesAndLambdaBodyInSystem)
            {
                var templateLambdaMethodBody = jobStructDeclaration.DescendantNodes().OfType<MethodDeclarationSyntax>().First(
                    method => method.Identifier.ValueText == "OriginalLambdaBody").DescendantNodes().OfType<BlockSyntax>().First();
                jobStructDeclaration = jobStructDeclaration.ReplaceNode(templateLambdaMethodBody, description.RewrittenLambdaBody.WithoutPreprocessorTrivia());
            }

            return jobStructDeclaration;
        }

        static string RunWithoutJobSystemMethod(LambdaJobDescription description)
        {
            if (description.LambdaJobKind == LambdaJobKind.Entities)
            {
                var type = description.NeedsEntityInQueryIndex ? "Unity.Entities.JobEntityBatchIndexExtensions" : "Unity.Entities.JobEntityBatchExtensions";
                if (description.WithFilter_EntityArray != null)
                {
                    return $@"
                    {BurstCompileAttribute(description)}
                    public static unsafe void RunWithoutJobSystem(EntityQuery* query, Entity* limitToEntityArray, int limitToEntityArrayLength, void* jobPtr)
                    {{
                        {type}.RunWithoutJobsInternal(ref Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AsRef<{description.JobStructName}>(jobPtr), ref *query, limitToEntityArray, limitToEntityArrayLength);
                    }}";
                }
                else
                {
                    return $@"
                    {BurstCompileAttribute(description)}
                    public static unsafe void RunWithoutJobSystem(ArchetypeChunkIterator* archetypeChunkIterator, void* jobPtr)
                    {{
                        {type}.RunWithoutJobsInternal(ref Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AsRef<{description.JobStructName}>(jobPtr), ref *archetypeChunkIterator);
                    }}";
                }
            }
            else
                return $@"
                    {BurstCompileAttribute(description)}
                    public static unsafe void RunWithoutJobSystem(void* jobPtr)
                    {{
                        Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AsRef<{description.JobStructName}>(jobPtr).Execute();
                    }}";
        }

        static string RunWithoutJobSystemDelegateFields(LambdaJobDescription description)
        {
            if (description.LambdaJobKind == LambdaJobKind.Entities)
            {
                if (description.WithFilter_EntityArray != null)
                {
                    return $@"
                    internal static InternalCompilerInterface.JobEntityBatchRunWithoutJobSystemDelegateLimitEntities s_RunWithoutJobSystemDelegateFieldNoBurst;
                    {"internal static InternalCompilerInterface.JobEntityBatchRunWithoutJobSystemDelegateLimitEntities s_RunWithoutJobSystemDelegateFieldBurst;".EmitIfTrue(description.UsesBurst)}";
                }
                else
                {
                    return $@"
                    internal static InternalCompilerInterface.JobChunkRunWithoutJobSystemDelegate s_RunWithoutJobSystemDelegateFieldNoBurst;
                    {"internal static InternalCompilerInterface.JobChunkRunWithoutJobSystemDelegate s_RunWithoutJobSystemDelegateFieldBurst;".EmitIfTrue(description.UsesBurst)}";
                }
            }
            else
                return $@"
                    internal static InternalCompilerInterface.JobRunWithoutJobSystemDelegate s_RunWithoutJobSystemDelegateFieldNoBurst;
                    {"internal static InternalCompilerInterface.JobRunWithoutJobSystemDelegate s_RunWithoutJobSystemDelegateFieldBurst;".EmitIfTrue(description.UsesBurst)}";
        }

        internal static FieldDeclarationSyntax EntityQueryFieldFor(LambdaJobDescription description)
        {
            var template = $@"EntityQuery {description.QueryName};";
            return (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(template);
        }

        internal static SyntaxNode SchedulingInvocationFor(LambdaJobDescription description)
        {
            string ExecuteMethodArgs()
            {
                var argStrings = description.VariablesCaptured.Where(variable => !variable.IsThis)
                    .Select(variable => (description.ScheduleMode == ScheduleMode.Run && variable.IsWritable)
                        ? $"ref {variable.OriginalVariableName}" : variable.OriginalVariableName).ToList();

                if (description.DependencyArgument != null)
                    argStrings.Add($@"{description.DependencyArgument.ToString()}");
                if (description.WithFilter_EntityArray != null)
                    argStrings.Add($@"{description.WithFilter_EntityArray.ToString()}");

                foreach (var argumentSyntax in description.WithSharedComponentFilterArgumentSyntaxes)
                {
                    if (argumentSyntax.Expression is IdentifierNameSyntax && description.VariablesCaptured.All(variable => variable.OriginalVariableName != argumentSyntax.ToString()))
                        argStrings.Add(argumentSyntax.ToString());
                }

                return argStrings.Distinct().SeparateByComma();
            }

            var template = $@"{description.ExecuteInSystemMethodName}{GenericArguments(description)}({ExecuteMethodArgs()}));";
            return SyntaxFactory.ParseStatement(template).DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        }

        static string SharedComponentFilterInvocations(LambdaJobDescription description)
        {
            var sb = new StringBuilder();
            foreach (var argumentSyntax in description.WithSharedComponentFilterArgumentSyntaxes)
                sb.AppendLine($@"{description.QueryName}.SetSharedComponentFilter({argumentSyntax});");
            return sb.ToString();
        }

        internal static MethodDeclarationSyntax ExecuteMethodFor(LambdaJobDescription description)
        {
            string ExecuteMethodParams()
            {
                string ParamsForCapturedVariable(LambdaCapturedVariableDescription variable)
                {
                    return (description.ScheduleMode == ScheduleMode.Run && variable.IsWritable)
                        ? $@"ref {variable.Symbol.GetSymbolTypeName()} {variable.Symbol.Name}"
                        : $@"{variable.Symbol.GetSymbolTypeName()} {variable.Symbol.Name}";
                }

                var paramStrings = new List<string>();
                paramStrings.AddRange(description.VariablesCaptured.Where(variable => !variable.IsThis).Select(ParamsForCapturedVariable));
                if (description.DependencyArgument != null)
                {
                    paramStrings.Add($@"Unity.Jobs.JobHandle __inputDependency");
                }
                if (description.WithFilter_EntityArray != null)
                {
                    paramStrings.Add($@"Unity.Collections.NativeArray<Entity> __entityArray");
                }
                foreach (var argumentSyntax in description.WithSharedComponentFilterArgumentSyntaxes)
                {
                    if (argumentSyntax.Expression is IdentifierNameSyntax argumentIdentifier &&
                        description.VariablesCaptured.All(variable => variable.OriginalVariableName != argumentSyntax.ToString()))
                    {
                        var argumentSymbol = description.Model.GetSymbolInfo(argumentIdentifier);
                        paramStrings.Add($@"{argumentSymbol.Symbol.GetSymbolTypeName()} {argumentSyntax.ToString()}");
                    }
                }

                return paramStrings.Distinct().SeparateByComma();
            }

            string JobStructFieldAssignment()
            {
                var allAssignments = new List<string>();
                allAssignments.AddRange(description.VariablesCaptured.Select(variable => $@"{variable.VariableFieldName} = {variable.OriginalVariableName}"));
                if (!description.WithStructuralChanges)
                    allAssignments.AddRange(description.LambdaParameters.Select(param => param.TypeHandleAssign()));
                allAssignments.AddRange(description.AdditionalFields.Select(field => field.JobStructAssign()));
                return allAssignments.SeparateByCommaAndNewLine();
            }

            string ScheduleJobInvocation()
            {
                switch(description.ScheduleMode)
                {
                    case ScheduleMode.Run:
                    {
                        if (description.UsesBurst)
                        {
                            return $@"
                            {"CompleteDependency();".EmitIfTrue(description.ContainingSystemType == ContainingSystemType.SystemBase)}
                            var __functionPointer = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobCompilerEnabled
                                ? {description.JobStructName}.s_RunWithoutJobSystemDelegateFieldBurst
                                : {description.JobStructName}.s_RunWithoutJobSystemDelegateFieldNoBurst;
                            var __jobPtr = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref __job);
                            Unity.Entities.InternalCompilerInterface.UnsafeRunIJob(__jobPtr, __functionPointer);";
                        }
                        else
                        {
                            return $@"
                            {"CompleteDependency();".EmitIfTrue(description.ContainingSystemType == ContainingSystemType.SystemBase)}
                            __job.Execute();";
                        }
                    }

                    case ScheduleMode.Schedule:
                    {
                        if (description.DependencyArgument != null)
                            return $@"var __jobHandle = Unity.Jobs.IJobExtensions.Schedule(__job, __inputDependency);";
                        else
                            return $@"Dependency = Unity.Jobs.IJobExtensions.Schedule(__job, Dependency);";
                    }
                }
                throw new InvalidOperationException("Can't create ScheduleJobInvocation for invalid lambda description");
            }

            string ScheduleEntitiesInvocation()
            {
                string EntityQueryParameter(ArgumentSyntax entityArray, bool parallel)
                {
                    if (description.HasGenericParameters)
                    {
                        if (entityArray != null)
                            return $"EntityQueryWrapper{description.Name}<T>.GetQuery(this), __entityArray";
                        else if (parallel)
                            return $"EntityQueryWrapper{description.Name}<T>.GetQuery(this), 1";
                        else
                            return $"EntityQueryWrapper{description.Name}<T>.GetQuery(this)";
                    }
                    else
                    {
                        if (entityArray != null)
                            return $"{description.QueryName}, __entityArray";
                        else if (parallel)
                            return $"{description.QueryName}, 1";
                        else
                            return $"{description.QueryName}";
                    }
                }

                string JobEntityBatchExtensionType() =>
                    description.NeedsEntityInQueryIndex ? "Unity.Entities.JobEntityBatchIndexExtensions": "Unity.Entities.JobEntityBatchExtensions";

                switch (description.ScheduleMode)
                {
                    case ScheduleMode.Run:
                        if (description.WithStructuralChanges)
                        {
                            var entityArray = ", __entityArray".EmitIfTrue(description.WithFilter_EntityArray != null);

                            return $@"
                            {"CompleteDependency();".EmitIfTrue(description.ContainingSystemType == ContainingSystemType.SystemBase)}
                            __job.RunWithStructuralChange({description.QueryName}{entityArray});";
                        }
                        else if (description.UsesBurst)
                        {
                            var entityArray = ", __entityArray".EmitIfTrue(description.WithFilter_EntityArray != null);

                            return $@"
                            {"CompleteDependency();".EmitIfTrue(description.ContainingSystemType == ContainingSystemType.SystemBase)}
                            var __functionPointer = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobCompilerEnabled
                                ? {description.JobStructName}.s_RunWithoutJobSystemDelegateFieldBurst
                                : {description.JobStructName}.s_RunWithoutJobSystemDelegateFieldNoBurst;
                            var __jobPtr = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref __job);
                            Unity.Entities.InternalCompilerInterface.UnsafeRunJobEntityBatch(__jobPtr, {description.QueryName}{entityArray}, __functionPointer);";
                        }
                        else
                        {
                            if (description.WithFilter_EntityArray != null)
                            {
                                return $@"
                                {"CompleteDependency();".EmitIfTrue(description.ContainingSystemType == ContainingSystemType.SystemBase)}
                                {JobEntityBatchExtensionType()}.RunWithoutJobs(ref __job, {description.QueryName}, __entityArray);";
                            }
                            else
                            {
                                return $@"
                                {"CompleteDependency();".EmitIfTrue(description.ContainingSystemType == ContainingSystemType.SystemBase)}
                                {JobEntityBatchExtensionType()}.RunWithoutJobs(ref __job, {description.QueryName});";
                            }
                        }

                    // Special case where we treat Schedule as ScheduleParallel in JobComponentSystem
                    case ScheduleMode.Schedule
                        when description.ContainingSystemType == ContainingSystemType.JobComponentSystem:
                    {
                        return
                            $@"var __jobHandle = {JobEntityBatchExtensionType()}.ScheduleParallel(__job, {EntityQueryParameter(description.WithFilter_EntityArray, true)}, __inputDependency);";
                    }
                    case ScheduleMode.Schedule:
                    {
                        if (description.DependencyArgument != null)
                            return $@"var __jobHandle = {JobEntityBatchExtensionType()}.Schedule(__job, {EntityQueryParameter(description.WithFilter_EntityArray, false)}, __inputDependency);";
                        else
                            return $@"Dependency = {JobEntityBatchExtensionType()}.Schedule(__job, {EntityQueryParameter(description.WithFilter_EntityArray, false)}, Dependency);";
                    }

                    case ScheduleMode.ScheduleParallel:
                    {
                        if (description.DependencyArgument != null)
                            return $@"var __jobHandle = {JobEntityBatchExtensionType()}.ScheduleParallel(__job, {EntityQueryParameter(description.WithFilter_EntityArray, true)}, __inputDependency);";
                        else
                            return $@"Dependency = {JobEntityBatchExtensionType()}.ScheduleParallel(__job, {EntityQueryParameter(description.WithFilter_EntityArray, true)}, Dependency);";
                    }
                }

                throw new InvalidOperationException("Can't create ScheduleJobInvocation for invalid lambda description");
            }

            string ScheduleInvocation()
            {
                return description.LambdaJobKind == LambdaJobKind.Entities
                ? ScheduleEntitiesInvocation()
                : ScheduleJobInvocation();
            }

            string WriteBackCapturedVariablesAssignments()
            {
                if (description.ScheduleMode == ScheduleMode.Run)
                {
                    var writeBackStatements = new List<string>();
                    writeBackStatements.AddRange(description.VariablesCaptured.Where(variable => !variable.IsThis && variable.IsWritable)
                        .Select(variable => $@"{variable.OriginalVariableName} = __job.{variable.VariableFieldName};"));
                    return writeBackStatements.SeparateByNewLine();
                }

                return string.Empty;
            }

            string DisposeOnCompletionInvocation()
            {
                if (!description.DisposeOnJobCompletionVariables.Any())
                    return string.Empty;

                if (description.ScheduleMode == ScheduleMode.Run)
                    return $@"__job.DisposeOnCompletion();";
                else if (description.DependencyArgument != null)
                    return $@"__jobHandle = __job.DisposeOnCompletion(__jobHandle);";
                else
                    return $@"Dependency = __job.DisposeOnCompletion(Dependency);";
            }

            string ReturnType() => (description.DependencyArgument != null) ? "Unity.Jobs.JobHandle" : "void";


            var template = $@"
            {ReturnType()} {description.ExecuteInSystemMethodName}{GenericArguments(description)}({ExecuteMethodParams()}){GenericParameterConstraints(description)}
            {{
                var __job = new {description.JobStructName}{GenericArguments(description)}
                {{
                    {JobStructFieldAssignment()}
                }};
                {SharedComponentFilterInvocations(description)}
                {ScheduleInvocation()}
                {DisposeOnCompletionInvocation()}
                {WriteBackCapturedVariablesAssignments()}
                {"return __jobHandle;".EmitIfTrue(description.DependencyArgument != null)}
            }}";

            return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(template);
        }

        internal static MethodDeclarationSyntax LambdaBodyMethodFor(LambdaJobDescription description)
        {
            var template = $@"void {description.LambdaBodyMethodName}({description.LambdaParameters.Select(
                param => param.LambdaBodyMethodParameter(description.UsesBurst)).SeparateByComma()})
            {description.RewrittenLambdaBody.ToString()}";
            return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(template);
        }

        internal static FieldDeclarationSyntax ToFieldDeclaration(this DataFromEntityFieldDescription dataFromEntityFieldDescription)
        {
            var dataFromEntityType = (dataFromEntityFieldDescription.AccessorDataType == PatchableMethod.AccessorDataType.ComponentDataFromEntity)
                ? "ComponentDataFromEntity" : "BufferFromEntity";

            var accessAttribute = dataFromEntityFieldDescription.IsReadOnly ? "[Unity.Collections.ReadOnly]" : string.Empty;
            var template = $@"{accessAttribute} public {dataFromEntityType}<{dataFromEntityFieldDescription.Type.ToFullName()}> {dataFromEntityFieldDescription.ToFieldName()};";
            return (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(template);
        }

        internal static string ToFieldName(this DataFromEntityFieldDescription dataFromEntityFieldDescription) =>
            $"__{dataFromEntityFieldDescription.Type.ToValidVariableName()}_FromEntity";
    }

    static class EnumerableHelpers
    {
        public static string SeparateByComma(this IEnumerable<string> things) => string.Join(",", things.Where(s => s != null));
        public static string SeparateByCommaAndNewLine(this IEnumerable<string> things) => string.Join(",\r\n", things.Where(s => s != null));
        public static string SeparateByNewLine(this IEnumerable<string> things) => string.Join("\r\n", things.Where(s => s != null));
        public static string SeparateBySemicolonAndNewLine(this IEnumerable<string> things) => string.Join(";\r\n", things.Where(s => s != null));
        public static bool IsReadOnly(this ParameterSyntax parameter) => parameter.Modifiers.Any(mod => mod.IsKind(SyntaxKind.InKeyword));
        public static string GetModifierString(this ParameterSyntax parameter)
        {
            if (parameter.Modifiers.Any(mod => mod.IsKind(SyntaxKind.InKeyword)))
                return "in";
            else if (parameter.Modifiers.Any(mod => mod.IsKind(SyntaxKind.RefKeyword)))
                return "ref";
            else
                return "";
        }
        public static string JoinAttributes(this IEnumerable<string> attributes) => string.Join("", attributes.Where(s => s != null).Select(s => $"[{s}] "));
        public static string EmitIfTrue(this string emitString, bool someCondition) => (someCondition ? emitString : string.Empty);
    }
}
