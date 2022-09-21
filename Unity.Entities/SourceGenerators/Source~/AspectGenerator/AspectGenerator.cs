using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.Aspect
{
    public static class BoolExt
    {
        /// <summary>
        /// Select the value based on the bool value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="b"></param>
        /// <param name="falseValue"></param>
        /// <param name="trueValue"></param>
        /// <returns></returns>
        public static T Select<T>(this bool b, T trueValue, T falseValue) => b ? trueValue : falseValue;

        /// <summary>
        /// Select the value based on the bool value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="b"></param>
        /// <param name="falseValue"></param>
        /// <param name="trueValue"></param>
        /// <returns></returns>
        public static T SelectOrDefault<T>(this bool b, T trueValue) => b ? trueValue : default;

        /// <summary>
        /// execute the function and return the result if the bool is true, otherwise return default
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="b">this bool</param>
        /// <param name="trueValue">Func to execute if bool is true</param>
        /// <returns></returns>
        public static T SelectFuncOrDefault<T>(this bool b, Func<bool, T> trueValue) => b ? trueValue(b) : default;
    }
    public static class DictionaryExt
    {
        /// <summary>
        /// If the key does not exist in the dictionary, funcNew is called first.
        /// Then funcSet(value) is called to set the desired value.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dictionary"></param>
        /// <param name="key"></param>
        /// <param name="funcNew">Called when the value does not exist and must be created</param>
        /// <param name="funcSet">Called to set the desired new value</param>
        public static void AddOrSetValue<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, Func<TValue> funcNew, Func<TValue, TValue> funcSet)
            where TValue : struct
        {
            if (!dictionary.TryGetValue(key, out var value))
                value = funcNew();
            dictionary[key] = funcSet(value);
        }
    }

    [Generator]
    public class AspectGenerator : ISourceGenerator, IDiagnosticFrame
    {
        static readonly string s_GeneratorName = "Aspect";

        /// <summary>
        /// Register our syntax receiver
        /// </summary>
        /// <param name="context"></param>
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new AspectReceiver(context.CancellationToken));
        }

        bool IsZeroSize(ITypeSymbol symbol)
        {
            // Check if it's a primitive type, which are non-zero
            switch (symbol.SpecialType)
            {
                case SpecialType.System_Void:
                    return true;
                case SpecialType.None:
                    break;
                default:
                    return false;
            }

            // visit all member for anything that is non-zero
            foreach (var member in symbol.GetMembers())
            {
                if(member is IFieldSymbol fieldSymbol)
                {
                    // Ignore static and const fields
                    if (fieldSymbol.IsStatic || fieldSymbol.IsConst)
                        continue;

                    //var namedTypeSymbol = fieldSymbol.Type as INamedTypeSymbol;

                    if (fieldSymbol.Type != null)
                    {
                        if (fieldSymbol.Type.TypeKind == TypeKind.Struct)
                            return IsZeroSize(fieldSymbol.Type);
                        return false;
                    }
                }
            }
            return true;
        }

        /// </summary>
        /// <param name="semanticModel"></param>
        /// <param name="node"></param>
        /// <param name="aspectField"></param>
        /// <returns></returns>
        bool TryParseField(SemanticModel semanticModel, SyntaxNode node, AspectDefinition aspectDefinition, out AspectField aspectField)
        {
            aspectField = default;
            if (!node.TryGetFirstChildByType(out VariableDeclarationSyntax variableDeclaration)) return false;

            var symbolInfo = semanticModel.GetSymbolInfo(variableDeclaration.Type);
            if (symbolInfo.Symbol == null) return false;

            var fieldTypeName = symbolInfo.Symbol.GetSymbolTypeName();

            // RefRW<T>
            if (fieldTypeName.StartsWith(AspectStrings.k_RefRWFullName))
                return TryParseFieldRef(semanticModel, node, aspectDefinition, out aspectField, variableDeclaration, isRO: false);

            // RefRO<T>
            if (fieldTypeName.StartsWith(AspectStrings.k_RefROFullName))
                return TryParseFieldRef(semanticModel, node, aspectDefinition, out aspectField, variableDeclaration, isRO: true);

            // DynamicBuffer<T>
            if (fieldTypeName.StartsWith(AspectStrings.k_DynamicBufferFullName))
                return TryParseFieldDynamicBuffer(semanticModel, node, aspectDefinition, out aspectField, variableDeclaration);

            // EnabledRefRW
            if (fieldTypeName.StartsWith(AspectStrings.k_EnabledRefRWFullName))
                return TryParseFieldEnabledRef(semanticModel, node, aspectDefinition, out aspectField, variableDeclaration, isRO: false);

            // EnabledRefRO
            if (fieldTypeName.StartsWith(AspectStrings.k_EnabledRefROFullName))
                return TryParseFieldEnabledRef(semanticModel, node, aspectDefinition, out aspectField, variableDeclaration, isRO: false);

            // Entity
            if (fieldTypeName.StartsWith(AspectStrings.k_EntityFullName))
                return TryParseFieldEntity(semanticModel, node, aspectDefinition, out aspectField, variableDeclaration);
            return TryParseFieldAspect(semanticModel, node, aspectDefinition, out aspectField, variableDeclaration, symbolInfo);
        }

        bool TryParseFieldRef(SemanticModel semanticModel, SyntaxNode node, AspectDefinition aspectDefinition, out AspectField aspectField,
            VariableDeclarationSyntax variableDeclaration, bool isRO)
        {
            var genericTypeSymbol = variableDeclaration.GetGenericParam1Symbol(semanticModel, out var symbolName);
            if (genericTypeSymbol is INamedTypeSymbol genericNameTypeSymbol)
            {
                var fieldName = variableDeclaration.Variables.First().Identifier.ToString();
                if (node.HasAttributeCandidate(AspectStrings.k_CollectionPackageNamespace, AspectStrings.k_ReadOnly))
                {
                    AspectErrors.SGA0011(node.GetLocation());
                    aspectField = default;
                    return false;
                }
                ComponentRefField field;
                if (isRO)
                {
                    field = new ComponentRefROField
                    {
                        SourceSyntaxNode = variableDeclaration,
                        FieldName = fieldName,
                        InternalFieldName = fieldName,
                        TypeName = symbolName,
                        InternalVariableName = fieldName.ToLower(),
                        IsReadOnly = true,
                        IsOptional = node.HasAttributeCandidate(AspectStrings.k_EntityPackageNamespace, AspectStrings.k_Optional),
                        IsZeroSize = IsZeroSize(genericNameTypeSymbol)
                    };
                }
                else
                {
                    field = new ComponentRefRWField
                    {
                        SourceSyntaxNode = variableDeclaration,
                        FieldName = fieldName,
                        InternalFieldName = fieldName,
                        TypeName = symbolName,
                        InternalVariableName = fieldName.ToLower(),
                        IsReadOnly = false,
                        IsOptional = node.HasAttributeCandidate(AspectStrings.k_EntityPackageNamespace, AspectStrings.k_Optional),
                        IsZeroSize = IsZeroSize(genericNameTypeSymbol)
                    };
                }

                if (!field.IsZeroSize)
                {
                    aspectDefinition.Lookup.AddFieldRequireComponentLookup(field);
                    aspectDefinition.TypeHandle.AddFieldRequireCTH(field);
                }

                aspectDefinition.AddQueryField(field);

                if (!field.IsZeroSize)
                {
                    aspectDefinition.FieldsNeedContruction.Add(field);
                    aspectDefinition.ResolvedChunk.ComponentDataNativeArray.Add(field);
                }
                else
                    aspectDefinition.FieldsRequiringDefaultConstruction.Add(field);
                aspectField = field;
                return true;
            }
            aspectField = default;
            return false;
        }

        bool TryParseFieldDynamicBuffer(SemanticModel semanticModel, SyntaxNode node, AspectDefinition aspectDefinition, out AspectField aspectField,
            VariableDeclarationSyntax variableDeclaration)
        {
            if (variableDeclaration.TryGetGenericParam1TypeName(semanticModel, out var symbolName))
            {
                var fieldName = variableDeclaration.Variables.First().Identifier.ToString();
                var field = new BufferAspectField
                {
                    SourceSyntaxNode = variableDeclaration,
                    FieldName = fieldName,
                    InternalFieldName = fieldName + AspectStrings.k_DynamicBufferTag,
                    TypeName = symbolName,
                    InternalVariableName = fieldName.ToLower(),
                    IsReadOnly = node.HasAttributeCandidate(AspectStrings.k_CollectionPackageNamespace, AspectStrings.k_ReadOnly),
                    IsOptional = node.HasAttributeCandidate(AspectStrings.k_EntityPackageNamespace, AspectStrings.k_Optional)
                };

                aspectDefinition.AddQueryField(field);
                aspectDefinition.FieldsNeedContruction.Add(field);
                aspectDefinition.Lookup.BufferLookup.Add(field);
                aspectDefinition.Lookup.Update.Add(field);
                aspectDefinition.ResolvedChunk.BufferAccessors.Add(field);
                aspectDefinition.TypeHandle.BufferTypeHandle.Add(field);
                aspectDefinition.TypeHandle.Update.Add(field);

                aspectField = field;
                return true;
            }
            aspectField = default;
            return false;
        }

        bool TryParseFieldEntity(SemanticModel semanticModel, SyntaxNode node, AspectDefinition aspectDefinition, out AspectField aspectField,
            VariableDeclarationSyntax variableDeclaration)
        {
            // Entity-in-Aspect
            if (aspectDefinition.HasEntityField)
                AspectErrors.SGA0006(variableDeclaration.GetLocation());
            else
            {
                aspectDefinition.EntityField = new EntityField
                {
                    SourceSyntaxNode = variableDeclaration,
                    FieldName = variableDeclaration.Variables.First().Identifier.ToString(),
                    ConstructorAssignment = "entity"
                };
                aspectField = aspectDefinition.EntityField;
                return true;
            }
            aspectField = default;
            return false;
        }

        bool TryParseFieldAspect(SemanticModel semanticModel, SyntaxNode node, AspectDefinition aspectDefinition, out AspectField aspectField,
            VariableDeclarationSyntax variableDeclaration, SymbolInfo symbolInfo)
        {

            var type = symbolInfo.Symbol.GetSymbolType();
            if (type != null && type.Interfaces.Any(i => i.ToFullName() == AspectStrings.k_AspectInterfaceFullName))
            {
                // Nested Aspect Field
                var symbolName = symbolInfo.Symbol.GetSymbolTypeName();
                var fieldName = variableDeclaration.Variables.First().Identifier.ToString();
                var field = new NestedAspectAspectField
                {
                    SourceSyntaxNode = variableDeclaration,
                    FieldName = fieldName,
                    InternalFieldName = fieldName,
                    TypeName = symbolName,
                    InternalVariableName = fieldName.ToLower(),
                    IsReadOnly = node.HasAttributeCandidate(AspectStrings.k_CollectionPackageNamespace, AspectStrings.k_ReadOnly),
                    IsOptional = node.HasAttributeCandidate(AspectStrings.k_EntityPackageNamespace, AspectStrings.k_Optional)
                };

                // aspect do not use the same query implementation as the other field
                // no need to do: aspectDefinition.QueryField.Add(field);

                aspectDefinition.FieldsNeedContruction.Add(field);
                aspectDefinition.AspectFields.Add(field);
                aspectDefinition.Lookup.Update.Add(field);
                aspectDefinition.TypeHandle.Update.Add(field);

                aspectField = field;
                return true;
            }
            aspectField = default;
            return false;
        }

        bool TryParseFieldEnabledRef(SemanticModel semanticModel, SyntaxNode node, AspectDefinition aspectDefinition,
            out AspectField aspectField,
            VariableDeclarationSyntax variableDeclaration, bool isRO)
        {
            var genericTypeSymbol = variableDeclaration.GetGenericParam1Symbol(semanticModel, out var symbolName);
            if (genericTypeSymbol is INamedTypeSymbol genericNameTypeSymbol)
            {
                var fieldName = variableDeclaration.Variables.First().Identifier.ToString();
                if (node.HasAttributeCandidate(AspectStrings.k_CollectionPackageNamespace, AspectStrings.k_ReadOnly))
                {
                    AspectErrors.SGA0011(node.GetLocation());
                    aspectField = default;
                    return false;
                }
                EnabledField field;
                if (isRO)
                {
                    field = new EnabledRefROField
                    {
                        SourceSyntaxNode = variableDeclaration,
                        FieldName = fieldName,
                        InternalFieldName = fieldName,
                        TypeName = symbolName,
                        InternalVariableName = fieldName.ToLower(),
                        IsReadOnly = true,
                        IsOptional = node.HasAttributeCandidate(AspectStrings.k_EntityPackageNamespace, AspectStrings.k_Optional),
                        IsZeroSize = IsZeroSize(genericNameTypeSymbol)
                    };
                }
                else
                {
                    field = new EnabledRefRWField
                    {
                        SourceSyntaxNode = variableDeclaration,
                        FieldName = fieldName,
                        InternalFieldName = fieldName,
                        TypeName = symbolName,
                        InternalVariableName = fieldName.ToLower(),
                        IsReadOnly = false,
                        IsOptional = node.HasAttributeCandidate(AspectStrings.k_EntityPackageNamespace, AspectStrings.k_Optional),
                        IsZeroSize = IsZeroSize(genericNameTypeSymbol)
                    };
                }

                // Require a ComponentLookup for Aspect.Lookup
                aspectDefinition.Lookup.AddFieldRequireComponentLookup(field);

                // Require a ComponentTypeHandle for Aspect.TypeHandle
                aspectDefinition.TypeHandle.AddFieldRequireCTH(field);

                // Require a ComponentEnableBitBuffer for Aspect.ResolvedChunk
                aspectDefinition.ResolvedChunk.ComponentEnableBitBuffer.Add(field);

                // Require the field to be initialized from the constructor parameter
                aspectDefinition.FieldsNeedContruction.Add(field);

                // The field adds constraints to the aspect's query
                aspectDefinition.AddQueryField(field);

                aspectField = field;
                return true;
            }
            aspectField = default;
            return false;
        }

        /// <summary>
        /// Generate all aspects found
        /// </summary>
        /// <param name="context"></param>
        public void Execute(GeneratorExecutionContext context)
        {
            if (!SourceGenHelpers.ShouldRun(context))
                return;

            SourceGenHelpers.Setup(context);

            // Scope a DiagnosticLogger for the entirety of the scope of execution.
            using (Service<IDiagnosticLogger>.Scoped(new DiagnosticLogger(context)))
            // Scope a diagnostic frame, for the entirety of the scope of execution, that provide context on the current aspect being generated
            using (Service<IDiagnosticFrame>.Scoped(this))
            {
                // Keep dictionary of seen semantic models so we can re-use symbol caching
                var treeToSemanticModel = new Dictionary<SyntaxTree, SemanticModel>();
                SemanticModel GetSemanticModel(SyntaxNode syntaxNode)
                {
                    if (treeToSemanticModel.ContainsKey(syntaxNode.SyntaxTree))
                        return treeToSemanticModel[syntaxNode.SyntaxTree];

                    var model = context.Compilation.GetSemanticModel(syntaxNode.SyntaxTree);
                    treeToSemanticModel[syntaxNode.SyntaxTree] = model;
                    return model;
                }

                // Go through aspect candidates and resolve them to the same type (multiple aspect partial declarations can map to a single definition)
                var fullNameToAspectDefinition = new Dictionary<string, AspectDefinition>();
                var aspectReceiver = (AspectReceiver)context.SyntaxReceiver;
                foreach (var aspectCandidate in aspectReceiver._AspectCandidates)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    var aspectDeclarationSymbol = GetSemanticModel(aspectCandidate).GetDeclaredSymbol(aspectCandidate);
                    var aspectFullName = aspectDeclarationSymbol.ToFullName();
                    if (fullNameToAspectDefinition.ContainsKey(aspectFullName))
                        fullNameToAspectDefinition[aspectFullName].SourceSyntaxNodes.Add(aspectCandidate);
                    else
                        fullNameToAspectDefinition[aspectFullName] = new AspectDefinition(aspectCandidate, aspectCandidate.Identifier.ToString(), aspectFullName);
                }

                // Group by syntax tree so we can emit new source per original source file (for inspection of generated code that matches source)
                var aspectsGroupedBySyntaxTree = fullNameToAspectDefinition.Values.GroupBy(kvp => kvp.SourceSyntaxNodes.First().SyntaxTree);

                foreach (var aspectTreeGrouping in aspectsGroupedBySyntaxTree)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    var syntaxTree = aspectTreeGrouping.Key;
                    var syntaxTreeSourceBuilder = new System.IO.StringWriter(new StringBuilder());

                    // Gather all 'using' statement from all source nodes and output
                    foreach (var @using in GetAllUsingsInSyntaxTree(aspectTreeGrouping))
                        syntaxTreeSourceBuilder.WriteLine(@using);

                    foreach (var aspectDef in aspectTreeGrouping)
                    {
                        try
                        {
                            // Check for Disable Generation
                            if (aspectDef.SyntaxHasAttributeCandidate(AspectStrings.k_EntityPackageNamespace, AspectStrings.k_DisableGeneration))
                                continue;

                            bool valid = true;

                            // Report Aspect errors (try to report as many as possible(
                            foreach (var node in aspectDef.SourceSyntaxNodes)
                            {
                                // All instance of the aspect must be declared 'partial'
                                if (!node.HasTokenOfKind(SyntaxKind.PartialKeyword))
                                {
                                    AspectErrors.SGA0003(node.GetLocation(), aspectDef.Name);
                                    valid = false;
                                }

                                // Aspects must be readonly otherwise they don't work well with foreach enumerators
                                // (can't have setters on the foreach value, unless it is marked read-only)
                                if (!node.HasTokenOfKind(SyntaxKind.ReadOnlyKeyword))
                                {
                                    AspectErrors.SGA0005(node.GetLocation());
                                    valid = false;
                                }

                                if(node.TypeParameterList != null)
                                {
                                    AspectErrors.SGA0009(node.GetLocation());
                                    valid = false;
                                }

                                if (node.Parent != null)
                                {
                                    if (node.Parent.IsKind(SyntaxKind.StructDeclaration) ||
                                        node.Parent.IsKind(SyntaxKind.ClassDeclaration))
                                    {
                                        AspectErrors.SGA0010(node.GetLocation());
                                        valid = false;
                                    }
                                }

                                var correctOrAbsent = VerifyIAspectCreateImplementationCorrectOrAbsent(aspectDef, node);

                                if (!correctOrAbsent.Success)
                                {
                                    AspectErrors.SGA0002(node.GetLocation(), correctOrAbsent.ErrorMessage);
                                    valid = false;
                                }
                            }

                            // gather all fields
                            foreach (var childNode in aspectDef.ChildNodes)
                            {
                                var semanticModel = context.Compilation.GetSemanticModel(childNode.SyntaxTree);

                                if (!childNode.IsKind(SyntaxKind.FieldDeclaration)) continue;

                                if (!TryParseField(semanticModel, childNode, aspectDef, out _))
                                {
                                    if (!childNode.HasTokenOfKind(SyntaxKind.ConstKeyword))
                                        AspectErrors.SGA0007(childNode.GetLocation());
                                }
                            }

                            // If there are no field that participates to the query, the aspect in invalid, raise SGA0004
                            if (aspectDef.QueryFields.Count == 0 && aspectDef.AspectFields.Count == 0)
                                AspectErrors.SGA0004(aspectDef.SourceSyntaxNodes.First().GetLocation());

                            if (!valid)
                                continue;

                            aspectDef.ResolveFieldDependencies();

                            syntaxTreeSourceBuilder.Write(AspectSyntaxFactory.GenerateAspectSource(aspectDef));
                        }
                        catch (Exception exception)
                        {
                            if (exception is OperationCanceledException)
                                throw;

                            // Log the exception as "SGA0000"
                            Location loc = default;
                            if (aspectDef != null)
                            {
                                if (aspectDef.SourceSyntaxNodes != null && aspectDef.SourceSyntaxNodes.Any())
                                {
                                    if (aspectDef.SourceSyntaxNodes != null && aspectDef.SourceSyntaxNodes.Any())
                                    {
                                        loc = aspectDef.SourceSyntaxNodes.First().GetLocation();
                                    }
                                }

                                AspectErrors.SGAICE0000(aspectDef.Name, loc, exception);
                            }
                        }
                    }

                    syntaxTreeSourceBuilder.Flush();

                    var generatedSourceHint = syntaxTree.GetGeneratedSourceFileName(s_GeneratorName);
                    var generatedSourceFullPath = syntaxTree.GetGeneratedSourceFilePath(context.Compilation.Assembly, s_GeneratorName);

                    SourceGenHelpers.LogInfo($"Outputting generated aspect source to file {generatedSourceFullPath}...");

                    var code = syntaxTreeSourceBuilder.ToString();
                    SourceGenHelpers.OutputSourceToFile(context, generatedSourceFullPath, Microsoft.CodeAnalysis.Text.SourceText.From(code));
                    context.AddSource(generatedSourceHint, code);

                    syntaxTreeSourceBuilder.Dispose();
                }
            }
        }

        static (bool Success, string ErrorMessage)
            VerifyIAspectCreateImplementationCorrectOrAbsent(AspectDefinition aspect, StructDeclarationSyntax node)
        {
            foreach (var childNode in node.BaseList.Types)
            {
                var aspectCreateCandidate = childNode.Type switch
                {
                    GenericNameSyntax name => name,
                    QualifiedNameSyntax {Right: GenericNameSyntax name} => name,
                    _ => null
                };

                if (aspectCreateCandidate == null)
                    continue;
                if (aspectCreateCandidate.Identifier.ValueText != AspectStrings.k_AspectCreateInterface)
                    continue;

                var aspectTypeName = aspectCreateCandidate.TypeArgumentList.Arguments[0] switch
                {
                    SimpleNameSyntax name => name.Identifier.ValueText,
                    QualifiedNameSyntax { Right: { } name } => name.Identifier.ValueText,
                    _ => null
                };

                aspect.IsIAspectCreateCorrectlyImplementedByUser = node.Identifier.ValueText == aspectTypeName;

                if (aspect.IsIAspectCreateCorrectlyImplementedByUser)
                    return (Success: true, string.Empty);

                return (Success: false,
                    ErrorMessage:
                    $"You have implemented `IAspect<{aspectTypeName}>` on `{node.Identifier.ValueText}`. This is incorrect. Please implement `IAspect<{node.Identifier.ValueText}>` instead. " +
                    $"If you do not implement `IAspect<{node.Identifier.ValueText}>`, a default implementation will be generated for you automatically.");
            }
            return (Success: true, string.Empty);
        }

        static SortedSet<string> GetAllUsingsInSyntaxTree(IGrouping<SyntaxTree, AspectDefinition> aspectTreeGrouping)
        {
            var usings = new SortedSet<string>();
            foreach (var aspect in aspectTreeGrouping)
            {
                foreach (var node in aspect.SourceSyntaxNodes)
                {
                    var root = node.SyntaxTree.GetRoot();
                    foreach (var childNode in root.ChildNodes().OfType<UsingDirectiveSyntax>())
                    {
                        var @using = childNode.WithoutTrivia();
                        usings.Add(@using.ToString());
                    }
                }
            }

            return usings;
        }

        /// <summary>
        /// Diagnostic frame brief information
        /// </summary>
        /// <returns></returns>
        string IDiagnosticFrame.GetBrief()
        {
            return null;

            // TODO: fix this so that it doesn't rely on fields stored in AspectGenerator (generators are not allowed to store state)
            /*
            if (m_CurrentAspectDefinition == null) return "";
            var sb = new StringBuilder();
            sb.Append($"Aspect: \"{m_CurrentAspectDefinition.Name}\"");
            return sb.ToString();
            */
        }

        /// <summary>
        /// Diagnostic frame complete information
        /// </summary>
        /// <returns></returns>
        string IDiagnosticFrame.GetMessage()
        {
            return null;

            // TODO: fix this so that it doesn't rely on fields stored in AspectGenerator (generators are not allowed to store state)
            /*
            if (m_CurrentAspectDefinition == null) return "AspectGenerator";
            var sb = new StringBuilder();
            sb.AppendLine($"Aspect: \"{m_CurrentAspectDefinition.Name}\"");
            if (m_CurrentAspectDefinition.SourceSyntaxNodes.Count == 1)
            {
                sb.AppendLine($"Location: {m_CurrentAspectDefinition.SourceSyntaxNodes.Single().GetLocation()}");
            }
            else
            {
                sb.AppendLine($"Locations:");
                sb.Append(m_CurrentAspectDefinition.SourceSyntaxNodes.Select(x => x.LocationString()).SeparateByNewLine());
            }
            return sb.ToString();
            */
        }
    }
}
