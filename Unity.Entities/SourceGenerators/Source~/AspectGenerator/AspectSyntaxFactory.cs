using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Entities.SourceGen.Aspect;
using Unity.Entities.SourceGen.Common;

public static class AspectSyntaxFactory
{
    /// <summary>
    /// Format code for a static array of Unity.Entities.ComponentType representing the requirements for the aspect components
    /// </summary>
    /// <param name="aspect"></param>
    /// <param name="fieldName"></param>
    /// <param name="readOnly"></param>
    /// <returns></returns>
    static string FormatComponentRequirement(AspectDefinition aspect, string fieldName, bool readOnly)
    {
        var fieldsWithComponentTypeRequirements = aspect.QueryFields.ToArray();
        var components = @$"new [] {{ {fieldsWithComponentTypeRequirements.Select(field => $@" global::Unity.Entities.ComponentType.{(field.IsReadOnly | readOnly ? "ReadOnly" : "ReadWrite")}<global::{field.TypeName}>()").SeparateByComma()} }}";
        var aspects = @$"{aspect.AspectFields.Select(field => $@" {field.TypeName}.RequiredComponents{(readOnly | field.IsReadOnly ? "RO" : "")}").SeparateByComma()}";
        var componentTypeCount = fieldsWithComponentTypeRequirements.Length;

        if (aspect.AspectFields.Count > 0 && componentTypeCount > 0)
            return $"static global::Unity.Entities.ComponentType[] {fieldName} => global::Unity.Entities.ComponentType.Combine({components}, {aspects});";
        if (aspect.AspectFields.Count > 1)
            return $"static global::Unity.Entities.ComponentType[] {fieldName} => global::Unity.Entities.ComponentType.Combine({aspects});";
        if (aspect.AspectFields.Count == 1)
            return $"static global::Unity.Entities.ComponentType[] {fieldName} => {aspects};";
        return $"static global::Unity.Entities.ComponentType[] {fieldName} => {components};";
    }

    public static bool AspectNeedConstructor(this AspectDefinition aspect) => aspect.HasEntityField || aspect.FieldsNeedContruction.Any();

    public static IEnumerable<string> GetConstructerParameters(this AspectDefinition aspect)
    {
        if (aspect.HasEntityField)
            yield return "global::Unity.Entities.Entity entity";
        foreach (var field in aspect.FieldsNeedContruction)
            yield return $"{field.SourceTypeName} {field.InternalVariableName}";
    }

    public static IEnumerable<string> GetConstructionArgumentsAspectLookup(this AspectDefinition aspect)
    {
        if (aspect.HasEntityField)
            yield return "entity";
        foreach (var field in aspect.FieldsNeedContruction)
            yield return field.GetAspectLookupParameter();
    }

    public static IEnumerable<string> GetConstructionArgumentsResolvedChunk(this AspectDefinition aspect)
    {
        if (aspect.HasEntityField)
            yield return "m_Entities[index]";
        foreach (var field in aspect.FieldsNeedContruction)
            yield return field.GetResolvedChunkParameter();
    }
public static string GenerateAspectSource(AspectDefinition aspect)
    {
        var aspectNamespace = aspect.SourceSyntaxNodes.First().GetParentNamespace();
        var namespaceOpen = "";
        var namespaceClose = "";

        if (!String.IsNullOrWhiteSpace(aspectNamespace))
        {
            namespaceOpen = $"namespace {aspectNamespace}\n{{\n";
            namespaceClose = "}\n";
        }
        Printer printer = new Printer
        {
            Builder = new StringBuilder(),
            CurrentIndent = ""
        };

        string interfaceToImplement = "global::Unity.Entities.IAspect";
        if (!aspect.IsIAspectCreateCorrectlyImplementedByUser)
            interfaceToImplement += $", global::Unity.Entities.IAspectCreate<{aspect.Name}>";

        return @$"
{namespaceOpen}
    {string.Join(" ", aspect.SourceSyntaxNodes.First().Modifiers.Select(token => token.ToString()))} struct {aspect.Name} : {interfaceToImplement}
    {{
{
    // Output a constructor that build an aspect from each component ComponentDataRef as parameters.
    (aspect.AspectNeedConstructor() ? @$"        {aspect.Name}({aspect.GetConstructerParameters().SeparateByComma()})
        {{
{aspect.FieldsNeedContruction.Select(x => $"            this.{x.FieldName} = {x.InternalVariableName};").SeparateByNewLine()}
{printer.WithIndentCleared("            ").PrintLinesToString(aspect.FieldsRequiringDefaultConstruction.Select(x => $"this.{x.FieldName} = default;"))}
{aspect.HasEntityField.SelectFuncOrDefault(x => $"            this.{aspect.EntityField.FieldName} = {aspect.EntityField.ConstructorAssignment};\n")}
        }}" : "")
}
        {$@"public {aspect.Name} CreateAspect(global::Unity.Entities.Entity entity, ref global::Unity.Entities.SystemState systemState, bool isReadOnly)
        {{
            var lookup = new Lookup(ref systemState, isReadOnly);
            return lookup[entity];
        }}".EmitIfTrue(!aspect.IsIAspectCreateCorrectlyImplementedByUser)}

        public static global::Unity.Entities.ComponentType[] ExcludeComponents => global::System.Array.Empty<Unity.Entities.ComponentType>();
        {FormatComponentRequirement(aspect, "s_RequiredComponents", false)}
        {FormatComponentRequirement(aspect, "s_RequiredComponentsRO", true)}
        public static global::Unity.Entities.ComponentType[] RequiredComponents => s_RequiredComponents;
        public static global::Unity.Entities.ComponentType[] RequiredComponentsRO => s_RequiredComponentsRO;
        public struct Lookup
        {{
            bool _IsReadOnly
            {{
                get {{ return __IsReadOnly == 1; }}
                set {{ __IsReadOnly = value ? (byte) 1 : (byte) 0; }}
            }}
            private byte __IsReadOnly;

{aspect.Lookup.DeclareCode(ref printer.WithIndentCleared("            "))}
{aspect.Lookup.BufferLookup.Select(field => $"{field.ReadOnlyAttribute}            global::Unity.Entities.BufferLookup<{field.TypeName}> {field.InternalFieldName};").SeparateByNewLine()}
{aspect.AspectFields.Select(field => $"{field.ReadOnlyAttribute}            global::{field.TypeName}.Lookup {field.InternalFieldName};").SeparateByNewLine()}
            public Lookup(ref global::Unity.Entities.SystemState state, bool isReadOnly)
            {{
                __IsReadOnly = isReadOnly ? (byte) 1u : (byte) 0u;
{aspect.Lookup.ConstructCode(ref printer.WithIndentCleared("                "))}
{aspect.Lookup.BufferLookup.Select(field => $"                this.{field.InternalFieldName} = state.GetBufferLookup<{field.TypeName}>({field.ResolveReadOnly("isReadOnly")});").SeparateByNewLine()}
{aspect.AspectFields.Select(field => $"                this.{field.InternalFieldName} = new global::{field.TypeName}.Lookup(ref state, {field.ResolveReadOnly("isReadOnly")});").SeparateByNewLine()}
            }}
            public void Update(ref global::Unity.Entities.SystemState state)
            {{
{aspect.Lookup.UpdateCode(ref printer.WithIndentCleared("                "))}
{aspect.Lookup.Update.Select(field => $"                this.{field.InternalFieldName}.Update(ref state);").SeparateByNewLine()}
            }}
            public {aspect.Name} this[global::Unity.Entities.Entity entity]
            {{
                get
                {{
                    return new {aspect.Name}({aspect.GetConstructionArgumentsAspectLookup().SeparateByComma()});
                }}
            }}
        }}
        public struct ResolvedChunk
        {{
{aspect.HasEntityField.SelectOrDefault("            internal global::Unity.Collections.NativeArray<global::Unity.Entities.Entity> m_Entities;")}
{aspect.ResolvedChunk.DeclareCode(ref printer.WithIndentCleared("            "))}
{aspect.AspectFields.Select(field => $"            internal global::{field.TypeName}.ResolvedChunk {field.InternalFieldName};").SeparateByNewLine()}
            public {aspect.Name} this[int index]
            {{
                get
                {{
                    return new {aspect.Name}({aspect.GetConstructionArgumentsResolvedChunk().SeparateByCommaAndNewLine()});
                }}
            }}
            public int Length;
        }}
        public struct TypeHandle
        {{
{aspect.TypeHandle.DeclareCode(ref printer.WithIndentCleared("            "))}
{aspect.HasEntityField.SelectOrDefault($"            global::Unity.Entities.EntityTypeHandle m_Entities;")}
{aspect.TypeHandle.BufferTypeHandle.Select(field => $"{field.ReadOnlyAttribute}            global::Unity.Entities.BufferTypeHandle<global::{field.TypeName}> {field.InternalFieldName};").SeparateByNewLine()}
{aspect.AspectFields.Select(field => $"{field.ReadOnlyAttribute}            global::{field.TypeName}.TypeHandle {field.InternalFieldName};").SeparateByNewLine()}
            public TypeHandle(ref global::Unity.Entities.SystemState state, bool isReadOnly)
            {{
{aspect.TypeHandle.ConstructCode(ref printer.WithIndentCleared("                "))}
{aspect.HasEntityField.SelectOrDefault($"                this.m_Entities = state.GetEntityTypeHandle();")}
{aspect.TypeHandle.BufferTypeHandle.Select(field => $"                this.{field.InternalFieldName} = state.GetBufferTypeHandle<global::{field.TypeName}>({(field.IsReadOnly ? "true" : "isReadOnly")});").SeparateByNewLine()}
{aspect.AspectFields.Select(field => $"                this.{field.InternalFieldName} = new global::{field.TypeName}.TypeHandle(ref state, {(field.IsReadOnly ? "true" : "isReadOnly")});").SeparateByNewLine()}
            }}
            public void Update(ref global::Unity.Entities.SystemState state)
            {{
{aspect.TypeHandle.UpdateCode(ref printer.WithIndentCleared("                "))}
{aspect.HasEntityField.SelectOrDefault($"                this.m_Entities.Update(ref state);")}
{aspect.TypeHandle.Update.Select(field => $"                this.{field.InternalFieldName}.Update(ref state);").SeparateByNewLine()}
            }}
            public ResolvedChunk Resolve(global::Unity.Entities.ArchetypeChunk chunk)
            {{
                ResolvedChunk resolved;
{ aspect.HasEntityField.SelectOrDefault($"                resolved.m_Entities = chunk.GetNativeArray(this.m_Entities);")}
{ aspect.AspectFields.Select(field => $"                resolved.{field.InternalFieldName} = this.{field.InternalFieldName}.Resolve(chunk);").SeparateByNewLine()}
{aspect.ResolvedChunk.ResolveCode(ref printer.WithIndentCleared("                "))}
                resolved.Length = chunk.Count;
                return resolved;
            }}
        }}
        public static Enumerator Query(global::Unity.Entities.EntityQuery query, TypeHandle typeHandle) {{ return new Enumerator(query, typeHandle); }}
        public struct Enumerator : global::System.Collections.Generic.IEnumerator<{aspect.Name}>, global::System.Collections.Generic.IEnumerable<{aspect.Name}>
        {{
            ResolvedChunk                                _Resolved;
            global::Unity.Entities.EntityQueryEnumerator _QueryEnumerator;
            TypeHandle                                   _Handle;
            internal Enumerator(global::Unity.Entities.EntityQuery query, TypeHandle typeHandle)
            {{
                _QueryEnumerator = new global::Unity.Entities.EntityQueryEnumerator(query);
                _Handle = typeHandle;
                _Resolved = default;
            }}
            public void Dispose() {{ _QueryEnumerator.Dispose(); }}
            public bool MoveNext()
            {{
                if (_QueryEnumerator.MoveNextHotLoop())
                    return true;
                return MoveNextCold();
            }}
            [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            bool MoveNextCold()
            {{
                var didMove = _QueryEnumerator.MoveNextColdLoop(out var chunk);
                if (didMove)
                    _Resolved = _Handle.Resolve(chunk);
                return didMove;
            }}
            public {aspect.Name} Current {{
                get {{
                    #if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        _QueryEnumerator.CheckDisposed();
                    #endif
                        return _Resolved[_QueryEnumerator.IndexInChunk];
                    }}
            }}
            public Enumerator GetEnumerator()  {{ return this; }}
            void global::System.Collections.IEnumerator.Reset() => throw new global::System.NotImplementedException();
            object global::System.Collections.IEnumerator.Current => throw new global::System.NotImplementedException();
            global::System.Collections.Generic.IEnumerator<{aspect.Name}> global::System.Collections.Generic.IEnumerable<{aspect.Name}>.GetEnumerator() => throw new global::System.NotImplementedException();
            global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator()=> throw new global::System.NotImplementedException();
        }}

        /// <summary>
        /// Completes the dependency chain required for this aspect to have read access.
        /// So it completes all write dependencies of the components, buffers, etc. to allow for reading.
        /// </summary>
        /// <param name=""state"">The <see cref=""SystemState""/> containing an <see cref=""EntityManager""/> storing all dependencies.</param>
        public static void CompleteDependencyBeforeRO(ref global::Unity.Entities.SystemState state){{
{aspect.FieldsNeedContruction.Select(f => "           "+f.GetCompleteDependencyXX(true)).SeparateByNewLine()}
        }}

        /// <summary>
        /// Completes the dependency chain required for this component to have read and write access.
        /// So it completes all write dependencies of the components, buffers, etc. to allow for reading,
        /// and it completes all read dependencies, so we can write to it.
        /// </summary>
        /// <param name=""state"">The <see cref=""SystemState""/> containing an <see cref=""EntityManager""/> storing all dependencies.</param>
        public static void CompleteDependencyBeforeRW(ref global::Unity.Entities.SystemState state){{
{aspect.FieldsNeedContruction.Select(f => "           "+f.GetCompleteDependencyXX(false)).SeparateByNewLine()}
        }}
    }}
{namespaceClose}";
    }
}
