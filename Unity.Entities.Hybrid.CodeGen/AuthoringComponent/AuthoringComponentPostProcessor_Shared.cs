using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Unity.Entities.CodeGen;
using UnityEngine;

[assembly: InternalsVisibleTo("Unity.Entities.Hybrid.CodeGen.Tests")]
namespace Unity.Entities.Hybrid.CodeGen
{
    internal partial class AuthoringComponentPostProcessor : EntitiesILPostProcessor
    {
#if !ROSLYN_SOURCEGEN_ENABLED
        private bool _isRunningTests;
        private TypeDefinition _typeToTest;
#endif

        private enum Interface
        {
            IComponentData,
            IBufferElementData,
            None
        }

        private static Interface GetAuthoringComponentTypeInterface(TypeDefinition typeDefinition)
        {
            if (typeDefinition.Interfaces.Any(i => i.InterfaceType.Name == nameof(IBufferElementData)))
            {
                return Interface.IBufferElementData;
            }

            if (typeDefinition.Interfaces.Any(i => i.InterfaceType.Name == nameof(IComponentData)))
            {
                return Interface.IComponentData;
            }

            return Interface.None;
        }

        static TypeDefinition CreateAuthoringType(TypeDefinition componentType)
        {
            var authoringType = new TypeDefinition(componentType.Namespace, $"{componentType.Name}Authoring", TypeAttributes.Class)
            {
                Scope = componentType.Scope
            };

            authoringType.CustomAttributes.Add(
                new CustomAttribute(componentType.Module.ImportReference(
                    typeof(DOTSCompilerGeneratedAttribute).GetConstructors().Single())));
            authoringType.CustomAttributes.Add(
                new CustomAttribute(componentType.Module.ImportReference(
                    typeof(DisallowMultipleComponent).GetConstructors().Single(c => !c.GetParameters().Any()))));

            return authoringType;
        }

#if !ROSLYN_SOURCEGEN_ENABLED
        private static AuthoringComponentPostProcessor TestPostProcessor(TypeDefinition typeDefinition)
        {
            return new AuthoringComponentPostProcessor
            {
                _isRunningTests = true,
                _typeToTest = typeDefinition
            };
        }

        internal static bool RunTest(TypeDefinition typeDefinitionToTest)
        {
            return TestPostProcessor(typeDefinitionToTest).PostProcessImpl(new TypeDefinition[0]);
        }
#endif

        protected override bool PostProcessImpl(TypeDefinition[] _)
        {
#if ROSLYN_SOURCEGEN_ENABLED
            return false;
#else
            TypeDefinition[] typesWithGenerateAuthoringComponentAttribute =
                 _isRunningTests
                     ? new []{_typeToTest}
                     : AssemblyDefinition.MainModule.Types.Where(HasGenerateAuthoringComponentAttribute).ToArray();

            if (typesWithGenerateAuthoringComponentAttribute.Length == 0)
            {
                return false;
            }

            foreach (TypeDefinition typeDefinition in typesWithGenerateAuthoringComponentAttribute)
            {
                Interface @interface = GetAuthoringComponentTypeInterface(typeDefinition);
                switch (@interface)
                {
                    case Interface.IComponentData:
                        CreateComponentDataAuthoringType(typeDefinition);
                        break;
                    case Interface.IBufferElementData:
                        CreateBufferElementDataAuthoringType(typeDefinition);
                        break;
                    default:
                        UserError.DC3003(typeDefinition).Throw();
                        break;
                }
            }
            return true;


            bool HasGenerateAuthoringComponentAttribute(TypeDefinition typeDefinition)
            {
                return typeDefinition.HasCustomAttributes
                       && typeDefinition.CustomAttributes.Any(c =>
                           c.AttributeType.Name == nameof(GenerateAuthoringComponentAttribute));
            }
#endif
        }

        static MethodDefinition CreateEmptyConvertMethod(ModuleDefinition componentDataModule, TypeDefinition authoringType)
        {
            return
                CecilHelpers.AddMethodImplementingInterfaceMethod(
                componentDataModule,
                authoringType,
                typeof(IConvertGameObjectToEntity).GetMethod(nameof(IConvertGameObjectToEntity.Convert)));
        }
    }
}
