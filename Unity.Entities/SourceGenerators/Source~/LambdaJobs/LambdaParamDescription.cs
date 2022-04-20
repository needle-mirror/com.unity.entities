using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    interface IParamDescription
    {
        public ParameterSyntax Syntax { get; }
        public ITypeSymbol TypeSymbol { get; }
        public ITypeSymbol EntityQueryTypeSymbol { get; }
        public string Name { get; }
        public bool IsByRef();
    }

    interface IManagedComponentParamDescription : IParamDescription
    {
    }

    interface ISharedComponentParamDescription : IParamDescription
    {
    }

    interface IComponentParamDescription : IParamDescription
    {
    }

    interface IDynamicBufferParamDescription : IParamDescription
    {
    }

    // E.g. writing Entities.ForEach((ref MyComponent a, ref MyComponent b) => {}), where MyComponent is an IComponentData type, is not allowed.
    // On the other hand, we allow two int parameters, e.g. when writing Entities.ForEach((Entity entity, int entityInQueryIndex, int nativeThreadIndex) => {}).
    interface IAllowDuplicateTypes
    {
    }

    abstract class LambdaParamDescription
    {
        public ParameterSyntax Syntax { get; }
        public ITypeSymbol TypeSymbol { get; }
        public ITypeSymbol EntityQueryTypeSymbol { get; set; }
        public string Name { get => Syntax.Identifier.ToString(); }
        public string JobName { get; set; }
        public string ComponentTypeHandleFieldName { get; set; }

        public bool IsByRef() => Syntax.GetModifierString() == "ref";
        internal virtual bool QueryTypeIsReadOnly() => false;
        internal virtual string FieldAssignmentInGeneratedJobEntityBatchType(LambdaJobDescription lambdaJobDescription) => null;
        internal virtual string FieldInGeneratedJobEntityBatchType() => null;
        internal virtual string GetNativeArrayOrAccessor() => null;

        internal LambdaParamDescription(ParameterSyntax syntax, ITypeSymbol typeSymbol)
        {
            Syntax = syntax;
            TypeSymbol = typeSymbol;
            EntityQueryTypeSymbol = typeSymbol;
        }

        internal static LambdaParamDescription From(SystemGeneratorContext context, ParameterSyntax param)
        {
            var typeSymbol = context.SemanticModel.GetTypeInfo(param.Type).Type;
            if (typeSymbol.InheritsFromInterface("Unity.Entities.ISharedComponentData"))
                return new LambdaParamDescription_SharedComponent(param, typeSymbol);
            else if (typeSymbol.IsDynamicBuffer())
                return new LambdaParamDescription_DynamicBuffer(param, typeSymbol);
            else if (typeSymbol.Is("Unity.Entities.Entity"))
                return new LambdaParamDescription_Entity(param, typeSymbol);
            else if (typeSymbol.IsInt())
            {
                switch (param.Identifier.ValueText)
                {
                    case "entityInQueryIndex":
                        return new LambdaParamDescription_EntityInQueryIndex(param, typeSymbol);
                    case "nativeThreadIndex":
                        return new LambdaParamDescription_NativeThreadIndex(param, typeSymbol);
                    default:
                        LambdaJobsErrors.DC0014(context, param.GetLocation(), param.Identifier.ValueText, new[] {"entityInQueryIndex", "nativeThreadIndex"});
                        return null;
                }
            }
            else if (typeSymbol.IsValueType)
            {
                if (typeSymbol.InheritsFromInterface("Unity.Entities.IComponentData"))
                {
                    // TODO: we can probably loosen this restriction with source generators (and allow generic IComponentData within reason), needs tests and validation
                    if (typeSymbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.TypeArguments.Any())
                    {
                        LambdaJobsErrors.DC0050(context, param.GetLocation(), typeSymbol.Name);
                        return null;
                    }
                    else
                    {
                        if (!typeSymbol.GetMembers().OfType<IFieldSymbol>().Any())
                            return new LambdaParamDescription_TagComponent(param, typeSymbol);
                        else
                            return new LambdaParamDescription_Component(param, typeSymbol);
                    }
                }
                else
                {
                    if (typeSymbol.InheritsFromInterface("Unity.Entities.IBufferElementData"))
                        LambdaJobsErrors.DC0033(context, param.GetLocation(), param.Identifier.ValueText, typeSymbol.Name);
                    else
                    {
                        if (typeSymbol is ITypeParameterSymbol)
                            LambdaJobsErrors.DC0050(context, param.GetLocation(), typeSymbol.Name);
                        else
                            LambdaJobsErrors.DC0021(context, param.GetLocation(), param.Identifier.ValueText, typeSymbol.Name);
                    }
                    return null;
                }
            }
            else if (typeSymbol.InheritsFromInterface("Unity.Entities.IComponentData") || typeSymbol.InheritsFromType("UnityEngine.Object"))
            {
                // TODO: we can probably loosen this restriction with source generators (and allow generic IComponentData within reason), needs tests and validation
                if (typeSymbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.TypeArguments.Any())
                {
                    LambdaJobsErrors.DC0050(context, param.GetLocation(), typeSymbol.Name);
                    return null;
                }
                return new LambdaParamDescription_ManagedComponent(param, typeSymbol);
            }
            else
            {
                LambdaJobsErrors.DC0005(context, param.GetLocation(), param.Identifier.ToString(), typeSymbol.ToDisplayString());
                return null;
            }
        }

        internal virtual string LambdaBodyMethodParameter(bool usesBurst) => null;
        internal virtual string LambdaBodyParameterSetup() => null;
        internal virtual string LambdaBodyParameter() => null;
        internal virtual string StructuralChanges_ReadLambdaParam() => null;
        internal virtual string StructuralChanges_WriteBackLambdaParam() => null;
        internal virtual string StructuralChanges_LambdaBodyParameter() => null;
        internal virtual string StructuralChanges_GetTypeIndex(LambdaJobDescription description) => null;
        internal virtual string ComponentTypeHandleFieldDeclaration() => null;
        internal virtual string ComponentTypeHandleUpdateInvocation(LambdaJobDescription description) => null;

        internal virtual string EntitiesJournaling_RecordChunkMethodParams() => null;
        internal virtual string EntitiesJournaling_RecordChunkArguments() => null;
        internal virtual string EntitiesJournaling_RecordChunkSetComponent() => null;
        internal virtual string EntitiesJournaling_RecordEntityMethodParams() => null;
        internal virtual string EntitiesJournaling_RecordEntityArguments() => null;
        internal virtual string EntitiesJournaling_RecordEntitySetComponent() => null;
    }

    class LambdaParamDescription_Component : LambdaParamDescription, IComponentParamDescription
    {
        internal LambdaParamDescription_Component(ParameterSyntax syntax, ITypeSymbol typeSymbol) : base(syntax, typeSymbol) {}
        internal override string FieldInGeneratedJobEntityBatchType() => $@"{(Syntax.IsReadOnly() ? "[Unity.Collections.ReadOnly] " : "")}public Unity.Entities.ComponentTypeHandle<{TypeSymbol.ToFullName()}> __{Syntax.Identifier}TypeHandle;";
        internal virtual ITypeSymbol QueryType() => TypeSymbol;
        internal override bool QueryTypeIsReadOnly() => Syntax.IsReadOnly();
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"{"[Unity.Burst.NoAlias] ".EmitIfTrue(usesBurst)}{Syntax.GetModifierString()} {TypeSymbol.ToFullName()} {Syntax.Identifier}";
        internal override string GetNativeArrayOrAccessor()
        {
            return QueryTypeIsReadOnly()
                ? $@"var {Syntax.Identifier}ArrayPtr = Unity.Entities.InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr<{TypeSymbol.ToFullName()}>(chunk, __{Syntax.Identifier}TypeHandle);"
                : $@"var {Syntax.Identifier}ArrayPtr = Unity.Entities.InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr<{TypeSymbol.ToFullName()}>(chunk, __{Syntax.Identifier}TypeHandle);";
        }
        internal override string LambdaBodyParameter()
        {
            return Syntax.GetModifierString() switch
            {
                "ref" => $@"ref Unity.Entities.InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<{TypeSymbol.ToFullName()}>({Syntax.Identifier}ArrayPtr, entityIndex)",
                "in" => $@"in Unity.Entities.InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<{TypeSymbol.ToFullName()}>({Syntax.Identifier}ArrayPtr, entityIndex)",
                _ => $@"Unity.Entities.InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<{TypeSymbol.ToFullName()}>({Syntax.Identifier}ArrayPtr, entityIndex)"
            };
        }

        internal override string FieldAssignmentInGeneratedJobEntityBatchType(LambdaJobDescription lambdaJobDescription) =>
            $@"__{Syntax.Identifier}TypeHandle = {ComponentTypeHandleFieldName}";

        internal override string StructuralChanges_GetTypeIndex(LambdaJobDescription description) =>
            $@"var {Syntax.Identifier}TypeIndex = Unity.Entities.TypeManager.GetTypeIndex<{TypeSymbol.ToFullName()}>();";
        internal override string StructuralChanges_ReadLambdaParam() =>
            $@"var {Syntax.Identifier} = Unity.Entities.InternalCompilerInterface.GetComponentData<{TypeSymbol.ToFullName()}>(__this.EntityManager, entity, {Syntax.Identifier}TypeIndex, out var original{Syntax.Identifier});";
        internal override string StructuralChanges_WriteBackLambdaParam() =>
            Syntax.IsReadOnly() ? null : $@"Unity.Entities.InternalCompilerInterface.WriteComponentData<{TypeSymbol.ToFullName()}>(__this.EntityManager, entity, {Syntax.Identifier}TypeIndex, ref {Syntax.Identifier}, ref original{Syntax.Identifier});";
        internal override string StructuralChanges_LambdaBodyParameter() =>
            $@"{Syntax.GetModifierString()} {Syntax.Identifier}";
        internal override string ComponentTypeHandleFieldDeclaration() =>
            $@"Unity.Entities.ComponentTypeHandle<{TypeSymbol.ToFullName()}> {ComponentTypeHandleFieldName};";

        internal override string ComponentTypeHandleUpdateInvocation(LambdaJobDescription description)
        {
            if (description.InStructSystem)
                return $@"{ComponentTypeHandleFieldName}.Update(ref {description.SystemStateParameterName});";
            else
                return $@"{ComponentTypeHandleFieldName}.Update(this);";
        }

        internal override string EntitiesJournaling_RecordChunkMethodParams() =>
            !QueryTypeIsReadOnly() ? $@"in Unity.Entities.ComponentTypeHandle<{TypeSymbol.ToFullName()}> {Syntax.Identifier}TypeHandle, global::System.IntPtr {Syntax.Identifier}ArrayPtr" : null;
        internal override string EntitiesJournaling_RecordChunkArguments() =>
            !QueryTypeIsReadOnly() ? $@"in __{Syntax.Identifier}TypeHandle, {Syntax.Identifier}ArrayPtr" : null;
        internal override string EntitiesJournaling_RecordChunkSetComponent() =>
            !QueryTypeIsReadOnly() ? $@"Unity.Entities.InternalCompilerInterface.EntitiesJournaling_RecordSetComponentData(__worldSequenceNumber, in __executingSystem, chunk, {Syntax.Identifier}TypeHandle, {Syntax.Identifier}ArrayPtr);" : null;
    }

    class LambdaParamDescription_TagComponent : LambdaParamDescription, IComponentParamDescription
    {
        internal LambdaParamDescription_TagComponent(ParameterSyntax syntax, ITypeSymbol typeSymbol) : base(syntax, typeSymbol) {}
        internal override bool QueryTypeIsReadOnly() => true;
        internal override string LambdaBodyMethodParameter(bool usesBurst) =>
            $@"{"[Unity.Burst.NoAlias] ".EmitIfTrue(usesBurst)}{Syntax.GetModifierString()} {TypeSymbol.ToFullName()} {Syntax.Identifier}";
        internal override string LambdaBodyParameterSetup() => $@"{TypeSymbol.ToFullName()} {Syntax.Identifier}Local = default;";
        internal override string LambdaBodyParameter() => $"{Syntax.GetModifierString()} {Syntax.Identifier}Local";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"{Syntax.GetModifierString()} {Syntax.Identifier}Local";
    }

    class LambdaParamDescription_ManagedComponent : LambdaParamDescription, IManagedComponentParamDescription
    {
        internal LambdaParamDescription_ManagedComponent(ParameterSyntax syntax, ITypeSymbol typeSymbol) : base(syntax, typeSymbol) {}
        internal override string FieldInGeneratedJobEntityBatchType() => $@"public Unity.Entities.ComponentTypeHandle<{TypeSymbol.ToFullName()}> __{Syntax.Identifier}TypeHandle;";
        internal override bool QueryTypeIsReadOnly() => Syntax.IsReadOnly();
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"{TypeSymbol.ToFullName()} {Syntax.Identifier}";
        internal override string GetNativeArrayOrAccessor() =>
            $@"var {Syntax.Identifier}Accessor = chunk.GetManagedComponentAccessor(__{Syntax.Identifier}TypeHandle, __this.EntityManager);";
        internal override string LambdaBodyParameter() => $@"{Syntax.Identifier}Accessor[entityIndex]";
        internal override string FieldAssignmentInGeneratedJobEntityBatchType(LambdaJobDescription lambdaJobDescription) =>
            $@"__{Syntax.Identifier}TypeHandle = this.EntityManager.GetComponentTypeHandle<{TypeSymbol.ToFullName()}>({(Syntax.IsReadOnly() ? "true" : "false")})";

        internal override string StructuralChanges_GetTypeIndex(LambdaJobDescription description) =>
            description.HasJournalingEnabled && description.HasJournalingRecordableEntityParameters ? $@"var {Syntax.Identifier}TypeIndex = Unity.Entities.TypeManager.GetTypeIndex<{TypeSymbol.ToFullName()}>();" : string.Empty;
        internal override string StructuralChanges_ReadLambdaParam() =>
            $@"var {Syntax.Identifier} = __this.EntityManager.GetComponentObject<{TypeSymbol.ToFullName()}>(entity);";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"{Syntax.Identifier}";

        internal override string EntitiesJournaling_RecordChunkMethodParams() =>
            !QueryTypeIsReadOnly() ? $@"in Unity.Entities.ComponentTypeHandle<{TypeSymbol.ToFullName()}> {Syntax.Identifier}TypeHandle" : null;
        internal override string EntitiesJournaling_RecordChunkArguments() =>
            !QueryTypeIsReadOnly() ? $@"in __{Syntax.Identifier}TypeHandle" : null;
        internal override string EntitiesJournaling_RecordChunkSetComponent() =>
            !QueryTypeIsReadOnly() ? $@"Unity.Entities.InternalCompilerInterface.EntitiesJournaling_RecordSetComponentObject(__worldSequenceNumber, in __executingSystem, chunk, {Syntax.Identifier}TypeHandle);" : null;
        internal override string EntitiesJournaling_RecordEntityMethodParams() =>
            !QueryTypeIsReadOnly() ? $@"int {Syntax.Identifier}TypeIndex" : null;
        internal override string EntitiesJournaling_RecordEntityArguments() =>
            !QueryTypeIsReadOnly() ? $@"{Syntax.Identifier}TypeIndex" : null;
        internal override string EntitiesJournaling_RecordEntitySetComponent() =>
            !QueryTypeIsReadOnly() ? $@"Unity.Entities.InternalCompilerInterface.EntitiesJournaling_RecordSetComponentObject<{TypeSymbol.ToFullName()}>(__worldSequenceNumber, in __executingSystem, entity, {Syntax.Identifier}TypeIndex);" : null;
    }

    class LambdaParamDescription_SharedComponent : LambdaParamDescription, ISharedComponentParamDescription
    {
        internal LambdaParamDescription_SharedComponent(ParameterSyntax syntax, ITypeSymbol typeSymbol) : base(syntax, typeSymbol) {}
        internal override string FieldInGeneratedJobEntityBatchType() =>
            $@"{(Syntax.IsReadOnly() ? "[Unity.Collections.ReadOnly] " : "")}public Unity.Entities.SharedComponentTypeHandle<{TypeSymbol.ToFullName()}> __{Syntax.Identifier}TypeHandle;";
        internal override bool QueryTypeIsReadOnly() => Syntax.IsReadOnly();
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"{Syntax.GetModifierString()} {TypeSymbol.ToFullName()} {Syntax.Identifier}";
        internal override string GetNativeArrayOrAccessor() =>
            $@"var __{Syntax.Identifier}Data = chunk.GetSharedComponentData(__{Syntax.Identifier}TypeHandle, __this.EntityManager);";
        internal override string LambdaBodyParameter() => $"__{Syntax.Identifier}Data";
        internal override string FieldAssignmentInGeneratedJobEntityBatchType(LambdaJobDescription lambdaJobDescription) => $@"__{Syntax.Identifier}TypeHandle = GetSharedComponentTypeHandle<{TypeSymbol.ToFullName()}>()";

        internal override string StructuralChanges_GetTypeIndex(LambdaJobDescription description) =>
            description.HasJournalingEnabled && description.HasJournalingRecordableEntityParameters ? $@"var {Syntax.Identifier}TypeIndex = Unity.Entities.TypeManager.GetTypeIndex<{TypeSymbol.ToFullName()}>();" : string.Empty;
        internal override string StructuralChanges_ReadLambdaParam() =>
            $@"var {Syntax.Identifier} = __this.EntityManager.GetSharedComponentData<{TypeSymbol.ToFullName()}>(entity);";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"{Syntax.GetModifierString()} {Syntax.Identifier}";

        internal override string EntitiesJournaling_RecordChunkMethodParams() =>
            !QueryTypeIsReadOnly() ? $@"in Unity.Entities.SharedComponentTypeHandle<{TypeSymbol.ToFullName()}> {Syntax.Identifier}TypeHandle" : null;
        internal override string EntitiesJournaling_RecordChunkArguments() =>
            !QueryTypeIsReadOnly() ? $@"in __{Syntax.Identifier}TypeHandle" : null;
        internal override string EntitiesJournaling_RecordChunkSetComponent() =>
            !QueryTypeIsReadOnly() ? $@"Unity.Entities.InternalCompilerInterface.EntitiesJournaling_RecordSetSharedComponentData(__worldSequenceNumber, in __executingSystem, chunk, {Syntax.Identifier}TypeHandle);" : null;
        internal override string EntitiesJournaling_RecordEntityMethodParams() =>
            !QueryTypeIsReadOnly() ? $@"int {Syntax.Identifier}TypeIndex" : null;
        internal override string EntitiesJournaling_RecordEntityArguments() =>
            !QueryTypeIsReadOnly() ? $@"{Syntax.Identifier}TypeIndex" : null;
        internal override string EntitiesJournaling_RecordEntitySetComponent() =>
            !QueryTypeIsReadOnly() ? $@"Unity.Entities.InternalCompilerInterface.EntitiesJournaling_RecordSetSharedComponentData<{TypeSymbol.ToFullName()}>(__worldSequenceNumber, in __executingSystem, entity, {Syntax.Identifier}TypeIndex);" : null;
    }

    class LambdaParamDescription_DynamicBuffer : LambdaParamDescription, IDynamicBufferParamDescription
    {
        ITypeSymbol _bufferGenericArgumentType;
        internal LambdaParamDescription_DynamicBuffer(ParameterSyntax syntax, ITypeSymbol typeSymbol) : base(syntax, typeSymbol)
        {
            var namedTypeSymbol = (INamedTypeSymbol)typeSymbol;
            _bufferGenericArgumentType = namedTypeSymbol.TypeArguments.First();
            EntityQueryTypeSymbol = _bufferGenericArgumentType;
        }
        internal override string FieldInGeneratedJobEntityBatchType() =>
            $@"public BufferTypeHandle<{_bufferGenericArgumentType}> __{Syntax.Identifier}TypeHandle;";
        internal override bool QueryTypeIsReadOnly() => Syntax.IsReadOnly();
        internal override string LambdaBodyMethodParameter(bool usesBurst) =>
            $@"DynamicBuffer<{_bufferGenericArgumentType}> {Syntax.Identifier}";
        internal override string GetNativeArrayOrAccessor() =>
            $@"var {Syntax.Identifier}Accessor = chunk.GetBufferAccessor(__{Syntax.Identifier}TypeHandle);";
        internal override string LambdaBodyParameter() => $@"{Syntax.Identifier}Accessor[entityIndex]";

        internal override string FieldAssignmentInGeneratedJobEntityBatchType(LambdaJobDescription lambdaJobDescription)
        {
            if (lambdaJobDescription.InStructSystem)
                return $@"__{Syntax.Identifier}TypeHandle = {lambdaJobDescription.SystemStateParameterName}.GetBufferTypeHandle<{_bufferGenericArgumentType}>({(Syntax.IsReadOnly() ? "true" : "")})";
            else
                return $@"__{Syntax.Identifier}TypeHandle = GetBufferTypeHandle<{_bufferGenericArgumentType}>({(Syntax.IsReadOnly() ? "true" : "")})";
        }

        internal override string StructuralChanges_GetTypeIndex(LambdaJobDescription description) =>
            description.HasJournalingEnabled && description.HasJournalingRecordableEntityParameters ? $@"var {Syntax.Identifier}TypeIndex = Unity.Entities.TypeManager.GetTypeIndex<{_bufferGenericArgumentType}>();" : string.Empty;
        internal override string StructuralChanges_ReadLambdaParam() =>
            $@"var {Syntax.Identifier} = __this.EntityManager.GetBuffer<{_bufferGenericArgumentType}>(entity);";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"{Syntax.Identifier}";

        internal override string EntitiesJournaling_RecordChunkMethodParams() =>
            !QueryTypeIsReadOnly() ? $@"in Unity.Entities.BufferTypeHandle<{_bufferGenericArgumentType}> {Syntax.Identifier}TypeHandle" : null;
        internal override string EntitiesJournaling_RecordChunkArguments() =>
            !QueryTypeIsReadOnly() ? $@"in __{Syntax.Identifier}TypeHandle" : null;
        internal override string EntitiesJournaling_RecordChunkSetComponent() =>
            !QueryTypeIsReadOnly() ? $@"Unity.Entities.InternalCompilerInterface.EntitiesJournaling_RecordSetBuffer(__worldSequenceNumber, in __executingSystem, chunk, {Syntax.Identifier}TypeHandle);" : null;
        internal override string EntitiesJournaling_RecordEntityMethodParams() =>
            !QueryTypeIsReadOnly() ? $@"int {Syntax.Identifier}TypeIndex" : null;
        internal override string EntitiesJournaling_RecordEntityArguments() =>
            !QueryTypeIsReadOnly() ? $@"{Syntax.Identifier}TypeIndex" : null;
        internal override string EntitiesJournaling_RecordEntitySetComponent() =>
            !QueryTypeIsReadOnly() ? $@"Unity.Entities.InternalCompilerInterface.EntitiesJournaling_RecordSetBuffer<{_bufferGenericArgumentType}>(__worldSequenceNumber, in __executingSystem, entity, {Syntax.Identifier}TypeIndex);" : null;
    }

    class LambdaParamDescription_Entity : LambdaParamDescription, IAllowDuplicateTypes
    {
        internal LambdaParamDescription_Entity(ParameterSyntax syntax, ITypeSymbol typeSymbol) : base(syntax, typeSymbol) {}
        internal override string FieldInGeneratedJobEntityBatchType() => $@"[Unity.Collections.ReadOnly] public Unity.Entities.EntityTypeHandle __{Syntax.Identifier}TypeHandle;";
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"Unity.Entities.Entity {Syntax.Identifier}";
        internal override string LambdaBodyParameter() =>
            $@"Unity.Entities.InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<Unity.Entities.Entity>(__{Syntax.Identifier}ArrayPtr, entityIndex)";
        internal override string GetNativeArrayOrAccessor() =>
            $@"var __{Syntax.Identifier}ArrayPtr = Unity.Entities.InternalCompilerInterface.UnsafeGetChunkEntityArrayIntPtr(chunk, __{Syntax.Identifier}TypeHandle);";

        internal override string FieldAssignmentInGeneratedJobEntityBatchType(LambdaJobDescription lambdaJobDescription)
        {
            if (lambdaJobDescription.InStructSystem)
                return $@"__{Syntax.Identifier}TypeHandle = {lambdaJobDescription.SystemStateParameterName}.GetEntityTypeHandle()";
            else
                return $@"__{Syntax.Identifier}TypeHandle = GetEntityTypeHandle()";
        }

        internal override string StructuralChanges_LambdaBodyParameter() => $@"entity";
    }

    class LambdaParamDescription_EntityInQueryIndex : LambdaParamDescription, IAllowDuplicateTypes
    {
        internal LambdaParamDescription_EntityInQueryIndex(ParameterSyntax syntax, ITypeSymbol typeSymbol) : base(syntax, typeSymbol) { }
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"int {Syntax.Identifier}";
        internal override string LambdaBodyParameter() => $@"entityInQueryIndex";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"entityIndex";
    }

    class LambdaParamDescription_NativeThreadIndex : LambdaParamDescription, IAllowDuplicateTypes
    {
        internal LambdaParamDescription_NativeThreadIndex(ParameterSyntax syntax, ITypeSymbol typeSymbol) : base(syntax, typeSymbol) { }
        internal override string FieldInGeneratedJobEntityBatchType() =>
            @"[Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndexAttribute] internal int __NativeThreadIndex;";
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"int {Syntax.Identifier}";
        internal override string LambdaBodyParameter() => $@"__NativeThreadIndex";
        internal override string FieldAssignmentInGeneratedJobEntityBatchType(LambdaJobDescription lambdaJobDescription) => $@"__NativeThreadIndex = 0";
        internal override string StructuralChanges_LambdaBodyParameter() => @"__NativeThreadIndex";
    }
}
