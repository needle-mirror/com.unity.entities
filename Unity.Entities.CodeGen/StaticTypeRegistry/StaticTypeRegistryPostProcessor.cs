#if !DISABLE_TYPEMANAGER_ILPP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using TypeGenInfoList = System.Collections.Generic.List<Unity.Entities.CodeGen.StaticTypeRegistryPostProcessor.TypeGenInfo>;
using SystemList = System.Collections.Generic.List<Unity.Entities.CodeGen.StaticTypeRegistryPostProcessor.SystemTypeGenInfo>;
using TypeReferenceEqualityComparer = Unity.Cecil.Awesome.Comparers.TypeReferenceEqualityComparer;
using static Unity.Entities.BuildUtils.MonoExtensions;
using Unity.Entities.BuildUtils;
using Unity.Cecil.Awesome;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Unity.Assertions;
using UnityEngine.Scripting;
using static Unity.Entities.TypeManager;

namespace Unity.Entities.CodeGen
{
    /// <summary>
    /// This PostProcessor will generate type information for all types inheriting from a component type interface such
    /// as IComponentData, as well as generate the appropriate type information for ComponentSystems for all types
    /// with ComponentSystemBase in their type hierarchy. This information is then collected into a TypeRegistry class
    /// and injected into the assembly. At boot each TypeRegistry generated will be read to register all ECS type
    /// information upfront.
    ///
    /// TypeRegistry types in essence contain the TypeInfo struct that will be consumed at runtime, however some
    /// information in TypeInfo cannot be fully determined until we have all other assemblies' TypeRegistry instances.
    /// (e.g. runtime type indicies cannot be resolved until a TypeRegistry has been registered, and a type's WriteGroup
    /// list, which uses typeIndices, requires global knowledge about all the types declaring an dependency on a given
    /// component type, which again can't be resolved until we globally register TypeRegistry instances). As such, a
    /// TypeRegistry instance contains extra information to be used during registration type as well as at runtime to
    /// allow per assembly type information to be provided. Such information includes managed type information such as
    /// the array of `Type`s each TypeInfo is for, but as well as generated functions (and the list of delegates for
    /// said functions) to handle the case when we need to perform a GetHashCode(object obj) or
    /// Equals (object obj, void* someComponent)
    ///
    /// The processor will take the following flow:
    /// - Find all relevant types (component and system types. We look for both at the same time
    ///     to avoid scanning all types in the assembly more often than we need to
    /// - For found types, generate the appropriate type info and inject it into a TypeRegistry struct
    ///     - For Types this means:
    ///         - TypeInfo fields like alignment, size in chunk, actual type size, type category, entity offsets etc...
    ///         - Generate equality functions for each component. For pure value types this points to XXHash32 but for
    ///             managed types we need to generate a function specific to the type.
    ///         - Debug information such as the TypeName as that isn't available in DOTSRuntime dynamically
    ///     - For Systems this means:
    ///         - System info such as it's attributes for schedule order
    ///         - Sorting systems based on their attribute order
    ///         - Debug information such as SystemName as this isn't available in DOTSRuntime dynamically
    ///
    /// For DOTSRuntime, we still need the runtime to find all generated TypeRegistry instances for all assemblies and
    /// register them with the TypeManager at runtime. We would do this via ModuleInitializers however there is no
    /// support for those in il2cpp yet. So instead this registration is done elsewhere via TypeRegGen (part of the
    /// DOTS Runtime compilation pipeline).
    /// </summary>
    internal partial class StaticTypeRegistryPostProcessor : EntitiesILPostProcessor
    {
        MethodReference m_TypeInfoConstructorRef;
        MethodReference m_GetTypeFromHandleFnRef;

        Dictionary<TypeReference, (bool isChunkSerializable, bool hasChunkSerializableAttribute)> m_ChunkSerializableCache;

        TypeUtils TypeUtilsInstance;

        TypeDefinition GeneratedRegistryDef;
        MethodDefinition GeneratedRegistryCCTORDef;

        bool IsReleaseConfig;
        bool IsToolConfig;
        int ArchBits;

        protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
        {
            return false;
        }


        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        protected override bool PostProcessImpl(TypeDefinition[] componentSystemTypes)
        {
            if (AssemblyDefinition.Name.Name != "UnityEngine.CoreModule" &&
                !AssemblyDefinition.MainModule.AssemblyReferences.Any(r => r.Name.Contains("UnityEngine.CoreModule"))) return false;
            bool madeChange = false;

            IsReleaseConfig = false;//!EntitiesILPostProcessors.Defines.Contains("DEBUG");
            IsToolConfig = EntitiesILPostProcessors.Defines.Contains("UNITY_ENTITIES_RUNTIME_TOOLING");
            ArchBits = 64;//EntitiesILPostProcessors.Defines.Contains("UNITY_DOTSRUNTIME64") ? 64 : 32;

            m_GetTypeFromHandleFnRef = AssemblyDefinition.MainModule.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle"));


            (var typeGenInfoList, var systemList) = GatherTypeInformation();
            if (typeGenInfoList.Count > 0 || systemList.Count > 0)
            {
                madeChange = true;
                InjectAssemblyTypeRegistry(typeGenInfoList, systemList);
            }

            // We are modifying the TypeManager in these functions so only
            // do so if we are modifying the assembly with typemanager in it
            if (AssemblyDefinition.Name.Name == "Unity.Entities")
            {
                // Promote the SharedTypeIndex as we use it in our injected AssemblyRegistries.
                //Note that this is not _technically necessary because mono won't actually complain
                //about people using private types in somebody else's assembly (whereas coreclr will),
                //but it seems more correct to have it this way.
                var sharedTypeIndex = AssemblyDefinition.MainModule.ImportReference(typeof(TypeManager.SharedTypeIndex<>)).Resolve();
                sharedTypeIndex.MakeTypePublic();

                // Promote the TypeRegistry as we use it in our injected AssemblyRegistries
                var typeRegistry = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry)).Resolve();
                typeRegistry.MakeTypePublic();

                InjectEntityStableTypeHash();
                madeChange = true;
            }

            // Disabled for now as IL2CPP doesn't support module initializers
            //InjectModuleInitializer(assemblyTypeRegistryField);

            return madeChange;
        }

        // XXX KEEP IN SYNC WITH UNITYENGINE (except for the None i guess)
        public enum TypeCategory : int
        {
            /// <summary>
            /// Implements IComponentData (can be either a struct or a class)
            /// </summary>
            ComponentData,
            /// <summary>
            /// Implements IBufferElementData (struct only)
            /// </summary>
            BufferData,
            /// <summary>
            /// Implement ISharedComponentData (can be either a struct or a class)
            /// </summary>
            ISharedComponentData,
            /// <summary>
            /// Is an Entity
            /// </summary>
            EntityData,
            /// <summary>
            /// Inherits from UnityEngine.Object (class only)
            /// </summary>
            UnityEngineObject
        }

        void InitializeForTypeGeneration()
        {
            TypeUtilsInstance = new TypeUtils();
            TypeUtilsInstance.runnerOfMe = runnerOfMe;

            m_TypeInfoConstructorRef = runnerOfMe._TypeManager_TypeInfoDef.Resolve().GetConstructors().First(c=>c.Parameters.Count > 0);

            // Initialize Internal State
            m_ChunkSerializableCache = new Dictionary<TypeReference, (bool isChunkSerializable, bool hasChunkSerializableAttribute)>();
        }

        TypeCategory FindTypeCategoryForType(TypeDefinition typeDef)
        {
            var interfaces = typeDef.Interfaces;
            foreach (var iface in interfaces)
            {
                if (iface.InterfaceType.Name == "IComponentData" && iface.InterfaceType.Namespace == "Unity.Entities")
                    return TypeCategory.ComponentData;
                if (iface.InterfaceType.Name == "ISharedComponentData" && iface.InterfaceType.Namespace == "Unity.Entities")
                    return TypeCategory.ISharedComponentData;
                if (iface.InterfaceType.Name == "IBufferElementData" && iface.InterfaceType.Namespace == "Unity.Entities")
                    return TypeCategory.BufferData;
            }

            if (typeDef.TypeReferenceEqualsOrInheritsFrom(AssemblyDefinition.MainModule.ImportReference(runnerOfMe._UnityEngine_ObjectDef)))
            {
                return TypeCategory.UnityEngineObject;
            }
                
            return TypeCategory.UnityEngineObject;
        }

        TypeCategory FindTypeCategoryForTypeRecursive(TypeDefinition typeDef)
        {
            var typeCategory = FindTypeCategoryForType(typeDef);
            if (typeCategory == TypeCategory.UnityEngineObject && typeDef.BaseType != null)
            {
                var baseTypeDef = typeDef.BaseType.Resolve();
                if (baseTypeDef != null)
                    typeCategory = FindTypeCategoryForTypeRecursive(baseTypeDef);
            }

            return typeCategory;
        }

        //todo: why are there two functions that both check for the three component data interfaces
        bool IsInstantiableComponentType(TypeReference type)
        {
            var def = type.Resolve();

            //we do expect you to have a TypeIndex if you're abstract but you inherit from UnityEngine.Component
            if (def.IsAbstract && !def.IsChildTypeOf(AssemblyDefinition.MainModule.ImportReference(runnerOfMe._UnityEngine_ComponentDef).Resolve()))
                return false;

            bool HasAnyOfTheInterfaces(TypeDefinition def)
            {
                var isUO = def.IsChildTypeOf(runnerOfMe._UnityEngine_ObjectDef) ? 1 : 0;
                var isICD = def.Interfaces.Any(i =>
                        TypeReferenceEqualityComparer.AreEqual(
                            i.InterfaceType.Resolve(),
                            runnerOfMe._IComponentDataDef)) ? 1 : 0;

                var isIBED = def.Interfaces
                        .Any(i => TypeReferenceEqualityComparer.AreEqual(
                            i.InterfaceType.Resolve(),
                            runnerOfMe._IBufferElementDataDef)) ? 1 : 0;
                var isISCD = def.Interfaces
                        .Any(i => TypeReferenceEqualityComparer.AreEqual(
                            i.InterfaceType.Resolve(),
                            runnerOfMe._ISharedComponentDataDef)) ? 1 : 0;

                if (isUO > 0 && (isIBED > 0 || isISCD > 0))
                {
                    throw new InvalidOperationException(@$"Type initialization failure: {def.Name} inherits from UnityEngine.Object and also
							at least one of IBufferElementData or ISharedComponentData, which is not allowed.");
                }

                if (isICD + isIBED + isISCD > 1)
                {
                    throw new InvalidOperationException(@$"Type initialization failure: {def.Name} inherits from more than one of IBufferElementData,
							IComponentData, and ISharedComponentData, which is not allowed.");
                }
                return isUO + isICD + isIBED + isISCD >= 1;
            }

            bool HasAnyOfTheInterfacesRecursive(TypeDefinition def)
            {
                return HasAnyOfTheInterfaces(def) || (def.BaseType != null && HasAnyOfTheInterfacesRecursive(def.BaseType.Resolve()));
            }

            if (!HasAnyOfTheInterfacesRecursive(def))
            {
                return false;
            }

            if (type.HasAttribute("DisableAutoTypeRegistrationAttribute"))
                return false;

            return true;
        }

        bool IsInstantiableUnityEngineObject(TypeReference type)
        {
            if (type.HasGenericParameters)
                return false;
            var d = type.Resolve();
            if (d.IsAbstract ||
                !d.IsChildTypeOf(AssemblyDefinition.MainModule.ImportReference(runnerOfMe._UnityEngine_ComponentDef).Resolve()))
                return false;

            return true;
        }

        bool AddTypeToListIfSupported(HashSet<TypeReference> typeSet, TypeReference type)
        {
            if (type.Resolve().CustomAttributes.Any(a => a.AttributeType.Name.Contains("DisableAutoTypeRegistrationAttribute"))) return false;

            if (IsInstantiableComponentType(type) || IsInstantiableUnityEngineObject(type))
            { 
                typeSet.Add(type);
                type.Resolve().MakeTypeInternal();		
                return true;
            }
            return false;
        }
         
        /// <summary>
        /// Generates a list of type information for all component types in the assembly
        /// </summary>
        /// <returns></returns>
        (TypeGenInfoList, SystemList) GatherTypeInformation()
        {
            var components = new HashSet<TypeReference>();
            var typeGenInfoList = new TypeGenInfoList();
            var systemList = new List<TypeReference>();
            var invalidAutoSystems = new List<TypeDefinition>();

            // It's possible for the whole assembly to disable auto creation so check for it
            var disableAsmAutoCreation = AssemblyDefinition.CustomAttributes.Any(attr => attr.AttributeType.Name == "DisableAutoCreationAttribute");
            var componentSystemBaseClass = AssemblyDefinition.MainModule.ImportReference(typeof(ComponentSystemBase)).Resolve();

            foreach (var type in AssemblyDefinition.MainModule.GetAllTypes())
            {
                if ((type.IsValueType() && !type.IsInterface && type.Interfaces.Count > 0) || !type.IsValueType())
                {
                    // Generic components are handled below
                    if (type.HasGenericParameters) continue;

                    if (AddTypeToListIfSupported(components, type)) continue;

                    // If we're here the type isn't a component so see if it's a system

                    // these types obviously cannot be instantiated
                    if (type.IsAbstract)
                        continue;

                    // only derivatives of ComponentSystemBase are systems
                    if (!type.IsChildTypeOf(componentSystemBaseClass) &&
                        !type.TypeImplements(AssemblyDefinition.MainModule.ImportReference(typeof(ISystem)))) 
                        continue;

                    // the auto-creation system instantiates using the default ctor, so if we can't find one, exclude from list
                    // that said, if it's a value type, it's fine, because you can just do default
                    if (!type.IsValueType() && type.GetConstructors().All(c => c.HasParameters))
                    {
                        var disableTypeAutoCreation = type.CustomAttributes.Any(attr => attr.AttributeType.Name == "DisableAutoCreationAttribute");

                        // we want users to be explicit system creation
                        if (!disableAsmAutoCreation && !disableTypeAutoCreation)
                            invalidAutoSystems.Add(type);

                        continue;
                    }

                    // We will be referencing this type in generated functions in this assembly so make
                    // the type internal if it's private
                    type.MakeTypeInternal();
                    systemList.Add(type);
                }
            }

            foreach (var attr in AssemblyDefinition.CustomAttributes)
            {
                if (attr.AttributeType.Name.Contains("RegisterGenericSystemTypeAttribute"))
                {
                    var closedSystemType = AssemblyDefinition.MainModule.ImportReference((GenericInstanceType)attr.ConstructorArguments[0].Value);

                    closedSystemType.Resolve().MakeTypeInternal();
                    systemList.Add(closedSystemType);
                }
            }

            // For any found generic components, validate the user has registered the closed form with the assembly
            var genericComponents = AssemblyDefinition.CustomAttributes
                .Where(ca => ca.AttributeType.Name == "RegisterGenericComponentTypeAttribute" || ca.AttributeType.Name == "RegisterUnityEngineComponentTypeAttribute")
                .Select(ca => ca.ConstructorArguments.First().Value as TypeReference)
                .Distinct();
            foreach (var genericComponent in genericComponents)
            {
                if (IsInstantiableComponentType(genericComponent) || IsInstantiableUnityEngineObject(genericComponent))
                {
                    components.Add(genericComponent);
                }
                else
                    throw new Exception($"Unable to register component type {genericComponent} specified with RegisterGenericComponentType or RegisterUnityEngineComponentType.");
            }

            if (invalidAutoSystems.Any())
            {
                throw new ArgumentException(
                    "A default constructor is necessary for automatic system scheduling for Component Systems not marked with [DisableAutoCreation]: "
                    + string.Join(", ", invalidAutoSystems.Select(cs => cs.FullNameLikeRuntime())));
            }

            // Move the BuildComponentType here so we can keep assemblies with no components quick to process
            if (components.Count > 0 || systemList.Count > 0)
            {
                InitializeForTypeGeneration();
                foreach (var type in components)
                {
                    typeGenInfoList.Add(BuildComponentType(type));
                }

                //this just makes a dict of outgoing writegroup attributes, to be gathered together at runtime on startup
                PopulateWriteGroups(typeGenInfoList);
            }

            var systemTypeGenInfoList = new SystemList();
            foreach (var system in systemList)
            {
                systemTypeGenInfoList.Add(BuildSystemType(system, disableAsmAutoCreation)); 
            }

            return (typeGenInfoList, systemTypeGenInfoList);
        }

        internal SystemAttributeKind AttributeTypeToKindNoThrow(TypeReference attributeType, out bool wasSystemAttribute)
        {
            wasSystemAttribute = true;

            //todo: move all this to the proton-but-without-the-charge-friendly way of doing things
            if (TypeReferenceEqualityComparer
                        .AreEqual(AssemblyDefinition.MainModule.ImportReference(typeof(UpdateBeforeAttribute)), attributeType))
                return SystemAttributeKind.UpdateBefore;
            if (TypeReferenceEqualityComparer
                        .AreEqual(AssemblyDefinition.MainModule.ImportReference(typeof(UpdateAfterAttribute)), attributeType))
                return SystemAttributeKind.UpdateAfter;
            if (TypeReferenceEqualityComparer
                        .AreEqual(AssemblyDefinition.MainModule.ImportReference(typeof(CreateBeforeAttribute)), attributeType))
                return SystemAttributeKind.CreateBefore;
            if (TypeReferenceEqualityComparer
                        .AreEqual(AssemblyDefinition.MainModule.ImportReference(typeof(CreateAfterAttribute)), attributeType))
                return SystemAttributeKind.CreateAfter;
            if (TypeReferenceEqualityComparer
                        .AreEqual(AssemblyDefinition.MainModule.ImportReference(typeof(DisableAutoCreationAttribute)), attributeType))
                return SystemAttributeKind.DisableAutoCreation;
            if (TypeReferenceEqualityComparer
                        .AreEqual(AssemblyDefinition.MainModule.ImportReference(typeof(UpdateInGroupAttribute)), attributeType))
                return SystemAttributeKind.UpdateInGroup;
            if (TypeReferenceEqualityComparer
                        .AreEqual(AssemblyDefinition.MainModule.ImportReference(typeof(RequireMatchingQueriesForUpdateAttribute)), attributeType))
                return SystemAttributeKind.RequireMatchingQueriesForUpdate;

            wasSystemAttribute = false;
            return SystemAttributeKind.UpdateBefore;
        }

        struct MyComparer : IEqualityComparer<SystemAttributeWithTypeReference>
        {
            public bool Equals(SystemAttributeWithTypeReference x, SystemAttributeWithTypeReference y)
            {
                if (x.Kind != y.Kind) return false;
                if (x.Flags != y.Flags) return false;
                if (x.TargetSystemType == null && y.TargetSystemType == null) return true;
                return x.TargetSystemType.FullName == y.TargetSystemType.FullName; 
            }

            //if you use HashCode.Combine, there's a runtime error because of mismatched bcl's
            private static int CombineHashCodes(int h1, int h2)
            {
                unchecked // allow arithmetic overflow (wrap-around)
                {
                    const uint magic = 0x9E3779B9u;       // 2^32 / φ  ≈ 2654435769
                    uint a = (uint)h1;
                    uint b = (uint)h2;

                    // Mix:  a = a XOR ( b + magic + (a << 6) + (a >> 2) )
                    a ^= b + magic + (a << 6) + (a >> 2);

                    return (int)a;
                }
            }
            public int GetHashCode(SystemAttributeWithTypeReference obj)
            {
                return CombineHashCodes(obj.Kind.GetHashCode(), CombineHashCodes(obj.Flags.GetHashCode(), obj.TargetSystemType?.FullName.GetHashCode() ?? 0));
            }
        }

        SystemTypeGenInfo BuildSystemType(TypeReference type, bool asmLevelDisableAutoCreation)
        {
            SystemTypeGenInfo ret = default;

            ret.TypeReference = type;
            ret.TypeFlags = 0;
            var resolvedSystemType = type.Resolve();
            if (resolvedSystemType
                .IsChildTypeOf(AssemblyDefinition.MainModule.ImportReference(typeof(ComponentSystemGroup))
                    .Resolve()))
                ret.TypeFlags |= TypeManager.SystemTypeInfo.kIsSystemGroupFlag;

            if (TypeUtilsInstance.IsManagedType(type, 0))
                ret.TypeFlags |= TypeManager.SystemTypeInfo.kIsSystemManagedFlag;

            if (type.TypeImplements(AssemblyDefinition.MainModule.ImportReference(typeof(ISystemStartStop))))
                ret.TypeFlags |= TypeManager.SystemTypeInfo.kIsSystemISystemStartStopFlag;

            ret.FilterFlags = GetFilterFlag(resolvedSystemType);

            ret.Attributes = new HashSet<SystemAttributeWithTypeReference>(0, new MyComparer());

            var allAttributes = new List<CustomAttribute>();
            allAttributes.AddRange(resolvedSystemType.CustomAttributes);

            var baseType = resolvedSystemType.BaseType;
            var iters = 0;
            while (baseType != null)
            {

                var resolvedBaseType = baseType.Resolve();
                //don't inherit disableautocreation
                allAttributes.AddRange(resolvedBaseType.CustomAttributes.Where(a=>!a.AttributeType.Name.Contains("DisableAutoCreation")));
                baseType = resolvedBaseType.BaseType;
                iters++;
                if (iters > 100)
                {
                    break;
                }
            }

            foreach (var attr in allAttributes)
            {
                var attributeKind = AttributeTypeToKindNoThrow(AssemblyDefinition.MainModule.ImportReference(attr.AttributeType), out var wasSystemAttribute);
                if (wasSystemAttribute)
                {
                    switch (attributeKind)
                    {
                        case SystemAttributeKind.UpdateInGroup:
                            int flags = 0;
                            foreach (var f in attr.Fields)
                            {
                                if (f.Name == "OrderFirst" && f.Argument.Value is bool value)
                                {
                                    if (value)
                                        flags |= SystemAttribute.kOrderFirstFlag;
                                    continue;
                                }
                                if (f.Name == "OrderLast" && f.Argument.Value is bool value2)
                                {
                                    if (value2)
                                        flags |= SystemAttribute.kOrderLastFlag;
                                    continue;
                                }
                            }

                            ret.Attributes.Add(new SystemAttributeWithTypeReference
                            {
                                /*
                                 * make a systemattribute type that carries the type as a type object instead of as a type index
                                 * make and init big array of those in the constructor
                                 * translate that array one by one to type indices on startup
                                 */


                                Kind = attributeKind,
                                TargetSystemType = (TypeReference)attr.ConstructorArguments[0].Value,
                                Flags = flags
                            });
                            break;
                        case SystemAttributeKind.RequireMatchingQueriesForUpdate:
                            ret.Attributes.Add(new SystemAttributeWithTypeReference
                            {
                                Kind = attributeKind,
                                TargetSystemType = null,
                                Flags = 0
                            });
                            break;
                        case SystemAttributeKind.CreateAfter:
                        case SystemAttributeKind.CreateBefore:
                            ret.Attributes.Add(new SystemAttributeWithTypeReference
                            {
                                Kind = attributeKind,
                                TargetSystemType = (TypeReference)attr.ConstructorArguments[0].Value,
                                Flags = 0
                            });
                            break;
                        case SystemAttributeKind.UpdateAfter:
                        case SystemAttributeKind.UpdateBefore:
                            ret.Attributes.Add(new SystemAttributeWithTypeReference
                            {
                                /*
                                 * make a systemattribute type that carries the type as a type object instead of as a type index
                                 * make and init big array of those in the constructor
                                 * translate that array one by one to type indices on startup
                                 *
                                 */
                                Kind = attributeKind,
                                TargetSystemType = (TypeReference)attr.ConstructorArguments[0].Value,
                                Flags = 0
                            });
                            break;
                        case SystemAttributeKind.DisableAutoCreation:
                            if (asmLevelDisableAutoCreation) //it's assembly-wide anyway, so don't waste space
                                break;
                            ret.Attributes.Add(new SystemAttributeWithTypeReference
                            {
                                Kind = attributeKind,
                                TargetSystemType = null,
                                Flags = 0
                            });
                            break;
                    }
                }
            }

            /*if (AssemblyDefinition.Name.Name.Contains("Assembly-CSharp"))
            {
                Debugger.Launch();
            }*/

            return ret;
        }


        [MethodImpl(MethodImplOptions.NoOptimization)]
        unsafe FieldReference InjectAssemblyTypeRegistry(TypeGenInfoList typeGenInfoList, SystemList systemtypeGenInfoList)
        {
            var assemblyWideDisableAsmAutoCreation = AssemblyDefinition.CustomAttributes.Any(attr => attr.AttributeType.Name == "DisableAutoCreationAttribute");
            var typeRegistryDef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef);

            GeneratedRegistryDef = new TypeDefinition(
                "Unity.Entities.CodeGeneratedRegistry",
                "AssemblyTypeRegistry",
                TypeAttributes.Class | TypeAttributes.Public,
                AssemblyDefinition.MainModule.ImportReference(typeof(object)));
            GeneratedRegistryDef.CustomAttributes.Add(new CustomAttribute(AssemblyDefinition.MainModule.ImportReference(typeof(PreserveAttribute).GetConstructor(new Type[] { }))));
            AssemblyDefinition.MainModule.Types.Add(GeneratedRegistryDef);

            // Declares: "static TypeRegistry() { }" (i.e. the static ctor / .cctor)
            GeneratedRegistryCCTORDef = new MethodDefinition(
                ".cctor",
                MethodAttributes.Static
                | MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
                AssemblyDefinition.MainModule.ImportReference(typeof(void)));
            GeneratedRegistryDef.Methods.Add(GeneratedRegistryCCTORDef);
            GeneratedRegistryDef.IsBeforeFieldInit = true;
            GeneratedRegistryCCTORDef.Body.InitLocals = true;

            // Defines: class Unity.Entities.StaticTypeRegistry { public static readonly TypeRegistry TypeRegistry; }
            var assemblyTypeRegistryFieldDef = new FieldDefinition(
                "TypeRegistry",
                Mono.Cecil.FieldAttributes.Static
                | Mono.Cecil.FieldAttributes.Public
                | Mono.Cecil.FieldAttributes.InitOnly,
                typeRegistryDef);
            GeneratedRegistryDef.Fields.Add(assemblyTypeRegistryFieldDef);

            var assemblyTypeRegistryLocal = new VariableDefinition(typeRegistryDef);
            var systemAttributeLocal = new VariableDefinition(AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry.SystemAttributeWithType)));
            var systemAttributeArrayLocal = new VariableDefinition(AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry.SystemAttributeWithType[])));
            GeneratedRegistryCCTORDef.Body.Variables.Add(assemblyTypeRegistryLocal);
            GeneratedRegistryCCTORDef.Body.Variables.Add(systemAttributeLocal);
            GeneratedRegistryCCTORDef.Body.Variables.Add(systemAttributeArrayLocal);


            var il = GeneratedRegistryCCTORDef.Body.GetILProcessor();


            // Create a new TypeRegistry type
            //XXX CACHE ALL THESE RESOLVES
            var assemblyTypeRegistryCtorRef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef.Resolve().GetConstructors().Single(c => c.Parameters.Count == 0));
            il.Emit(OpCodes.Newobj, assemblyTypeRegistryCtorRef);
            il.Emit(OpCodes.Stloc_0);

            // Store TypeRegistry.AssemblyName
            il.Emit(OpCodes.Ldloc_0);
            var assemblyNameFieldDef =
                AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("AssemblyName"));
            il.Emit(OpCodes.Ldstr, AssemblyDefinition.Name.Name);
            il.Emit(OpCodes.Stfld, assemblyNameFieldDef);

            il.Emit(OpCodes.Ldloc_0);
            var hasAssemblyWideDisableAutoCreationFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("HasAssemblyWideDisableAutoCreation"));
            if (assemblyWideDisableAsmAutoCreation)
                il.Emit(OpCodes.Ldc_I4_1);
            else
                il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, hasAssemblyWideDisableAutoCreationFieldDef);

            // Store TypeRegistry.TypeInfos[]
            il.Emit(OpCodes.Ldloc_0);
            var typeInfoCount = typeGenInfoList.Count;
            var typeInfosFieldDef = 
                AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("TypeInfosPtr"));
            var typeInfoBlob = GenerateTypeInfoBlobArray(typeGenInfoList);
            if (typeInfoBlob.Length > 0)
            {
                var constantTypeInfoFieldDef = GenerateConstantData(GeneratedRegistryDef, typeInfoBlob);
                il.Emit(OpCodes.Ldsflda, constantTypeInfoFieldDef);
                if (typeInfoCount != (typeInfoBlob.Length / sizeof(TypeInfo)))
                {
                    throw new Exception("Internal error: type info blob array length doesn't match type info size * " +
                        "type info count. This is a bug in Unity; please use Help->Report a Bug... to let us know!");
                }
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Conv_U);
            }

            il.Emit(OpCodes.Stfld, typeInfosFieldDef);

            // Store TypeRegistry.TypeInfosCount
            il.Emit(OpCodes.Ldloc_0);
            var typeInfosCountFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("TypeInfosCount", BindingFlags.Public | BindingFlags.Instance));
            il.Emit(OpCodes.Ldc_I4, typeInfoCount);
            il.Emit(OpCodes.Stfld, typeInfosCountFieldDef);


            // Store TypeRegistry.Types[]
            il.Emit(OpCodes.Ldloc_0);
            var typesFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("Types", BindingFlags.Public | BindingFlags.Instance));
            GenerateTypeArray(il, typeGenInfoList.Select(tgi => tgi.TypeReference).ToList(), typesFieldDef, false);

            // Store TypeRegistry.TypeNames[]
            il.Emit(OpCodes.Ldloc_0);
            var typeNamesFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("TypeNames", BindingFlags.Public | BindingFlags.Instance));
            var typeNames = IsReleaseConfig ? new List<string>() : typeGenInfoList.Select(t => t.TypeReference.FullNameLikeRuntime()).ToList();
            EntitiesILPostProcessors.StoreStringArrayInField(il, typeNames, typeNamesFieldDef, false);

            // Store TypeRegistry.EntityOffsets
            il.Emit(OpCodes.Ldloc_0);
            int entityOffsetCount = typeGenInfoList.Sum(ti => ti.EntityOffsets.Count);
            var entityOffsetsFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("EntityOffsetsPtr", BindingFlags.Public | BindingFlags.Instance));
            var entityOffsetDataBlob = typeGenInfoList.SelectMany(tgi => tgi.EntityOffsets).SelectMany(offset => BitConverter.GetBytes(offset)).ToArray();
            if (entityOffsetDataBlob.Length > 0)
            {
                Assert.AreEqual(entityOffsetCount, entityOffsetDataBlob.Length / sizeof(int));

                var constantEntityOffsetsFieldDef = GenerateConstantData(GeneratedRegistryDef, entityOffsetDataBlob);
                il.Emit(OpCodes.Ldsflda, constantEntityOffsetsFieldDef);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Conv_U);
            }
            il.Emit(OpCodes.Stfld, entityOffsetsFieldDef);

            // Store TypeRegistry.EntityOffsetsCount
            il.Emit(OpCodes.Ldloc_0);
            var entityOffsetsCountFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("EntityOffsetsCount", BindingFlags.Public | BindingFlags.Instance));
            il.Emit(OpCodes.Ldc_I4, entityOffsetCount);
            il.Emit(OpCodes.Stfld, entityOffsetsCountFieldDef);


            // Store TypeRegistry.BlobAssetReferenceOffsets
            il.Emit(OpCodes.Ldloc_0);
            int blobAssetReferenceOffsetsCount = typeGenInfoList.Sum(ti => ti.BlobAssetRefOffsets.Count);
            var blobOffsetsFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("BlobAssetReferenceOffsetsPtr", BindingFlags.Public | BindingFlags.Instance));
            var blobOffsetsDataBlob = typeGenInfoList.SelectMany(tgi => tgi.BlobAssetRefOffsets).SelectMany(offset => BitConverter.GetBytes(offset)).ToArray();
            if (blobOffsetsDataBlob.Length > 0)
            {
                Assert.AreEqual(blobAssetReferenceOffsetsCount, blobOffsetsDataBlob.Length / sizeof(int));

                var constantblobOffsetsFieldDef = GenerateConstantData(GeneratedRegistryDef, blobOffsetsDataBlob);
                il.Emit(OpCodes.Ldsflda, constantblobOffsetsFieldDef);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Conv_U);
            }
            il.Emit(OpCodes.Stfld, blobOffsetsFieldDef);

            // Store TypeRegistry.BlobAssetReferenceOffsetsCount
            il.Emit(OpCodes.Ldloc_0);
            var blobOffsetsCountFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("BlobAssetReferenceOffsetsCount", BindingFlags.Public | BindingFlags.Instance));
            il.Emit(OpCodes.Ldc_I4, blobAssetReferenceOffsetsCount);
            il.Emit(OpCodes.Stfld, blobOffsetsCountFieldDef);

            //Store TypeRegistry.UnityObjectReferenceOffsets
            il.Emit(OpCodes.Ldloc_0);
            int unityObjectReferenceOffsetsCount = typeGenInfoList.Sum(ti => ti.UnityObjectRefOffsets.Count);
            var unityObjOffsetsFieldDef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef.Resolve().Fields.Single(f => f.Name == "UnityObjectReferenceOffsetsPtr"));
            var unityObjOffsetsDataBlob = typeGenInfoList.SelectMany(tgi => tgi.UnityObjectRefOffsets).SelectMany(offset => BitConverter.GetBytes(offset)).ToArray();
            if (unityObjOffsetsDataBlob.Length > 0)
            {
                var constantUnityObjOffsetsFieldDef = GenerateConstantData(GeneratedRegistryDef, unityObjOffsetsDataBlob);
                il.Emit(OpCodes.Ldsflda, constantUnityObjOffsetsFieldDef);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Conv_U);
            }
            il.Emit(OpCodes.Stfld, unityObjOffsetsFieldDef);

            // Store TypeRegistry.UnityObjectReferenceOffsetsCount
            il.Emit(OpCodes.Ldloc_0);
            var unityObjOffsetsCountFieldDef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef.Resolve().Fields.Single(f => f.Name == "UnityObjectReferenceOffsetsCount"));
            il.Emit(OpCodes.Ldc_I4, unityObjectReferenceOffsetsCount);
            il.Emit(OpCodes.Stfld, unityObjOffsetsCountFieldDef);


            //Store TypeRegistry.WeakAssetReferenceOffsets
            il.Emit(OpCodes.Ldloc_0);
            int weakAssetReferenceOffsetsCount = typeGenInfoList.Sum(ti => ti.WeakAssetRefOffsets.Count);
            var weakAssetOffsetsFieldDef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef.Resolve().Fields.Single(f => f.Name == "WeakAssetReferenceOffsetsPtr"));
            var weakAssetOffsetsDataBlob = typeGenInfoList.SelectMany(tgi => tgi.WeakAssetRefOffsets).SelectMany(offset => BitConverter.GetBytes(offset)).ToArray();
            if (weakAssetOffsetsDataBlob.Length > 0)
            {
                var constantWeakAssetOffsetsFieldDef = GenerateConstantData(GeneratedRegistryDef, weakAssetOffsetsDataBlob);
                il.Emit(OpCodes.Ldsflda, constantWeakAssetOffsetsFieldDef);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Conv_U);
            }
            il.Emit(OpCodes.Stfld, weakAssetOffsetsFieldDef);

            // Store TypeRegistry.WeakAssetReferenceOffsetsCount
            il.Emit(OpCodes.Ldloc_0);
            var weakAssetOffsetsCountFieldDef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef.Resolve().Fields.Single(f => f.Name == "WeakAssetReferenceOffsetsCount"));
            il.Emit(OpCodes.Ldc_I4, weakAssetReferenceOffsetsCount);
            il.Emit(OpCodes.Stfld, weakAssetOffsetsCountFieldDef);


            // Store TypeRegistry.WriteGroups[]
            il.Emit(OpCodes.Ldloc_0);
            var writeGroupsFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("WriteGroups", BindingFlags.Public | BindingFlags.Instance));
            GenerateWriteGroupArray(il, typeGenInfoList, writeGroupsFieldDef, false);




            // Store TypeRegistry.SystemTypeInfos[]
            il.Emit(OpCodes.Ldloc_0);
            var systemtypeInfoCount = systemtypeGenInfoList.Count;
            var systemtypeInfosFieldDef =
                AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("SystemTypeInfosPtr", BindingFlags.Public | BindingFlags.Instance));
            var systemtypeInfoBlob = GenerateSystemTypeInfoBlobArray(systemtypeGenInfoList);
            if (systemtypeInfoBlob.Length > 0)
            {
                var constantSystemTypeInfoFieldDef = GenerateConstantData(GeneratedRegistryDef, systemtypeInfoBlob);
                il.Emit(OpCodes.Ldsflda, constantSystemTypeInfoFieldDef);
                if (systemtypeInfoCount != (systemtypeInfoBlob.Length / sizeof(SystemTypeInfo)))
                {
                    throw new Exception("Internal error: system type info blob array length doesn't match system type info size * " +
                        "system type info count. This is a bug in Unity; please use Help->Report a Bug... to let us know!");
                }
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Conv_U);
            }
            il.Emit(OpCodes.Stfld, systemtypeInfosFieldDef);


            // Store TypeRegistry.SystemAttributes[]
            il.Emit(OpCodes.Ldloc_0);
            var systemAttributesFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("SystemAttributes", BindingFlags.Public | BindingFlags.Instance));
            GenerateSystemAttributesArray(il, systemtypeGenInfoList.SelectMany(stgi => stgi.Attributes).ToList(), systemAttributesFieldDef, false);

            var systemList = systemtypeGenInfoList.Select(tgi=>tgi.TypeReference).ToList(); 

            // Store TypeRegistry.SystemTypes[] 
            il.Emit(OpCodes.Ldloc_0);
            var systemTypesFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("SystemTypes", BindingFlags.Public | BindingFlags.Instance));
            GenerateTypeArray(il, systemList, systemTypesFieldDef, false);

            // Store TypeRegistry.SystemTypeNames[]
            il.Emit(OpCodes.Ldloc_0);
            var systemTypeNamesFieldDef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef.Resolve().Fields.Single(f => f.Name == "SystemTypeNames"));
            // TODO: SystemNames are currently _required_ for runtime component sorting. This should be replaced with a systemid at which point we can remove systemnames
            //var systemTypeNames = IsReleaseConfig ? new List<string>() : systemtypeGenInfoList.Select(t => t.FullNameLikeRuntime()).ToList();
            var systemTypeNames = systemList.Select(t => t.FullNameLikeRuntime()).ToList();
            EntitiesILPostProcessors.StoreStringArrayInField(il, systemTypeNames, systemTypeNamesFieldDef, false);

            //Store TypeRegistry.SystemTypeSizes[]
            il.Emit(OpCodes.Ldloc_0);
            var systemTypeSizesFieldDef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef.Resolve().Fields.Single(f => f.Name == "SystemTypeSizes"));
            GenerateSystemTypeSizeArray(il, systemList, systemTypeSizesFieldDef, false, ArchBits);

            //Generate calls to BurstRuntime.GetHashCode64 to populate TypeRegistry.SystemTypeHashes[]
            il.Emit(OpCodes.Ldloc_0);
            var systemTypeHashesFieldDef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef.Resolve().Fields.Single(f => f.Name == "SystemTypeHashes"));
            GenerateSystemTypeHashArray(il, systemList, systemTypeHashesFieldDef, false);


            // Store Delegates
            ///////////////////
            (var boxedEqualsFn, var boxedEqualsPtrFn, var boxedGetHashCodeFn) = InjectEqualityFunctions(typeGenInfoList);
            /*
            // Store TypeRegistry.BoxedEquals
            il.Emit(OpCodes.Ldloc_0);
            var boxedEqualsFnCtor = AssemblyDefinition.MainModule.ImportReference(
                typeof(TypeRegistry.GetBoxedEqualsFn).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) }));
            var boxedEqualsFnFieldDef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef.Resolve().Fields.Single(f => f.Name == "BoxedEquals"));
            il.Emit(OpCodes.Ldnull); // no this ptr
            il.Emit(OpCodes.Ldftn, boxedEqualsFn);
            il.Emit(OpCodes.Newobj, boxedEqualsFnCtor);
            il.Emit(OpCodes.Stfld, boxedEqualsFnFieldDef);

            // Store TypeRegistry.BoxedEqualsPtr
            il.Emit(OpCodes.Ldloc_0);
            var boxedEqualsPtrFnCtor = AssemblyDefinition.MainModule.ImportReference(
                typeof(TypeRegistry.GetBoxedEqualsPtrFn).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) }));
            var boxedEqualsPtrFnFieldDef = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeRegistryDef.Resolve().Fields.Single(f => f.Name == "BoxedEqualsPtr"));
            il.Emit(OpCodes.Ldnull); // no this ptr
            il.Emit(OpCodes.Ldftn, boxedEqualsPtrFn);
            il.Emit(OpCodes.Newobj, boxedEqualsPtrFnCtor);
            il.Emit(OpCodes.Stfld, boxedEqualsPtrFnFieldDef);

            // Store TypeRegistry.BoxedGetHashCode
            il.Emit(OpCodes.Ldloc_0);
            var boxedGetHashCodeFnCtor = AssemblyDefinition.MainModule.ImportReference(
                typeof(TypeRegistry.BoxedGetHashCodeFn).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) }));
            var boxedGetHashCodeFnFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("BoxedGetHashCode", BindingFlags.Public | BindingFlags.Instance));
            il.Emit(OpCodes.Ldnull); // no this ptr
            il.Emit(OpCodes.Ldftn, boxedGetHashCodeFn);
            il.Emit(OpCodes.Newobj, boxedGetHashCodeFnCtor);
            il.Emit(OpCodes.Stfld, boxedGetHashCodeFnFieldDef);*/

            // Store TypeRegistry.SetSharedTypeIndices
            var setSharedTypeIndicesFn = InjectSetSharedStaticTypeIndices(typeGenInfoList);
            GeneratedRegistryDef.Methods.Add(setSharedTypeIndicesFn);
            il.Emit(OpCodes.Ldloc_0);
            var setSharedStaticTypeIndicesFnCtor = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry.SetSharedTypeIndicesFn).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) }));
            var setSharedStaticTypeIndicesFnFnFieldDef = AssemblyDefinition.MainModule.ImportReference(typeof(TypeRegistry).GetField("SetSharedTypeIndices", BindingFlags.Public | BindingFlags.Instance));
            il.Emit(OpCodes.Ldnull); // no this ptr
            il.Emit(OpCodes.Ldftn, setSharedTypeIndicesFn);
            il.Emit(OpCodes.Newobj, setSharedStaticTypeIndicesFnCtor);
            il.Emit(OpCodes.Stfld, setSharedStaticTypeIndicesFnFnFieldDef);

            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Stsfld, assemblyTypeRegistryFieldDef);

            il.Emit(OpCodes.Ret);

            return assemblyTypeRegistryFieldDef;
        }

        [MethodImpl(MethodImplOptions.NoOptimization)]
        MethodDefinition InjectSetSharedStaticTypeIndices(TypeGenInfoList typeGenInfos)
        {
            var setSharedStaticTypeIndicesFn = new MethodDefinition("SetSharedStaticTypeIndices",
                MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig,
                AssemblyDefinition.MainModule.ImportReference(typeof(void)));

            var typeInfosPtrArg =
                new ParameterDefinition("pTypeInfos",
                    Mono.Cecil.ParameterAttributes.None,
                    AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def.MakePointerType()));
            setSharedStaticTypeIndicesFn.Parameters.Add(typeInfosPtrArg);

            var countArg = new ParameterDefinition("count",
                Mono.Cecil.ParameterAttributes.None,
                AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_Int32Def));
            setSharedStaticTypeIndicesFn.Parameters.Add(countArg);

            setSharedStaticTypeIndicesFn.Body.InitLocals = true;
            var il = setSharedStaticTypeIndicesFn.Body.GetILProcessor();

            if (!IsReleaseConfig)
            {
                // Check if the count != the number of Components types we expect, as this means the runtime code is passing
                // the wrong data into this function
                var branchEndOp = Instruction.Create(OpCodes.Nop);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, typeGenInfos.Count);
                il.Emit(OpCodes.Beq, branchEndOp);
                var argumentExceptionConstructor = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._System_ArgumentExceptionDef).Resolve().GetConstructors()
                    .Single(c => c.Parameters.Count == 1 && c.Parameters[0].ParameterType.MetadataType == MetadataType.String);
                il.Emit(OpCodes.Ldstr, $"The passed in 'count' does not match the expected count of '{typeGenInfos.Count}' component types");
                il.Emit(OpCodes.Newobj, AssemblyDefinition.MainModule.ImportReference(argumentExceptionConstructor));
                il.Emit(OpCodes.Throw);
                il.Append(branchEndOp);
            }

            // Burst needs to hae some IL that statically declares a SharedStatic for all component types
            // so it can map a hash to the type name which DOTS Runtime cannot do at runtime
            var openSharedTypeIndex = AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeManager_SharedTypeIndexDef);
            var sharedTypeIndexRefField = AssemblyDefinition.MainModule.ImportReference(
                runnerOfMe._TypeManager_SharedTypeIndexDef.Fields.First(f => f.Name == "Ref"));

            var closedSharedStatic = new GenericInstanceType(runnerOfMe._Burst_SharedStaticDef);
            closedSharedStatic.GenericArguments.Add(AssemblyDefinition.MainModule.ImportReference(runnerOfMe._TypeIndexDef));

            var sharedStaticGetDataFn = AssemblyDefinition.MainModule.ImportReference(
                runnerOfMe._Burst_SharedStaticDef.Properties.First(p => p.Name == "Data").GetMethod);

            for (int i = 0; i < typeGenInfos.Count; ++i)
            {
                var closedSharedTypeIndex = AssemblyDefinition.MainModule.ImportReference(openSharedTypeIndex.MakeGenericInstanceType(typeGenInfos[i].TypeReference));
                var closeSharedTypeIndexRefField = new FieldReference(sharedTypeIndexRefField.Name, sharedTypeIndexRefField.FieldType, closedSharedTypeIndex);

                // SharedTypeIndex<typeGenInfo.TypeReference>.Ref.Data = 0;
                il.Emit(OpCodes.Ldsflda, AssemblyDefinition.MainModule.ImportReference(closeSharedTypeIndexRefField));

                var mygetdatafn = AssemblyDefinition.MainModule.ImportReference(sharedStaticGetDataFn.MakeGenericHostMethod(closedSharedStatic));

                il.Emit(OpCodes.Call, mygetdatafn);

                // Fetch TypeIndex from the array
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldc_I4_4); // sizeof(int)
                il.Emit(OpCodes.Mul);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldind_I4);

                // Store loaded int to our SharedStatic's Ref.Data
                il.Emit(OpCodes.Stind_I4);
            }

            il.Emit(OpCodes.Ret);

            return setSharedStaticTypeIndicesFn;
        }

        void InjectEntityStableTypeHash()
        {
            var entityStableTypeHash = TypeHash.CalculateStableTypeHash(AssemblyDefinition.MainModule.ImportReference(typeof(Entity)));
            var typeManagerDef = AssemblyDefinition.MainModule.GetType("Unity.Entities.TypeManager");
            var getEntityStableTypeHashFn = typeManagerDef.GetMethods().First(m => m.Parameters.Count == 0 && m.Name == "GetEntityStableTypeHash");
            var il = getEntityStableTypeHashFn.Body.GetILProcessor();
            il.Body.Instructions.Clear();

            il.Emit(OpCodes.Ldc_I8, (long)entityStableTypeHash);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Customizes the <Module> initializer for our assembly to call out to TypeManager.RegisterAssemblyTypes
        /// </summary>
        /// <param name="typeRegistryField"></param>
        /// <exception cref="ArgumentException"></exception>
        ///
        // XXX todo elliotc: this is probably stupid because we're just going to call it by reflection anyway, no?
        void InjectModuleInitializer(FieldReference typeRegistryField)
        {
            const MethodAttributes Attributes = MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
            var initializerReturnType = AssemblyDefinition.MainModule.ImportReference(typeof(void));

            var moduleClass = AssemblyDefinition.MainModule.Types.FirstOrDefault(t => t.Name == "<Module>");
            if (moduleClass == null)
            {
                throw new ArgumentException($"Failed to find the module class for '{AssemblyDefinition.Name.Name}'");
            }

            var cctor = moduleClass.Methods.FirstOrDefault(m => m.Name == ".cctor");
            ILProcessor il = null;
            if (cctor == null)
            {
                // Create a blank cctor that simply returns (we'll add to it below)
                cctor = new MethodDefinition(".cctor", Attributes, initializerReturnType);
                il = cctor.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Ret));
                moduleClass.Methods.Add(cctor);
            }

            // Insert ourselves as the first thing performed in module initialization

            var loadAssemblyTypeRegistry = il.Create(OpCodes.Ldsfld, typeRegistryField);
            var callRegisterAssemblyTypes = il.Create(OpCodes.Call, AssemblyDefinition.MainModule.ImportReference(typeof(TypeManager).GetMethod(nameof(TypeManager.RegisterAssemblyTypes))));
            il.InsertBefore(il.Body.Instructions[0], new[] { loadAssemblyTypeRegistry, callRegisterAssemblyTypes });
        }
    }
}
#endif // !DISABLE_TYPEMANAGER_ILPP
