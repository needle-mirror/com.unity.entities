using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    class DataLookupFieldDescription
    {
        public bool IsReadOnly { get; }
        public ITypeSymbol Type { get; }
        public LambdaJobsPatchableMethod.AccessorDataType AccessorDataType { get; }
        public string FieldName { get; }

        public DataLookupFieldDescription(bool isReadOnly, ITypeSymbol type, LambdaJobsPatchableMethod.AccessorDataType accessorDataType)
        {
            IsReadOnly = isReadOnly;
            Type = type;
            AccessorDataType = accessorDataType;
            FieldName = AccessorDataType switch
            {
                LambdaJobsPatchableMethod.AccessorDataType.ComponentLookup =>
                    $"__{Type.ToValidIdentifier()}_ComponentLookup",
                LambdaJobsPatchableMethod.AccessorDataType.BufferLookup =>
                    $"__{Type.ToValidIdentifier()}_BufferLookup",
                LambdaJobsPatchableMethod.AccessorDataType.AspectLookup =>
                    $"__{Type.ToValidIdentifier()}_AspectLookup",
                _ => $"__{Type.ToValidIdentifier()}_AspectLookup"
            };

        }

        public string JobStructAssign(string systemStateParam)
        {
            return AccessorDataType switch
            {
                LambdaJobsPatchableMethod.AccessorDataType.ComponentLookup =>
                    $"{FieldName} = __{Type.ToValidIdentifier()}_{(IsReadOnly ? "RO" : "RW")}_ComponentLookup",
                LambdaJobsPatchableMethod.AccessorDataType.BufferLookup =>
                    $"{FieldName} = __{Type.ToValidIdentifier()}_{(IsReadOnly ? "RO" : "RW")}_BufferLookup",
                LambdaJobsPatchableMethod.AccessorDataType.AspectLookup =>
                    $"{FieldName} = __{Type.ToValidIdentifier()}_{(IsReadOnly ? "RO" : "RW")}_AspectLookup",
                LambdaJobsPatchableMethod.AccessorDataType.EntityStorageInfoLookup =>
                    $"{FieldName} = {systemStateParam}GetEntityStorageInfoLookup()",
                _ => ""
            };
        }
        public string FormatUpdateInvocation(LambdaJobDescription description)
        {
            var fieldNameWithReadAccess = AccessorDataType switch
            {
                LambdaJobsPatchableMethod.AccessorDataType.ComponentLookup =>
                    $"__{Type.ToValidIdentifier()}_{(IsReadOnly ? "RO" : "RW")}_ComponentLookup",
                LambdaJobsPatchableMethod.AccessorDataType.BufferLookup =>
                    $"__{Type.ToValidIdentifier()}_{(IsReadOnly ? "RO" : "RW")}_BufferLookup",
                LambdaJobsPatchableMethod.AccessorDataType.AspectLookup =>
                    $"__{Type.ToValidIdentifier()}_{(IsReadOnly ? "RO" : "RW")}_AspectLookup",
                _ => $"__{Type.ToValidIdentifier()}_UnknownLookup"
            };
            return $@"{fieldNameWithReadAccess}.Update(ref {description.SystemStateParameterName});";
        }

        public FieldDeclarationSyntax ToFieldDeclaration()
        {
            var accessAttribute = IsReadOnly ? "[Unity.Collections.ReadOnly]" : string.Empty;

            var dataLookupType = AccessorDataType switch
            {
                LambdaJobsPatchableMethod.AccessorDataType.ComponentLookup => "Unity.Entities.ComponentLookup",
                LambdaJobsPatchableMethod.AccessorDataType.BufferLookup => "Unity.Entities.BufferLookup",
                LambdaJobsPatchableMethod.AccessorDataType.EntityStorageInfoLookup => "Unity.Entities.EntityStorageInfoLookup",
                LambdaJobsPatchableMethod.AccessorDataType.AspectLookup => $"{Type.ToFullName()}.Lookup",
                _ => throw new ArgumentOutOfRangeException()
            };

            var typeArgumentSnippet = $"<{Type.ToFullName()}>".EmitIfTrue(AccessorDataType == LambdaJobsPatchableMethod.AccessorDataType.ComponentLookup | AccessorDataType == LambdaJobsPatchableMethod.AccessorDataType.BufferLookup);
            var template = $@"{accessAttribute} public {dataLookupType}{typeArgumentSnippet} {FieldName};";


            return (FieldDeclarationSyntax) SyntaxFactory.ParseMemberDeclaration(template);
        }
    }
}
