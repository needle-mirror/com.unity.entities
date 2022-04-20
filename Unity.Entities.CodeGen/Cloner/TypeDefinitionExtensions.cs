using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unity.Entities.CodeGen.Cloner
{
    public static class TypeDefinitionExtensions
    {
        public static (List<MethodDefinition> Original, List<(MethodDefinition Definition, string Source)> Rewritten)
            GetOriginalAndRewrittenMethods(this TypeDefinition typeDefinition)
        {
            var original = new List<MethodDefinition>();
            var rewritten = new List<(MethodDefinition, string)>();

            foreach (MethodDefinition method in typeDefinition.Methods)
            {
                var patchedAttribute =
                    method.CustomAttributes.FirstOrDefault(attr => attr.AttributeType.Name == nameof(DOTSCompilerPatchedMethodAttribute));

                if (patchedAttribute != null)
                {
                    rewritten.Add((method, patchedAttribute.ConstructorArguments.First().Value.ToString()));
                }
                else
                {
                    original.Add(method);
                }
            }

            return (original, rewritten);
        }

        public static (List<PropertyDefinition> Original, List<(PropertyDefinition Definition, string Source)> Rewritten)
            GetOriginalAndRewrittenProperties(this TypeDefinition typeDefinition)
        {
            var original = new List<PropertyDefinition>();
            var rewritten = new List<(PropertyDefinition, string)>();

            foreach (var property in typeDefinition.Properties)
            {
                var patchedAttribute =
                    property.CustomAttributes.FirstOrDefault(attr => attr.AttributeType.Name == nameof(DOTSCompilerPatchedPropertyAttribute));

                if (patchedAttribute != null)
                {
                    rewritten.Add((property, patchedAttribute.ConstructorArguments.First().Value.ToString()));
                }
                else
                {
                    original.Add(property);
                }
            }

            return (original, rewritten);
        }

        public static void UpdateOriginalProperty(
            this TypeDefinition typeDefinition,
            Dictionary<string, PropertyDefinition> originalPropertyIdsToDefinitions,
            (PropertyDefinition Definition, string ConstructorArgument) rewrittenProperty)
        {
            var originalProperty = originalPropertyIdsToDefinitions[rewrittenProperty.ConstructorArgument];
            originalProperty.GetMethod = rewrittenProperty.Definition.GetMethod;
            originalProperty.SetMethod = rewrittenProperty.Definition.SetMethod;

            typeDefinition.Properties.Remove(rewrittenProperty.Definition);
        }

        public static void UpdateOriginalMethod(
            this TypeDefinition typeDef,
            Dictionary<string, MethodDefinition> originalMethodIdsToDefinitions,
            (MethodDefinition Definition, string ConstructorArgument) rewrittenMethod)
        {
            var originalMethod = originalMethodIdsToDefinitions[rewrittenMethod.ConstructorArgument];

            // If we are using a display class in a method but not in the source, we need to remove the display class usage
            // (otherwise Mono will get confused when trying to debug multiple sequence points that point to the same source file location)
            var displayClassesUsedInOriginal = GetAllDisplayClassUsagesInMethod(originalMethod);
            var displayClassesUsedInRewritten = GetAllDisplayClassUsagesInMethod(rewrittenMethod.Definition);

            foreach (var originalDisplayClass in displayClassesUsedInOriginal)
            {
                if (!displayClassesUsedInRewritten.Contains(originalDisplayClass) && originalMethod.DeclaringType.NestedTypes.Contains(originalDisplayClass))
                    originalMethod.DeclaringType.NestedTypes.Remove(originalDisplayClass);
            }

            originalMethod.Body = rewrittenMethod.Definition.Body;
            typeDef.Methods.Remove(rewrittenMethod.Definition);

            var sequencePoints = originalMethod.DebugInformation.SequencePoints;
            sequencePoints.Clear();

            foreach (var sp in rewrittenMethod.Definition.DebugInformation.SequencePoints)
                sequencePoints.Add(sp);

            originalMethod.DebugInformation.Scope = rewrittenMethod.Definition.DebugInformation.Scope;

            if (rewrittenMethod.Definition.HasGenericParameters && originalMethod.HasGenericParameters)
            {
                originalMethod.GenericParameters.Clear();
                foreach (var genericParam in rewrittenMethod.Definition.GenericParameters)
                {
                    originalMethod.GenericParameters.Add(genericParam);
                }
            }
        }

        // Check both field and variable usages
        static HashSet<TypeDefinition> GetAllDisplayClassUsagesInMethod(MethodDefinition method)
        {
            var displayClassesUsedInMethod = new HashSet<TypeDefinition>();

            foreach (var local in method.Body.Variables.Where(local => local.VariableType.IsDisplayClass()))
                displayClassesUsedInMethod.Add(local.VariableType.Resolve());

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.IsLoadField() || instruction.IsLoadStaticField())
                {
                    var fieldOperand = (FieldReference)instruction.Operand;
                    if (fieldOperand.FieldType.IsDisplayClass())
                        displayClassesUsedInMethod.Add(fieldOperand.FieldType.Resolve());
                }
                else if (instruction.OpCode == OpCodes.Newobj)
                {
                    var methodOperand = (MethodReference)instruction.Operand;
                    if (methodOperand.DeclaringType.IsDisplayClass())
                        displayClassesUsedInMethod.Add(method.DeclaringType.Resolve());
                }
            }

            return displayClassesUsedInMethod;
        }

    }
}
