using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Entities.SourceGen.Aspect
{

    /// <summary>
    /// An AspectField represents the associated aspect-functionalities to a FieldDeclaration of the aspect StructDeclaraction
    /// </summary>
    public abstract class AspectField
    {
        public SyntaxNode SourceSyntaxNode;

        // Name of the user declared field.
        public string FieldName;

        // used for generating declarations of member field name
        public string InternalFieldName;

        public string TypeName;

        public string InternalVariableName;
        public bool IsReadOnly;
        public bool IsOptional;
        public bool IsZeroSize;

        public string ResolveReadOnly(string readonlyParameter) => IsReadOnly ? "true" : readonlyParameter;
        public string ReadOnlyAttribute => IsReadOnly ? "            [global::Unity.Collections.ReadOnly]\n" : "";

        /// <summary>
        /// Type name of the source FieldDeclaration node;
        /// </summary>
        public abstract string SourceTypeName { get; }



        public abstract string GetAspectLookupParameter();
        public abstract string GetResolvedChunkParameter();

        public abstract string GetCompleteDependencyXX(bool callIsReadOnly);

    }

    /// <summary>
    /// Represents a reference field that relates to a component type.
    /// This field requires both a ComponentLookup and
    /// a ComponentTypeHandle to perform its functionality.
    /// </summary>
    public abstract class ComponentRefField : AspectField
        , IFieldDependency<ComponentLookup>
        , IFieldDependency<ComponentTypeHandle>
    {
        /// <summary>
        /// a Ref field requires a ComponentLookup in the AspectLookup of the feature.
        /// this field will be the one declaring the ComponentLookup field.
        /// </summary>
        public AspectField FieldComponentLookup;
        AspectField IFieldDependency<ComponentLookup>.DeclaringField { get => FieldComponentLookup; set => FieldComponentLookup = value; }

        public string ComponentLookupFieldName =>
            FieldComponentLookup.FieldName + ComponentLookup.Tag;

        /// <summary>
        /// a Ref field requires a ComponentTypeHandle in the TypeHandle of the feature.
        /// this field will be the one declaring the ComponentTypeHandle field.
        /// </summary>
        public AspectField FieldComponentTypeHandle;
        AspectField IFieldDependency<ComponentTypeHandle>.DeclaringField { get => FieldComponentTypeHandle; set => FieldComponentTypeHandle = value; }
        public string ComponentTypeHandleFieldName =>
            FieldComponentTypeHandle.FieldName + ComponentTypeHandle.Tag;
    }

    /// <summary>
    /// ComponentData AspectField, declared using RefRW<MyIComponentData>
    /// </summary>
    public class ComponentRefRWField : ComponentRefField
    {
        public override string SourceTypeName => $"global::Unity.Entities.RefRW<global::{TypeName}>";

        public override string GetAspectLookupParameter() => $"this.{ComponentLookupFieldName}.{(IsOptional ? "GetRefRWOptional(entity, " : "GetRefRW(entity, ")}_IsReadOnly)";

        public override string GetResolvedChunkParameter() => IsOptional ?
                    $"                        global::Unity.Entities.RefRW<{TypeName}>.Optional(this.{InternalFieldName}, index)" :
                    $"                        new global::Unity.Entities.RefRW<{TypeName}>(this.{InternalFieldName}, index)";

        public override string GetCompleteDependencyXX(bool callIsReadOnly)
        {
            if (callIsReadOnly || IsReadOnly) // Either callsite or aspect is readonly
                return $"state.EntityManager.CompleteDependencyBeforeRO<global::{TypeName}>();";
            return $"state.EntityManager.CompleteDependencyBeforeRW<global::{TypeName}>();";
        }
    }

    /// <summary>
    /// ComponentData AspectField, declared using RefRO<MyIComponentData>
    /// </summary>
    public class ComponentRefROField : ComponentRefField
    {
        public override string SourceTypeName => $"global::Unity.Entities.RefRO<global::{TypeName}>";

        public override string GetAspectLookupParameter() => $"this.{ComponentLookupFieldName}.{(IsOptional ? "GetRefROOptional(entity)" : "GetRefRO(entity)")}";


        public override string GetResolvedChunkParameter() => IsOptional ?
            $"                        global::Unity.Entities.RefRO<{TypeName}>.Optional(this.{InternalFieldName}, index)" :
            $"                        new global::Unity.Entities.RefRO<{TypeName}>(this.{InternalFieldName}, index)";

        public override string GetCompleteDependencyXX(bool callIsReadOnly) => $"state.EntityManager.CompleteDependencyBeforeRO<global::{TypeName}>();";
    }

    /// <summary>
    /// Nested Aspect AspectField, declared using the aspect type name.
    /// </summary>
    public class NestedAspectAspectField : AspectField
    {
        public override string SourceTypeName => $"global::{TypeName}";
        public override string GetAspectLookupParameter() => $"this.{InternalFieldName}[entity]";
        public override string GetResolvedChunkParameter() => $"this.{InternalFieldName}[index]";
        public override string GetCompleteDependencyXX(bool callIsReadOnly)
        {
            if (callIsReadOnly || IsReadOnly) // Either callsite or aspect is readonly
                return $"{TypeName}.CompleteDependencyBeforeRW(ref state);";
            return $"{TypeName}.CompleteDependencyBeforeRO(ref state);";
        }
    }

    /// <summary>
    /// BufferAccessor AspectField, declared using BufferAccessor<MyBufferElementData>.
    /// </summary>
    public class BufferAspectField : AspectField
    {
        public override string SourceTypeName => $"global::Unity.Entities.DynamicBuffer<global::{TypeName}>";
        public override string GetAspectLookupParameter() => $"this.{InternalFieldName}[entity]";
        public override string GetResolvedChunkParameter() => $"this.{InternalFieldName}[index]";

        public override string GetCompleteDependencyXX(bool callIsReadOnly)
        {
            if (callIsReadOnly || IsReadOnly) // Either callsite or aspect is readonly
                return $"state.EntityManager.CompleteDependencyBeforeRO<global::{TypeName}>();";
            return $"state.EntityManager.CompleteDependencyBeforeRW<global::{TypeName}>();";
        }

    }
    /// <summary>
    /// A field that hold the Entity instance.
    /// </summary>
    public class EntityField : AspectField
    {
        public string ConstructorAssignment;

        // TODO refactor the field hierarchy to better reflect the new set of aspect fields.
        // in the meantime, EntityField will act as a fake AspectField and be handled
        // separately from other AspectField
        public override string SourceTypeName => throw new InvalidOperationException();
        public override string GetAspectLookupParameter() => throw new InvalidOperationException();
        public override string GetResolvedChunkParameter() => throw new InvalidOperationException();
        public override string GetCompleteDependencyXX(bool callIsReadOnly) => "";
    }

    /// <summary>
    /// Component Enabled AspectField, declared using ComponentEnabledRefRO<MyIComponentData>
    /// </summary>
    public abstract class EnabledField : ComponentRefField
    {
    }

    /// <summary>
    /// Component Enabled AspectField, declared using ComponentEnabledRefRO<MyIComponentData>
    /// </summary>
    public class EnabledRefROField : EnabledField
    {
        public override string SourceTypeName => $"global::Unity.Entities.EnabledRefRO<global::{TypeName}>";
        public override string GetAspectLookupParameter() => $"this.{ComponentLookupFieldName}.{(IsOptional ? "GetOptionalEnabledRefRO" : "GetEnabledRefRO")}<global::{TypeName}>(entity)";

        public override string GetResolvedChunkParameter() => IsOptional ?
            $"this.{InternalFieldName}.GetOptionalEnabledRefRO<{TypeName}>(index)" :
            $"this.{InternalFieldName}.GetEnabledRefRO<{TypeName}>(index)";

        public override string GetCompleteDependencyXX(bool callIsReadOnly) =>
            $"state.EntityManager.CompleteDependencyBeforeRO<global::{TypeName}>();";
    }

    /// <summary>
    /// Component Enabled AspectField, declared using ComponentEnabledRefRO<MyIComponentData>
    /// </summary>
    public class EnabledRefRWField : EnabledField
    {
        public override string SourceTypeName => $"global::Unity.Entities.EnabledRefRW<global::{TypeName}>";
        public override string GetAspectLookupParameter() => $"this.{ComponentLookupFieldName}.{(IsOptional ? "GetOptionalEnabledRefRW" : "GetEnabledRefRW")}<global::{TypeName}>(entity, _IsReadOnly)";

        public override string GetResolvedChunkParameter() => IsOptional ?
            $"this.{InternalFieldName}.GetOptionalEnabledRefRW<{TypeName}>(index)" :
            $"this.{InternalFieldName}.GetEnabledRefRW<{TypeName}>(index)";

        public override string GetCompleteDependencyXX(bool callIsReadOnly)
        {
            if (callIsReadOnly || IsReadOnly) // Either callsite or aspect is readonly
                return $"state.EntityManager.CompleteDependencyBeforeRO<global::{TypeName}>();";
            return $"state.EntityManager.CompleteDependencyBeforeRW<global::{TypeName}>();";
        }
    }

    public class AspectDefinition
    {
        public AspectDefinition(StructDeclarationSyntax sourceSyntax, string name, string fullName)
        {
            SourceSyntaxNodes.Add(sourceSyntax);
            Name = name;
            FullName = fullName;
        }

        /// <summary>
        /// All StructDeclarationSyntax that are partial declaration of the aspect.
        /// </summary>
        public List<StructDeclarationSyntax> SourceSyntaxNodes = new List<StructDeclarationSyntax>();

        /// <summary>
        /// Name of the aspect struct from the source code.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Full type name of the aspect struct from the source code (Including namespace)
        /// </summary>
        public string FullName { get; }

        public AspectLookupDescription Lookup = AspectLookupDescription.Default;
        public AspectTypeHandleDescription TypeHandle = AspectTypeHandleDescription.Default;

        /// <summary>
        /// Description of the Resolved chunk for an aspect.
        /// </summary>
        public struct ResolvedChunkDescription
        {
            public List<ComponentRefField> ComponentDataNativeArray;
            public List<AspectField> BufferAccessors;
            public List<EnabledField> ComponentEnableBitBuffer;

            public string DeclareCode(ref Printer printer)
            {
                foreach (var bufferAccessor in BufferAccessors)
                    printer.PrintLine($"internal global::Unity.Entities.BufferAccessor<global::{bufferAccessor.TypeName}> {bufferAccessor.InternalFieldName};");

                foreach (var nativeArray in ComponentDataNativeArray)
                    printer.PrintLine($"internal global::Unity.Collections.NativeArray<global::{nativeArray.TypeName}> {nativeArray.InternalFieldName};");

                foreach (var ComponentEnableBit in ComponentEnableBitBuffer)
                    printer.PrintLine($"internal global::Unity.Entities.EnabledMask {ComponentEnableBit.InternalFieldName};");

                return printer.Result;
            }

            public string ResolveCode(ref Printer printer)
            {
                foreach (var bufferAccessor in BufferAccessors)
                    printer.PrintLine($"resolved.{bufferAccessor.InternalFieldName} = chunk.GetBufferAccessor(this.{bufferAccessor.InternalFieldName});");

                foreach (var nativeArray in ComponentDataNativeArray)
                    printer.PrintLine($"resolved.{nativeArray.InternalFieldName} = chunk.GetNativeArray(this.{nativeArray.ComponentTypeHandleFieldName});");

                foreach (var ComponentEnableBit in ComponentEnableBitBuffer)
                    printer.PrintLine($"resolved.{ComponentEnableBit.InternalFieldName} = chunk.GetEnabledMask(ref this.{ComponentEnableBit.ComponentTypeHandleFieldName});");

                return printer.Result;
            }
        }

        public ResolvedChunkDescription ResolvedChunk = new ResolvedChunkDescription
        {
            ComponentDataNativeArray = new List<ComponentRefField>(),
            BufferAccessors = new List<AspectField>(),
            ComponentEnableBitBuffer = new List<EnabledField>()
        };

        /// <summary>
        /// Fields that require data to be set in the aspect (must be constructed) and 'struct-of-array' data like TypeHandle, ComponentLookup or lookup structures.
        /// </summary>
        public List<AspectField> FieldsNeedContruction = new List<AspectField>();

        /// <summary>
        /// Fields that requires to be default constructed.
        /// </summary>
        public List<AspectField> FieldsRequiringDefaultConstruction = new List<AspectField>();

        /// <summary>
        /// Aspect fields require output in all 3 structs: Lookup, ResolvedChunk, TypeHandle
        /// </summary>
        public List<NestedAspectAspectField> AspectFields = new List<NestedAspectAspectField>();

        /// <summary>
        /// These fields take part in the creation of the entity queries
        /// </summary>
        public List<AspectField> QueryFields = new List<AspectField>();

        /// <summary>
        /// [Entity-In-Aspect] : An aspect may define an field of type Unity.Entities.Entity to be initialized with the current entity the aspect is constructed with.
        /// </summary>
        public EntityField EntityField;
        public bool HasEntityField => EntityField != null;

        readonly HashSet<string> m_ComponentTypeSet = new HashSet<string>();

        public bool AddQueryField(AspectField aspectField)
        {
            if (!aspectField.IsOptional)
            {
                QueryFields.Add(aspectField);
            }
            if (!m_ComponentTypeSet.Add(aspectField.TypeName))
            {
                AspectErrors.SGA0001(aspectField.SourceSyntaxNode.GetLocation(), aspectField.TypeName);
                return false;
            }
            return true;
        }

        public void ResolveFieldDependencies()
        {
            foreach (var e in Lookup.ComponentLookups.Values)
                e.Dependencies.Resolve();
            foreach (var e in TypeHandle.ComponentTypeHandles.Values)
                e.Dependencies.Resolve();
        }

        /// <summary>
        /// Tell if any of the partial declarations of the aspect has an attribute
        /// </summary>
        /// <param name="typeNameNamesapce"></param>
        /// <param name="typeName"></param>
        /// <returns></returns>
        public bool SyntaxHasAttributeCandidate(string typeNameNamesapce, string typeName) => SourceSyntaxNodes.Where(x => x.HasAttributeCandidate(typeNameNamesapce, typeName)).Any();

        public IEnumerable<SyntaxNode> ChildNodes => SourceSyntaxNodes.SelectMany(n => n.ChildNodes());

        private string _cached_FileAndGeneratorName;

        internal string FileAndGeneratorName
        {
            get
            {
                if(string.IsNullOrEmpty(_cached_FileAndGeneratorName))
                    _cached_FileAndGeneratorName = $"{Sanitize(SourceSyntaxNodes.First().GetParentNamespace())}_{Name}";
                return _cached_FileAndGeneratorName;
            }
        }

        public bool IsIAspectCreateCorrectlyImplementedByUser { get; set; }

        private static string Sanitize(string ns)
        {
            return string.IsNullOrEmpty(ns) ? "" : ns.Replace('.', '_');
        }

    }
}
