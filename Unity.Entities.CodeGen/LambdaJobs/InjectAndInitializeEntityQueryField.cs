using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Unity.CompilationPipeline.Common.Diagnostics;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace Unity.Entities.CodeGen
{
    static class InjectAndInitializeEntityQueryField
    {
        public static FieldDefinition InjectAndInitialize(List<DiagnosticMessage> diagnosticMessages, MethodDefinition methodToAnalyze,
            LambdaJobDescriptionConstruction descriptionConstruction, Collection<ParameterDefinition> closureParameters)
        {
            /* We're going to generate this code:
             *
             * protected void override OnCreate()
             * {
             *     _entityQuery = GetEntityQuery_ForMyJob_From(this);
             * }
             *
             * static void GetEntityQuery_ForMyJob_From(ComponentSystem componentSystem)
             * {
             *     var result = componentSystem.GetEntityQuery(new[] { new EntityQueryDesc() {
             *         All = new[] { ComponentType.ReadWrite<Position>(), ComponentType.ReadOnly<Velocity>() },
             *         None = new[] { ComponentType.ReadWrite<IgnoreTag>() }
             *     }});
             *     result.SetChangedFilter(new[] { ComponentType.ReadOnly<Position>() } );
             * }
             */

            var module = methodToAnalyze.Module;

            var entityQueryField = new FieldDefinition($"<>{descriptionConstruction.LambdaJobName}_entityQuery",
                FieldAttributes.Private, module.ImportReference(typeof(EntityQuery)));
            var userSystemType = methodToAnalyze.DeclaringType;
            userSystemType.Fields.Add(entityQueryField);

            var getEntityQueryFromMethod = AddGetEntityQueryFromMethod(diagnosticMessages, descriptionConstruction, closureParameters.ToArray(),
                methodToAnalyze.DeclaringType);

            List<Instruction> instructionsToInsert = new List<Instruction>();
            instructionsToInsert.Add(
                new[]
                {
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Call, getEntityQueryFromMethod),
                    Instruction.Create(OpCodes.Stfld, entityQueryField)
                }
            );

            // Store our generated query in a user-specified field if one was given
            if (descriptionConstruction.StoreQueryInField != null)
            {
                instructionsToInsert.Add(
                    new[]
                    {
                        Instruction.Create(OpCodes.Ldarg_0),
                        Instruction.Create(OpCodes.Ldarg_0),
                        Instruction.Create(OpCodes.Ldfld, entityQueryField),
                        Instruction.Create(OpCodes.Stfld, descriptionConstruction.StoreQueryInField),
                    });
            }
            InsertIntoOnCreateForCompilerMethod(userSystemType, instructionsToInsert.ToArray());

            return entityQueryField;
        }

        public static void InjectAndInitialize(TypeDefinition userSystemType, FieldDefinition entityQueryField, TypeReference singletonType, bool asReadOnly)
        {
            userSystemType.Fields.Add(entityQueryField);

            var getEntityQueryMethod = userSystemType.Module.ImportReference(typeof(ComponentSystemBase)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single(m =>
                    m.Name == "GetEntityQuery" && m.GetParameters().Length == 1 &&
                    m.GetParameters().Single().ParameterType == typeof(ComponentType[])));

            var componentTypeReference = userSystemType.Module.ImportReference(typeof(ComponentType));
            List<Instruction> instructionsToInsert = new List<Instruction>();

            instructionsToInsert.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructionsToInsert.Add(Instruction.Create(OpCodes.Ldarg_0));
            foreach (var instruction in InstructionsToPutArrayOfComponentTypesOnStack(
                new(TypeReference typeReference, bool readOnly)[] {(singletonType, asReadOnly)}, componentTypeReference))
                instructionsToInsert.Add(instruction);
            instructionsToInsert.Add(Instruction.Create(OpCodes.Call, getEntityQueryMethod));
            instructionsToInsert.Add(Instruction.Create(OpCodes.Stfld, entityQueryField));

            InsertIntoOnCreateForCompilerMethod(userSystemType, instructionsToInsert.ToArray());
        }

        public static void InsertIntoOnCreateForCompilerMethod(TypeDefinition userSystemType, Instruction[] instructions)
        {
            var methodBody = EntitiesILHelpers.GetOrMakeOnCreateForCompilerMethodFor(userSystemType).Body.GetILProcessor().Body;
            methodBody.GetILProcessor().InsertBefore(methodBody.Instructions.Last(), instructions);
        }

        static bool DoTypeGroupsContainMatch(List<TypeReference> typeGroup1, List<TypeReference> typeGroup2, out TypeReference matchingType)
        {
            matchingType = typeGroup1.FirstOrDefault(x => typeGroup2.Any(y => y.TypeReferenceEquals(x)));
            return (matchingType != null);
        }

        static MethodDefinition AddGetEntityQueryFromMethod(List<DiagnosticMessage> diagnosticMessages, LambdaJobDescriptionConstruction descriptionConstruction,
            ParameterDefinition[] closureParameters, TypeDefinition typeToInjectIn)
        {
            var moduleDefinition = typeToInjectIn.Module;
            var typeDefinition = typeToInjectIn;
            var getEntityQueryFromMethod =
                new MethodDefinition($"<>GetEntityQuery_For{descriptionConstruction.LambdaJobName}_From",
                    MethodAttributes.Public | MethodAttributes.Static,
                    moduleDefinition.ImportReference(typeof(EntityQuery)))
            {
                DeclaringType = typeDefinition,
                HasThis = false,
                Parameters =
                {
                    new ParameterDefinition("componentSystem", ParameterAttributes.None, moduleDefinition.ImportReference(typeof(ComponentSystemBase)))
                }
            };

            typeDefinition.Methods.Add(getEntityQueryFromMethod);
            var body = getEntityQueryFromMethod.Body;
            body.InitLocals = true; // initlocals must be set for verifiable methods with one or more local variables

            var getEntityQueryMethod = moduleDefinition.ImportReference(typeof(ComponentSystemBase)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single(m =>
                    m.Name == "GetEntityQuery" && m.GetParameters().Length == 1 &&
                    m.GetParameters().Single().ParameterType == typeof(EntityQueryDesc[])));

            var entityQueryDescConstructor = moduleDefinition.ImportReference(typeof(EntityQueryDesc).GetConstructor(Array.Empty<Type>()));
            var componentTypeReference = moduleDefinition.ImportReference(typeof(ComponentType));

            var withNoneTypes = AllTypeArgumentsOfMethod(moduleDefinition, descriptionConstruction, nameof(LambdaJobQueryConstructionMethods.WithNone));
            var withAllTypes = AllTypeArgumentsOfMethod(moduleDefinition, descriptionConstruction, nameof(LambdaJobQueryConstructionMethods.WithAll));
            var withSharedComponentFilterTypes = AllTypeArgumentsOfMethod(moduleDefinition, descriptionConstruction, nameof(LambdaJobQueryConstructionMethods.WithSharedComponentFilter));
            foreach (var allType in withAllTypes.Where(t => withSharedComponentFilterTypes.Contains(t)))
            {
                UserError.DC0026(allType.Name, descriptionConstruction.ContainingMethod,
                    descriptionConstruction.InvokedConstructionMethods.First().InstructionInvokingMethod).Throw();
            }
            var withAllAndSharedComponentFilterTypes = withAllTypes.Concat(withSharedComponentFilterTypes);

            var withAnyTypes = AllTypeArgumentsOfMethod(moduleDefinition, descriptionConstruction, nameof(LambdaJobQueryConstructionMethods.WithAny));
            var withChangeFilterTypes = AllTypeArgumentsOfMethod(moduleDefinition, descriptionConstruction, nameof(LambdaJobQueryConstructionMethods.WithChangeFilter));

            var arrayOfSingleEQDVariable = new VariableDefinition(moduleDefinition.ImportReference(typeof(EntityQueryDesc[])));
            var localVarOfEQD = new VariableDefinition(moduleDefinition.ImportReference(typeof(EntityQueryDesc)));
            var localVarOfResult = new VariableDefinition(moduleDefinition.ImportReference(typeof(EntityQuery)));

            foreach (var closureParameter in closureParameters)
            {
                void ThrowGenericTypeError(TypeReference genericType) =>
                    UserError.DC0050(genericType, descriptionConstruction.ContainingMethod, descriptionConstruction.InvokedConstructionMethods.First().InstructionInvokingMethod).Throw();
                var parameterElementType = closureParameter.ParameterType.GetElementType();
                if (parameterElementType.IsGenericInstance || parameterElementType.IsGenericParameter ||
                    (parameterElementType.HasGenericParameters && !parameterElementType.IsDynamicBufferOfT()))
                    ThrowGenericTypeError(closureParameter.ParameterType);
                else if (parameterElementType.IsDynamicBufferOfT() && parameterElementType.HasGenericParameters && closureParameter.ParameterType is ByReferenceType byReferenceType &&
                         byReferenceType.ElementType is GenericInstanceType genericInstanceType && genericInstanceType.HasGenericArguments)
                {
                    var firstGenericArgument = genericInstanceType.GenericArguments.OfType<GenericInstanceType>().FirstOrDefault(argument => argument.HasGenericArguments);
                    if (firstGenericArgument != null)
                        ThrowGenericTypeError(firstGenericArgument);
                }
            }

            var parameterComponentTypeInfos = closureParameters.Select(ComponentTypeInfoForLambdaParameter).Where(t => t.typeReference != null);

            // Check that we aren't accessing any parameter component types by value
            if (parameterComponentTypeInfos.Any(cta => cta.accessType == ComponentAccessType.ByValue && cta.dataType == ComponentDataType.ComponentDataStruct))
            {
                diagnosticMessages.Add(UserError.DC0055(parameterComponentTypeInfos.First(cta => cta.accessType == ComponentAccessType.ByValue).typeReference,
                    descriptionConstruction.ContainingMethod, descriptionConstruction.InvokedConstructionMethods.First().InstructionInvokingMethod));
            }

            // Check WithNone
            if (DoTypeGroupsContainMatch(withNoneTypes, withAllTypes, out var matchingType1))
            {
                UserError.DC0056(nameof(LambdaJobQueryConstructionMethods.WithNone), nameof(LambdaJobQueryConstructionMethods.WithAll), matchingType1,
                    descriptionConstruction.ContainingMethod, descriptionConstruction.InvokedConstructionMethods.First().InstructionInvokingMethod).Throw();
            }
            if (DoTypeGroupsContainMatch(withNoneTypes, withAnyTypes, out var matchingType2))
            {
                UserError.DC0056(nameof(LambdaJobQueryConstructionMethods.WithNone), nameof(LambdaJobQueryConstructionMethods.WithAny), matchingType2,
                    descriptionConstruction.ContainingMethod, descriptionConstruction.InvokedConstructionMethods.First().InstructionInvokingMethod).Throw();
            }
            if (DoTypeGroupsContainMatch(withNoneTypes, parameterComponentTypeInfos.Select(t => t.typeReference).ToList(), out var matchingType3))
            {
                UserError.DC0056(nameof(LambdaJobQueryConstructionMethods.WithNone), "lambda parameter", matchingType3, descriptionConstruction.ContainingMethod,
                    descriptionConstruction.InvokedConstructionMethods.First().InstructionInvokingMethod).Throw();
            }

            // Check WithAny
            if (DoTypeGroupsContainMatch(withAnyTypes, withAllTypes, out var matchingType4))
            {
                UserError.DC0056(nameof(LambdaJobQueryConstructionMethods.WithAny), nameof(LambdaJobQueryConstructionMethods.WithAll), matchingType4,
                    descriptionConstruction.ContainingMethod, descriptionConstruction.InvokedConstructionMethods.First().InstructionInvokingMethod).Throw();
            }
            if (DoTypeGroupsContainMatch(withAnyTypes, parameterComponentTypeInfos.Select(t => t.typeReference).ToList(), out var matchingType6))
            {
                UserError.DC0056(nameof(LambdaJobQueryConstructionMethods.WithAny), "lambda parameter", matchingType6, descriptionConstruction.ContainingMethod,
                    descriptionConstruction.InvokedConstructionMethods.First().InstructionInvokingMethod).Throw();
            }

            body.Variables.Add(arrayOfSingleEQDVariable);
            body.Variables.Add(localVarOfEQD);
            body.Variables.Add(localVarOfResult);

            // Combine WithAll types with parameter types and change filter types (and then resolve duplicates)
            var combinedAllTypes = withAllAndSharedComponentFilterTypes.Select(typeReference => (typeReference, true))
                .Concat(parameterComponentTypeInfos.Select(typeInfo => (typeInfo.typeReference, typeInfo.isReadOnly)))
                .Concat(withChangeFilterTypes.Select((t => (t, true))));
            var resolvedAllTypes = ResolveTypeDuplicatesAndReadOnlyAccess(combinedAllTypes.ToArray());

            var instructions = new List<Instruction>()
            {
                //var arrayOfSingleEQDVariable = new EnityQueryDesc[1];
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Newarr, moduleDefinition.ImportReference(typeof(EntityQueryDesc))),
                Instruction.Create(OpCodes.Stloc, arrayOfSingleEQDVariable),

                //var localVarOfEQD = new EntityQuery();
                Instruction.Create(OpCodes.Newobj, entityQueryDescConstructor),
                Instruction.Create(OpCodes.Stloc, localVarOfEQD),

                // arrayOfSingleEQDVariable[0] = localVarOfEQD;
                Instruction.Create(OpCodes.Ldloc, arrayOfSingleEQDVariable),
                Instruction.Create(OpCodes.Ldc_I4_0),
                Instruction.Create(OpCodes.Ldloc, localVarOfEQD),
                Instruction.Create(OpCodes.Stelem_Any, moduleDefinition.ImportReference(typeof(EntityQueryDesc))),

                InstructionsToSetEntityQueryDescriptionField(nameof(EntityQueryDesc.All), resolvedAllTypes, componentTypeReference,
                    localVarOfEQD, entityQueryDescConstructor),
                InstructionsToSetEntityQueryDescriptionField(nameof(EntityQueryDesc.None), withNoneTypes.Select(t => (t, false)).ToArray(), componentTypeReference,
                    localVarOfEQD, entityQueryDescConstructor),
                InstructionsToSetEntityQueryDescriptionField(nameof(EntityQueryDesc.Any), withAnyTypes.Select(t => (t, false)).ToArray(), componentTypeReference,
                    localVarOfEQD, entityQueryDescConstructor),

                InstructionsToSetEntityQueryDescriptionOptions(localVarOfEQD, entityQueryDescConstructor, descriptionConstruction),

                Instruction.Create(OpCodes.Ldarg_0), //the this for this.GetEntityQuery()
                Instruction.Create(OpCodes.Ldloc, arrayOfSingleEQDVariable),
                Instruction.Create(OpCodes.Call, getEntityQueryMethod),

                Instruction.Create(OpCodes.Stloc, localVarOfResult),

                InstructionsToSetChangedVersionFilterFor(moduleDefinition, withChangeFilterTypes, localVarOfResult, componentTypeReference),

                Instruction.Create(OpCodes.Ldloc, localVarOfResult),
                Instruction.Create(OpCodes.Ret),
            };

            var ilProcessor = getEntityQueryFromMethod.Body.GetILProcessor();
            ilProcessor.Append(instructions);

            return getEntityQueryFromMethod;
        }

        // Remove duplicates and favor non-read access only when there is a match
        static (TypeReference typeReference, bool readOnly)[] ResolveTypeDuplicatesAndReadOnlyAccess((TypeReference typeReference, bool readOnly)[] typeInfos)
        {
            List<(TypeReference typeReference, bool readOnly)> typeAndReadOnlyStatus = new List<(TypeReference typeReference, bool readOnly)>();

            foreach (var typeInfo in typeInfos)
            {
                int index = typeAndReadOnlyStatus.FindIndex(t => t.typeReference.TypeReferenceEquals(typeInfo.typeReference));
                if(index != -1)
                    typeAndReadOnlyStatus[index] = (typeInfo.typeReference, typeAndReadOnlyStatus[index].readOnly && typeInfo.readOnly);
                else
                    typeAndReadOnlyStatus.Add((typeInfo.typeReference, typeInfo.readOnly));
            }

            return typeAndReadOnlyStatus.ToArray();
        }

        static IEnumerable<Instruction> InstructionsToCreateComponentTypeFor(TypeReference typeReference, bool isReadOnly, int arrayIndex,
            TypeReference componentTypeReference)
        {
            yield return Instruction.Create(OpCodes.Dup); //put the array on the stack again
            yield return Instruction.Create(OpCodes.Ldc_I4, arrayIndex);

            var componentTypeDefinition = componentTypeReference.Resolve();

            MethodReference ComponentTypeMethod(string name) =>
                typeReference.Module.ImportReference(
                    componentTypeDefinition.Methods.Single(m => m.Name == name && m.Parameters.Count == 0));

            var readOnlyMethod = ComponentTypeMethod(nameof(ComponentType.ReadOnly));
            var readWriteMethod = ComponentTypeMethod(nameof(ComponentType.ReadWrite));

            var method = isReadOnly ? readOnlyMethod : readWriteMethod;
            yield return Instruction.Create(OpCodes.Call, method.MakeGenericInstanceMethod(typeReference.GetElementType()));
            yield return Instruction.Create(OpCodes.Stelem_Any, componentTypeReference);
        }

        static IEnumerable<Instruction> InstructionsToPutArrayOfComponentTypesOnStack((TypeReference typeReference, bool readOnly)[] typeReferences,
            TypeReference componentTypeReference)
        {
            yield return Instruction.Create(OpCodes.Ldc_I4, typeReferences.Length);
            yield return Instruction.Create(OpCodes.Newarr, componentTypeReference);

            for (int i = 0; i != typeReferences.Length; i++)
            {
                foreach (var instruction in InstructionsToCreateComponentTypeFor(typeReferences[i].typeReference, typeReferences[i].readOnly, i,
                    componentTypeReference))
                {
                    yield return instruction;
                }
            }
        }

        static List<TypeReference> AllTypeArgumentsOfMethod(ModuleDefinition moduleDefinition, LambdaJobDescriptionConstruction descriptionConstruction, string methodName)
        {
            var invokedConstructionMethods = descriptionConstruction.InvokedConstructionMethods.Where(m => m.MethodName == methodName);
            var result = new List<TypeReference>();

            foreach (var m in invokedConstructionMethods)
            {
                foreach (var argumentType in m.TypeArguments)
                {
                    if (argumentType.IsGenericParameter || argumentType.IsGenericInstance)
                        UserError.DC0051(argumentType, m.MethodName, descriptionConstruction.ContainingMethod, m.InstructionInvokingMethod).Throw();
                    var argumentTypeDefinition = argumentType.Resolve();
                    if (!LambdaParamaterValueProviderInformation.IsTypeValidForEntityQuery(argumentTypeDefinition))
                        UserError.DC0052(argumentType, m.MethodName, descriptionConstruction.ContainingMethod, m.InstructionInvokingMethod).Throw();

                    result.Add(moduleDefinition.ImportReference(argumentType));
                }
            }

            return result;
        }

        static IEnumerable<Instruction> InstructionsToSetChangedVersionFilterFor(ModuleDefinition moduleDefinition, List<TypeReference> typeReferences,
            VariableDefinition localVarOfResult, TypeReference componentTypeReference)
        {
            if (typeReferences.Count == 0)
                yield break;

            yield return Instruction.Create(OpCodes.Ldloca, localVarOfResult); //<- target of the SetChangedFilter call

            //create the array for the first argument:   new[] { ComponentType.ReadOnly<Position>(), ComponentType>.ReadOnly<Velocity>() }
            foreach (var instruction in InstructionsToPutArrayOfComponentTypesOnStack(typeReferences.Select(t => (t, false)).ToArray(), componentTypeReference))
                yield return instruction;

            EntityQuery eq;
            var setChangedVersionFilter = moduleDefinition.ImportReference(
                typeof(EntityQuery).GetMethod(nameof(eq.SetChangedVersionFilter), new[] {typeof(ComponentType[])}));

            //and do the actual invocation
            yield return Instruction.Create(OpCodes.Call, setChangedVersionFilter);
        }

        static IEnumerable<Instruction> InstructionsToSetEntityQueryDescriptionField(string fieldName,
            (TypeReference typeReference, bool readOnly)[] typeReferences, TypeReference componentTypeReference, VariableDefinition localVarOfEQD,
            MethodReference entityQueryDescConstructor)
        {
            if (typeReferences.Length == 0)
                yield break;

            yield return Instruction.Create(OpCodes.Ldloc, localVarOfEQD);
            foreach (var instruction in InstructionsToPutArrayOfComponentTypesOnStack(typeReferences, componentTypeReference))
                yield return instruction;
            var fieldReference = new FieldReference(fieldName, componentTypeReference.Module.ImportReference(typeof(ComponentType[])),
                entityQueryDescConstructor.DeclaringType);
            yield return Instruction.Create(OpCodes.Stfld, fieldReference);
        }

        static IEnumerable<Instruction> InstructionsToSetEntityQueryDescriptionOptions(VariableDefinition localVarOfEQD,
            MethodReference entityQueryDescConstructor, LambdaJobDescriptionConstruction descriptionConstruction)
        {
            var withOptionsInvocation = descriptionConstruction.InvokedConstructionMethods.FirstOrDefault(m =>
                m.MethodName == nameof(LambdaJobQueryConstructionMethods.WithEntityQueryOptions));
            if (withOptionsInvocation == null)
                yield break;

            yield return Instruction.Create(OpCodes.Ldloc, localVarOfEQD);
            yield return Instruction.Create(OpCodes.Ldc_I4, (int)withOptionsInvocation.Arguments.Single());
            var fieldReference = new FieldReference(nameof(EntityQueryDesc.Options), entityQueryDescConstructor.Module.ImportReference(typeof(EntityQueryOptions)),
                entityQueryDescConstructor.DeclaringType);
            yield return Instruction.Create(OpCodes.Stfld, fieldReference);
        }

        enum ComponentAccessType
        {
            ByRef,
            ByIn,
            ByValue
        }

        enum ComponentDataType
        {
            ComponentDataStruct,
            ComponentDataClass,
            SharedComponent,
            UnityEngineObject,
            DynamicBuffer
        }

        static (TypeReference typeReference, ComponentAccessType accessType, ComponentDataType dataType, bool isReadOnly)
            ComponentTypeInfoForLambdaParameter(ParameterDefinition p)
        {
            ComponentAccessType accessType;
            if (!p.ParameterType.IsByReference)
                accessType = ComponentAccessType.ByValue;
            else if (p.HasCompilerServicesIsReadOnlyAttribute())
                accessType = ComponentAccessType.ByIn;
            else
                accessType = ComponentAccessType.ByRef;

            var type = p.ParameterType.Resolve();

            if (type.IsIComponentDataStruct())
                return (p.ParameterType.GetElementType(), accessType, ComponentDataType.ComponentDataStruct, accessType != ComponentAccessType.ByRef);
            if (type.IsISharedComponentData())
                return (p.ParameterType.GetElementType(), accessType, ComponentDataType.SharedComponent, accessType != ComponentAccessType.ByRef);

            if (type.IsIComponentDataClass())
                return (p.ParameterType.GetElementType(), accessType, ComponentDataType.ComponentDataClass, accessType == ComponentAccessType.ByIn);
            if (type.IsUnityEngineObject())
                return (p.ParameterType.GetElementType(), accessType, ComponentDataType.UnityEngineObject, accessType == ComponentAccessType.ByIn);

            if (type.IsDynamicBufferOfT())
            {
                var typeReference = ((GenericInstanceType)p.ParameterType.StripRef()).GenericArguments.Single();
                return (typeReference, accessType, ComponentDataType.DynamicBuffer, accessType == ComponentAccessType.ByIn);
            }

            return (null, default, default, default);
        }
    }
}
