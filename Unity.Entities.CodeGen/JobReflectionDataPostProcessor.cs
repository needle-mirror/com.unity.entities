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

        protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
        {
            return false;
        }

        public static MethodReference AttributeConstructorReferenceFor(Type attributeType, ModuleDefinition module)
        {
            return module.ImportReference(attributeType.GetConstructors().Single(c => !c.GetParameters().Any()));
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

            var declaredGenerics = new HashSet<string>();
            var genericJobs = new List<TypeReference>();

            foreach (var attr in assemblyDefinition.CustomAttributes)
            {
                if (attr.AttributeType.FullName != RegisterGenericJobTypeAttributeName)
                    continue;

                var openTypeRef = (TypeReference)attr.ConstructorArguments[0].Value;
                var openType = assemblyDefinition.MainModule.ImportReference(openTypeRef).Resolve();

                if (!openTypeRef.IsGenericInstance || !openType.IsValueType)
                {
                    AddDiagnostic(UserError.DC3001(openType));
                    continue;
                }

                TypeReference result = new GenericInstanceType(assemblyDefinition.MainModule.ImportReference(new TypeReference(openType.Namespace, openType.Name, assemblyDefinition.MainModule, openTypeRef.Scope, true)));

                foreach (var ga in ((GenericInstanceType)openTypeRef).GenericArguments)
                {
                    ((GenericInstanceType)result).GenericArguments.Add(assemblyDefinition.MainModule.ImportReference(ga));
                }

                genericJobs.Add(result);


                var fn = openType.FullName;
                if (!declaredGenerics.Contains(fn))
                {
                    declaredGenerics.Add(fn);
                }
            }

            foreach (var t in assemblyDefinition.MainModule.Types)
            {
                anythingChanged |= VisitJobStructs(t, processor, body, declaredGenerics);
            }

            foreach (var t in genericJobs)
            {
                anythingChanged |= VisitJobStructs(t, processor, body, declaredGenerics);
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

        private bool VisitJobStructs(TypeReference t, ILProcessor processor, MethodBody body, HashSet<string> declaredGenerics)
        {
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
                        didAnything |= GenerateCalls(producerRef, t, body, processor, declaredGenerics);
                    }
                }
            }

            foreach (var nestedType in rt.NestedTypes)
            {
                didAnything |= VisitJobStructs(nestedType, processor, body, declaredGenerics);
            }

            return didAnything;
        }

        private bool GenerateCalls(TypeReference producerRef, TypeReference jobStructType, MethodBody body, ILProcessor processor, HashSet<string> declaredGenerics)
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

                // We need a separate solution for generic jobs
                if (jobStructType.HasGenericParameters)
                {
                    if (!declaredGenerics.Contains(jobStructType.FullName))
                        AddDiagnostic(UserError.DC3002(jobStructType));
                    return false;
                }

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
    }
#endif
}

