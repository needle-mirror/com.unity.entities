using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    abstract class LambdaParamDescription
    {
        public ParameterSyntax Syntax { get; }
        public IParameterSymbol Symbol { get; }
        public ITypeSymbol Type { get => Symbol.Type; }

        internal LambdaParamDescription(ParameterSyntax syntax, IParameterSymbol symbol)
        {
            Syntax = syntax;
            Symbol = symbol;
        }

        internal static LambdaParamDescription From(ParameterSyntax param, IParameterSymbol symbol)
        {
            if (symbol.Type.InheritsFromInterface("Unity.Entities.IComponentData") && symbol.Type.IsValueType)
            {
                if (!symbol.Type.GetMembers().OfType<IFieldSymbol>().Any())
                    return new LambdaParamDescription_TagComponent(param, symbol);
                else
                    return new LambdaParamDescription_Component(param, symbol);
            }
            else if (symbol.Type.InheritsFromInterface("Unity.Entities.ISharedComponentData"))
                return new LambdaParamDescription_SharedComponent(param, symbol);
            else if (symbol.Type.IsDynamicBuffer())
                return new LambdaParamDescription_DynamicBuffer(param, symbol);
            else if (symbol.Type.Is("Unity.Entities.Entity"))
                return new LambdaParamDescription_Entity(param, symbol);
            else if (symbol.Type.IsInt() && param.Identifier.ValueText == "entityInQueryIndex")
                return new LambdaParamDescription_EntityInQueryIndex(param, symbol);
            else if (symbol.Type.IsInt() && param.Identifier.ValueText == "nativeThreadIndex")
                return new LambdaParamDescription_NativeThreadIndex(param, symbol);
            else if (!symbol.Type.IsValueType &&
                     (symbol.Type.InheritsFromInterface("Unity.Entities.IComponentData") || symbol.Type.InheritsFromType("UnityEngine.Behaviour")))
                return new LambdaParamDescription_ManagedComponent(param, symbol);
#if GENERIC_ENTITIES_FOREACH_SUPPORT
            else if (symbol.Type.TypeKind == TypeKind.TypeParameter)
                return new LambdaParamDescription_Generic(param, symbol);
#endif

            throw new InvalidOperationException($"Unidentifiable lambda parameter: {symbol.Name}");
        }

        internal virtual string TypeHandleField() => null;
        internal virtual string QueryTypeForParameter() => null;
        internal virtual ITypeSymbol QueryType() => null;
        internal virtual bool QueryTypeIsReadOnly() => false;
        internal virtual string LambdaBodyMethodParameter(bool usesBurst) => null;
        internal virtual string GetNativeArray() => null;
        internal virtual string LambdaBodyParameterSetup() => null;
        internal virtual string LambdaBodyParameter() => null;
        internal virtual string TypeHandleAssign() => null;

        internal virtual string StructuralChanges_ReadLambdaParam() => null;
        internal virtual string StructuralChanges_WriteBackLambdaParam() => null;
        internal virtual string StructuralChanges_LambdaBodyParameter() => null;
        internal virtual string StructuralChanges_GetTypeIndex() => null;
    }

    class LambdaParamDescription_Component : LambdaParamDescription
    {
        internal LambdaParamDescription_Component(ParameterSyntax syntax, IParameterSymbol symbol) : base(syntax, symbol) {}
        internal override string TypeHandleField() => $@"{(Syntax.IsReadOnly() ? "[Unity.Collections.ReadOnly] " : "")}public ComponentTypeHandle<{Type.ToFullName()}> __{Syntax.Identifier}TypeHandle;";
        internal override string QueryTypeForParameter() => $@"ComponentType.{(Syntax.IsReadOnly() ? "ReadOnly" : "ReadWrite")}<{Type.ToFullName()}>()";
        internal override ITypeSymbol QueryType() => Type;
        internal override bool QueryTypeIsReadOnly() => Syntax.IsReadOnly();
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"{"[Unity.Burst.NoAlias] ".EmitIfTrue(usesBurst)}{Syntax.GetModifierString()} {Type.ToFullName()} {Syntax.Identifier}";
        internal override string GetNativeArray()
        {
            if (Syntax.IsReadOnly())
                return $@"var {Syntax.Identifier}Accessor = ({Type.ToFullName()}*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(chunk.GetNativeArray(__{Syntax.Identifier}TypeHandle));";
            else
                return $@"var {Syntax.Identifier}Accessor = ({Type.ToFullName()}*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(chunk.GetNativeArray(__{Syntax.Identifier}TypeHandle));";
        }
        internal override string LambdaBodyParameter()
        {
            if (Syntax.GetModifierString() == "ref")
                return $@"ref Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AsRef<{Type.ToFullName()}>({Syntax.Identifier}Accessor + entityIndex)";
            else
                return $@"*({Syntax.Identifier}Accessor + entityIndex)";
        }
        internal override string TypeHandleAssign() => $@"__{Syntax.Identifier}TypeHandle = GetComponentTypeHandle<{Type.ToFullName()}>({(Syntax.IsReadOnly() ? "true" : "")})";

        internal override string StructuralChanges_GetTypeIndex() => $@"var {Syntax.Identifier}TypeIndex = TypeManager.GetTypeIndex<{Type.ToFullName()}>();";
        internal override string StructuralChanges_ReadLambdaParam() => $@"var {Syntax.Identifier} = Unity.Entities.InternalCompilerInterface.GetComponentData<{Type.ToFullName()}>(__this.EntityManager, entity, {Syntax.Identifier}TypeIndex, out var original{Syntax.Identifier});";
        internal override string StructuralChanges_WriteBackLambdaParam() => $@"Unity.Entities.InternalCompilerInterface.WriteComponentData<{Type.ToFullName()}>(__this.EntityManager, entity, {Syntax.Identifier}TypeIndex, ref {Syntax.Identifier}, ref original{Syntax.Identifier});";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"{Syntax.GetModifierString()} {Syntax.Identifier}";
    }

    class LambdaParamDescription_TagComponent : LambdaParamDescription
    {
        internal LambdaParamDescription_TagComponent(ParameterSyntax syntax, IParameterSymbol symbol) : base(syntax, symbol) {}
        internal override string QueryTypeForParameter() => $@"ComponentType.ReadOnly<{Type.ToFullName()}>()";
        internal override ITypeSymbol QueryType() => Type;
        internal override bool QueryTypeIsReadOnly() => true;
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"{"[Unity.Burst.NoAlias] ".EmitIfTrue(usesBurst)}{Syntax.GetModifierString()} {Type.ToFullName()} {Syntax.Identifier}";
        internal override string LambdaBodyParameterSetup() => $@"{Type.ToFullName()} {Syntax.Identifier}Local = default;";
        internal override string LambdaBodyParameter() => $"{Syntax.GetModifierString()} {Syntax.Identifier}Local";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"{Syntax.GetModifierString()} {Syntax.Identifier}Local";
    }

    class LambdaParamDescription_ManagedComponent : LambdaParamDescription
    {
        internal LambdaParamDescription_ManagedComponent(ParameterSyntax syntax, IParameterSymbol symbol) : base(syntax, symbol) {}
        internal override string TypeHandleField() => $@"public ComponentTypeHandle<{Type.ToFullName()}> __{Syntax.Identifier}TypeHandle;";
        internal override string QueryTypeForParameter() => $@"ComponentType.{(Syntax.IsReadOnly() ? "ReadOnly" : "ReadWrite")}<{Type.ToFullName()}>()";
        internal override ITypeSymbol QueryType() => Type;
        internal override bool QueryTypeIsReadOnly() => Syntax.IsReadOnly();
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"{Type.ToFullName()} {Syntax.Identifier}";
        internal override string GetNativeArray() =>
            $@"var {Syntax.Identifier}Accessor = chunk.GetManagedComponentAccessor(__{Syntax.Identifier}TypeHandle, __this.EntityManager);";
        internal override string LambdaBodyParameter() => $@"{Syntax.Identifier}Accessor[entityIndex]";
        internal override string TypeHandleAssign() => $@"__{Syntax.Identifier}TypeHandle = this.EntityManager.GetComponentTypeHandle<{Type.ToFullName()}>({(Syntax.IsReadOnly() ? "true" : "false")})";

        internal override string StructuralChanges_ReadLambdaParam() => $@"var {Syntax.Identifier} = __this.EntityManager.GetComponentObject<{Type.ToFullName()}>(entity);";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"{Syntax.Identifier}";
    }

    class LambdaParamDescription_SharedComponent : LambdaParamDescription
    {
        internal LambdaParamDescription_SharedComponent(ParameterSyntax syntax, IParameterSymbol symbol) : base(syntax, symbol) {}
        internal override string TypeHandleField() => $@"{(Syntax.IsReadOnly() ? "[Unity.Collections.ReadOnly] " : "")}public SharedComponentTypeHandle<{Type.ToFullName()}> __{Syntax.Identifier}TypeHandle;";
        internal override string QueryTypeForParameter() => $@"ComponentType.{(Syntax.IsReadOnly() ? "ReadOnly" : "ReadWrite")}<{Type.ToFullName()}>()";
        internal override ITypeSymbol QueryType() => Type;
        internal override bool QueryTypeIsReadOnly() => Syntax.IsReadOnly();
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"{Syntax.GetModifierString()} {Type.ToFullName()} {Syntax.Identifier}";
        internal override string GetNativeArray() =>
            $@"var __{Syntax.Identifier}Data = chunk.GetSharedComponentData(__{Syntax.Identifier}TypeHandle, __this.EntityManager);";
        internal override string LambdaBodyParameter() =>  $"__{Syntax.Identifier}Data";
        internal override string TypeHandleAssign() => $@"__{Syntax.Identifier}TypeHandle = GetSharedComponentTypeHandle<{Type.ToFullName()}>()";

        internal override string StructuralChanges_ReadLambdaParam() => $@"var {Syntax.Identifier} = __this.EntityManager.GetSharedComponentData<{Type.ToFullName()}>(entity);";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"{Syntax.GetModifierString()} {Syntax.Identifier}";
    }

#if GENERIC_ENTITIES_FOREACH_SUPPORT
    class LambdaParamDescription_Generic : LambdaParamDescription_Component
    {
        internal LambdaParamDescription_Generic(ParameterSyntax syntax, IParameterSymbol symbol) : base(syntax, symbol) { }
        internal ImmutableArray<ITypeSymbol> Constraints { get
            {
                ITypeParameterSymbol typeParameter = (ITypeParameterSymbol)Symbol.Type;
                return typeParameter.ConstraintTypes;
            }
        }
        internal override string QueryTypeForParameter()
        {
            return $@"ComponentType.{(Syntax.IsReadOnly() ? "ReadOnly" : "ReadWrite")}<{Type.ToFullName()}>()";
        }
    }
#endif

    class LambdaParamDescription_DynamicBuffer : LambdaParamDescription
    {
        ITypeSymbol _bufferGenericArgumentType;
        internal LambdaParamDescription_DynamicBuffer(ParameterSyntax syntax, IParameterSymbol symbol) : base(syntax, symbol)
        {
            var namedTypeSymbol = (INamedTypeSymbol)symbol.Type;
            _bufferGenericArgumentType = namedTypeSymbol.TypeArguments.First();
        }
        internal override string TypeHandleField() => $@"public BufferTypeHandle<{_bufferGenericArgumentType}> __{Syntax.Identifier}TypeHandle;";
        internal override string QueryTypeForParameter() => $@"ComponentType.ReadWrite<{_bufferGenericArgumentType}>()";
        internal override ITypeSymbol QueryType() => _bufferGenericArgumentType;
        internal override bool QueryTypeIsReadOnly() => Syntax.IsReadOnly();
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"DynamicBuffer<{_bufferGenericArgumentType}> {Syntax.Identifier}";
        internal override string GetNativeArray() =>
            $@"var {Syntax.Identifier}Accessor = chunk.GetBufferAccessor(__{Syntax.Identifier}TypeHandle);";
        internal override string LambdaBodyParameter() => $@"{Syntax.Identifier}Accessor[entityIndex]";
        internal override string TypeHandleAssign() => $@"__{Syntax.Identifier}TypeHandle = GetBufferTypeHandle<{_bufferGenericArgumentType}>({(Syntax.IsReadOnly() ? "true" : "")})";

        internal override string StructuralChanges_ReadLambdaParam() => $@"var {Syntax.Identifier} = __this.EntityManager.GetBuffer<{_bufferGenericArgumentType}>(entity);";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"{Syntax.Identifier}";
    }

    class LambdaParamDescription_Entity : LambdaParamDescription
    {
        internal LambdaParamDescription_Entity(ParameterSyntax syntax, IParameterSymbol symbol) : base(syntax, symbol) {}
        internal override string TypeHandleField() => $@"[Unity.Collections.ReadOnly] public EntityTypeHandle __{Syntax.Identifier}TypeHandle;";
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"Entity {Syntax.Identifier}";
        internal override string LambdaBodyParameter() => $@"*(__entityPtr_{Syntax.Identifier} + entityIndex)";
        internal override string GetNativeArray() =>
            $@"var __entityPtr_{Syntax.Identifier} = (Entity*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(chunk.GetNativeArray(__{Syntax.Identifier}TypeHandle));";
        internal override string TypeHandleAssign() => $@"__{Syntax.Identifier}TypeHandle = GetEntityTypeHandle()";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"entity";
    }

    class LambdaParamDescription_EntityInQueryIndex : LambdaParamDescription
    {
        internal LambdaParamDescription_EntityInQueryIndex(ParameterSyntax syntax, IParameterSymbol symbol) : base(syntax, symbol) { }
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"int {Syntax.Identifier}";
        internal override string LambdaBodyParameter() => $@"entityInQueryIndex";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"entityIndex";
    }

    class LambdaParamDescription_NativeThreadIndex : LambdaParamDescription
    {
        internal LambdaParamDescription_NativeThreadIndex(ParameterSyntax syntax, IParameterSymbol symbol) : base(syntax, symbol) { }
        internal override string TypeHandleField() => $@"[Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndexAttribute] internal int __NativeThreadIndex;";
        internal override string LambdaBodyMethodParameter(bool usesBurst) => $@"int {Syntax.Identifier}";
        internal override string LambdaBodyParameter() => $@"__NativeThreadIndex";
        internal override string TypeHandleAssign() => $@"__NativeThreadIndex = 0";
        internal override string StructuralChanges_LambdaBodyParameter() => $@"__NativeThreadIndex";
    }
}


