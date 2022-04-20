using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    static partial class EntitiesSourceFactory
    {
        public static class LambdaJobs
        {
            public static StructDeclarationSyntax JobStructFor(LambdaJobDescription description)
            {
                var template = $@"
			    {Common.GeneratedLineTriviaToGeneratedSource}
                {Common.NoAliasAttribute(description)}
                {Common.BurstCompileAttribute(description)}
                {(description.NeedsUnsafe ? "unsafe " : string.Empty)}struct {description.JobStructName}
                {JobInterface()}
                {{
                    {(description.IsForDOTSRuntime ? IJobBaseMethods(description) : string.Empty)}
                    {RunWithoutJobSystemDelegateFields(description).EmitIfTrue(description.NeedsJobFunctionPointers)}
                    {EntitiesJournaling_VariableFields(description)}
                    {CapturedVariableFields()}
                    {DeclarationsForLocalFunctionsUsed()}
                    {TypeHandleFields().EmitIfTrue(!description.WithStructuralChanges)}
                    {AdditionalDataFromEntityFields()}
                    {MethodsForLocalFunctions()}

                    {(!description.WithStructuralChangesAndLambdaBodyInSystem ? OriginalLambdaBody() : string.Empty)}

                    {ExecuteMethod()}
                    {DisposeOnCompletionMethod()}

                    {RunWithoutJobSystemMethod(description).EmitIfTrue(description.NeedsJobFunctionPointers)}
                    {EntitiesJournaling_RecordChunkMethod(description)}
                    {EntitiesJournaling_RecordEntityMethod(description)}
                }}";

                var jobStructDeclaration = (StructDeclarationSyntax) SyntaxFactory.ParseMemberDeclaration(template);

                // Find lambda body in job struct template and replace rewritten lambda body into method
                if (!description.WithStructuralChangesAndLambdaBodyInSystem)
                {
                    var templateLambdaMethodBody = jobStructDeclaration.DescendantNodes()
                        .OfType<MethodDeclarationSyntax>().First(
                            method => method.Identifier.ValueText == "OriginalLambdaBody").DescendantNodes()
                        .OfType<BlockSyntax>().First();
                    jobStructDeclaration = jobStructDeclaration.ReplaceNode(templateLambdaMethodBody,
                        description.RewrittenLambdaBody.WithoutPreprocessorTrivia());
                }

                return jobStructDeclaration;

                static string GetChunkNativeArrays(LambdaJobDescription description) =>
                    description.LambdaParameters.Select(param => param.GetNativeArrayOrAccessor()).SeparateByNewLine();

                //var rotationTypeIndex = Unity.Entities.TypeManager.GetTypeIndex<Rotation>();
                static string StructuralChanges_GetTypeIndices(LambdaJobDescription description) =>
                    description.LambdaParameters.Select(param => param.StructuralChanges_GetTypeIndex(description))
                        .SeparateByNewLine();

                // var rotationOriginal = _rotationFromEntity[entity]; var rotation = rotationOriginal;
                static string StructuralChanges_ReadLambdaParams(LambdaJobDescription description) =>
                    description.LambdaParameters.Select(param => param.StructuralChanges_ReadLambdaParam())
                        .SeparateByNewLine();

                // UnsafeUnsafeWriteComponentData<Rotation>(__this.EntityManager, entity, rotationTypeIndex, ref rotation, ref T originalrotation);";
                static string StructuralChanges_WriteBackLambdaParams(LambdaJobDescription description) =>
                    description.LambdaParameters.Select(param => param.StructuralChanges_WriteBackLambdaParam())
                        .SeparateByNewLine();

                static string EntitiesJournaling_VariableFields(LambdaJobDescription description) =>
                    description.HasJournalingEnabled && description.HasJournalingRecordableParameters ?
                        new[] {
                            $@"[Unity.Collections.ReadOnly] public ulong __worldSequenceNumber;",
                            $@"[Unity.Collections.ReadOnly] public Unity.Entities.SystemHandleUntyped __executingSystem;"
                        }.SeparateByNewLine() : string.Empty;

                static string EntitiesJournaling_RecordChunkMethod(LambdaJobDescription description) =>
                    description.HasJournalingEnabled && description.HasJournalingRecordableChunkParameters ?
                        $@"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                        void EntitiesJournaling_RecordChunk(in ArchetypeChunk chunk, {EntitiesJournaling_RecordChunkMethodParams(description)})
                        {{
                            {EntitiesJournaling_RecordChunkSetComponent(description)}
                        }}" : string.Empty;

                static string EntitiesJournaling_RecordChunkMethodParams(LambdaJobDescription description) =>
                    description.HasJournalingEnabled && description.HasJournalingRecordableChunkParameters ?
                        description.LambdaParameters
                            .Select(param => param.EntitiesJournaling_RecordChunkMethodParams())
                            .SeparateByComma() : string.Empty;

                static string EntitiesJournaling_RecordChunk(LambdaJobDescription description) =>
                    description.HasJournalingEnabled && description.HasJournalingRecordableChunkParameters ?
                        $@"if (Unity.Entities.EntitiesJournaling.Enabled)
                            EntitiesJournaling_RecordChunk(in chunk, {EntitiesJournaling_RecordChunkArguments(description)});" : string.Empty;

                static string EntitiesJournaling_RecordChunkArguments(LambdaJobDescription description) =>
                    description.HasJournalingEnabled && description.HasJournalingRecordableChunkParameters ?
                        description.LambdaParameters
                            .Select(param => param.EntitiesJournaling_RecordChunkArguments())
                            .SeparateByComma() : string.Empty;

                static string EntitiesJournaling_RecordChunkSetComponent(LambdaJobDescription description) =>
                    description.HasJournalingEnabled ?
                        description.LambdaParameters.Select(param => param.EntitiesJournaling_RecordChunkSetComponent())
                            .SeparateByNewLine() : string.Empty;

                static string EntitiesJournaling_RecordEntityMethod(LambdaJobDescription description) =>
                    description.HasJournalingEnabled && description.HasJournalingRecordableEntityParameters ?
                        $@"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                        void EntitiesJournaling_RecordEntity(in Entity entity, {EntitiesJournaling_RecordEntityMethodParams(description)})
                        {{
                            {EntitiesJournaling_RecordEntitySetComponent(description)}
                        }}" : string.Empty;

                static string EntitiesJournaling_RecordEntityMethodParams(LambdaJobDescription description) =>
                    description.HasJournalingEnabled && description.HasJournalingRecordableEntityParameters ?
                        description.LambdaParameters
                            .Select(param => param.EntitiesJournaling_RecordEntityMethodParams())
                            .SeparateByComma() : string.Empty;

                static string EntitiesJournaling_RecordEntity(LambdaJobDescription description) =>
                    description.HasJournalingEnabled && description.HasJournalingRecordableEntityParameters ?
                        $@"if (Unity.Entities.EntitiesJournaling.Enabled)
                            EntitiesJournaling_RecordEntity(in entity, {EntitiesJournaling_RecordEntityArguments(description)});" : string.Empty;

                static string EntitiesJournaling_RecordEntityArguments(LambdaJobDescription description) =>
                    description.HasJournalingEnabled && description.HasJournalingRecordableEntityParameters ?
                        description.LambdaParameters
                            .Select(param => param.EntitiesJournaling_RecordEntityArguments())
                            .SeparateByComma() : string.Empty;

                static string EntitiesJournaling_RecordEntitySetComponent(LambdaJobDescription description) =>
                    description.HasJournalingEnabled ?
                        description.LambdaParameters.Select(param => param.EntitiesJournaling_RecordEntitySetComponent())
                            .SeparateByNewLine() : string.Empty;

                // public [ReadOnly] CapturedFieldType capturedFieldName;
                // Need to also declare these for variables used by local methods
                string CapturedVariableFields()
                {
                    static string FieldForCapturedVariable(LambdaCapturedVariableDescription variable) =>
                        $@"{variable.Attributes.JoinAttributes()}public {variable.Symbol.GetSymbolTypeName()} {variable.VariableFieldName};"
                    ;

                    return description.VariablesCaptured.Concat(description.VariablesCapturedOnlyByLocals)
                        .Select(FieldForCapturedVariable).SeparateByNewLine();
                }

                string DeclarationsForLocalFunctionsUsed() => description.LocalFunctionUsedInLambda.Select(localMethod => localMethod.localFunction.ToString()).SeparateByNewLine();

                // public ComponentTypeHandle<ComponentType> _rotationTypeAccessor;
                string TypeHandleFields() => description.LambdaParameters.Select(param => param.FieldInGeneratedJobEntityBatchType())
                    .SeparateByNewLine();

                // public Unity.Entities.ComponentDataFromEntity<ComponentType> _rotationDataFromEntity;
                string AdditionalDataFromEntityFields() =>
                    description.AdditionalFields
                        .Select(dataFromEntityField => ToFieldDeclaration(dataFromEntityField).ToString())
                        .SeparateByNewLine();

                // void OriginalLambdaBody(ref ComponentType1 component1, in ComponentType2 component2) {}";
                string OriginalLambdaBody() => $@"
                {"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]".EmitIfTrue(description.Burst.IsEnabled)}
                void OriginalLambdaBody({description.LambdaParameters.Select(param => param.LambdaBodyMethodParameter(description.Burst.IsEnabled)).SeparateByComma()}) {{}}
                {Common.GeneratedLineTriviaToGeneratedSource}";

                // OriginalLambdaBody(ref Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AsRef<ComponentType1>(componentArray1 + i), *(componentArray2 + i));
                string PerformLambda()
                {
                    var result = string.Empty;

                    result += description.LambdaParameters.Select(param => param.LambdaBodyParameterSetup())
                        .SeparateBySemicolonAndNewLine();

                    if (description.WithStructuralChangesAndLambdaBodyInSystem)
                        result +=
                            $@"__this.{description.LambdaBodyMethodName}({description.LambdaParameters.Select(param => param.StructuralChanges_LambdaBodyParameter()).SeparateByCommaAndNewLine()});";
                    else if (description.WithStructuralChanges)
                        result +=
                            $@"OriginalLambdaBody({description.LambdaParameters.Select(param => param.StructuralChanges_LambdaBodyParameter()).SeparateByCommaAndNewLine()});";
                    else
                        result +=
                            $@"OriginalLambdaBody({description.LambdaParameters.Select(param => param.LambdaBodyParameter()).SeparateByCommaAndNewLine()});";

                    return result;
                }

                string MethodsForLocalFunctions() => description.MethodsForLocalFunctions
                    .Select(method => method.ToString()).SeparateByNewLine();

                string ExecuteMethodForJob() =>
                    $@"
                    public void Execute()
                    {{
                        {PerformLambda()}
                    }}";

                string ExecuteMethodDefault() => $@"
                    public void Execute(Unity.Entities.ArchetypeChunk chunk, int batchIndex)
                    {{
                        {GetChunkNativeArrays(description)}

                        int count = chunk.Count;
                        for (int entityIndex = 0; entityIndex != count; entityIndex++)
                        {{
                            {PerformLambda()}
                        }}

                        {EntitiesJournaling_RecordChunk(description)}
                    }}";

                string ExecuteMethodWithEntityInQueryIndex() =>
                    $@"
                    public void Execute(Unity.Entities.ArchetypeChunk chunk, int batchIndex, int indexOfFirstEntityInQuery)
                    {{
                        {GetChunkNativeArrays(description)}

                        int count = chunk.Count;
                        for (int entityIndex = 0; entityIndex != count; entityIndex++)
                        {{
                            int entityInQueryIndex = indexOfFirstEntityInQuery + entityIndex;
                            {PerformLambda()}
                        }}

                        {EntitiesJournaling_RecordChunk(description)}
                    }}";

                string ExecuteMethodForStructuralChanges() =>
                    $@"
                    public void RunWithStructuralChange(Unity.Entities.EntityQuery query)
                    {{
                        {Common.GeneratedLineTriviaToGeneratedSource}
                        var mask = __this.EntityManager.GetEntityQueryMask(query);
                        Unity.Entities.InternalCompilerInterface.UnsafeCreateGatherEntitiesResult(ref query, out var gatherEntitiesResult);
                        {StructuralChanges_GetTypeIndices(description)}

                        try
                        {{
                            int entityCount = gatherEntitiesResult.EntityCount;
                            for (int entityIndex = 0; entityIndex != entityCount; entityIndex++)
                            {{
                                var entity = Unity.Entities.InternalCompilerInterface.UnsafeGetEntityFromGatheredEntities(ref gatherEntitiesResult, entityIndex);
                                if (mask.Matches(entity))
                                {{
                                    {StructuralChanges_ReadLambdaParams(description)}
                                    {PerformLambda()}
                                    {StructuralChanges_WriteBackLambdaParams(description)}
                                    {EntitiesJournaling_RecordEntity(description)}
                                }}
                            }}
                        }}
                        finally
                        {{
                            Unity.Entities.InternalCompilerInterface.UnsafeReleaseGatheredEntities(ref query, ref gatherEntitiesResult);
                        }}
                    }}";

                string ExecuteMethodForStructuralChangesWithEntities() =>
                    $@"
                    public void RunWithStructuralChange(Unity.Entities.EntityQuery query, NativeArray<Entity> withEntities)
                    {{
                        {Common.GeneratedLineTriviaToGeneratedSource}
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
                                {EntitiesJournaling_RecordEntity(description)}
                            }}
                        }}
                    }}";

                string ExecuteMethod()
                {
                    if (description.LambdaJobKind == LambdaJobKind.Job)
                        return ExecuteMethodForJob();
                    if (description.WithStructuralChanges && description.WithFilterEntityArray == null)
                        return ExecuteMethodForStructuralChanges();
                    if (description.WithStructuralChanges && description.WithFilterEntityArray != null)
                        return ExecuteMethodForStructuralChangesWithEntities();
                    if (description.NeedsEntityInQueryIndex)
                        return ExecuteMethodWithEntityInQueryIndex();
                    return ExecuteMethodDefault();
                }

                string DisposeOnCompletionMethod()
                {
                    if (!description.DisposeOnJobCompletionVariables.Any())
                        return string.Empty;

                    var allDisposableFieldsAndChildren = new List<string>();
                    foreach (var variable in description.DisposeOnJobCompletionVariables)
                        allDisposableFieldsAndChildren.AddRange(
                            variable.NamesOfAllDisposableMembersIncludingOurselves());

                    return description.Schedule.Mode switch
                    {
                        ScheduleMode.Run =>
                            $@"
                            public void DisposeOnCompletion()
				            {{
                                {allDisposableFieldsAndChildren.Select(disposable => $"{disposable}.Dispose();").SeparateByNewLine()}
                            }}",

                        _ => $@"
                            public Unity.Jobs.JobHandle DisposeOnCompletion(Unity.Jobs.JobHandle jobHandle)
				            {{
                                {allDisposableFieldsAndChildren.Select(disposable => $"jobHandle = {disposable}.Dispose(jobHandle);").SeparateByNewLine()}
                                return jobHandle;
                            }}"
                    };
                }

                string JobInterface()
                {
                    var jobInterface = "";
                    if (description.LambdaJobKind == LambdaJobKind.Job)
                        jobInterface = " : Unity.Jobs.IJob";
                    else if (!description.WithStructuralChanges)
                    {
                        jobInterface = description.NeedsEntityInQueryIndex
                            ? " : Unity.Entities.IJobEntityBatchWithIndex"
                            : " : Unity.Entities.IJobEntityBatch";
                    }

                    if (!string.IsNullOrEmpty(jobInterface) && description.IsForDOTSRuntime)
                        jobInterface += ", Unity.Jobs.IJobBase";

                    return jobInterface;
                }

                static string RunWithoutJobSystemMethod(LambdaJobDescription description)
                {
                    switch (description.LambdaJobKind)
                    {
                        case LambdaJobKind.Entities:
                        {
                            string type =
                                description.NeedsEntityInQueryIndex
                                    ? "Unity.Entities.JobEntityBatchIndexExtensions"
                                    : "Unity.Entities.JobEntityBatchExtensions";

                            return
                                description.WithFilterEntityArray != null
                                    ? $@"
                                    {Common.BurstCompileAttribute(description)}
                                    {Common.MonoPInvokeCallbackAttributeAttribute(description)}
                                    public static void RunWithoutJobSystem(ref EntityQuery query, global::System.IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength, global::System.IntPtr jobPtr)
                                    {{
                                        {EnterTempMemoryScope(description)}
                                        try
                                        {{
                                            {type}.RunWithoutJobsInternal(ref Unity.Entities.InternalCompilerInterface.UnsafeAsRef<{description.JobStructName}>(jobPtr), ref query, limitToEntityArrayPtr, limitToEntityArrayLength);
                                        }}
                                        finally
                                        {{
                                            {ExitTempMemoryScope(description)}
                                        }}
                                    }}"
                                    : $@"
                                    {Common.BurstCompileAttribute(description)}
                                    {Common.MonoPInvokeCallbackAttributeAttribute(description)}
                                    public static void RunWithoutJobSystem(ref Unity.Entities.ArchetypeChunkIterator archetypeChunkIterator, global::System.IntPtr jobPtr)
                                    {{
                                        {EnterTempMemoryScope(description)}
                                        try
                                        {{
                                            {type}.RunWithoutJobsInternal(ref Unity.Entities.InternalCompilerInterface.UnsafeAsRef<{description.JobStructName}>(jobPtr), ref archetypeChunkIterator);
                                        }}
                                        finally
                                        {{
                                            {ExitTempMemoryScope(description)}
                                        }}
                                    }}";
                        }

                        default:
                            return
                                $@"
                                {Common.BurstCompileAttribute(description)}
                                {Common.MonoPInvokeCallbackAttributeAttribute(description)}
                                public static void RunWithoutJobSystem(global::System.IntPtr jobPtr)
                                {{
                                    {EnterTempMemoryScope(description)}
                                    try
                                    {{
                                        Unity.Entities.InternalCompilerInterface.UnsafeAsRef<{description.JobStructName}>(jobPtr).Execute();
                                    }}
                                    finally
                                    {{
                                        {ExitTempMemoryScope(description)}
                                    }}
                                }}";
                    }
                }

                static string EnterTempMemoryScope(LambdaJobDescription description)
                {
                    return !description.IsForDOTSRuntime ? "" : "Unity.Runtime.TempMemoryScope.EnterScope();";
                }

                static string ExitTempMemoryScope(LambdaJobDescription description)
                {
                    return !description.IsForDOTSRuntime ? "" : "Unity.Runtime.TempMemoryScope.ExitScope();";
                }

                static string RunWithoutJobSystemDelegateFields(LambdaJobDescription description)
                {
                    var delegateName = description.LambdaJobKind switch
                    {
                        LambdaJobKind.Entities when description.WithFilterEntityArray != null => "Unity.Entities.InternalCompilerInterface.JobEntityBatchRunWithoutJobSystemDelegateLimitEntities",
                        LambdaJobKind.Entities => "Unity.Entities.InternalCompilerInterface.JobEntityBatchRunWithoutJobSystemDelegate",
                        LambdaJobKind.Job => "Unity.Entities.InternalCompilerInterface.JobRunWithoutJobSystemDelegate",
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    var fieldDeclaration = string.Empty;

                    /* TODO: Once Burst 1.6 lands in Entities, we should be able to just run through .Invoke everywhere as Burst will do IL patching to remove
                     the performance issues around calling .Invoke directly. */
                    if (description.InStructSystem)
                    {
                        fieldDeclaration += @"internal class FunctionPtrFieldKeyNoBurst {}";
                        fieldDeclaration += $@"internal readonly static Unity.Burst.SharedStatic<Unity.Burst.FunctionPointer<{delegateName}>> FunctionPtrFieldNoBurst =
                                        Unity.Burst.SharedStatic<Unity.Burst.FunctionPointer<{delegateName}>>.GetOrCreate<{description.JobStructName}, FunctionPtrFieldKeyNoBurst>();";
                        if (description.Burst.IsEnabled)
                        {
                            fieldDeclaration += @"internal class FunctionPtrFieldKeyBurst {}";
                            fieldDeclaration +=
                                @$"internal readonly static Unity.Burst.SharedStatic<Unity.Burst.FunctionPointer<{delegateName}>> FunctionPtrFieldBurst =
                                        Unity.Burst.SharedStatic<Unity.Burst.FunctionPointer<{delegateName}>>.GetOrCreate<{description.JobStructName}, FunctionPtrFieldKeyBurst>();";
                        }
                    }
                    else
                    {
                        fieldDeclaration = $"internal static {delegateName} FunctionPtrFieldNoBurst;";
                        if (description.Burst.IsEnabled)
                            fieldDeclaration += $"internal static {delegateName} FunctionPtrFieldBurst;";
                    }


                    return fieldDeclaration;
                }

                static FieldDeclarationSyntax ToFieldDeclaration(
                    DataFromEntityFieldDescription dataFromEntityFieldDescription)
                {
                    var dataFromEntityType = (dataFromEntityFieldDescription.AccessorDataType ==
                                              LambdaJobsPatchableMethod.AccessorDataType.ComponentDataFromEntity)
                        ? "ComponentDataFromEntity"
                        : "BufferFromEntity";

                    var accessAttribute = dataFromEntityFieldDescription.IsReadOnly
                        ? "[Unity.Collections.ReadOnly]"
                        : string.Empty;
                    var template =
                        $@"{accessAttribute} public {dataFromEntityType}<{dataFromEntityFieldDescription.Type.ToFullName()}> {dataFromEntityFieldDescription.FieldName};";
                    return (FieldDeclarationSyntax) SyntaxFactory.ParseMemberDeclaration(template);
                }

                static string IJobBaseMethods(LambdaJobDescription description)
                {
                    return @"
                    public void PrepareJobAtExecuteTimeFn_Gen(int jobIndex, global::System.IntPtr localNodes)
                    {
                        throw new global::System.NotImplementedException();
                    }

                    public void CleanupJobAfterExecuteTimeFn_Gen()
                    {
                        throw new global::System.NotImplementedException();
                    }

                    public void CleanupJobFn_Gen()
                    {
                        throw new global::System.NotImplementedException();
                    }

                    public Unity.Jobs.LowLevel.Unsafe.JobsUtility.ManagedJobForEachDelegate GetExecuteMethod_Gen()
                    {
                        throw new global::System.NotImplementedException();
                    }

                    public int GetUnmanagedJobSize_Gen()
                    {
                        throw new global::System.NotImplementedException();
                    }

                    public Unity.Jobs.LowLevel.Unsafe.JobsUtility.ManagedJobMarshalDelegate GetMarshalToBurstMethod_Gen()
                    {
                        throw new global::System.NotImplementedException();
                    }

                    public Unity.Jobs.LowLevel.Unsafe.JobsUtility.ManagedJobMarshalDelegate GetMarshalFromBurstMethod_Gen()
                    {
                        throw new global::System.NotImplementedException();
                    }

                    public int IsBursted_Gen()
                    {
                        throw new global::System.NotImplementedException();
                    }" +
                    (description.SafetyChecksEnabled ? @"
                    public int PrepareJobAtPreScheduleTimeFn_Gen(ref Unity.Development.JobsDebugger.DependencyValidator data, ref Unity.Jobs.JobHandle dependsOn, global::System.IntPtr deferredSafety)
                    {
                        throw new global::System.NotImplementedException();
                    }

                    public void PrepareJobAtPostScheduleTimeFn_Gen(ref Unity.Development.JobsDebugger.DependencyValidator data, ref Unity.Jobs.JobHandle scheduledJob)
                    {
                        throw new global::System.NotImplementedException();
                    }

                    public void PatchMinMax_Gen(Unity.Jobs.LowLevel.Unsafe.JobsUtility.MinMax param)
                    {
                        throw new global::System.NotImplementedException();
                    }

                    public int GetSafetyFieldCount()
                    {
                        throw new global::System.NotImplementedException();
                    }" : "") +
                    (description.SafetyChecksEnabled || description.DOTSRuntimeProfilerEnabled ? @"
                    public int GetJobNameIndex_Gen()
                    {
                        throw new global::System.NotImplementedException();
                    }" : "");
                }
            }

            public static MethodDeclarationSyntax LambdaBodyMethodFor(LambdaJobDescription description)
            {
                var template = $@"{(description.NeedsUnsafe ? "unsafe " : string.Empty)} void {description.LambdaBodyMethodName}({description.LambdaParameters.Select(
                    param => param.LambdaBodyMethodParameter(description.Burst.IsEnabled)).SeparateByComma()})
                {description.RewrittenLambdaBody.ToString()}";
                return (MethodDeclarationSyntax) SyntaxFactory.ParseMemberDeclaration(template);
            }

            public static MethodDeclarationSyntax CreateExecuteMethod(LambdaJobDescription description)
            {
                var dependencyArg = (description.InStructSystem) ? $"{description.SystemStateParameterName}.Dependency" : "Dependency";
                var completeDependencyStatement = (description.InStructSystem) ? $"{description.SystemStateParameterName}.CompleteDependency();" : "CompleteDependency();";

                var template =
                    $@"{(description.NeedsUnsafe ? "unsafe " : string.Empty)} {ReturnType()} {description.ExecuteInSystemMethodName}({ExecuteMethodParams()})
                    {{
                        {ComponentTypeHandleFieldUpdate()}
                        var __job = new {description.JobStructName}
                        {{
                            {JobStructFieldAssignment()}
                        }};
                        {Common.SharedComponentFilterInvocations(description)}
                        {ScheduleInvocation()}
                        {DisposeOnCompletionInvocation()}
                        {WriteBackCapturedVariablesAssignments()}
                        {"return __jobHandle;".EmitIfTrue(description.Schedule.DependencyArgument != null)}
                    }}";

                return (MethodDeclarationSyntax) SyntaxFactory.ParseMemberDeclaration(template);

                string ExecuteMethodParams()
                {
                    string ParamsForCapturedVariable(LambdaCapturedVariableDescription variable)
                    {
                        return
                            description.Schedule.Mode == ScheduleMode.Run && variable.IsWritable
                                ? $@"ref {variable.Symbol.GetSymbolTypeName()} {variable.Symbol.Name}"
                                : $@"{variable.Symbol.GetSymbolTypeName()} {variable.Symbol.Name}";
                    }

                    var paramStrings = new List<string>();
                    paramStrings.AddRange(description.VariablesCaptured.Where(variable => !variable.IsThis)
                        .Select(ParamsForCapturedVariable));
                    if (description.Schedule.DependencyArgument != null)
                    {
                        paramStrings.Add(@"Unity.Jobs.JobHandle __inputDependency");
                    }

                    if (description.WithFilterEntityArray != null)
                    {
                        paramStrings.Add($@"Unity.Collections.NativeArray<Entity> __entityArray");
                    }

                    foreach (var argument in description.AdditionalVariablesCapturedForScheduling)
                        paramStrings.Add($@"{argument.Symbol.GetSymbolTypeName()} {argument.Name}");
                    
                    if (description.InStructSystem)
                        paramStrings.Add($"ref SystemState {description.SystemStateParameterName}");

                    return paramStrings.Distinct().SeparateByComma();
                }

                string JobStructFieldAssignment()
                {
                    var allAssignments = new List<string>();
                    if (description.HasJournalingEnabled && description.HasJournalingRecordableParameters)
                    {
                        if (description.InStructSystem)
                        {
                            allAssignments.Add($"__worldSequenceNumber = {description.SystemStateParameterName}.WorldUnmanaged.SequenceNumber");
                            allAssignments.Add($"__executingSystem = {description.SystemStateParameterName}.SystemHandleUntyped");
                        }
                        else
                        {
                            allAssignments.Add($"__worldSequenceNumber = this.World.SequenceNumber");
                            allAssignments.Add($"__executingSystem = this.SystemHandleUntyped");
                        }
                    }
                    allAssignments.AddRange(description.VariablesCaptured.Select(variable =>
                        $@"{variable.VariableFieldName} = {variable.OriginalVariableName}"));
                    if (!description.WithStructuralChanges)
                        allAssignments.AddRange(description.LambdaParameters.Select(param => param.FieldAssignmentInGeneratedJobEntityBatchType(description)));
                    allAssignments.AddRange(description.AdditionalFields.Select(field => field.JobStructAssign()));
                    return allAssignments.SeparateByCommaAndNewLine();
                }

                string ComponentTypeHandleFieldUpdate() {
                    var setupStrings = new List<string>();
                    if (description is LambdaJobDescription lambdaJobDescription)
                    {
                        foreach (var lambdaParameter in lambdaJobDescription.LambdaParameters)
                        {
                            var componentTypeHandleSetupString = lambdaParameter.ComponentTypeHandleUpdateInvocation(lambdaJobDescription);
                            if (!string.IsNullOrEmpty(componentTypeHandleSetupString))
                            {
                                setupStrings.Add(componentTypeHandleSetupString);
                            }
                        }
                    }
                    return setupStrings.SeparateByNewLine();
                }

                string ScheduleJobInvocation()
                {
                    switch (description.Schedule.Mode)
                    {
                        case ScheduleMode.Run:
                        {
                            if (description.Burst.IsEnabled)
                            {
                                var setupFunctionPointer = description.InStructSystem switch
                                {
                                    true => $@"var __functionPointer = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobCompilerEnabled
                                        ? {description.JobStructName}.FunctionPtrFieldBurst.Data
                                        : {description.JobStructName}.FunctionPtrFieldNoBurst.Data;",
                                    false => $@"var __functionPointer = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobCompilerEnabled
                                        ? {description.JobStructName}.FunctionPtrFieldBurst
                                        : {description.JobStructName}.FunctionPtrFieldNoBurst;"
                                };

                                return $@"
                                        {completeDependencyStatement.EmitIfTrue(description.IsInSystemBase)}
                                        {setupFunctionPointer}
                                        Unity.Entities.InternalCompilerInterface.UnsafeRunIJob(ref __job, __functionPointer);";
                            }
                            else
                            {
                                return $@"
                                        {completeDependencyStatement.EmitIfTrue(description.IsInSystemBase)}
                                        __job.Execute();";
                            }
                        }

                        case ScheduleMode.Schedule:
                        {
                            return
                                description.Schedule.DependencyArgument != null
                                    ? "var __jobHandle = Unity.Jobs.IJobExtensions.Schedule(__job, __inputDependency);"
                                    : $"{dependencyArg} = Unity.Jobs.IJobExtensions.Schedule(__job, {dependencyArg});";
                        }
                    }

                    throw new InvalidOperationException(
                        "Can't create ScheduleJobInvocation for invalid lambda description");
                }

                string ScheduleEntitiesInvocation()
                {
                    string EntityQueryParameter()
                    {
                        return $"{description.EntityQueryFieldName}";
                    }

                    string OutputOptionalEntityArrayParameter(ArgumentSyntax entityArray)
                    {
                        if (entityArray != null) return ", __entityArray";

                        // Special case when we have WithScheduleGranularity but no entity array
                        // the only ScheduleParallel signature available with a granularity parameter we can use here is
                        // ScheduleParallel(job, query, granularity, entity array)
                        // to call it, entity array must be set to "default"
                        if (description.WithScheduleGranularityArgumentSyntaxes.Count > 0)
                        {
                            return $", default";
                        }
                        return "";
                    }

                    string OutputOptionalGranularityParameter()
                    {
                        if (description.WithScheduleGranularityArgumentSyntaxes.Count > 0)
                        {
                            return $", {description.WithScheduleGranularityArgumentSyntaxes[0]}";
                        }
                        return "";
                    }

                    string JobEntityBatchExtensionType() =>
                        description.NeedsEntityInQueryIndex
                            ? "Unity.Entities.JobEntityBatchIndexExtensions"
                            : "Unity.Entities.JobEntityBatchExtensions";

                    switch (description.Schedule.Mode)
                    {
                        case ScheduleMode.Run:
                        {
                            if (description.WithStructuralChanges)
                            {
                                var entityArray =
                                    ", __entityArray".EmitIfTrue(description.WithFilterEntityArray != null);

                                return $@"
                                {completeDependencyStatement.EmitIfTrue(description.IsInSystemBase)}
                                __job.RunWithStructuralChange({description.EntityQueryFieldName}{entityArray});";
                            }

                            else if (description.Burst.IsEnabled)
                            {
                                var scheduleMethod = description.NeedsEntityInQueryIndex ? "UnsafeRunJobEntityBatchWithIndex" : "UnsafeRunJobEntityBatch";

                                var additionalSetup = description.InStructSystem
                                    ? @$"var __functionPointer = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobCompilerEnabled
                                    ? {description.JobStructName}.FunctionPtrFieldBurst.Data
                                    : {description.JobStructName}.FunctionPtrFieldNoBurst.Data;"
                                    : @$"var __functionPointer = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobCompilerEnabled
                                    ? {description.JobStructName}.FunctionPtrFieldBurst
                                    : {description.JobStructName}.FunctionPtrFieldNoBurst;";
                                var scheduleArguments = (description.WithFilterEntityArray != null)
                                    ? $"ref __job, {description.EntityQueryFieldName}, Unity.Entities.InternalCompilerInterface.UnsafeGetEntityArrayIntPtr(__entityArray), __entityArray.Length, __functionPointer"
                                    : $"ref __job, {description.EntityQueryFieldName}, __functionPointer";

                                return $@"
                                {completeDependencyStatement.EmitIfTrue(description.IsInSystemBase)}
                                {additionalSetup}
                                Unity.Entities.InternalCompilerInterface.{scheduleMethod}({scheduleArguments});";
                            }
                            else
                            {
                                return
                                    description.WithFilterEntityArray != null
                                        ? $@"
                                            {completeDependencyStatement.EmitIfTrue(description.IsInSystemBase)}
                                            {JobEntityBatchExtensionType()}.RunWithoutJobs(ref __job, {description.EntityQueryFieldName}, __entityArray);"
                                        : $@"
                                            {completeDependencyStatement.EmitIfTrue(description.IsInSystemBase)}
                                            {JobEntityBatchExtensionType()}.RunWithoutJobs(ref __job, {description.EntityQueryFieldName});";
                            }
                        }

                        case ScheduleMode.Schedule:
                        {
                            return
                                description.Schedule.DependencyArgument != null
                                    ? $@"var __jobHandle = {JobEntityBatchExtensionType()}.Schedule(__job, {EntityQueryParameter()}{OutputOptionalEntityArrayParameter(description.WithFilterEntityArray)}, __inputDependency);"
                                    : $@"{dependencyArg} = {JobEntityBatchExtensionType()}.Schedule(__job, {EntityQueryParameter()}{OutputOptionalEntityArrayParameter(description.WithFilterEntityArray)}, {dependencyArg});";
                        }

                        case ScheduleMode.ScheduleParallel:
                        {
                            return
                                description.Schedule.DependencyArgument != null
                                    ? $@"var __jobHandle = {JobEntityBatchExtensionType()}.ScheduleParallel(__job, {EntityQueryParameter()}{OutputOptionalGranularityParameter()}{OutputOptionalEntityArrayParameter(description.WithFilterEntityArray)}, __inputDependency);"
                                    : $@"{dependencyArg} = {JobEntityBatchExtensionType()}.ScheduleParallel(__job, {EntityQueryParameter()}{OutputOptionalGranularityParameter()}{OutputOptionalEntityArrayParameter(description.WithFilterEntityArray)}, {dependencyArg});";
                        }
                    }

                    throw new InvalidOperationException(
                        "Can't create ScheduleJobInvocation for invalid lambda description");
                }

                string ScheduleInvocation()
                {
                    return description.LambdaJobKind == LambdaJobKind.Entities
                        ? ScheduleEntitiesInvocation()
                        : ScheduleJobInvocation();
                }

                string WriteBackCapturedVariablesAssignments()
                {
                    if (description.Schedule.Mode != ScheduleMode.Run)
                    {
                        return string.Empty;
                    }

                    return
                        description
                            .VariablesCaptured
                            .Where(variable => !variable.IsThis && variable.IsWritable)
                            .Select(variable => $@"{variable.OriginalVariableName} = __job.{variable.VariableFieldName};")
                            .SeparateByNewLine();
                }

                string DisposeOnCompletionInvocation()
                {
                    if (!description.DisposeOnJobCompletionVariables.Any())
                        return string.Empty;
                    if (description.Schedule.Mode == ScheduleMode.Run)
                        return @"__job.DisposeOnCompletion();";
                    if (description.Schedule.DependencyArgument != null)
                        return @"__jobHandle = __job.DisposeOnCompletion(__jobHandle);";
                    return $@"{dependencyArg} = __job.DisposeOnCompletion({dependencyArg});";
                }

                string ReturnType() =>
                    description.Schedule.DependencyArgument != null ? "Unity.Jobs.JobHandle" : "void";
            }


            public static string GenericParameterConstraints(LambdaJobDescription _) => string.Empty;
        }
    }
}
