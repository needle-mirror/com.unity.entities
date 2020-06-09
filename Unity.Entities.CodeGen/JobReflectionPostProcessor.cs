using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities.CodeGen
{
    class JobReflectionPostProcessor : ILPostProcessor
    {
        private static readonly string ProducerAttributeName = typeof(JobProducerTypeAttribute).FullName;

        private static readonly string AutoCreateAttributeName = "Unity.Jobs.AutoCreateReflectionDataAttribute";

        public static string[] Defines { get; internal set; }

        public override ILPostProcessor GetInstance()
        {
            return this;
        }

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            return compiledAssembly.References.Any(f => Path.GetFileName(f) == "Unity.Jobs.dll");
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            if (!WillProcess(compiledAssembly))
                return null;

            Defines = compiledAssembly.Defines;

            var assemblyDefinition = EntitiesILPostProcessors.AssemblyDefinitionFor(compiledAssembly);
            var diagnostics = new List<DiagnosticMessage>();

            bool anythingChanged = GenerateReflectionDataSetup(assemblyDefinition, diagnostics);

            if (!anythingChanged)
            {
                return new ILPostProcessResult(null, diagnostics);
            }

            var pe = new MemoryStream();
            var pdb = new MemoryStream();
            var writerParameters = new WriterParameters
            {
                SymbolWriterProvider = new PortablePdbWriterProvider(), SymbolStream = pdb, WriteSymbols = true
            };

            assemblyDefinition.Write(pe, writerParameters);
            return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diagnostics);
        }

        private bool GenerateReflectionDataSetup(AssemblyDefinition assemblyDefinition, List<DiagnosticMessage> diagnostics)
        {
            var autoClassName = $"__JobReflectionRegistrationOutput__{(uint) assemblyDefinition.FullName.GetHashCode()}";

            var classDef = new TypeDefinition("", autoClassName, TypeAttributes.Class, assemblyDefinition.MainModule.ImportReference(typeof(object)));
            classDef.IsBeforeFieldInit = false;
            assemblyDefinition.MainModule.Types.Add(classDef);

            var funcDef = new MethodDefinition(".cctor", MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, assemblyDefinition.MainModule.ImportReference(typeof(void)));
            funcDef.Body.InitLocals = false;

#if !UNITY_DOTSPLAYER // This will need a different solution
            if (!Defines.Contains("UNITY_DOTSPLAYER"))
            {
                // Needs to run automatically in the player.
                var loadTypeEnumType = assemblyDefinition.MainModule.ImportReference(typeof(UnityEngine.RuntimeInitializeLoadType));
                var attributeCtor = assemblyDefinition.MainModule.ImportReference(typeof(UnityEngine.RuntimeInitializeOnLoadMethodAttribute).GetConstructor(new[] { typeof(UnityEngine.RuntimeInitializeLoadType) }));
                var attribute = new CustomAttribute(attributeCtor);
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(loadTypeEnumType, UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded));
                funcDef.CustomAttributes.Add(attribute);
            }

            if (Defines.Contains("UNITY_EDITOR"))
            {
                // Needs to run automatically in the editor.
                var attributeCtor2 = assemblyDefinition.MainModule.ImportReference(typeof(UnityEditor.InitializeOnLoadMethodAttribute).GetConstructor(Type.EmptyTypes));
                funcDef.CustomAttributes.Add(new CustomAttribute(attributeCtor2));
            }
#endif

            classDef.Methods.Add(funcDef);

            var processor = funcDef.Body.GetILProcessor();

            bool anythingChanged = false;

            foreach (var t in assemblyDefinition.MainModule.Types)
            {
                anythingChanged |= VisitJobStructs(assemblyDefinition, t, processor, diagnostics);
            }

            funcDef.Body.GetILProcessor().Emit(OpCodes.Ret);
            return anythingChanged;
        }

        private bool VisitJobStructs(AssemblyDefinition assemblyDefinition, TypeDefinition t, ILProcessor processor, List<DiagnosticMessage> diagnostics)
        {
            if (!t.HasInterfaces)
                return false;

            bool didAnything = false;

            foreach (var iface in t.Interfaces)
            {
                var idef = iface.InterfaceType.Resolve();

                if (!idef.HasCustomAttributes)
                    continue;

                foreach (var attr in idef.CustomAttributes)
                {
                    if (attr.AttributeType.FullName == ProducerAttributeName)
                    {
                        var producerRef = (TypeReference) attr.ConstructorArguments[0].Value;
                        didAnything |= GenerateCalls(assemblyDefinition, producerRef, t, processor, diagnostics);
                    }
                }
            }

            foreach (var nestedType in t.NestedTypes)
            {
                didAnything |= VisitJobStructs(assemblyDefinition, nestedType, processor, diagnostics);
            }

            return didAnything;
        }

        private bool GenerateCalls(AssemblyDefinition assemblyDefinition, TypeReference producerRef, TypeDefinition jobStructType, ILProcessor processor, List<DiagnosticMessage> diagnostics)
        {
            var didAnything = false;
            try
            {
                var structChecked = false;

                // Should cache for perf!
                foreach (var method in producerRef.Resolve().Methods)
                {
                    if (!method.HasCustomAttributes ||
                        method.CustomAttributes.FirstOrDefault(x =>
                            x.AttributeType.FullName == AutoCreateAttributeName) == null)
                        continue;

                    if (!method.IsStatic)
                    {
                        diagnostics.Add(new DiagnosticMessage
                        {
                            DiagnosticType = DiagnosticType.Error,
                            MessageData = $"{jobStructType.FullName}.{method.Name}: auto-registration functions must be static",
                        });
                        return didAnything;
                    }

                    // We need a separate solution for generic jobs
                    if (!structChecked)
                    {
                        structChecked = true;

                        if (jobStructType.HasGenericParameters)
                        {
                            diagnostics.Add(new DiagnosticMessage
                            {
                                DiagnosticType = DiagnosticType.Error,
                                MessageData = $"{jobStructType.FullName}: generic jobs cannot have their reflection data auto-registered",
                            });
                            return didAnything;
                        }
                    }

                    var asm = assemblyDefinition.MainModule;
                    var t = asm.ImportReference(producerRef).MakeGenericInstanceType(jobStructType);
                    var mref = asm.ImportReference(method.MakeGenericHostMethod(t));
                    //Debug.Log($"Generating call to {mref.FullName}");
                    processor.Append(Instruction.Create(OpCodes.Call, mref));
                    didAnything = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"couldn't resolve {producerRef.FullName} via {jobStructType.FullName}: {ex}");
            }

            return didAnything;
        }
    }
}
