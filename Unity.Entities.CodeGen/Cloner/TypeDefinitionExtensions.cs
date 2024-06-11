using System.Collections.Generic;
using System.Diagnostics;
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

        static string GetPropertyName(PropertyDefinition propertyDefinition) => $"{propertyDefinition.DeclaringType.ToString().Replace('/', '.')}.{propertyDefinition.Name}";
        public static (Dictionary<string, PropertyDefinition> OriginalLookup, List<(PropertyDefinition Definition, string Source)> Rewritten)
            GetOriginalAndRewrittenProperties(this TypeDefinition typeDefinition)
        {
            var originalLookup = new Dictionary<string, PropertyDefinition>();
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
                    originalLookup[GetPropertyName(property)] = property;
                }
            }

            return (originalLookup, rewritten);
        }

        public static void UpdateOriginalMethod(
            this TypeDefinition typeDef,
            Dictionary<string, MethodDefinition> originalMethodIdsToDefinitions,
            (MethodDefinition Definition, string ConstructorArgument) rewrittenMethodIn)
        {
            var originalMethod = originalMethodIdsToDefinitions[rewrittenMethodIn.ConstructorArgument];
            var rewrittenMethod = rewrittenMethodIn.Definition;

            // If we are using a display class in a method but not in the source, we need to remove the display class usage
            // (otherwise Mono will get confused when trying to debug multiple sequence points that point to the same source file location)
            // Similarly we need to remove all local functions declared in the original method.
            var (displayClassesUsedInOriginal, localFunctionsUsedInOriginal) =
                GetDisplayClassAndLocalFunctionUsages(originalMethod, true);
            var (displayClassesUsedInRewritten, _) = GetDisplayClassAndLocalFunctionUsages(rewrittenMethod, false);
            foreach (var originalDisplayClass in displayClassesUsedInOriginal)
            {
                if (!displayClassesUsedInRewritten.Contains(originalDisplayClass) &&
                    originalMethod.DeclaringType.NestedTypes.Contains(originalDisplayClass))
                {
                    originalMethod.DeclaringType.NestedTypes.Remove(originalDisplayClass);
                    for (var i = typeDef.Methods.Count - 1; i >= 0; i--)
                    {
                        if (typeDef.Methods[i].Parameters.FirstOrDefault()?.ParameterType.GetElementType() ==
                            originalDisplayClass)
                            typeDef.Methods.RemoveAt(i);
                    }
                }
            }
            foreach (var localFunction in localFunctionsUsedInOriginal)
                typeDef.Methods.Remove(localFunction);

            originalMethod.Body = rewrittenMethod.Body;
            typeDef.Methods.Remove(rewrittenMethod);

            var sequencePoints = originalMethod.DebugInformation.SequencePoints;
            sequencePoints.Clear();

            foreach (var sp in rewrittenMethod.DebugInformation.SequencePoints)
                sequencePoints.Add(sp);

            originalMethod.DebugInformation.Scope = rewrittenMethod.DebugInformation.Scope;

            if (rewrittenMethod.HasGenericParameters && originalMethod.HasGenericParameters)
            {
                originalMethod.GenericParameters.Clear();
                foreach (var genericParam in rewrittenMethod.GenericParameters)
                {
                    originalMethod.GenericParameters.Add(genericParam);
                }
            }
        }

        // Check both field and variable usages for display classes
        // Also scan method IL for display class or local function usages
        // (need to use heuristic since there is nothing that marks a method as a local function or display class in IL)
        static (HashSet<TypeDefinition>, HashSet<MethodDefinition>)
            GetDisplayClassAndLocalFunctionUsages(MethodDefinition method, bool alsoGetLocalFunctions)
        {
            var displayClassesUsedInMethod = new HashSet<TypeDefinition>();
            HashSet<MethodDefinition> localFunctionUsagesInMethod = null;
            if (alsoGetLocalFunctions)
                localFunctionUsagesInMethod = new HashSet<MethodDefinition>();

            foreach (var local in method.Body.Variables.Where(local => local.VariableType.IsDisplayClassCandidate()))
                displayClassesUsedInMethod.Add(local.VariableType.Resolve());

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.IsLoadField() || instruction.IsLoadStaticField())
                {
                    var fieldOperand = (FieldReference)instruction.Operand;
                    if (fieldOperand.FieldType.IsDisplayClassCandidate())
                        displayClassesUsedInMethod.Add(fieldOperand.FieldType.Resolve());
                }
                else if (instruction.OpCode == OpCodes.Newobj)
                {
                    var methodOperand = (MethodReference)instruction.Operand;
                    if (methodOperand.DeclaringType.IsDisplayClassCandidate())
                        displayClassesUsedInMethod.Add(method.DeclaringType.Resolve());
                }
                else if (alsoGetLocalFunctions && instruction.OpCode == OpCodes.Call ||
                         instruction.OpCode == OpCodes.Callvirt)
                {
                    var methodOperand = (MethodReference)instruction.Operand;
                    if (methodOperand.DeclaringType.FullName == method.DeclaringType.FullName)
                    {
                        var calledMethod = methodOperand.Resolve();
                        if (calledMethod != null && calledMethod.IsLocalFunctionCandidate())
                        {
                            localFunctionUsagesInMethod.Add(calledMethod);
                        }
                    }
                }
            }

            return (displayClassesUsedInMethod, localFunctionUsagesInMethod);
        }
    }
}
