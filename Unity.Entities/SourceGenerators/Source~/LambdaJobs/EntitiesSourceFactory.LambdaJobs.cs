using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using static Unity.Entities.SourceGen.LambdaJobs.LambdaParamDescription_EntityCommandBuffer;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    static partial class EntitiesSourceFactory
    {
        public static class LambdaJobs
        {
            public static StructDeclarationSyntax JobStructFor(LambdaJobDescription description)
            {
                var template = $@"
			    {TypeCreationHelpers.GeneratedLineTriviaToGeneratedSource}
                {Common.NoAliasAttribute(description)}
                {Common.BurstCompileAttribute(description)}
                {(description.NeedsUnsafe ? "unsafe " : string.Empty)}struct {description.JobStructName}
                {JobInterface()}
                {{
                    {(description.IsForDOTSRuntime ? IJobBaseMethods(description) : string.Empty)}
                    {(description.NeedsEntityInQueryIndex ? ChunkBaseEntityIndicesField() : string.Empty)}
                    {RunWithoutJobSystemDelegateFields(description).EmitIfTrue(description.NeedsJobFunctionPointers)}
                    {StructSystemFields()}
                    {CapturedVariableFields()}
                    {DeclarationsForLocalFunctionsUsed()}
                    {TypeHandleFields().EmitIfTrue(!description.WithStructuralChanges)}
                    {AdditionalDataLookupFields()}
                    {MethodsForLocalFunctions()}
                    {GenerateProfilerMarker(description)}

                    {(description.WithStructuralChangesAndLambdaBodyInSystem ? string.Empty : OriginalLambdaBody())}

                    {ExecuteMethod()}
                    {DisposeOnCompletionMethod()}

                    {RunWithoutJobSystemMethod(description).EmitIfTrue(description.NeedsJobFunctionPointers)}
                }}";

                static string ChunkBaseEntityIndicesField() =>
                    @"[Unity.Collections.ReadOnly]
                    [Unity.Collections.DeallocateOnJobCompletion]
                    public Unity.Collections.NativeArray<int> __ChunkBaseEntityIndices;";

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

                // var rotationOriginal = _rotationLookup[entity]; var rotation = rotationOriginal;
                static string StructuralChanges_ReadLambdaParams(LambdaJobDescription description) =>
                    description.LambdaParameters.Select(param => param.StructuralChanges_ReadLambdaParam())
                        .SeparateByNewLine();

                // UnsafeUnsafeWriteComponentData<Rotation>(__this.EntityManager, entity, rotationTypeIndex, ref rotation, ref T originalrotation);";
                static string StructuralChanges_WriteBackLambdaParams(LambdaJobDescription description) =>
                    description.LambdaParameters.Select(param => param.StructuralChanges_WriteBackLambdaParam())
                        .SeparateByNewLine();

                string StructSystemFields() =>
                    description.NeedsTimeData ? "public Unity.Core.TimeData __Time;" : string.Empty;

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
                string TypeHandleFields() => description.LambdaParameters.Select(param => param.FieldInGeneratedJobChunkType())
                    .SeparateByNewLine();

                // public Unity.Entities.ComponentLookup<ComponentType> _rotationLookup;
                string AdditionalDataLookupFields() =>
                    description.AdditionalFields
                        .Select(dataLookupField => dataLookupField.ToFieldDeclaration().ToString())
                        .SeparateByNewLine();

                // void OriginalLambdaBody(ref ComponentType1 component1, in ComponentType2 component2) {}";
                string OriginalLambdaBody() => $@"
                {"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]".EmitIfTrue(description.Burst.IsEnabled)}
                void OriginalLambdaBody({description.LambdaParameters.Select(param => param.LambdaBodyMethodParameter(description.Burst.IsEnabled)).SeparateByComma()}) {{}}
                {TypeCreationHelpers.GeneratedLineTriviaToGeneratedSource}";

                // OriginalLambdaBody(ref Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AsRef<ComponentType1>(componentArray1 + i), *(componentArray2 + i));
                string PerformLambda()
                {
                    var result = string.Empty;

                    result += description.LambdaParameters.Select(param => param.LambdaBodyParameterSetup()).SeparateBySemicolonAndNewLine();

                    if (description.WithStructuralChangesAndLambdaBodyInSystem)
                        result +=
                            $@"__this.{description.LambdaBodyMethodName}({description.LambdaParameters.Select(param => param.StructuralChanges_LambdaBodyParameter()).SeparateByCommaAndNewLine()});";
                    else if (description.WithStructuralChanges)
                        result +=
                            $@"OriginalLambdaBody({description.LambdaParameters.Select(param => param.StructuralChanges_LambdaBodyParameter())
                                .SeparateByCommaAndNewLine()});";
                    else
                        result +=
                            $@"OriginalLambdaBody({description.LambdaParameters.Select(param => param.LambdaBodyParameter())
                                .SeparateByCommaAndNewLine()});";

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

                /*
                string ExecuteMethodDefault() => $@"
                    public void Execute(Unity.Entities.ArchetypeChunk chunk, int batchIndex)
                    {{
                        {GetChunkNativeArrays(description)}

                        int count = chunk.Count;
                        for (int entityIndex = 0; entityIndex != count; entityIndex++)
                        {{
                            {PerformLambda()}
                        }}
                    }}";
                */

                string ExecuteMethodDefault() => $@"
                        [global::System.Runtime.CompilerServices.CompilerGenerated]
                        public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
                        {{
                            {GetChunkNativeArrays(description)}
                            int chunkEntityCount = chunk.ChunkEntityCount;
                            {"int matchingEntityCount = 0;".EmitIfTrue(description.NeedsEntityInQueryIndex)}
                            if (!useEnabledMask)
                            {{
                                for(var entityIndex = 0; entityIndex < chunkEntityCount; ++entityIndex)
                                {{
                                    {"var entityInQueryIndex = __ChunkBaseEntityIndices[batchIndex] + matchingEntityCount++;".EmitIfTrue(description.NeedsEntityInQueryIndex)}
                                    {PerformLambda()}
                                }}
                            }}
                            else
                            {{
                                int edgeCount = Unity.Mathematics.math.countbits(chunkEnabledMask.ULong0 ^ (chunkEnabledMask.ULong0 << 1)) +
                                                Unity.Mathematics.math.countbits(chunkEnabledMask.ULong1 ^ (chunkEnabledMask.ULong1 << 1)) - 1;
                                bool useRanges = edgeCount <= 4;
                                if (useRanges)
                                {{
                                    var enabledMask = chunkEnabledMask;
                                    int entityIndex = 0;
                                    int batchEndIndex = 0;
                                    while (EnabledBitUtility.GetNextRange(ref enabledMask, ref entityIndex, ref batchEndIndex))
                                    {{
                                        while (entityIndex < batchEndIndex)
                                        {{
                                            {"var entityInQueryIndex = __ChunkBaseEntityIndices[batchIndex] + matchingEntityCount++;".EmitIfTrue(description.NeedsEntityInQueryIndex)}
                                            {PerformLambda()}
                                            entityIndex++;
                                        }}
                                    }}
                                }}
                                else
                                {{
                                    ulong mask64 = chunkEnabledMask.ULong0;
                                    int count = Unity.Mathematics.math.min(64, chunkEntityCount);
                                    for (var entityIndex = 0; entityIndex < count; ++entityIndex)
                                    {{
                                        if ((mask64 & 1) != 0)
                                        {{
                                            {"var entityInQueryIndex = __ChunkBaseEntityIndices[batchIndex] + matchingEntityCount++;".EmitIfTrue(description.NeedsEntityInQueryIndex)}
                                            {PerformLambda()}
                                        }}
                                        mask64 >>= 1;
                                    }}
                                    mask64 = chunkEnabledMask.ULong1;
                                    for (var entityIndex = 64; entityIndex < chunkEntityCount; ++entityIndex)
                                    {{
                                        if ((mask64 & 1) != 0)
                                        {{
                                            {"var entityInQueryIndex = __ChunkBaseEntityIndices[batchIndex] + matchingEntityCount++;".EmitIfTrue(description.NeedsEntityInQueryIndex)}
                                            {PerformLambda()}
                                        }}
                                        mask64 >>= 1;
                                    }}
                                }}
                           }}
                    }}";

                /*
                string ExecuteMethodWithEntityInQueryIndex() =>
                    $@"
                    public void Execute(in Unity.Entities.ArchetypeChunk chunk,
                        int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
                    public void Execute(Unity.Entities.ArchetypeChunk chunk, int batchIndex, int indexOfFirstEntityInQuery)
                    {{
                        {GetChunkNativeArrays(description)}

                        int count = chunk.Count;
                        for (int entityIndex = 0; entityIndex != count; entityIndex++)
                        {{
                            int entityInQueryIndex = indexOfFirstEntityInQuery + entityIndex;
                            {PerformLambda()}
                        }}
                    }}";
                    */

                string ExecuteMethodForStructuralChanges() =>
                    $@"
                    public void RunWithStructuralChange(Unity.Entities.EntityQuery query)
                    {{
                        {TypeCreationHelpers.GeneratedLineTriviaToGeneratedSource}
                        var mask = query.GetEntityQueryMask();
                        Unity.Entities.InternalCompilerInterface.UnsafeCreateGatherEntitiesResult(ref query, out var gatherEntitiesResult);
                        {StructuralChanges_GetTypeIndices(description)}

                        try
                        {{
                            int entityCount = gatherEntitiesResult.EntityCount;
                            for (int entityIndex = 0; entityIndex != entityCount; entityIndex++)
                            {{
                                var entity = Unity.Entities.InternalCompilerInterface.UnsafeGetEntityFromGatheredEntities(ref gatherEntitiesResult, entityIndex);
                                if (mask.MatchesIgnoreFilter(entity))
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

                string ExecuteMethodForStructuralChangesWithEntities() =>
                    $@"
                    public void RunWithStructuralChange(Unity.Entities.EntityQuery query, NativeArray<Entity> withEntities)
                    {{
                        {TypeCreationHelpers.GeneratedLineTriviaToGeneratedSource}
                        var mask = query.GetEntityQueryMask();
                        {StructuralChanges_GetTypeIndices(description)}

                        int entityCount = withEntities.Length;
                        for (int entityIndex = 0; entityIndex != entityCount; entityIndex++)
                        {{
                            var entity = withEntities[entityIndex];
                            if (mask.MatchesIgnoreFilter(entity))
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
                    if (description.WithStructuralChanges)
                        return ExecuteMethodForStructuralChanges();
                    if (description.WithStructuralChanges && description.WithFilterEntityArray != null)
                        return ExecuteMethodForStructuralChangesWithEntities();
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
                        jobInterface = " : Unity.Entities.IJobChunk";

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
                            var jobInterfaceType = "Unity.Entities.InternalCompilerInterface.JobChunkInterface";

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
                                            {jobInterfaceType}.RunWithoutJobsInternal(ref Unity.Entities.InternalCompilerInterface.UnsafeAsRef<{description.JobStructName}>(jobPtr), ref query, limitToEntityArrayPtr, limitToEntityArrayLength);
                                        }}
                                        finally
                                        {{
                                            {ExitTempMemoryScope(description)}
                                        }}
                                    }}"
                                    : $@"
                                    {Common.BurstCompileAttribute(description)}
                                    {Common.MonoPInvokeCallbackAttributeAttribute(description)}
                                    public static void RunWithoutJobSystem(ref Unity.Entities.EntityQuery query, global::System.IntPtr jobPtr)
                                    {{
                                        {EnterTempMemoryScope(description)}
                                        try
                                        {{
                                            {jobInterfaceType}.RunWithoutJobsInternal(ref Unity.Entities.InternalCompilerInterface.UnsafeAsRef<{description.JobStructName}>(jobPtr), ref query);
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
                        LambdaJobKind.Entities when description.WithFilterEntityArray != null => "Unity.Entities.InternalCompilerInterface.JobChunkRunWithoutJobSystemDelegateLimitEntities",
                        LambdaJobKind.Entities => "Unity.Entities.InternalCompilerInterface.JobChunkRunWithoutJobSystemDelegate",
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

                static string GenerateProfilerMarker(LambdaJobDescription description)
                {
                    if (!description.ProfilerEnabled || description.IsForDOTSRuntime)
                        return "";
                    string marker = "public static readonly Unity.Profiling.ProfilerMarker s_ProfilerMarker = new Unity.Profiling.ProfilerMarker(";
                    if (description.Schedule.Mode == ScheduleMode.Run)
                    {
                        if (description.Burst.IsEnabled)
                            marker +=
                                "new Unity.Profiling.ProfilerCategory(\"Burst\", Unity.Profiling.ProfilerCategoryColor.BurstJobs), ";
                        marker += "\"";
                        marker += description.Name;
                        marker += "\");\n";
                    }
                    else
                    {
                        marker += "\"";
                        marker += description.Name;
                        if (description.Schedule.Mode == ScheduleMode.Schedule)
                            marker += ".Schedule";
                        else
                            marker += ".ScheduleParallel";
                        marker += "\");\n";
                    }

                    return marker;
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
                var temporaryEcbCreation = CreateTemporaryEcb(description);

                bool addJobHandleForProducer = description.EntityCommandBufferParameter is {Playback: {IsImmediate: false}}
                                               && description.EntityCommandBufferParameter.Playback.ScheduleMode != ScheduleMode.Run;

                var dependencyArg = (description.InStructSystem) ? $"{description.SystemStateParameterName}.Dependency" : "Dependency";
                var completeDependencyStatement = (description.InStructSystem) ? $"{description.SystemStateParameterName}.CompleteDependency();" : "CompleteDependency();";
                var emitProfilerMarker = description.ProfilerEnabled && !description.IsForDOTSRuntime;

                var template =
                    $@"{(description.NeedsUnsafe ? "unsafe " : string.Empty)} {ReturnType()} {description.ExecuteInSystemMethodName}({ExecuteMethodParams()})
                    {{
                        {ComponentTypeHandleFieldUpdate()}
                        {ComponentLookupFieldUpdate()}
                        {temporaryEcbCreation.Code}
                        var __job = new {description.JobStructName}
                        {{
                            {JobStructFieldAssignments().SeparateByCommaAndNewLine()}
                        }};
                        {Common.SharedComponentFilterInvocations(description)}
                        {"Unity.Jobs.JobHandle __jobHandle;".EmitIfTrue(description.Schedule.DependencyArgument != null)}
                        {CalculateChunkBaseEntityIndices()}
                        {$"using ({description.JobStructName}.s_ProfilerMarker.Auto()) {{".EmitIfTrue(emitProfilerMarker)}
                        {ScheduleInvocation()}
                        {"}".EmitIfTrue(emitProfilerMarker)}
                        {DisposeOnCompletionInvocation()}
                        {$"{description.EntityCommandBufferParameter?.GeneratedEcbFieldNameInSystemBaseType}.AddJobHandleForProducer(Dependency);".EmitIfTrue(addJobHandleForProducer)}
                        {$"{TemporaryJobEntityCommandBufferVariableName}.Playback(EntityManager);".EmitIfTrue(temporaryEcbCreation.Success)}
                        {$"{TemporaryJobEntityCommandBufferVariableName}.Dispose();".EmitIfTrue(temporaryEcbCreation.Success)}
                        {WriteBackCapturedVariablesAssignments()}
                        {"return __jobHandle;".EmitIfTrue(description.Schedule.DependencyArgument != null)}
                    }}";

                string CalculateChunkBaseEntityIndices()
                {
                    if (!description.NeedsEntityInQueryIndex)
                        return string.Empty;

                    if (description.Schedule.Mode == ScheduleMode.Run)
                        return $"__job.__ChunkBaseEntityIndices = {description.EntityQueryFieldName}.CalculateBaseEntityIndexArray(Unity.Collections.Allocator.TempJob);";

                    if (description.Schedule.DependencyArgument != null)
                        return @$"
                            Unity.Collections.NativeArray<int> {description.ChunkBaseEntityIndexFieldName} = {description.EntityQueryFieldName}.CalculateBaseEntityIndexArrayAsync(Unity.Collections.Allocator.TempJob, __inputDependency, out __inputDependency);
                            __job.__ChunkBaseEntityIndices = {description.ChunkBaseEntityIndexFieldName};";
                    else
                        return @$"
                            Unity.Jobs.JobHandle outHandle;
                            Unity.Collections.NativeArray<int> {description.ChunkBaseEntityIndexFieldName} = {description.EntityQueryFieldName}.CalculateBaseEntityIndexArrayAsync(Unity.Collections.Allocator.TempJob, Dependency, out outHandle);
                            __job.__ChunkBaseEntityIndices = {description.ChunkBaseEntityIndexFieldName};
                            Dependency = outHandle;";
                }

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

                IEnumerable<string> JobStructFieldAssignments()
                {
                    foreach (var capturedVariable in description.VariablesCaptured)
                        yield return $@"{capturedVariable.VariableFieldName} = {capturedVariable.OriginalVariableName}";

                    if (!description.WithStructuralChanges)
                        foreach (var param in description.LambdaParameters)
                            yield return $"{param.FieldAssignmentInGeneratedJobChunkType(description)}";

                    var systemStateAccess = description.SystemStateParameterName == null ? string.Empty : $"{description.SystemStateParameterName}.";
                    foreach (var field in description.AdditionalFields)
                        yield return field.JobStructAssign(systemStateAccess);

                    if (description.NeedsTimeData)
                        yield return $"__Time = {systemStateAccess}WorldUnmanaged.Time";
                }

                string ComponentTypeHandleFieldUpdate() =>
                    description
                        .LambdaParameters
                        .OfType<IParamRequireUpdate>()
                        .Select(c => c.FormatUpdateInvocation(description))
                        .SeparateByNewLine();

                string ComponentLookupFieldUpdate()
                {
                    //for now, only the ComponentLookup and BufferLookup will be provided update methods
                    return description.AdditionalFields
                        .Where(c => c.AccessorDataType != LambdaJobsPatchableMethod.AccessorDataType.EntityStorageInfoLookup)
                        .Select(c => c.FormatUpdateInvocation(description))
                        .SeparateByNewLine();

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
                            return $@"
                                        {completeDependencyStatement.EmitIfTrue(description.IsInSystemBase)}
                                        __job.Execute();";
                        }

                        case ScheduleMode.Schedule:
                        {
                            return
                                description.Schedule.DependencyArgument != null
                                    ? "__jobHandle = Unity.Jobs.IJobExtensions.Schedule(__job, __inputDependency);"
                                    : $"{dependencyArg} = Unity.Jobs.IJobExtensions.Schedule(__job, {dependencyArg});";
                        }
                    }

                    throw new InvalidOperationException(
                        "Can't create ScheduleJobInvocation for invalid lambda description");
                }

                string ScheduleEntitiesInvocation()
                {
                    string EntityQueryParameter() => $"{description.EntityQueryFieldName}";

                    string OutputOptionalEntityArrayParameter(ArgumentSyntax entityArray)
                    {
                        if (entityArray != null) return ", __entityArray";

                        // Special case when we have WithScheduleGranularity but no entity array
                        // the only ScheduleParallel signature available with a granularity parameter we can use here is
                        // ScheduleParallel(job, query, granularity, entity array)
                        // to call it, entity array must be set to "default"
                        return description.WithScheduleGranularityArgumentSyntaxes.Count > 0 ? ", default" : "";
                    }

                    string OutputOptionalGranularityParameter() =>
                        description.WithScheduleGranularityArgumentSyntaxes.Count > 0 ? $", {description.WithScheduleGranularityArgumentSyntaxes[0]}" : "";

                    string OutputOptionalChunkBaseIndexArrayParameter()
                    {
                        // If the job requires entityInQueryIndex, the array of per-chunk base entity indices must be passed as
                        // the last parameter of ScheduleParallel() in order to call JobsUtility.PatchBufferMinMaxRanges()
                        bool needsEntityInQueryIndex = description.NeedsEntityInQueryIndex;
                        bool isScheduleParallel = (description.Schedule.Mode == ScheduleMode.ScheduleParallel);
                        return (needsEntityInQueryIndex && isScheduleParallel) ? $", {description.ChunkBaseEntityIndexFieldName}" : "";
                    }

                    // Certain schedule paths require .Schedule/.Run calls that aren't in the IJobChunk public API,
                    // and only appear in InternalCompilerInterface
                    string JobChunkExtensionType() =>
                        (description.Schedule.Mode == ScheduleMode.Run || (description.Schedule.Mode == ScheduleMode.ScheduleParallel && description.NeedsEntityInQueryIndex))
                            ? "Unity.Entities.InternalCompilerInterface.JobChunkInterface"
                            : "Unity.Entities.JobChunkExtensions";

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

                            if (description.Burst.IsEnabled)
                            {
                                var scheduleMethod = "UnsafeRunJobChunk";

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

                            return
                                /*
                                description.WithFilterEntityArray != null
                                    ? $@"
                                            {completeDependencyStatement.EmitIfTrue(description.IsInSystemBase)}
                                            {JobChunkExtensionType()}.RunByRefWithoutJobs(ref __job, {description.EntityQueryFieldName}, __entityArray);"
                                    :*/ $@"
                                            {completeDependencyStatement.EmitIfTrue(description.IsInSystemBase)}
                                            {JobChunkExtensionType()}.RunByRefWithoutJobs(ref __job, {description.EntityQueryFieldName});";
                        }

                        case ScheduleMode.Schedule:
                        {
                            return
                                description.Schedule.DependencyArgument != null
                                    ? $@"__jobHandle = {JobChunkExtensionType()}.Schedule(__job, {EntityQueryParameter()}{OutputOptionalEntityArrayParameter(description.WithFilterEntityArray)}, __inputDependency);"
                                    : $@"{dependencyArg} = {JobChunkExtensionType()}.Schedule(__job, {EntityQueryParameter()}{OutputOptionalEntityArrayParameter(description.WithFilterEntityArray)}, {dependencyArg});";
                        }

                        case ScheduleMode.ScheduleParallel:
                        {
                            return
                                description.Schedule.DependencyArgument != null
                                    ? $@"__jobHandle = {JobChunkExtensionType()}.ScheduleParallel(__job, {EntityQueryParameter()}{OutputOptionalGranularityParameter()}{OutputOptionalEntityArrayParameter(description.WithFilterEntityArray)}, __inputDependency{OutputOptionalChunkBaseIndexArrayParameter()});"
                                    : $@"{dependencyArg} = {JobChunkExtensionType()}.ScheduleParallel(__job, {EntityQueryParameter()}{OutputOptionalGranularityParameter()}{OutputOptionalEntityArrayParameter(description.WithFilterEntityArray)}, {dependencyArg}{OutputOptionalChunkBaseIndexArrayParameter()});";
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
                    if (description.Schedule.Mode != ScheduleMode.Run)
                        return string.Empty;

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

            private static (bool Success, string Code) CreateTemporaryEcb(LambdaJobDescription description)
            {
                if (description.EntityCommandBufferParameter == null)
                    return (false, string.Empty);
                return
                    description.EntityCommandBufferParameter.Playback.IsImmediate
                    ? (true, $"Unity.Entities.EntityCommandBuffer {TemporaryJobEntityCommandBufferVariableName} = new EntityCommandBuffer(this.World.UpdateAllocator.ToAllocator);")
                        : (false, string.Empty);
            }
        }
    }
}
