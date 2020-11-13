using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Unity.Entities.CodeGen
{
    internal class UnmanagedSystemPostprocessor : EntitiesILPostProcessor
    {
        protected override bool PostProcessImpl(TypeDefinition[] types)
        {
            return false;
        }

        struct TypeMemo
        {
            public TypeDefinition m_SystemType;
            public MethodDefinition[] m_Wrappers;
            public int m_BurstCompileBits;
        }

        protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
        {
            bool changes = false;

            var memos = new List<TypeMemo>();

            foreach (var td in unmanagedComponentSystemTypes)
            {
                if (td.HasGenericParameters)
                    continue;

                changes = true;

                // We will be generating functions using these types (e.g. GetHashCode64<T>()) which will require internal access rights
                td.MakeTypeInternal();
                memos.Add(AddStaticForwarders(td));
            }

            if (!changes)
                return false;

            AddRegistrationCode(memos);

            return changes;
        }

        private static readonly string GeneratedPrefix = "__codegen__";

        static readonly string[] MethodNames = new string[] { "OnCreate", "OnUpdate", "OnDestroy", "OnStartRunning", "OnStopRunning" };

        static readonly string[] MethodFullNames = new string[]
        {
            "Unity.Entities.ISystemBase.OnCreate",
            "Unity.Entities.ISystemBase.OnUpdate",
            "Unity.Entities.ISystemBase.OnDestroy",
            "Unity.Entities.ISystemBaseStartStop.OnStartRunning",
            "Unity.Entities.ISystemBaseStartStop.OnStopRunning"
        };

        private TypeMemo AddStaticForwarders(TypeDefinition systemType)
        {
            var mod = systemType.Module;
            var intPtrRef = mod.ImportReference(typeof(IntPtr));
            var intPtrToVoid = mod.ImportReference(intPtrRef.Resolve().Methods.FirstOrDefault(x => x.Name == nameof(IntPtr.ToPointer)));

            TypeMemo memo = default;
            memo.m_SystemType = systemType;
            memo.m_Wrappers = new MethodDefinition[5];

            var hasStartStop = systemType.Interfaces.Any((x) => x.InterfaceType.FullName == "Unity.Entities.ISystemBaseStartStop");

            for (int i = 0; i < MethodNames.Length; ++i)
            {
                var name = MethodNames[i];
                var fullName = MethodFullNames[i]; // Cecil sees interface method names from other assemblies as the full namespaced name

                if (!hasStartStop && i >= 3)
                    break;

                var methodDef = new MethodDefinition(GeneratedPrefix + name, MethodAttributes.Static | MethodAttributes.Assembly, mod.ImportReference(typeof(void)));
                methodDef.Parameters.Add(new ParameterDefinition("self", ParameterAttributes.None, intPtrRef));
                methodDef.Parameters.Add(new ParameterDefinition("state", ParameterAttributes.None, intPtrRef));

                var targetMethod = systemType.Methods.FirstOrDefault(x => x.Parameters.Count == 1 && (x.Name == name || x.Name == fullName));
                if (targetMethod == null)
                    continue;

                // Transfer any BurstCompile attribute from target function to the forwarding wrapper
                var burstAttribute = targetMethod.CustomAttributes.FirstOrDefault(x => x.Constructor.DeclaringType.Name == nameof(BurstCompileAttribute));
                if (burstAttribute != null)
                {
                    methodDef.CustomAttributes.Add(new CustomAttribute(burstAttribute.Constructor, burstAttribute.GetBlob()));
                    memo.m_BurstCompileBits |= 1 << i;
                }

#if UNITY_DOTSRUNTIME
                // Burst CompileFunctionPointer in DOTS Runtime will not currently decorate methods as [MonoPInvokeCallback]
                // so we add that here until that is supported
                var monoPInvokeCallbackAttributeConstructor = typeof(Jobs.MonoPInvokeCallbackAttribute).GetConstructor(Type.EmptyTypes);
                methodDef.CustomAttributes.Add(new CustomAttribute(mod.ImportReference(monoPInvokeCallbackAttributeConstructor)));
#else
                // Adding MonoPInvokeCallbackAttribute needed for IL2CPP to work when burst is disabled
                var monoPInvokeCallbackAttributeConstructors = typeof(MonoPInvokeCallbackAttribute).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var monoPInvokeCallbackAttribute = new CustomAttribute(mod.ImportReference(monoPInvokeCallbackAttributeConstructors[0]));
                monoPInvokeCallbackAttribute.ConstructorArguments.Add(new CustomAttributeArgument(mod.ImportReference(typeof(Type)), mod.ImportReference(typeof(SystemBaseDelegates.Function))));
                methodDef.CustomAttributes.Add(monoPInvokeCallbackAttribute);
#endif


                var processor = methodDef.Body.GetILProcessor();

                processor.Emit(OpCodes.Ldarga, 0);
                processor.Emit(OpCodes.Call, intPtrToVoid);
                processor.Emit(OpCodes.Ldarga, 1);
                processor.Emit(OpCodes.Call, intPtrToVoid);
                processor.Emit(OpCodes.Call, targetMethod);
                processor.Emit(OpCodes.Ret);

                systemType.Methods.Add(methodDef);
                memo.m_Wrappers[i] = methodDef;
            }

            return memo;
        }

        private void AddRegistrationCode(List<TypeMemo> memos)
        {
            var autoClassName = $"__UnmanagedPostProcessorOutput__{(uint)AssemblyDefinition.FullName.GetHashCode()}";
            var mod = AssemblyDefinition.MainModule;

            var classDef = new TypeDefinition("", autoClassName, TypeAttributes.Class, AssemblyDefinition.MainModule.ImportReference(typeof(object)));
            classDef.IsBeforeFieldInit = false;
            mod.Types.Add(classDef);

            var funcDef = new MethodDefinition("EarlyInit", MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig, AssemblyDefinition.MainModule.ImportReference(typeof(void)));
            funcDef.Body.InitLocals = false;

#if !UNITY_DOTSRUNTIME
            if (!Defines.Contains("UNITY_EDITOR"))
            {
                // Needs to run automatically in the player, but we need to
                // exclude this attribute when building for the editor, or
                // it will re-run the registration for every enter play mode.
                var loadTypeEnumType = mod.ImportReference(typeof(UnityEngine.RuntimeInitializeLoadType));
                var attributeCtor = mod.ImportReference(typeof(UnityEngine.RuntimeInitializeOnLoadMethodAttribute).GetConstructor(new[] { typeof(UnityEngine.RuntimeInitializeLoadType) }));
                var attribute = new CustomAttribute(attributeCtor);
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(loadTypeEnumType, UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded));
                funcDef.CustomAttributes.Add(attribute);
            }

            if (Defines.Contains("UNITY_EDITOR"))
            {
                // Needs to run automatically in the editor.
                var attributeCtor2 = AssemblyDefinition.MainModule.ImportReference(typeof(UnityEditor.InitializeOnLoadMethodAttribute).GetConstructor(Type.EmptyTypes));
                funcDef.CustomAttributes.Add(new CustomAttribute(attributeCtor2));
            }
#endif

            classDef.Methods.Add(funcDef);

            var processor = funcDef.Body.GetILProcessor();

            var registryType = mod.ImportReference(typeof(SystemBaseRegistry)).Resolve();
            var addMethod = mod.ImportReference(registryType.Methods.FirstOrDefault((x) => x.Name == nameof(SystemBaseRegistry.AddUnmanagedSystemType)));
            var delegateCtor = mod.ImportReference(registryType.NestedTypes.FirstOrDefault((x) => x.Name == nameof(SystemBaseRegistry.ForwardingFunc)).GetConstructors().FirstOrDefault((x) => x.Parameters.Count == 2));
            var genericHashFunc = mod.ImportReference(typeof(BurstRuntime)).Resolve().Methods.FirstOrDefault((x) => x.Name == nameof(BurstRuntime.GetHashCode64) && x.HasGenericParameters);
            var typeType = mod.ImportReference(typeof(Type)).Resolve();
            var getTypeFromHandle = mod.ImportReference(typeType.Methods.FirstOrDefault((x) => x.Name == "GetTypeFromHandle"));

            foreach (var memo in memos)
            {
                // This craziness is equivalent to typeof(n)
                processor.Emit(OpCodes.Ldtoken, memo.m_SystemType);
                processor.Emit(OpCodes.Call, getTypeFromHandle);

                processor.Emit(OpCodes.Call, mod.ImportReference(genericHashFunc.MakeGenericInstanceMethod(memo.m_SystemType)));

                for (int i = 0; i < memo.m_Wrappers.Length; ++i)
                {
                    if (memo.m_Wrappers[i] != null)
                    {
                        processor.Emit(OpCodes.Ldnull);
                        processor.Emit(OpCodes.Ldftn, memo.m_Wrappers[i]);
                        processor.Emit(OpCodes.Newobj, delegateCtor);
                    }
                    else
                    {
                        processor.Emit(OpCodes.Ldnull);
                    }
                }

                processor.Emit(OpCodes.Ldstr, memo.m_SystemType.Name);
                processor.Emit(OpCodes.Ldc_I4, memo.m_BurstCompileBits);
                processor.Emit(OpCodes.Call, addMethod);
            }

            processor.Emit(OpCodes.Ret);
        }
    }
}
