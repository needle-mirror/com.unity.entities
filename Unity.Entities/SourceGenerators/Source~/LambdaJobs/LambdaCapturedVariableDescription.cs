using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
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
        { get
            {
                if (Symbol is ILocalSymbol localSymbol)
                    return localSymbol.Type;
                else if (Symbol is IParameterSymbol parameterSymbol)
                    return parameterSymbol.Type;
                else if (Symbol is ITypeSymbol typeSymbol)
                    return typeSymbol;
                else
                    throw new InvalidOperationException($"Cannot discover type for symbol {Symbol}");
            }
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

        public delegate bool CheckAttributeApplicable();

        public struct AttributeDescription
        {
            public AttributeDescription(string methodName, string attributeName, CheckAttributeApplicable check = null)
            {
                MethodName = methodName;
                AttributeName = attributeName;
                CheckAttributeApplicable = check;
            }

            public string MethodName;
            public string AttributeName;
            public CheckAttributeApplicable CheckAttributeApplicable;
        }

        public static readonly List<AttributeDescription> AttributesDescriptions = new List<AttributeDescription>
        {
            new AttributeDescription("WithReadOnly", "Unity.Collections.ReadOnly", CheckReadOnly),
            new AttributeDescription("WithNativeDisableContainerSafetyRestriction", "Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction", CheckNativeDisableContainerSafetyRestriction),
            new AttributeDescription("WithNativeDisableUnsafePtrRestriction", "Unity.Collections.LowLevel.Unsafe.NativeDisableUnsafePtrRestriction", CheckNativeDisableUnsafePtrRestriction),
            new AttributeDescription("WithNativeDisableParallelForRestriction", "Unity.Collections.NativeDisableParallelForRestriction", CheckNativeDisableParallelForRestriction),
        };

        // TODO: Add symbol checking here to make sure type is correct for these attributes
        static bool CheckReadOnly() => true;
        static bool CheckNativeDisableContainerSafetyRestriction() => true;
        static bool CheckNativeDisableUnsafePtrRestriction() => true;
        static bool CheckNativeDisableParallelForRestriction() => true;

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
