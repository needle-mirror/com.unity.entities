using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Text;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    static class AuthoringComponentFactory
    {
        internal static ClassDeclarationSyntax GenerateBufferingElementDataAuthoring(AuthoringComponent authoringComponent)
        {
            bool exactlyOneField = authoringComponent.FieldDescriptions.Count() == 1;

            if (exactlyOneField)
            {
                return GenerateBufferingElementDataAuthoringClass(authoringComponent, authoringComponent.FieldDescriptions.Single().FieldSymbol.Type.ToString());
            }

            string generatedStructName = $"___{authoringComponent.DeclaringType.Name}GeneratedStruct___";
            return GenerateBufferingElementDataAuthoringClass(authoringComponent, generatedStructName, hasNestedGeneratedClass: true);
        }

        internal static ClassDeclarationSyntax GenerateComponentDataAuthoring(AuthoringComponent authoringComponent)
        {
            string generatedClass = CreateComponentDataAuthoringClass(authoringComponent);
            return (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(generatedClass);
        }

        static ClassDeclarationSyntax GenerateBufferingElementDataAuthoringClass(
            AuthoringComponent authoringComponent, string storedType, bool hasNestedGeneratedClass = false)
        {
            const string placeholder = "___HasNestedGeneratedClassPlaceholder___";

            string generatedClass =
                $@"[UnityEngine.DisallowMultipleComponent]
                   [System.Runtime.CompilerServices.CompilerGenerated]
                   {authoringComponent.AttributesToPreserve.Select(a => $"[{a}]").SeparateByNewLine()}
                   public class {authoringComponent.DeclaringType.Name}Authoring : UnityEngine.MonoBehaviour, Unity.Entities.IConvertGameObjectToEntity
                   {{
                        {placeholder}            

                        public {storedType}[] Values;

                        public void Convert(Unity.Entities.Entity __entity,
                                            Unity.Entities.EntityManager __dstManager, 
                                            GameObjectConversionSystem _)
                        {{
                            Unity.Entities.DynamicBuffer<{authoringComponent.FullyQualifiedTypeName}> dynamicBuffer =
                                __dstManager.AddBuffer<{authoringComponent.FullyQualifiedTypeName}>(__entity);

                            dynamicBuffer.ResizeUninitialized(Values.Length);

                            for (int i = 0; i < dynamicBuffer.Length; i++)
                            {{
                                dynamicBuffer[i] = new {authoringComponent.FullyQualifiedTypeName} 
                                                   {{ 
                                                       {GenerateBufferElementDataFieldAssignments(authoringComponent)}
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
                   $@"[System.Serializable]
                                [System.Runtime.CompilerServices.CompilerGenerated]
                                public struct {storedType}
                                {{
                                    {CreateBufferElementDataClassFields(authoringComponent)}
                                }}");

            return (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(withNestedType);
        }

        static string GenerateBufferElementDataFieldAssignments(AuthoringComponent authoringComponent)
        {
            if (authoringComponent.FieldDescriptions.Count() > 1)
            {
                return authoringComponent.FieldDescriptions.Select(description =>
                {
                    string fieldName = description.FieldSymbol.Name;
                    return $"{fieldName} = Values[i].{fieldName}";
                }).SeparateByCommaAndNewLine();
            }
            return $"{authoringComponent.FieldDescriptions.Single().FieldSymbol.Name} = Values[i]";
        }

        static string CreateBufferElementDataClassFields(AuthoringComponent authoringComponent)
        {
            return authoringComponent.FieldDescriptions
                                     .Select(description => $"public {description.FieldSymbol.Type} {description.FieldSymbol.Name};")
                                     .SeparateByNewLine();
        }

        static string CreateComponentDataAuthoringClass(AuthoringComponent authoringComponent)
        {
            string declareReferencedPrefabsInterface =
                authoringComponent.ImplementIDeclareReferencedPrefabs ? ", Unity.Entities.IDeclareReferencedPrefabs" : string.Empty;

            const string placeholder = "$$$InsertDeclareReferencedPrefabsMethodHere$$$";

            string generatedClass =
                 $@"[UnityEngine.DisallowMultipleComponent]
                    [System.Runtime.CompilerServices.CompilerGenerated]
                    {authoringComponent.AttributesToPreserve.Select(a => $"[{a}]").SeparateByNewLine()}
                    public class {authoringComponent.DeclaringType.Name}Authoring : UnityEngine.MonoBehaviour, Unity.Entities.IConvertGameObjectToEntity{declareReferencedPrefabsInterface}
                    {{
                         {CreateAuthoringClassFieldDeclarations(authoringComponent)}
                    
                         public void Convert(Unity.Entities.Entity __entity, 
                                             Unity.Entities.EntityManager __dstManager, 
                                             GameObjectConversionSystem __conversionSystem)
                         {{
                             {CreateConversionMethodBody(authoringComponent)}
                         }}
                    
                         {placeholder}
                    }}";

            if (!authoringComponent.ImplementIDeclareReferencedPrefabs)
            {
                return generatedClass.Replace(oldValue: placeholder, newValue: string.Empty);
            }

            string declareReferencedPrefabsMethod =
                $@"public void DeclareReferencedPrefabs(System.Collections.Generic.List<UnityEngine.GameObject> __referencedPrefabs) 
                   {{
                       {CreateDeclareReferencedPrefabsMethodBody(authoringComponent)}
                   }}";

            return generatedClass.Replace(oldValue: placeholder, newValue: declareReferencedPrefabsMethod);
        }

        private static string CreateDeclareReferencedPrefabsMethodBody(AuthoringComponent authoringComponent)
        {
            var methodBody = new StringBuilder();

            foreach (AuthoringComponentFieldDescription description in authoringComponent.FieldDescriptions)
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

        private static string CreateConversionMethodBody(AuthoringComponent authoringComponent)
        {
            StringBuilder methodBody = new StringBuilder();

            if (authoringComponent.DeclaringType.IsValueType)
            {
                methodBody.AppendLine(
                    $"{authoringComponent.FullyQualifiedTypeName} component = default({authoringComponent.FullyQualifiedTypeName});");
            }
            else
            {
                methodBody.AppendLine(
                    $"{authoringComponent.FullyQualifiedTypeName} component = new {authoringComponent.FullyQualifiedTypeName}();");
            }

            foreach (var fieldDescription in authoringComponent.FieldDescriptions)
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

            methodBody.AppendLine(authoringComponent.FromValueType
                ? "__dstManager.AddComponentData(__entity, component);"
                : "Unity.Entities.EntityManagerManagedComponentExtensions.AddComponentData(__dstManager, __entity, component);");

            return methodBody.ToString();
        }

        private static string CreateAuthoringClassFieldDeclarations(AuthoringComponent authoringComponent)
        {
            var fieldDeclarations = new StringBuilder();

            foreach (var fieldDescription in authoringComponent.FieldDescriptions)
            {
                switch (fieldDescription.FieldType)
                {
                    case FieldType.SingleEntity:
                        fieldDeclarations.AppendLine($"public UnityEngine.GameObject {fieldDescription.FieldSymbol.Name};");
                        break;
                    case FieldType.EntityArray:
                        fieldDeclarations.AppendLine($"public UnityEngine.GameObject[] {fieldDescription.FieldSymbol.Name};");
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
