using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    public class LambdaCapturedVariableDescription
    {
        public ISymbol Symbol { get; }
        public bool IsThis => ExplicitThis || Symbol.Name == "this";
        public string VariableFieldName => $"{(IsThis ? "__this" : Symbol.Name)}";
        public string OriginalVariableName => $"{(IsThis ? "this" : Symbol.Name)}";
        public List<string> Attributes { get; }
        public bool ExplicitThis { get; }
        public bool IsWritable { get; }

        public ITypeSymbol Type
        {
            get
            {
                return Symbol switch
                {
                    ILocalSymbol localSymbol => localSymbol.Type,
                    IParameterSymbol parameterSymbol => parameterSymbol.Type,
                    ITypeSymbol typeSymbol => typeSymbol,
                    _ => throw new InvalidOperationException($"Cannot discover type for symbol {Symbol}")
                };
            }
        }

        public bool IsNativeContainer
        {
            get => Type.GetAttributes().Any(attribute => attribute.AttributeClass.ToFullName() == "Unity.Collections.LowLevel.Unsafe.NativeContainerAttribute");
        }

        public LambdaCapturedVariableDescription(ISymbol symbol, bool explicitThis = false)
        {
            Symbol = symbol;
            Attributes = new List<string>();
            ExplicitThis = explicitThis;

            // This is not fantastic, but I haven't found a better way to tell if this was declared using a using statement,
            // in which case, we cannot write back to this variable.
            // Searching for a better way: https://stackoverflow.com/questions/64467518/roslyn-detect-when-a-local-variable-has-been-declared-with-using
            IsWritable = true;
            var declaringSyntax = symbol.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault();
            if (declaringSyntax?.GetSyntax().Parent is VariableDeclarationSyntax variableDeclarationSyntax &&
                variableDeclarationSyntax?.Parent is UsingStatementSyntax)
                IsWritable = false;
        }

        public delegate bool CheckAttributeApplicable(SystemGeneratorContext systemGeneratorContext, SemanticModel model,
            LambdaCapturedVariableDescription capturedVariableDescription);

        public readonly struct AttributeDescription
        {
            public AttributeDescription(string methodName, string attributeName, CheckAttributeApplicable check = null)
            {
                MethodName = methodName;
                AttributeName = attributeName;
                CheckAttributeApplicable = check;
            }

            public readonly string MethodName;
            public readonly string AttributeName;
            public readonly CheckAttributeApplicable CheckAttributeApplicable;
        }

        public static readonly List<AttributeDescription> AttributesDescriptions = new List<AttributeDescription>
        {
            new AttributeDescription("WithReadOnly", "Unity.Collections.ReadOnly", CheckReadOnly),
            new AttributeDescription("WithNativeDisableContainerSafetyRestriction", "Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction", CheckNativeDisableContainerSafetyRestriction),
            new AttributeDescription("WithNativeDisableUnsafePtrRestriction", "Unity.Collections.LowLevel.Unsafe.NativeDisableUnsafePtrRestriction", CheckNativeDisableUnsafePtrRestriction),
            new AttributeDescription("WithNativeDisableParallelForRestriction", "Unity.Collections.NativeDisableParallelForRestriction", CheckNativeDisableParallelForRestriction),
        };

        static bool CheckHasNativeContainerAttribute(SystemGeneratorContext systemGeneratorContext, LambdaCapturedVariableDescription capturedVariableDescription,
            Action<SystemGeneratorContext, Location, string, string> action)
        {
            const string nativeContainerAttributeName = "Unity.Collections.LowLevel.Unsafe.NativeContainerAttribute";
            if (!(capturedVariableDescription.Symbol is ILocalSymbol localSymbol && localSymbol.Type.HasAttributeOrFieldWithAttribute(nativeContainerAttributeName) ||
                  capturedVariableDescription.Symbol is IParameterSymbol parameterSymbol && parameterSymbol.Type.HasAttributeOrFieldWithAttribute(nativeContainerAttributeName)))
            {
                action(systemGeneratorContext, capturedVariableDescription.Symbol.Locations.First(), capturedVariableDescription.Symbol.Name, capturedVariableDescription.Type.Name);
                return false;
            }
            return true;
        }

        static bool CheckReadOnly(SystemGeneratorContext context, SemanticModel model, LambdaCapturedVariableDescription capturedVariableDescription) =>
            CheckHasNativeContainerAttribute(context, capturedVariableDescription, LambdaJobsErrors.DC0034);
        static bool CheckNativeDisableContainerSafetyRestriction(SystemGeneratorContext context, SemanticModel model, LambdaCapturedVariableDescription capturedVariableDescription) =>
            CheckHasNativeContainerAttribute(context, capturedVariableDescription, LambdaJobsErrors.DC0036);
        static bool CheckNativeDisableUnsafePtrRestriction(SystemGeneratorContext context, SemanticModel model, LambdaCapturedVariableDescription capturedVariableDescription) => true;
        static bool CheckNativeDisableParallelForRestriction(SystemGeneratorContext context, SemanticModel model, LambdaCapturedVariableDescription capturedVariableDescription) =>
            CheckHasNativeContainerAttribute(context, capturedVariableDescription, LambdaJobsErrors.DC0037);

        public bool SupportsDeallocateOnJobCompletion()
        {
            if (Type.GetAttributes().Any(attribute =>
                attribute.AttributeClass.ToFullName() == "Unity.Collections.LowLevel.Unsafe.NativeContainerSupportsDeallocateOnJobCompletionAttribute"))
                return true;

            foreach (var field in Type.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.Type.GetAttributes().Any(attribute =>
                    attribute.AttributeClass.ToFullName() == "Unity.Collections.LowLevel.Unsafe.NativeContainerSupportsDeallocateOnJobCompletionAttribute"))
                    return true;
            }

            return false;
        }

        public IEnumerable<string> NamesOfAllDisposableMembersIncludingOurselves()
        {
            var allNames = new List<string>();
            //SourceGenHelpers.LogInfo($"Dispose Candidate Methods of {VariableFieldName}");
            //foreach (var meth in Type.GetMembers().OfType<IMethodSymbol>())
            //    SourceGenHelpers.LogInfo(meth.Name);
            if (Type.GetMembers().OfType<IMethodSymbol>().Any(method => method.Name == "Dispose"))
                allNames.Add(VariableFieldName);
            else
            {
                void LocalRecurse(string currentName, ITypeSymbol currentType)
                {
                    foreach (var field in currentType.GetMembers().OfType<IFieldSymbol>())
                    {
                        if (field.IsStatic || field.Type.TypeKind == TypeKind.Array || field.Type.TypeKind == TypeKind.Pointer)
                            continue;
                        var fieldMemberName = $"{currentName}.{field.Name}";
                        if (field.Type.GetMembers().OfType<IMethodSymbol>().Any(method => method.Name == "Dispose"))
                            allNames.Add(fieldMemberName);
                        else
                            LocalRecurse(fieldMemberName, field.Type);
                    }
                }

                LocalRecurse(VariableFieldName, Type);
            }
            return allNames;
        }
    }
}
