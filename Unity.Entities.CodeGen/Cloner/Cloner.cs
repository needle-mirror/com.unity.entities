using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Unity.Entities;
using Unity.Entities.CodeGen;

class Cloner : EntitiesILPostProcessor
{
    protected override bool PostProcessImpl(TypeDefinition[] componentSystemTypes)
    {
        var madeChange = false;
        foreach (var typeDef in componentSystemTypes)
        {
            var methodsToPatch =
                 typeDef.Methods
                        .Where(methodDef =>
                            methodDef.CustomAttributes.Any(attr =>
                                attr.AttributeType.Name == nameof(DOTSCompilerPatchedMethodAttribute)))
                        .ToArray();

            if (!methodsToPatch.Any())
                continue;

            var methodNameAndParamsToMethodDefs =
                typeDef.Methods.Where(methodDef => !methodsToPatch.Contains(methodDef)).ToDictionary(GetMethodNameAndParamsAsString, method => method);

            foreach (var sourceMethod in methodsToPatch)
            {
                var attributeValue =
                    sourceMethod.CustomAttributes
                        .First(attribute =>
                            attribute.AttributeType.Name == nameof(DOTSCompilerPatchedMethodAttribute))
                        .ConstructorArguments
                        .First()
                        .Value
                        .ToString();

                if (!methodNameAndParamsToMethodDefs.ContainsKey(attributeValue))
                    throw new InvalidOperationException(
                    $"Method Cloner ILPP: Cannot find method {attributeValue} in {typeDef.FullName}.  Method candidates are {string.Join(", ", methodNameAndParamsToMethodDefs.Keys)}");

                var destinationMethod = methodNameAndParamsToMethodDefs[attributeValue];

                // If we are using a display class in a method but not in the source, we need to remove the display class usage
                // (otherwise Mono will get confused when trying to debug multiple sequence points that point to the same source file location)
                var displayClassesUsedInDestination = GetAllDisplayClassUsagesInMethod(destinationMethod);
                var displayClassesUsedInSource = GetAllDisplayClassUsagesInMethod(sourceMethod);
                foreach (var displayClassUsedInDestination in displayClassesUsedInDestination)
                {
                    if (!displayClassesUsedInSource.Contains(displayClassUsedInDestination) &&
                        destinationMethod.DeclaringType.NestedTypes.Contains(displayClassUsedInDestination))
                        destinationMethod.DeclaringType.NestedTypes.Remove(displayClassUsedInDestination);
                }

                destinationMethod.Body = sourceMethod.Body;
                typeDef.Methods.Remove(sourceMethod);

                var sequencePoints = destinationMethod.DebugInformation.SequencePoints;
                sequencePoints.Clear();

                foreach (var sp in sourceMethod.DebugInformation.SequencePoints)
                    sequencePoints.Add(sp);

                destinationMethod.DebugInformation.Scope = sourceMethod.DebugInformation.Scope;

                if (sourceMethod.HasGenericParameters && destinationMethod.HasGenericParameters)
                {
                    destinationMethod.GenericParameters.Clear();
                    foreach (var genericParam in sourceMethod.GenericParameters)
                    {
                        destinationMethod.GenericParameters.Add(genericParam);
                    }
                }
                madeChange = true;
            }
        }
        return madeChange;
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

    protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
    {
        return false;
    }

    // Remove /& characters and `# for type arity
    static string CleanupTypeName(string typeName)
    {
        typeName = typeName.Replace('/', '.').Replace("&", "").Replace(" ", string.Empty);
        var indexOfArityStart = typeName.IndexOf('`');
        if (indexOfArityStart != -1)
        {
            var indexOfArityEnd = typeName.IndexOf('<');
            if (indexOfArityEnd != -1)
                return typeName.Remove(indexOfArityStart, indexOfArityEnd - indexOfArityStart);
        }

        return typeName;
    }

    static string GetMethodNameAndParamsAsString(MethodReference method)
    {
        var strBuilder = new StringBuilder();
        strBuilder.Append(method.Name);

        for (var typeIndex = 0; typeIndex < method.GenericParameters.Count; typeIndex++)
            strBuilder.Append($"_T{typeIndex}");

        foreach (var parameter in method.Parameters)
        {
            if (parameter.ParameterType.IsByReference)
            {
                if (parameter.IsIn)
                    strBuilder.Append($"_in");
                else if (parameter.IsOut)
                    strBuilder.Append($"_out");
                else
                    strBuilder.Append($"_ref");
            }


            strBuilder.Append($"_{CleanupTypeName(parameter.ParameterType.ToString())}");
        }

        return strBuilder.ToString();
    }
}
