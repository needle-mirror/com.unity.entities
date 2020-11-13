#if ROSLYN_SOURCEGEN_ENABLED

using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.Entities;
using Unity.Entities.CodeGen;

class Cloner : EntitiesILPostProcessor
{
    protected override bool PostProcessImpl(TypeDefinition[] componentSystemTypes)
    {
        var madeChange = false;
        foreach (var c in componentSystemTypes)
        {
            var typeMethods = c.Methods.ToArray();
            foreach (var patchedMethod in typeMethods.Where(m => m.CustomAttributes.Any(
                attribute => attribute.AttributeType.Name == nameof(DOTSCompilerPatchedMethodAttribute))))
            {
                var patchedMethodName = patchedMethod.CustomAttributes.First(attribute => attribute.AttributeType.Name == nameof(DOTSCompilerPatchedMethodAttribute))
                    .ConstructorArguments.First().Value.ToString();
                var original = c.Methods.Single(m => m.Name == patchedMethodName);

                foreach (var displayClass in original.Body.Variables.Select(v => v.VariableType).OfType<TypeDefinition>().Where(IsDisplayClass))
                {
                    original.DeclaringType.NestedTypes.Remove(displayClass);
                }

                original.Body = patchedMethod.Body;
                c.Methods.Remove(patchedMethod);

                var sequencePoints = original.DebugInformation.SequencePoints;
                sequencePoints.Clear();
                foreach (var sp in patchedMethod.DebugInformation.SequencePoints)
                    sequencePoints.Add(sp);
                original.DebugInformation.Scope = patchedMethod.DebugInformation.Scope;

                madeChange = true;
            }
        }
        return madeChange;
    }

    static bool IsDisplayClass(TypeDefinition arg) => arg.Name.Contains("<>");

    protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
    {
        return false;
    }
}

#endif

