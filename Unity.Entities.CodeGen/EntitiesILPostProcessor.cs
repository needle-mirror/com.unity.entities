using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.Cecil.Awesome;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using System.Runtime.InteropServices;
using Unity.Core;
using Unity.Burst;
using System.Reflection;
using System.Threading;
using UnityEngine;

[assembly: InternalsVisibleTo("Unity.Entities.Hybrid.CodeGen")]
namespace Unity.Entities.CodeGen
{
    internal partial class EntitiesILPostProcessors : ILPostProcessor
    {
        bool _ReferencesEntities;
        bool _ReferencesJobs;
        public static string[] Defines { get; internal set; }

        static EntitiesILPostProcessor[] FindAllEntitiesILPostProcessors()
        {
            var processorTypes = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.FullName.Contains(".CodeGen"))
                    processorTypes.AddRange(assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(EntitiesILPostProcessor)) && !t.IsAbstract));
            }

            var result = processorTypes.Select(t => (EntitiesILPostProcessor)Activator.CreateInstance(t)).ToArray();

            Array.Sort(result);

            return result;
        }
        AssemblyDefinition AssemblyDefinition;

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            if (!WillProcess(compiledAssembly))
                return null;

            using (var marker = new EntitiesILPostProcessorProfileMarker(compiledAssembly.Name))
            {
                var diagnostics = new List<DiagnosticMessage>();
                bool madeAnyChange = false;
                Defines = compiledAssembly.Defines;
                Initialize(compiledAssembly);

                var postProcessors = FindAllEntitiesILPostProcessors();

                TypeDefinition[] componentSystemTypes;
                var allTypes = AssemblyDefinition.MainModule.GetAllTypes().ToArray();

                try
                {
                    using (marker.CreateChildMarker("GetAllComponentTypes"))
                        componentSystemTypes = allTypes.Where(type => type.IsComponentSystem()).ToArray();
                    // Make sure IL2CPP doesn't strip systems
                    if (componentSystemTypes.Length > 0)
                    {
                        var alwaysLinkAssemblyAttribute = new CustomAttribute(AssemblyDefinition.MainModule.ImportReference(_alwaysLinkAssemblyAttributeCtorDef));
                        AssemblyDefinition.MainModule.Assembly.CustomAttributes.Add(alwaysLinkAssemblyAttribute);
                    }
                }
                catch (FoundErrorInUserCodeException e)
                {
                    diagnostics.AddRange(e.DiagnosticMessages);
                    return null;
                }

                foreach (var postProcessor in postProcessors)
                {
                    postProcessor.runnerOfMe = this;

                    postProcessor.Initialize(Defines, _ReferencesEntities, _ReferencesJobs);
                    if (!postProcessor.WillProcess())
                        continue;

                    using (marker.CreateChildMarker(postProcessor.GetType().Name))
                    {
                        diagnostics.AddRange(postProcessor.PostProcess(AssemblyDefinition, componentSystemTypes, out var madeChange));
                        madeAnyChange |= madeChange;
                    }
                }

                var unmanagedComponentSystemTypes = allTypes.Where((x) => x.TypeImplements(_ISystemDef)).ToArray();
                foreach (var postProcessor in postProcessors)
                {
                    diagnostics.AddRange(postProcessor.PostProcessUnmanaged(AssemblyDefinition, unmanagedComponentSystemTypes, out var madeChange));
                    madeAnyChange |= madeChange;
                }

                // Hack to remove Entities => Entities circular references
                var selfName = AssemblyDefinition.Name.FullName;
                foreach (var referenceName in AssemblyDefinition.MainModule.AssemblyReferences)
                {
                    if (referenceName.FullName == selfName)
                    {
                        AssemblyDefinition.MainModule.AssemblyReferences.Remove(referenceName);
                        break;
                    }
                }

                if (!madeAnyChange || diagnostics.Any(d => d.DiagnosticType == DiagnosticType.Error))
                    return new ILPostProcessResult(null, diagnostics);

                using (marker.CreateChildMarker("WriteAssembly"))
                {
                    var pe = new MemoryStream();
                    var pdb = new MemoryStream();
                    var writerParameters = new WriterParameters
                    {
                        SymbolWriterProvider = new PortablePdbWriterProvider(),
                        SymbolStream = pdb,
                        WriteSymbols = true
                    };

                    AssemblyDefinition.Write(pe, writerParameters);
                    return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diagnostics);
                }
            }
        }

        public override ILPostProcessor GetInstance()
        {
            return this;
        }

        // Today there is no mechanism for sorting which ILPostProcessor runs relative to another
        // As such a sort order mechanism was added to this ILPP via running "EntitiesILPostProcessor"s
        // and sorting by `SortWeight`. However, some "EntitiesILPostProcessor"s need to run even if an assembly
        // doesn't references Entities.dll, so we extend the WillProcess implementation here to be inclusive
        // to other assemblies until the CompilationPipeline.ILPostProcessing API is extended
        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            _ReferencesEntities = false;
            if (compiledAssembly.Name == "Unity.Entities")
            {
                _ReferencesEntities = true;
                _ReferencesJobs = true;
                return true;
            }
            if (compiledAssembly.Name == "Unity.Jobs")
            {
                _ReferencesEntities = false;
                _ReferencesJobs = true;
                return true;
            }

            if (compiledAssembly.Name.EndsWith("CodeGen.Tests", StringComparison.Ordinal))
                return false;

            for (int i = 0;
                (!_ReferencesEntities || !_ReferencesJobs) // If we found both we can stop searching
                && i < compiledAssembly.References.Length; ++i)
            {
                var fileName = Path.GetFileNameWithoutExtension(compiledAssembly.References[i]);
                if (fileName == "Unity.Entities")
                    _ReferencesEntities = true;
                else if (fileName == "Unity.Jobs")
                    _ReferencesJobs = true;
            }

            return _ReferencesEntities || _ReferencesJobs;
        }

        class PostProcessorAssemblyResolver : Mono.Cecil.IAssemblyResolver
        {
            private readonly string[] _referenceDirectories;
            private Dictionary<string, HashSet<string>> _referenceToPathMap;
            Dictionary<string, AssemblyDefinition> _cache = new Dictionary<string, AssemblyDefinition>();
            private ICompiledAssembly _compiledAssembly;
            private AssemblyDefinition _selfAssembly;

            public PostProcessorAssemblyResolver(ICompiledAssembly compiledAssembly)
            {
                _compiledAssembly = compiledAssembly;
                _referenceToPathMap = new Dictionary<string, HashSet<string>>();
                foreach (var reference in compiledAssembly.References)
                {
                    var assemblyName = Path.GetFileNameWithoutExtension(reference);
                    if (!_referenceToPathMap.TryGetValue(assemblyName, out var fileList))
                    {
                        fileList = new HashSet<string>();
                        _referenceToPathMap.Add(assemblyName, fileList);
                    }
                    fileList.Add(reference);
                }

                _referenceDirectories = _referenceToPathMap.Values.SelectMany(pathSet => pathSet.Select(Path.GetDirectoryName)).Distinct().ToArray();
            }

            public void Dispose()
            {
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                {
                    if (name.Name == _compiledAssembly.Name)
                        return _selfAssembly;

                    var fileName = FindFile(name);
                    if (fileName == null)
                        return null;

                    var cacheKey = fileName;

                    if (_cache.TryGetValue(cacheKey, out var result))
                        return result;

                    parameters.AssemblyResolver = this;

                    var ms = MemoryStreamFor(fileName);

                    var pdb = fileName + ".pdb";
                    if (File.Exists(pdb))
                        parameters.SymbolStream = MemoryStreamFor(pdb);

                    var assemblyDefinition = AssemblyDefinition.ReadAssembly(ms, parameters);
                    _cache.Add(cacheKey, assemblyDefinition);
                    return assemblyDefinition;
                }
            }

            private string FindFile(AssemblyNameReference name)
            {
                if (_referenceToPathMap.TryGetValue(name.Name, out var paths))
                {
                    if (paths.Count == 1)
                        return paths.First();

                    // If we have more than one assembly with the same name loaded we now need to figure out which one
                    // is being requested based on the AssemblyNameReference
                    foreach (var path in paths)
                    {
                        var onDiskAssemblyName = AssemblyName.GetAssemblyName(path);
                        if (onDiskAssemblyName.FullName == name.FullName)
                            return path;
                    }
                    throw new ArgumentException($"Tried to resolve a reference in assembly '{name.FullName}' however the assembly could not be found. Known references which did not match: \n{string.Join("\n", paths)}");
                }

                // Unfortunately the current ICompiledAssembly API only provides direct references.
                // It is very much possible that a postprocessor ends up investigating a type in a directly
                // referenced assembly, that contains a field that is not in a directly referenced assembly.
                // if we don't do anything special for that situation, it will fail to resolve.  We should fix this
                // in the ILPostProcessing api. As a workaround, we rely on the fact here that the indirect references
                // are always located next to direct references, so we search in all directories of direct references we
                // got passed, and if we find the file in there, we resolve to it.
                foreach (var parentDir in _referenceDirectories)
                {
                    var candidate = Path.Combine(parentDir, name.Name + ".dll");
                    if (File.Exists(candidate))
                    {
                        if (!_referenceToPathMap.TryGetValue(candidate, out var referencePaths))
                        {
                            referencePaths = new HashSet<string>();
                            _referenceToPathMap.Add(candidate, referencePaths);
                        }
                        referencePaths.Add(candidate);

                        return candidate;
                    }
                }

                return null;
            }

            static MemoryStream MemoryStreamFor(string fileName)
            {
                return Retry(10, TimeSpan.FromSeconds(1), () => {
                    byte[] byteArray;
                    using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        byteArray = new byte[fs.Length];
                        var readLength = fs.Read(byteArray, 0, (int)fs.Length);
                        if (readLength != fs.Length)
                            throw new InvalidOperationException("File read length is not full length of file.");
                    }

                    return new MemoryStream(byteArray);
                });
            }

            private static MemoryStream Retry(int retryCount, TimeSpan waitTime, Func<MemoryStream> func)
            {
                try
                {
                    return func();
                }
                catch (IOException)
                {
                    if (retryCount == 0)
                        throw;
                    Console.WriteLine($"Caught IO Exception, trying {retryCount} more times");
                    Thread.Sleep(waitTime);
                    return Retry(retryCount - 1, waitTime, func);
                }
            }

            public void AddAssemblyDefinitionBeingOperatedOn(AssemblyDefinition assemblyDefinition)
            {
                _selfAssembly = assemblyDefinition;

            }
        }

        internal static AssemblyDefinition AssemblyDefinitionFor(ICompiledAssembly compiledAssembly)
        {
            var resolver = new PostProcessorAssemblyResolver(compiledAssembly);
            var readerParameters = new ReaderParameters
            {
                SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData.ToArray()),
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                AssemblyResolver = resolver,
                ReflectionImporterProvider = new PostProcessorReflectionImporterProvider(),
                ReadingMode = ReadingMode.Immediate
            };

            var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData.ToArray());
            var assemblyDefinition = AssemblyDefinition.ReadAssembly(peStream, readerParameters);

            //apparently, it will happen that when we ask to resolve a type that lives inside Unity.Entities, and we
            //are also postprocessing Unity.Entities, type resolving will fail, because we do not actually try to resolve
            //inside the assembly we are processing. Let's make sure we do that, so that we can use postprocessor features inside
            //unity.entities itself as well.
            resolver.AddAssemblyDefinitionBeingOperatedOn(assemblyDefinition);

            return assemblyDefinition;
        }

        internal class PostProcessorReflectionImporterProvider : IReflectionImporterProvider
        {
            public IReflectionImporter GetReflectionImporter(ModuleDefinition module)
            {
                return new PostProcessorReflectionImporter(module);
            }
        }

        internal class PostProcessorReflectionImporter : DefaultReflectionImporter
        {
            private const string SystemPrivateCoreLib = "System.Private.CoreLib";
            private AssemblyNameReference _correctCorlib;

            public PostProcessorReflectionImporter(ModuleDefinition module) : base(module)
            {
                _correctCorlib = module.AssemblyReferences.FirstOrDefault(a => a.Name == "mscorlib" || a.Name == "netstandard" || a.Name == SystemPrivateCoreLib);
            }

            public override AssemblyNameReference ImportReference(AssemblyName reference)
            {
                if (_correctCorlib != null && reference.Name == SystemPrivateCoreLib)
                    return _correctCorlib;

                return base.ImportReference(reference);
            }
        }

        internal ReaderParameters readerParameters;
        internal AssemblyDefinition coreModule;
        internal AssemblyDefinition entitiesAsm;

        internal MethodDefinition _monoPInvokeAttributeCtorDef;
        internal MethodDefinition _alwaysLinkAssemblyAttributeCtorDef;
        internal MethodDefinition _preserveAttributeCtorDef;
        internal MethodDefinition _readOnlyAttributeCtorDef;
        internal MethodDefinition _UnsafeUtility_MemCmpFnDef;
        internal MethodDefinition _XXHash_Hash32Def;

        internal TypeDefinition _ISystemDef;
        internal TypeDefinition _SystemBaseDef;
        internal TypeDefinition _IComponentDataDef;
        internal TypeDefinition _IBufferElementDataDef;
        internal TypeDefinition _ISharedComponentDataDef;
        internal TypeDefinition _BufferHeaderDef;

        internal TypeDefinition _SystemBaseDelegatesFunctionDef; //SystemBaseDelegates.Function
        internal TypeDefinition _IRefCountedDef;
        internal TypeReference _voidStarRef;

        internal TypeDefinition _TypeRegistryDef;
        internal TypeDefinition _TypeRegistry_GetBoxedEqualsFnDef;
        internal TypeDefinition _TypeRegistry_GetBoxedEqualsPtrFnDef;
        internal TypeDefinition _TypeRegistry_BoxedGetHashCodeFnDef;
        internal TypeDefinition _TypeRegistry_ConstructComponentFromBufferFnDef;
        internal TypeDefinition _TypeRegistry_GetSystemAttributesFnDef;
        internal TypeDefinition _TypeRegistry_CreateSystemFnDef;
        internal TypeDefinition _TypeRegistry_SetSharedTypeIndicesFnDef;

        internal TypeDefinition _TypeManagerDef;
        internal TypeDefinition _TypeManager_TypeInfoDef;
        internal TypeDefinition _TypeManager_SystemTypeInfoDef;
        internal int _TMSTI_kIsSystemGroupFlag;
        internal int _TMSTI_kIsSystemManagedFlag;
        internal int _TMSTI_kIsSystemISystemStartStopFlag;
        internal int _TMSTI_kSystemHasDefaultCtorFlag;

        internal TypeDefinition _TypeManager_SharedTypeIndexDef;
        internal TypeDefinition _TypeIndexDef;

        internal TypeDefinition _ComponentSystemGroupDef;
        internal TypeDefinition _ISystemStartStopDef;
        internal TypeDefinition _NativeContainerAttributeDef;
        internal TypeDefinition _TypeManager_TypeOverridesAttributeDef;
        internal TypeDefinition _UnityEngine_ComponentDef;
        internal TypeDefinition _UnityEngine_ObjectDef;
        internal TypeDefinition _EntityDef;
        internal TypeDefinition _BlobAssetReferenceDataDef;
        internal TypeDefinition _UntypedUnityObjectRefDef;
        internal TypeDefinition _UntypedWeakReferenceIdDef;

        internal TypeDefinition _WorldSystemFilterFlagsDef;
        internal int _WSFF_Default;
        internal int _WSFF_LocalSimulation;
        internal int _WSFF_ServerSimulation;
        internal int _WSFF_ClientSimulation;
        internal int _WSFF_Editor;

        internal TypeDefinition _Burst_SharedStaticDef;
        internal TypeReference _System_Int32Def;
        internal TypeReference _System_Int64Def;
        internal TypeReference _System_BoolDef;
        internal TypeReference _System_DecimalDef;
        internal TypeReference _System_ArgumentExceptionDef;
        internal TypeReference _System_NotSupportedExceptionDef;
        internal TypeReference _System_Collections_Generic_List_T_Def;

        internal TypeReference _System_ValueTypeDef;

        internal MethodDefinition _System_Guid_GetHashCode;

        internal TypeReference _System_ObjectDef;
        internal TypeDefinition _IRefCounted_RefCountDef;//IRefCounted.RefCountDelegate;
        internal TypeDefinition _FastEquality_TypeInfo_CompareEqualDelegateDef;//FastEquality.TypeInfo.CompareEqualDelegate
        internal TypeDefinition _FastEquality_TypeInfo_GetHashCodeDelegateDef;//FastEquality.TypeInfo.GetHashCodeDelegate

        internal TypeDefinition _SystemBaseRegistryDef;
        internal TypeDefinition _BurstRuntimeDef;
        internal MethodDefinition _BurstRuntime_GetHashCode64Def;

        private static MethodDefinition GetCtorForAttribute(AssemblyDefinition asm, string name)
        {
            var attr = asm.MainModule.GetType(name);

            try
            {
                return attr.GetConstructors().First();
            }
            catch (Exception)
            {
                throw new InvalidOperationException($"Could not find type {name}");
            }
        }

        [MethodImpl(MethodImplOptions.NoOptimization)]
        internal void Initialize(ICompiledAssembly compiledAssembly)
        {
            var inMemoryAssembly = compiledAssembly.InMemoryAssembly;


            var peData = inMemoryAssembly.PeData;
            var pdbData = inMemoryAssembly.PdbData;

            AssemblyDefinition = AssemblyDefinitionFor(compiledAssembly);

            //don't load it twice
            coreModule = AssemblyDefinition.Name.Name == "UnityEngine.CoreModule" ? AssemblyDefinition : AssemblyDefinition.MainModule.ImportReference(typeof(MonoBehaviour)).Resolve().Module.Assembly;
            entitiesAsm = AssemblyDefinition.Name.Name == "Unity.Entities" ? AssemblyDefinition : AssemblyDefinition.MainModule.ImportReference(typeof(Entity)).Resolve().Module.Assembly;

            // Initialize References

            _monoPInvokeAttributeCtorDef = GetCtorForAttribute(coreModule, "AOT.MonoPInvokeCallbackAttribute");
            _alwaysLinkAssemblyAttributeCtorDef = GetCtorForAttribute(coreModule, "UnityEngine.Scripting.AlwaysLinkAssemblyAttribute");
            _preserveAttributeCtorDef = GetCtorForAttribute(coreModule, "UnityEngine.Scripting.PreserveAttribute");
            _readOnlyAttributeCtorDef = GetCtorForAttribute(coreModule, "Unity.Collections.ReadOnlyAttribute");

            var coreModuleMain = coreModule.MainModule;
            var entitiesAsmMain = entitiesAsm.MainModule;
            _ISystemDef = entitiesAsmMain.GetType("Unity.Entities.ISystem");
            _SystemBaseDef = entitiesAsmMain.GetType("Unity.Entities.SystemBase");
            _ComponentSystemGroupDef = entitiesAsmMain.GetType("Unity.Entities.ComponentSystemGroup");

            var unsafeutility = coreModuleMain.GetType("Unity.Collections.LowLevel.Unsafe.UnsafeUtility");

            _UnsafeUtility_MemCmpFnDef = unsafeutility.GetMethods().Single(m => m.Name == "MemCmp");

            var xxhash = AssemblyDefinition.MainModule.ImportReference(typeof(XXHash)).Resolve();
            _XXHash_Hash32Def = xxhash.GetMethods().Single(m => m.Name == "Hash32" && m.Parameters.Count == 3);

            _IComponentDataDef = entitiesAsmMain.GetType("Unity.Entities.IComponentData");
            _IBufferElementDataDef = entitiesAsmMain.GetType("Unity.Entities.IBufferElementData");
            _ISharedComponentDataDef = entitiesAsmMain.GetType("Unity.Entities.ISharedComponentData");
            _BufferHeaderDef = entitiesAsmMain.GetType("Unity.Entities.BufferHeader");
            _SystemBaseDelegatesFunctionDef = entitiesAsmMain.GetType("Unity.Entities.SystemBaseDelegates/Function");
            _IRefCountedDef = entitiesAsmMain.GetType("Unity.Entities.IRefCounted");

            _TypeRegistryDef = entitiesAsmMain.GetType("Unity.Entities.TypeRegistry");

            _TypeManagerDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeManager)).Resolve();

            _TypeManager_TypeInfoDef = _TypeManagerDef.NestedTypes.First(t => t.Name == "TypeInfo");

            _TypeManager_SystemTypeInfoDef = _TypeManagerDef.NestedTypes.First(t => t.Name == "SystemTypeInfo");
            _TMSTI_kIsSystemGroupFlag = (int)_TypeManager_SystemTypeInfoDef.Fields.Single(t => t.Name == "kIsSystemGroupFlag").Constant;
            _TMSTI_kIsSystemManagedFlag = (int)_TypeManager_SystemTypeInfoDef.Fields.Single(t => t.Name == "kIsSystemManagedFlag").Constant;
            _TMSTI_kIsSystemISystemStartStopFlag = (int)_TypeManager_SystemTypeInfoDef.Fields.Single(t => t.Name == "kIsSystemISystemStartStopFlag").Constant;
            _TMSTI_kSystemHasDefaultCtorFlag = (int)_TypeManager_SystemTypeInfoDef.Fields.Single(t => t.Name == "kSystemHasDefaultCtor").Constant;

#if !DISABLE_TYPEMANAGER_ILPP
            StaticTypeRegistryPostProcessor.MaximumTypesCount = (int)_TypeManagerDef.Fields.Single(t => t.Name == "MaximumTypesCount").Constant;
            StaticTypeRegistryPostProcessor.HasNoEntityReferencesFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "HasNoEntityReferencesFlag").Constant;
            StaticTypeRegistryPostProcessor.IsNotChunkSerializableTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "IsNotChunkSerializableTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.HasNativeContainerFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "HasNativeContainerFlag").Constant;
            StaticTypeRegistryPostProcessor.BakingOnlyTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "BakingOnlyTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.TemporaryBakingTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "TemporaryBakingTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.IRefCountedComponentFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "IRefCountedComponentFlag").Constant;
            StaticTypeRegistryPostProcessor.IEquatableTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "IEquatableTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.EnableableComponentFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "EnableableComponentFlag").Constant;
            StaticTypeRegistryPostProcessor.CleanupComponentTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "CleanupComponentTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.BufferComponentTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "BufferComponentTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.SharedComponentTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "SharedComponentTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.ManagedComponentTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "ManagedComponentTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.ChunkComponentTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "ChunkComponentTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.ZeroSizeInChunkTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "ZeroSizeInChunkTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.CleanupSharedComponentTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "CleanupSharedComponentTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.CleanupBufferComponentTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "CleanupBufferComponentTypeFlag").Constant;
            StaticTypeRegistryPostProcessor.ManagedSharedComponentTypeFlag = (int)_TypeManagerDef.Fields.Single(t => t.Name == "ManagedSharedComponentTypeFlag").Constant;

            StaticTypeRegistryPostProcessor.ClearFlagsMask = (int)_TypeManagerDef.Fields.Single(t => t.Name == "ClearFlagsMask").Constant;
            StaticTypeRegistryPostProcessor.MaximumChunkCapacity = (int)_TypeManagerDef.Fields.Single(t => t.Name == "MaximumChunkCapacity").Constant;
            StaticTypeRegistryPostProcessor.MaximumSupportedAlignment = (int)_TypeManagerDef.Fields.Single(t => t.Name == "MaximumSupportedAlignment").Constant;
            StaticTypeRegistryPostProcessor.DefaultBufferCapacityNumerator = (int)_TypeManagerDef.Fields.Single(t => t.Name == "DefaultBufferCapacityNumerator").Constant;
#endif

            _TypeManager_SharedTypeIndexDef = _TypeManagerDef.NestedTypes.Single(t => t.Name == "SharedTypeIndex`1");
            _TypeIndexDef = entitiesAsmMain.GetType("Unity.Entities.TypeIndex");

            _TypeManager_TypeOverridesAttributeDef = _TypeManagerDef.NestedTypes.First(t => t.Name == "TypeOverridesAttribute");

            _ISystemStartStopDef = entitiesAsmMain.GetType("Unity.Entities.ISystemStartStop");
            _NativeContainerAttributeDef = coreModuleMain.GetType("Unity.Collections.LowLevel.Unsafe.NativeContainerAttribute");
            _UnityEngine_ComponentDef = coreModuleMain.GetType("UnityEngine.Component");
            _UnityEngine_ObjectDef = coreModuleMain.GetType("UnityEngine.Object");
            _EntityDef = entitiesAsmMain.GetType("Unity.Entities.Entity");
            _BlobAssetReferenceDataDef = entitiesAsmMain.GetType("Unity.Entities.BlobAssetReferenceData");
            _UntypedUnityObjectRefDef = AssemblyDefinition.MainModule.ImportReference(typeof(UntypedUnityObjectRef)).Resolve();
            _UntypedWeakReferenceIdDef = entitiesAsmMain.GetType("Unity.Entities.Serialization.UntypedWeakReferenceId");

            _WorldSystemFilterFlagsDef = AssemblyDefinition.MainModule.ImportReference(typeof(WorldSystemFilterFlags)).Resolve();

            _WSFF_Default = _WSFF_LocalSimulation = _WSFF_ServerSimulation = _WSFF_ClientSimulation = _WSFF_Editor = -1;
            foreach (var f in _WorldSystemFilterFlagsDef.Fields)
            {
                if (f.Name == "Default")
                    _WSFF_Default = (int)(uint)f.Constant;
                else if (f.Name == "LocalSimulation")
                    _WSFF_LocalSimulation = (int)(uint)f.Constant;
                else if (f.Name == "ServerSimulation")
                    _WSFF_ServerSimulation = (int)(uint)f.Constant;
                else if (f.Name == "ClientSimulation")
                    _WSFF_ClientSimulation = (int)(uint)f.Constant;
                else if (f.Name == "Editor")
                    _WSFF_Editor = (int)(uint)f.Constant;
            }

            if (_WSFF_Default == -1 ||
                _WSFF_LocalSimulation == -1 ||
                _WSFF_ServerSimulation == -1 ||
                _WSFF_ClientSimulation == -1 ||
                _WSFF_Editor == -1)
                throw new InvalidOperationException(@"Couldn't find all enum values for WorldSystemFilterFlags! This indicates a mismatch 
						    between entities ILPP code and Entities runtime code. If you don't know what that means, please report a bug 
						    via Help->Report a bug.... Thanks!");


            _Burst_SharedStaticDef = AssemblyDefinition.MainModule.ImportReference(typeof(SharedStatic<>)).Resolve();

            _IRefCounted_RefCountDef = entitiesAsmMain.GetType("Unity.Entities.IRefCounted/RefCountDelegate");
            _FastEquality_TypeInfo_CompareEqualDelegateDef = entitiesAsmMain.GetType("Unity.Entities.FastEquality/TypeInfo/CompareEqualDelegate");
            _FastEquality_TypeInfo_GetHashCodeDelegateDef = entitiesAsmMain.GetType("Unity.Entities.FastEquality/TypeInfo/GetHashCodeDelegate");
            _SystemBaseRegistryDef = entitiesAsmMain.GetType("Unity.Entities.SystemBaseRegistry");
            _BurstRuntimeDef = AssemblyDefinition.MainModule.ImportReference(typeof(BurstRuntime)).Resolve();
            _BurstRuntime_GetHashCode64Def = _BurstRuntimeDef.Resolve().Methods.Single((x) => x.Name == "GetHashCode64" && x.HasGenericParameters);

            _voidStarRef = AssemblyDefinition.MainModule.ImportReference(typeof(void)).MakePointerType(); //note if you say resolve() on this, it will turn into `void` instead of `void*`
            _System_ObjectDef = AssemblyDefinition.MainModule.ImportReference(typeof(object));
            var systemguid = AssemblyDefinition.MainModule.ImportReference(typeof(Guid)).Resolve();

            _System_Guid_GetHashCode = systemguid.GetMethods().Single(x => x.Name == "GetHashCode");

            _System_Int32Def = AssemblyDefinition.MainModule.ImportReference(typeof(int));
            _System_BoolDef = AssemblyDefinition.MainModule.ImportReference(typeof(bool));
            _System_DecimalDef = AssemblyDefinition.MainModule.ImportReference(typeof(decimal));
            _System_Int64Def = AssemblyDefinition.MainModule.ImportReference(typeof(long));
            _System_ArgumentExceptionDef = AssemblyDefinition.MainModule.ImportReference(typeof(ArgumentException));
            _System_NotSupportedExceptionDef = AssemblyDefinition.MainModule.ImportReference(typeof(NotSupportedException));
            _System_ValueTypeDef = AssemblyDefinition.MainModule.ImportReference(typeof(ValueType));
            _System_Collections_Generic_List_T_Def = AssemblyDefinition.MainModule.ImportReference(typeof(List<>));

            var tu = new TypeUtils();
            tu.runnerOfMe = this;
#if !DISABLE_TYPEMANAGER_ILPP
            //alignandsizeoftype needs other stuff to be inited before we can use it, so we do it down here
            var mysize = Marshal.SizeOf<StaticTypeRegistryPostProcessor.TypeInfo>();

            if (mysize != tu.AlignAndSizeOfType(_TypeManager_TypeInfoDef, 64).size)
            {
                throw new InvalidOperationException(@"Found mismatch in size between ILPP's TypeInfo and Entities's TypeInfo struct.
Entities initialization has therefore failed. Please report a bug via Help->Report a bug.... Thanks!");
            }
#endif
        }

    }

    abstract class EntitiesILPostProcessor : IComparable<EntitiesILPostProcessor>
    {
        public virtual int SortWeight => 0;
        public string[] Defines { get; private set; }
        public bool ReferencesEntities { get; private set; }
        public bool ReferencesJobs { get; private set; }
        protected AssemblyDefinition AssemblyDefinition;

        public EntitiesILPostProcessors runnerOfMe;

        internal void Initialize(string[] compilationDefines, bool referencesEntities, bool referencesJobs)
        {
            Defines = compilationDefines;
            ReferencesEntities = referencesEntities;
            ReferencesJobs = referencesJobs;
        }

        protected List<DiagnosticMessage> _diagnosticMessages = new List<DiagnosticMessage>();

        public IEnumerable<DiagnosticMessage> PostProcess(AssemblyDefinition assemblyDefinition, TypeDefinition[] componentSystemTypes, out bool madeAChange)
        {
            AssemblyDefinition = assemblyDefinition;
            try
            {
                madeAChange = PostProcessImpl(componentSystemTypes);
            }
            catch (FoundErrorInUserCodeException e)
            {
                madeAChange = false;
                return e.DiagnosticMessages;
            }

            return _diagnosticMessages;
        }

        public virtual bool WillProcess()
        {
            return ReferencesEntities;
        }

        protected abstract bool PostProcessImpl(TypeDefinition[] componentSystemTypes);
        protected abstract bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes);

        protected void AddDiagnostic(DiagnosticMessage diagnosticMessage)
        {
            _diagnosticMessages.Add(diagnosticMessage);
        }

        public IEnumerable<DiagnosticMessage> PostProcessUnmanaged(AssemblyDefinition assemblyDefinition, TypeDefinition[] unmanagedComponentSystemTypes, out bool madeAChange)
        {
            AssemblyDefinition = assemblyDefinition;
            try
            {
                madeAChange = PostProcessUnmanagedImpl(unmanagedComponentSystemTypes);
            }
            catch (FoundErrorInUserCodeException e)
            {
                madeAChange = false;
                return e.DiagnosticMessages;
            }

            return _diagnosticMessages;
        }

        int IComparable<EntitiesILPostProcessor>.CompareTo(EntitiesILPostProcessor other)
        {
            // Sort the postprocessors according to weight primarily, and name secondarily
            // Needed for determinism and to allow things that work on results of other postprocessors to work
            // (such as job reflection data for jobs that previous post processors have just made)
            int diff = SortWeight - other.SortWeight;
            if (diff != 0)
                return diff;

            Type ltype = GetType();
            Type rtype = other.GetType();
            return ltype.Name.CompareTo(rtype.Name);
        }

    }
}
