#if UNITY_DOTSRUNTIME
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeGenInfoList = System.Collections.Generic.List<Unity.Entities.CodeGen.StaticTypeRegistryPostProcessor.TypeGenInfo>;
using SystemList = System.Collections.Generic.List<Mono.Cecil.TypeDefinition>;

namespace Unity.Entities.CodeGen
{
    internal partial class StaticTypeRegistryPostProcessor : EntitiesILPostProcessor
    {
        public List<bool> GetSystemIsGroupList(List<TypeReference> systems)
        {
            var inGroup = systems.Select(s => s.Resolve().IsChildTypeOf(AssemblyDefinition.MainModule.ImportReference(typeof(ComponentSystemGroup)).Resolve())).ToList();
            return inGroup;
        }

        int GetFilterFlag(TypeDefinition typeDef)
        {
            // If no flags are given we assume the default world
            int flags = (int) WorldSystemFilterFlags.Default;
            if (typeDef.HasCustomAttributes)
            {
                var filterFlagsAttribute = typeDef.CustomAttributes.FirstOrDefault(ca => ca.AttributeType.Name == nameof(WorldSystemFilterAttribute) && ca.ConstructorArguments.Count == 1);
                if(filterFlagsAttribute != null)
                {
                    // override the default value if flags are provided
                    flags = (int)((WorldSystemFilterFlags) filterFlagsAttribute.ConstructorArguments[0].Value);
                }
            }

            return flags;
        }

        public List<int> GetSystemFilterFlagList(List<TypeReference> systems)
        {
            var flags = systems.Select(s => GetFilterFlag(s.Resolve())).ToList();
            return flags;
        }

        public static List<string> GetSystemNames(List<TypeReference> systems)
        {
            return systems.Select(s => s.FullName).ToList();
        }

        public MethodDefinition InjectGetSystemAttributes(List<TypeReference> systems)
        {
            var createSystemsFunction = new MethodDefinition(
                "GetSystemAttributes",
                MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig,
                AssemblyDefinition.MainModule.ImportReference(typeof(Attribute).MakeArrayType()));

            createSystemsFunction.Parameters.Add(
                new ParameterDefinition("systemType",
                    ParameterAttributes.None,
                    AssemblyDefinition.MainModule.ImportReference(typeof(Type))));

            createSystemsFunction.Body.InitLocals = true;
            createSystemsFunction.Body.SimplifyMacros();

            var bc = createSystemsFunction.Body.Instructions;

            var allGroups = new string[]
            {
                typeof(UpdateBeforeAttribute).FullName,
                typeof(UpdateAfterAttribute).FullName,
                typeof(UpdateInGroupAttribute).FullName,
                typeof(AlwaysUpdateSystemAttribute).FullName,
                typeof(AlwaysSynchronizeSystemAttribute).FullName,
                typeof(DisableAutoCreationAttribute).FullName
            };

            foreach (var sysRef in systems)
            {
                var sysDef = sysRef.Resolve();
                bc.Add(Instruction.Create(OpCodes.Ldarg_0));
                bc.Add(Instruction.Create(OpCodes.Ldtoken, AssemblyDefinition.MainModule.ImportReference(sysRef)));
                bc.Add(Instruction.Create(OpCodes.Call, m_GetTypeFromHandleFnRef));
                // Stack: argtype Type
                bc.Add(Instruction.Create(OpCodes.Ceq));
                // Stack: bool
                int branchToNext = bc.Count;
                bc.Add(Instruction.Create(OpCodes.Nop));    // will be: Brfalse_S nextTestCase

                // Stack: <null>
                List<CustomAttribute> attrList = new List<CustomAttribute>();
                foreach (var g in allGroups)
                {
                    var list = sysDef.CustomAttributes.Where(t => t.AttributeType.FullName == g);
                    attrList.AddRange(list);
                }

                var disableAutoCreationAttr = sysRef.Module.Assembly.CustomAttributes.FirstOrDefault(ca => ca.AttributeType.Name == nameof(DisableAutoCreationAttribute));
                if (disableAutoCreationAttr != null)
                    attrList.Add(disableAutoCreationAttr);

                int arrayLen = attrList.Count;
                bc.Add(Instruction.Create(OpCodes.Ldc_I4, arrayLen));
                // Stack: arrayLen
                bc.Add(Instruction.Create(OpCodes.Newarr, AssemblyDefinition.MainModule.ImportReference(typeof(Attribute))));
                // Stack: array[]

                for (int i = 0; i < attrList.Count; ++i)
                {
                    var attr = attrList[i];

                    // The stelem.ref will gobble up the array ref we need to return, so dupe it.
                    bc.Add(Instruction.Create(OpCodes.Dup));
                    bc.Add(Instruction.Create(OpCodes.Ldc_I4, i));       // the index we will write
                                                                         // Stack: array[] array[] array-index

                    // CustomAttributes are usually injected into the ctor of the type being decorated, however for our purposes we want to construct
                    // an Attribute[] with all the custom initialization the decorated type would have. As such we do two passes: construct the attribute using
                    // the constructor call writer (including constant arguments), and then after construction, initialize fields using field CustomAttributeNamedArguments.
                    // Since both calls require generating IL based on the actual types used, we take a narrow approach of only supporting the subset of types we know we
                    // need to handle (Type, and bool) currently.
                    foreach (var ca in attr.ConstructorArguments)
                    {
                        if(!InjectLoadFromCustomArgument(bc, ca))
                            throw new ArgumentException($"Currently only 'Type', 'int' and 'bool' attribute constructor arguments are supported for ComponentSystems. '{sysDef.FullName}' has attribute '{attr.AttributeType.FullName}' using unsupported constructor '{attr.Constructor.FullName}'");
                    }

                    // Construct the attribute; push it on the list.
                    var ctor = AssemblyDefinition.MainModule.ImportReference(attr.Constructor);
                    bc.Add(Instruction.Create(OpCodes.Newobj, ctor));

                    foreach (var field in attr.Fields)
                    {
                        // custom attributes can set fields in the constructor so scan the fields for arguments
                        if (field.Argument.Value != null)
                        {
                            // Copy the element on the stack (the attribute) so we can store into it's fields
                            bc.Add(Instruction.Create(OpCodes.Dup));
                            if (!InjectLoadFromCustomArgument(bc, field.Argument))
                                throw new ArgumentException($"Currently initializing attribute fields supports 'Type', 'int' and 'bool' for ComponentSystems. '{sysDef.FullName}' has attribute '{attr.AttributeType.FullName}' initializing field '{field.Name}' of type '{field.Argument.Type.FullName}'");

                            var attributeTypeDef = attr.AttributeType.Resolve();
                            var attributeField = attributeTypeDef.Fields.Single(f => f.Name == field.Name);
                            bc.Add(Instruction.Create(OpCodes.Stfld, AssemblyDefinition.MainModule.ImportReference(attributeField)));
                        }
                    }

                    // Stack: array[] array[] array-index value(object)
                    bc.Add(Instruction.Create(OpCodes.Stelem_Ref));
                    // Stack: array[]
                }

                // Stack: array[]
                bc.Add(Instruction.Create(OpCodes.Ret));

                // Put a no-op to start the next test.
                var nextTest = Instruction.Create(OpCodes.Nop);
                bc.Add(nextTest);

                // And go back and patch the IL to jump to the next test no-op just created.
                bc[branchToNext] = Instruction.Create(OpCodes.Brfalse_S, nextTest);
            }
            bc.Add(Instruction.Create(OpCodes.Ldstr, "FATAL: GetSystemAttributes asked to create an unknown Type."));
            var arguementExceptionCtor = AssemblyDefinition.MainModule.ImportReference(typeof(ArgumentException)).Resolve().GetConstructors()
                .Single(c => c.Parameters.Count == 1 && c.Parameters[0].ParameterType.MetadataType == MetadataType.String);
            bc.Add(Instruction.Create(OpCodes.Newobj, AssemblyDefinition.MainModule.ImportReference(arguementExceptionCtor)));
            bc.Add(Instruction.Create(OpCodes.Throw));

            createSystemsFunction.Body.OptimizeMacros();

            return createSystemsFunction;
        }

        bool InjectLoadFromCustomArgument(Mono.Collections.Generic.Collection<Instruction> instructions, CustomAttributeArgument customArgument)
        {
            var arg = customArgument.Value;

            if (arg is TypeReference)
            {
                instructions.Add(Instruction.Create(OpCodes.Ldtoken, AssemblyDefinition.MainModule.ImportReference((TypeReference)arg)));
                instructions.Add(Instruction.Create(OpCodes.Call, m_GetTypeFromHandleFnRef));
            }
            else if (arg is bool)
            {
                instructions.Add(Instruction.Create(OpCodes.Ldc_I4, (bool)arg ? 1 : 0));
            }
            else
                return false;

            return true;
        }

        public MethodDefinition InjectCreateSystem(List<TypeReference> systems)
        {
            var createSystemsFunction = new MethodDefinition(
                "CreateSystem",
                MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig,
                AssemblyDefinition.MainModule.ImportReference(typeof(object)));

            createSystemsFunction.Parameters.Add(
                new ParameterDefinition("systemType",
                    ParameterAttributes.None,
                    AssemblyDefinition.MainModule.ImportReference(typeof(Type))));

            createSystemsFunction.Body.InitLocals = true;
            var bc = createSystemsFunction.Body.Instructions;

            foreach (var sysRef in systems)
            {
                var sysDef = sysRef.Resolve();
                var constructor = AssemblyDefinition.MainModule.ImportReference(sysDef.GetConstructors()
                    .FirstOrDefault(param => param.HasParameters == false));

                bc.Add(Instruction.Create(OpCodes.Ldarg_0));
                bc.Add(Instruction.Create(OpCodes.Ldtoken, AssemblyDefinition.MainModule.ImportReference(sysRef)));
                bc.Add(Instruction.Create(OpCodes.Call, m_GetTypeFromHandleFnRef));
                bc.Add(Instruction.Create(OpCodes.Ceq));
                int branchToNext = bc.Count;
                bc.Add(Instruction.Create(OpCodes.Nop));    // will be: Brfalse_S nextTestCase
                bc.Add(Instruction.Create(OpCodes.Newobj, constructor));
                bc.Add(Instruction.Create(OpCodes.Ret));

                var nextTest = Instruction.Create(OpCodes.Nop);
                bc.Add(nextTest);

                bc[branchToNext] = Instruction.Create(OpCodes.Brfalse_S, nextTest);
            }

            bc.Add(Instruction.Create(OpCodes.Ldstr, "FATAL: CreateSystem asked to create an unknown type. Only subclasses of ComponentSystemBase can be constructed."));
            var argumentExceptionCtor = AssemblyDefinition.MainModule.ImportReference(typeof(ArgumentException)).Resolve().GetConstructors()
                .Single(c => c.Parameters.Count == 1 && c.Parameters[0].ParameterType.MetadataType == MetadataType.String);
            bc.Add(Instruction.Create(OpCodes.Newobj, AssemblyDefinition.MainModule.ImportReference(argumentExceptionCtor)));
            bc.Add(Instruction.Create(OpCodes.Throw));

            return createSystemsFunction;
        }
    }
}
#endif
