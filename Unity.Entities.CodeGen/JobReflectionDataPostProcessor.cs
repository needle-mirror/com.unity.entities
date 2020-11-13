using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Unity.Entities.CodeGen
{
#if UNITY_2020_2_OR_NEWER || UNITY_DOTSRUNTIME
    class JobReflectionDataPostProcessor : EntitiesILPostProcessor
    {
        private static readonly string ProducerAttributeName = typeof(JobProducerTypeAttribute).FullName;
        private static readonly string RegisterGenericJobTypeAttributeName = typeof(RegisterGenericJobTypeAttribute).FullName;

        // This must happen very late, after all jobs have been set up
        public override int SortWeight => 0xff00;

        public override bool WillProcess()
        {
            return ReferencesJobs || ReferencesEntities;
        }

        protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
        {
            return false;
        }

        public static MethodReference AttributeConstructorReferenceFor(Type attributeType, ModuleDefinition module)
        {
            return module.ImportReference(attributeType.GetConstructors().Single(c => !c.GetParameters().Any()));
        }


        private TypeReference LaunderTypeRef(TypeReference r_)
        {
            ModuleDefinition mod = AssemblyDefinition.MainModule;

            TypeDefinition def = r_.Resolve();

            TypeReference result;

            if (r_ is GenericInstanceType git)
            {
                var gt = new GenericInstanceType(LaunderTypeRef(def));

                foreach (var gp in git.GenericParameters)
                {
                    gt.GenericParameters.Add(gp);
                }

                foreach (var ga in git.GenericArguments)
                {
                    gt.GenericArguments.Add(LaunderTypeRef(ga));
                }

                result = gt;

            }
            else
            {
                result = new TypeReference(def.Namespace, def.Name, def.Module, def.Scope, def.IsValueType);

                if (def.DeclaringType != null)
                {
                    result.DeclaringType = LaunderTypeRef(def.DeclaringType);
                }
            }

            return mod.ImportReference(result);
        }

        protected override bool PostProcessImpl(TypeDefinition[] componentSystemTypes)
        {
            var assemblyDefinition = AssemblyDefinition;

            var earlyInitHelpers = assemblyDefinition.MainModule.ImportReference(typeof(EarlyInitHelpers)).CheckedResolve();

            var autoClassName = $"__JobReflectionRegistrationOutput__{(uint) assemblyDefinition.FullName.GetHashCode()}";

            var classDef = new TypeDefinition("", autoClassName, TypeAttributes.Class, assemblyDefinition.MainModule.ImportReference(typeof(object)));
            classDef.IsBeforeFieldInit = false;

            classDef.CustomAttributes.Add(new CustomAttribute(AttributeConstructorReferenceFor(typeof(DOTSCompilerGeneratedAttribute), assemblyDefinition.MainModule)));

            var funcDef = new MethodDefinition("CreateJobReflectionData", MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig, assemblyDefinition.MainModule.ImportReference(typeof(void)));

            funcDef.Body.InitLocals = false;

            classDef.Methods.Add(funcDef);

            var body = funcDef.Body;
            var processor = body.GetILProcessor();

            bool anythingChanged = false;

            var genericJobs = new List<TypeReference>();
            var visited = new HashSet<string>();

            foreach (var attr in assemblyDefinition.CustomAttributes)
            {
                if (attr.AttributeType.FullName != RegisterGenericJobTypeAttributeName)
                    continue;

                var typeRef = (TypeReference)attr.ConstructorArguments[0].Value;
                var openType = typeRef.Resolve();

                if (!typeRef.IsGenericInstance || !openType.IsValueType)
                {
                    AddDiagnostic(UserError.DC3001(openType));
                    continue;
                }

                genericJobs.Add(typeRef);
                visited.Add(typeRef.FullName);
            }

            CollectGenericTypeInstances(AssemblyDefinition, genericJobs, visited);

            foreach (var t in assemblyDefinition.MainModule.Types)
            {
                anythingChanged |= VisitJobStructs(t, processor, body);
            }

            foreach (var t in genericJobs)
            {
                anythingChanged |= VisitJobStructs(t, processor, body);
            }

            processor.Emit(OpCodes.Ret);

            if (anythingChanged)
            {
                var ctorFuncDef = new MethodDefinition("EarlyInit", MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig, assemblyDefinition.MainModule.ImportReference(typeof(void)));

#if !UNITY_DOTSRUNTIME
                if (!Defines.Contains("UNITY_DOTSPLAYER") && !Defines.Contains("UNITY_EDITOR"))
                {
                    // Needs to run automatically in the player, but we need to
                    // exclude this attribute when building for the editor, or
                    // it will re-run the registration for every enter play mode.
                    var loadTypeEnumType = assemblyDefinition.MainModule.ImportReference(typeof(UnityEngine.RuntimeInitializeLoadType));
                    var attributeCtor = assemblyDefinition.MainModule.ImportReference(typeof(UnityEngine.RuntimeInitializeOnLoadMethodAttribute).GetConstructor(new[] { typeof(UnityEngine.RuntimeInitializeLoadType) }));
                    var attribute = new CustomAttribute(attributeCtor);
                    attribute.ConstructorArguments.Add(new CustomAttributeArgument(loadTypeEnumType, UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded));
                    ctorFuncDef.CustomAttributes.Add(attribute);
                }

                if (Defines.Contains("UNITY_EDITOR"))
                {
                    // Needs to run automatically in the editor.
                    var attributeCtor2 = assemblyDefinition.MainModule.ImportReference(typeof(UnityEditor.InitializeOnLoadMethodAttribute).GetConstructor(Type.EmptyTypes));
                    ctorFuncDef.CustomAttributes.Add(new CustomAttribute(attributeCtor2));
                }
#endif
                
                ctorFuncDef.Body.InitLocals = false;

                var p = ctorFuncDef.Body.GetILProcessor();

                p.Emit(OpCodes.Ldnull);
                p.Emit(OpCodes.Ldftn, funcDef);

                var delegateType = assemblyDefinition.MainModule.ImportReference(earlyInitHelpers.NestedTypes.First(x => x.Name == nameof(EarlyInitHelpers.EarlyInitFunction)));
                var delegateCtor = assemblyDefinition.MainModule.ImportReference(delegateType.CheckedResolve().GetConstructors().FirstOrDefault((x) => x.Parameters.Count == 2));
                p.Emit(OpCodes.Newobj, delegateCtor);

                p.Emit(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(earlyInitHelpers.Methods.First(x => x.Name == nameof(EarlyInitHelpers.AddEarlyInitFunction))));

                p.Emit(OpCodes.Ret);

                classDef.Methods.Add(ctorFuncDef);

                assemblyDefinition.MainModule.Types.Add(classDef);
            }

            return anythingChanged;
        }

        private bool VisitJobStructs(TypeReference t, ILProcessor processor, MethodBody body)
        {
            if (t.GenericParameters.Count > 0)
            {
                // Generic jobs need to be either reference in fully closed form, or registered explicitly with an attribute.
                return false;
            }

            var rt = t.CheckedResolve();

            bool didAnything = false;

            if (rt.HasInterfaces)
            {
                foreach (var iface in rt.Interfaces)
                {
                    var idef = iface.InterfaceType.CheckedResolve();

                    if (!idef.HasCustomAttributes)
                        continue;

                    foreach (var attr in idef.CustomAttributes)
                    {
                        if (attr.AttributeType.FullName != ProducerAttributeName)
                            continue;

                        var producerRef = (TypeReference)attr.ConstructorArguments[0].Value;
                        var launderedType = LaunderTypeRef(t);
                        didAnything |= GenerateCalls(producerRef, launderedType, body, processor);
                    }
                }
            }

            foreach (var nestedType in rt.NestedTypes)
            {
                didAnything |= VisitJobStructs(nestedType, processor, body);
            }

            return didAnything;
        }

        private bool GenerateCalls(TypeReference producerRef, TypeReference jobStructType, MethodBody body, ILProcessor processor)
        {
            try
            {
                var carrierType = producerRef.CheckedResolve();
                MethodDefinition methodToCall = null;
                while (carrierType != null)
                {
                    methodToCall = carrierType.GetMethods().FirstOrDefault((x) => x.Name == "EarlyJobInit" && x.Parameters.Count == 0 && x.IsStatic && x.IsPublic);

                    if (methodToCall != null)
                        break;

                    carrierType = carrierType.DeclaringType;
                }

                // Legacy jobs lazy initialize.
                if (methodToCall == null)
                    return false;

                var asm = AssemblyDefinition.MainModule;

                var errorHandler = asm.ImportReference((asm.ImportReference(typeof(EarlyInitHelpers)).Resolve().Methods.First(x => x.Name == nameof(EarlyInitHelpers.JobReflectionDataCreationFailed))));
                var typeType = asm.ImportReference(typeof(Type)).CheckedResolve();
                var getTypeFromHandle = asm.ImportReference(typeType.Methods.FirstOrDefault((x) => x.Name == "GetTypeFromHandle"));

                var mref = asm.ImportReference(asm.ImportReference(methodToCall).MakeGenericInstanceMethod(jobStructType));

                var callInsn = Instruction.Create(OpCodes.Call, mref);
                var handler = Instruction.Create(OpCodes.Nop);
                var landingPad = Instruction.Create(OpCodes.Nop);

                processor.Append(callInsn);
                processor.Append(handler);

                // This craziness is equivalent to typeof(n)
                processor.Append(Instruction.Create(OpCodes.Ldtoken, jobStructType));
                processor.Append(Instruction.Create(OpCodes.Call, getTypeFromHandle));
                processor.Append(Instruction.Create(OpCodes.Call, errorHandler));
                processor.Append(landingPad);

                var leaveSuccess = Instruction.Create(OpCodes.Leave, landingPad);
                var leaveFail = Instruction.Create(OpCodes.Leave, landingPad);
                processor.InsertAfter(callInsn, leaveSuccess);
                processor.InsertBefore(landingPad, leaveFail);

                var exc = new ExceptionHandler(ExceptionHandlerType.Catch);
                exc.TryStart = callInsn;
                exc.TryEnd = leaveSuccess.Next;
                exc.HandlerStart = handler;
                exc.HandlerEnd = leaveFail.Next;
                exc.CatchType = asm.ImportReference(typeof(Exception));
                body.ExceptionHandlers.Add(exc);
                return true;
            }
            catch (Exception ex)
            {
                AddDiagnostic(InternalCompilerError.DCICE300(producerRef, jobStructType, ex));
            }

            return false;
        }

        private static void CollectGenericTypeInstances(AssemblyDefinition assembly, List<TypeReference> types, HashSet<string> visited)
        {
            // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            // WARNING: THIS CODE HAS TO BE MAINTAINED IN SYNC WITH BurstReflection.cs in Unity.Burst package
            // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

            // From: https://gist.github.com/xoofx/710aaf86e0e8c81649d1261b1ef9590e
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            const int mdMaxCount = 1 << 24;
            foreach (var module in assembly.Modules)
            {
                for (int i = 1; i < mdMaxCount; i++)
                {
                    // Token base id for TypeSpec
                    const int mdTypeSpec = 0x1B000000;
                    var token = module.LookupToken(mdTypeSpec | i);
                    if (token is GenericInstanceType type)
                    {
                        if (type.IsGenericInstance && !type.ContainsGenericParameter)
                        {
                            CollectGenericTypeInstances(type, types, visited);
                        }
                    } else if (token == null) break;
                }

                for (int i = 1; i < mdMaxCount; i++)
                {
                    // Token base id for MethodSpec
                    const int mdMethodSpec = 0x2B000000;
                    var token = module.LookupToken(mdMethodSpec | i);
                    if (token is GenericInstanceMethod method)
                    {
                        foreach (var argType in method.GenericArguments)
                        {
                            if (argType.IsGenericInstance && !argType.ContainsGenericParameter)
                            {
                                CollectGenericTypeInstances(argType, types, visited);
                            }
                        }
                    }
                    else if (token == null) break;
                }

                for (int i = 1; i < mdMaxCount; i++)
                {
                    // Token base id for Field
                    const int mdField = 0x04000000;
                    var token = module.LookupToken(mdField | i);
                    if (token is FieldReference field)
                    {
                        var fieldType = field.FieldType;
                        if (fieldType.IsGenericInstance && !fieldType.ContainsGenericParameter)
                        {
                            CollectGenericTypeInstances(fieldType, types, visited);
                        }
                    }
                    else if (token == null) break;
                }
            }
        }

        private static void CollectGenericTypeInstances(TypeReference type, List<TypeReference> types, HashSet<string> visited)
        {
            if (type.IsPrimitive) return;
            if (!visited.Add(type.FullName)) return;

            // Add only concrete types
            if (type.IsGenericInstance && !type.ContainsGenericParameter)
            {
                types.Add(type);
            }

            // Collect recursively generic type arguments
            var genericInstanceType = type as GenericInstanceType;
            if (genericInstanceType != null)
            {
                foreach (var genericTypeArgument in genericInstanceType.GenericArguments)
                {
                    if (!genericTypeArgument.IsPrimitive)
                    {
                        CollectGenericTypeInstances(genericTypeArgument, types, visited);
                    }
                }
            }
        } 
    }
#endif
}

