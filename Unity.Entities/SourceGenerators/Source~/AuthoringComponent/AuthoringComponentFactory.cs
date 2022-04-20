using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Text;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.AuthoringComponent
{
    static class AuthoringComponentFactory
    {
        internal static ClassDeclarationSyntax GenerateBufferingElementDataAuthoring(AuthoringComponentDescription authoringComponentDescription)
        {
            bool exactlyOneField = authoringComponentDescription.FieldDescriptions.Count() == 1;

            if (exactlyOneField)
            {
                return GenerateBufferingElementDataAuthoringClass(authoringComponentDescription, authoringComponentDescription.FieldDescriptions.Single().FieldSymbol.Type.ToString());
            }

            string generatedStructName = $"___{authoringComponentDescription.DeclaringType.Name}GeneratedStruct___";
            return GenerateBufferingElementDataAuthoringClass(authoringComponentDescription, generatedStructName, hasNestedGeneratedClass: true);
        }

        internal static ClassDeclarationSyntax GenerateComponentDataAuthoring(AuthoringComponentDescription authoringComponentDescription)
        {
            string generatedClass = CreateComponentDataAuthoringClass(authoringComponentDescription);
            return (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(generatedClass);
        }

        static ClassDeclarationSyntax GenerateBufferingElementDataAuthoringClass(
            AuthoringComponentDescription authoringComponentDescription, string storedType, bool hasNestedGeneratedClass = false)
        {
            const string placeholder = "___HasNestedGeneratedClassPlaceholder___";

            string generatedClass =
                $@"[UnityEngine.DisallowMultipleComponent]
                   [global::System.Runtime.CompilerServices.CompilerGenerated]
                   {authoringComponentDescription.AttributesToPreserve.Select(a => $"[{a}]").SeparateByNewLine()}
                   public class {authoringComponentDescription.DeclaringType.Name}Authoring : UnityEngine.MonoBehaviour, Unity.Entities.IConvertGameObjectToEntity
                   {{
                        {placeholder}

                        public {storedType}[] Values;

                        public void Convert(Unity.Entities.Entity __entity,
                                            Unity.Entities.EntityManager __dstManager,
                                            GameObjectConversionSystem _)
                        {{
                            Unity.Entities.DynamicBuffer<{authoringComponentDescription.FullyQualifiedTypeName}> dynamicBuffer =
                                __dstManager.AddBuffer<{authoringComponentDescription.FullyQualifiedTypeName}>(__entity);

                            dynamicBuffer.ResizeUninitialized(Values.Length);

                            for (int i = 0; i < dynamicBuffer.Length; i++)
                            {{
                                dynamicBuffer[i] = new {authoringComponentDescription.FullyQualifiedTypeName}
                                                   {{
                                                       {GenerateBufferElementDataFieldAssignments(authoringComponentDescription)}
                                                   }};
                            }}
                        }}
                   }}";

            if (!hasNestedGeneratedClass)
            {
                return (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(generatedClass.Replace(placeholder, String.Empty));
            }

            string withNestedType =
                generatedClass.Replace(
                    placeholder,
                   $@"[global::System.Serializable]
                                [global::System.Runtime.CompilerServices.CompilerGenerated]
                                public struct {storedType}
                                {{
                                    {CreateBufferElementDataClassFields(authoringComponentDescription)}
                                }}");

            return (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(withNestedType);
        }

        static string GenerateBufferElementDataFieldAssignments(AuthoringComponentDescription authoringComponentDescription)
        {
            if (authoringComponentDescription.FieldDescriptions.Count() > 1)
            {
                return authoringComponentDescription.FieldDescriptions.Select(description =>
                {
                    string fieldName = description.FieldSymbol.Name;
                    return $"{fieldName} = Values[i].{fieldName}";
                }).SeparateByCommaAndNewLine();
            }
            return $"{authoringComponentDescription.FieldDescriptions.Single().FieldSymbol.Name} = Values[i]";
        }

        static string CreateBufferElementDataClassFields(AuthoringComponentDescription authoringComponentDescription)
        {
            return authoringComponentDescription.FieldDescriptions
                                     .Select(description => $"public {description.FieldSymbol.Type} {description.FieldSymbol.Name};")
                                     .SeparateByNewLine();
        }

        static string CreateComponentDataAuthoringClass(AuthoringComponentDescription authoringComponentDescription)
        {
            string declareReferencedPrefabsInterface =
                authoringComponentDescription.ImplementIDeclareReferencedPrefabs ? ", Unity.Entities.IDeclareReferencedPrefabs" : string.Empty;

            const string placeholder = "$$$InsertDeclareReferencedPrefabsMethodHere$$$";

            string generatedClass =
                 $@"[UnityEngine.DisallowMultipleComponent]
                    [global::System.Runtime.CompilerServices.CompilerGenerated]
                    {authoringComponentDescription.AttributesToPreserve.Select(a => $"[{a}]").SeparateByNewLine()}
                    public class {authoringComponentDescription.DeclaringType.Name}Authoring : UnityEngine.MonoBehaviour, Unity.Entities.IConvertGameObjectToEntity{declareReferencedPrefabsInterface}
                    {{
                         {CreateAuthoringClassFieldDeclarations(authoringComponentDescription)}

                         public void Convert(Unity.Entities.Entity __entity,
                                             Unity.Entities.EntityManager __dstManager,
                                             GameObjectConversionSystem __conversionSystem)
                         {{
                             {CreateConversionMethodBody(authoringComponentDescription)}
                         }}

                         {placeholder}
                    }}";

            if (!authoringComponentDescription.ImplementIDeclareReferencedPrefabs)
            {
                return generatedClass.Replace(oldValue: placeholder, newValue: string.Empty);
            }

            string declareReferencedPrefabsMethod =
                $@"public void DeclareReferencedPrefabs(global::System.Collections.Generic.List<UnityEngine.GameObject> __referencedPrefabs)
                   {{
                       {CreateDeclareReferencedPrefabsMethodBody(authoringComponentDescription)}
                   }}";

            return generatedClass.Replace(oldValue: placeholder, newValue: declareReferencedPrefabsMethod);
        }

        static string CreateDeclareReferencedPrefabsMethodBody(AuthoringComponentDescription authoringComponentDescription)
        {
            var methodBody = new StringBuilder();

            foreach (AuthoringComponentFieldDescription description in authoringComponentDescription.FieldDescriptions)
            {
                switch (description.FieldType)
                {
                    case FieldType.SingleEntity:
                        methodBody.AppendLine(
                            $@"Unity.Entities.Hybrid.Internal.GeneratedAuthoringComponentImplementation.AddReferencedPrefab(
                                   __referencedPrefabs,
                                   {description.FieldSymbol.Name});");
                        break;
                    case FieldType.EntityArray:
                        methodBody.AppendLine(
                            $@"Unity.Entities.Hybrid.Internal.GeneratedAuthoringComponentImplementation.AddReferencedPrefabs(
                                   __referencedPrefabs,
                                   {description.FieldSymbol.Name});");
                        break;
                }
            }

            return methodBody.ToString();
        }

        static string CreateConversionMethodBody(AuthoringComponentDescription authoringComponentDescription)
        {
            StringBuilder methodBody = new StringBuilder();

            if (authoringComponentDescription.DeclaringType.IsValueType)
            {
                methodBody.AppendLine(
                    $"{authoringComponentDescription.FullyQualifiedTypeName} component = default({authoringComponentDescription.FullyQualifiedTypeName});");
            }
            else
            {
                methodBody.AppendLine(
                    $"{authoringComponentDescription.FullyQualifiedTypeName} component = new {authoringComponentDescription.FullyQualifiedTypeName}();");
            }

            foreach (var fieldDescription in authoringComponentDescription.FieldDescriptions)
            {
                switch (fieldDescription.FieldType)
                {
                    case FieldType.SingleEntity:
                        methodBody.AppendLine(
                            $"component.{fieldDescription.FieldSymbol.Name} = __conversionSystem.GetPrimaryEntity({fieldDescription.FieldSymbol.Name});");
                        break;
                    case FieldType.EntityArray:
                        methodBody.AppendLine(
                            $@"Unity.Entities.GameObjectConversionUtility.ConvertGameObjectsToEntitiesField(
                                   __conversionSystem,
                                   {fieldDescription.FieldSymbol.Name},
                                   out component.{fieldDescription.FieldSymbol.Name});");
                        break;
                    default:
                        methodBody.AppendLine($"component.{fieldDescription.FieldSymbol.Name} = {fieldDescription.FieldSymbol.Name};");
                        break;
                }
            }

            methodBody.AppendLine(authoringComponentDescription.FromValueType
                ? "__dstManager.AddComponentData(__entity, component);"
                : "Unity.Entities.EntityManagerManagedComponentExtensions.AddComponentData(__dstManager, __entity, component);");

            return methodBody.ToString();
        }


        private static void SetupRegisterBindingAttribute(AuthoringComponentDescription authoringComponentDescription,
            ref StringBuilder fieldDeclarations,AuthoringComponentFieldDescription fieldDescription)
        {
            var vectorVariables = new[]{'x', 'y', 'z', 'w'};
            int count = 0;

            string className = authoringComponentDescription.DeclaringType.Name;
            string typeName  = fieldDescription.FieldSymbol.Type.ToFullName();
            string varName = fieldDescription.FieldSymbol.Name;

            if (typeName == "Unity.Mathematics.float4" || typeName == "Unity.Mathematics.bool4" || typeName == "Unity.Mathematics.int4")
                count = 4;
            else if (typeName == "Unity.Mathematics.float3" || typeName == "Unity.Mathematics.bool3" || typeName == "Unity.Mathematics.int3")
                count = 3;
            else if (typeName == "Unity.Mathematics.float2" || typeName == "Unity.Mathematics.bool2" || typeName == "Unity.Mathematics.int2")
                count = 2;

            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    fieldDeclarations.AppendLine($"[Unity.Entities.RegisterBinding(typeof({className})," +
                                                 $"\"{varName}." + vectorVariables[i] + "\",true)]");
                }
            }
            //default case of binding a name without some vector variable
            else
            {
                fieldDeclarations.AppendLine($"[Unity.Entities.RegisterBinding(typeof({className})," +
                                             $"\"{varName}\")]");
            }

            fieldDeclarations.AppendLine($"public {typeName} {varName};");
        }

        private static string CreateAuthoringClassFieldDeclarations(AuthoringComponentDescription authoringComponentDescription)
        {
            var fieldDeclarations = new StringBuilder();

            foreach (var fieldDescription in authoringComponentDescription.FieldDescriptions)
            {
                switch (fieldDescription.FieldType)
                {
                    case FieldType.SingleEntity:
                        fieldDeclarations.AppendLine($"public UnityEngine.GameObject {fieldDescription.FieldSymbol.Name};");
                        break;
                    case FieldType.EntityArray:
                        fieldDeclarations.AppendLine($"public UnityEngine.GameObject[] {fieldDescription.FieldSymbol.Name};");
                        break;
                    case FieldType.NonEntityValueType:
                        SetupRegisterBindingAttribute(authoringComponentDescription, ref fieldDeclarations, fieldDescription);
                        break;
                    default:
                        fieldDeclarations.AppendLine($"public {fieldDescription.FieldSymbol.Type} {fieldDescription.FieldSymbol.Name};");
                        break;
                }
            }
            return fieldDeclarations.ToString();
        }
    }
}
