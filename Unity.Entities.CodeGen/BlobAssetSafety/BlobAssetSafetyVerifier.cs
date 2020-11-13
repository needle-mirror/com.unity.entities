using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Unity.Entities.CodeGen
{
    [UsedImplicitly]
    internal class BlobAssetSafetyVerifier : EntitiesILPostProcessor
    {
        private static bool _enable = true;

        protected override bool PostProcessImpl(TypeDefinition[] componentSystemTypes)
        {
            if (_enable)
                AssertNoBlobAssetLeavesBlobAssetStorage();
            return false;
        }

        void AssertNoBlobAssetLeavesBlobAssetStorage()
        {
            HashSet<TypeReference> _nonRestrictedTypes = new HashSet<TypeReference>();

            foreach (var type in AssemblyDefinition.MainModule.GetAllTypes())
            {
                if (!type.HasMethods)
                    continue;
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    var verifyDiagnosticMessages = VerifyMethod(method, _nonRestrictedTypes);
                    _diagnosticMessages.AddRange(verifyDiagnosticMessages);
                }
            }
        }

        public static bool IsOrHasReferenceTypeField(TypeReference type, HashSet<TypeReference> validatedTypes, MethodDefinition method, List<DiagnosticMessage> diagnosticMessages, out string fieldDescription)
        {
            fieldDescription = null;
            if (!type.IsValueType())
                return true;

            if (validatedTypes.Contains(type))
                return false;

            validatedTypes.Add(type);

            if (type.IsGenericInstance)
            {
                var isBlobArray = type.GetElementType().TypeReferenceEquals(typeof(BlobArray<>));
                if (isBlobArray || type.GetElementType().TypeReferenceEquals(typeof(BlobPtr<>)))
                {
                    var instance = (GenericInstanceType) type;
                    if (instance.GenericArguments.Count == 1 && IsOrHasReferenceTypeField(instance.GenericArguments[0],
                        validatedTypes, method, diagnosticMessages, out var fieldInGenericArg))
                    {
                        var genericIdentifier = isBlobArray ? "[]" : ".Value";
                        fieldDescription = genericIdentifier + fieldInGenericArg;
                        return true;
                    }
                }
            }

            // Allow this if we fail resolve but generate warning (in TryResolve)
            if (!TryResolve(type, method, diagnosticMessages, out var typeDefinition))
                return false;

            foreach (var field in typeDefinition.Fields)
            {
                if(field.IsStatic)
                    continue;

                if (IsOrHasReferenceTypeField(field.FieldType, validatedTypes, method, diagnosticMessages, out var fieldInFieldType))
                {
                    fieldDescription = "." + field.Name + fieldInFieldType;
                    return true;
                }
            }

            return false;
        }

        public static List<DiagnosticMessage> VerifyMethod(MethodDefinition method, HashSet<TypeReference> _nonRestrictedTypes)
        {
            var diagnosticMessages = new List<DiagnosticMessage>();

            bool IsTypeRestrictedToBlobAssetStorage(TypeReference tr)
            {
                if (tr.IsPrimitive)
                    return false;
                if (tr is GenericParameter)
                    return false;
                if (tr is PointerType)
                    return false;
                if (tr is ArrayType)
                    return false;
                if (tr is RequiredModifierType || tr is GenericInstanceType)
                {
                    tr = tr.GetElementType();
                    return IsTypeRestrictedToBlobAssetStorage(tr);
                }
                if (_nonRestrictedTypes.Contains(tr))
                    return false;

                if (tr.Scope is AssemblyNameReference anr)
                    if (anr.Name == "UnityEngine" || anr.Name == "UnityEditor" || anr.Name == "mscorlib" ||
                        anr.Name == "System.Private.CoreLib")
                        return false;

                if (!TryResolve(tr, method, diagnosticMessages, out var td))
                {
                    _nonRestrictedTypes.Add(tr);
                    return false;
                }

                if (td.IsValueType())
                {
                    if (HasMayOnlyLiveInBlobStorageAttribute(td))
                        return true;

                    foreach (var field in td.Fields)
                    {
                        if (field.IsStatic)
                            continue;
                        if (IsTypeRestrictedToBlobAssetStorage(field.FieldType))
                            return true;
                    }
                }

                _nonRestrictedTypes.Add(tr);
                return false;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.IsInvocation(out var targetMethod) &&
                    targetMethod.DeclaringType.TypeReferenceEquals(typeof(BlobBuilder)) &&
                    targetMethod.Name == nameof(BlobBuilder.ConstructRoot) &&
                    targetMethod is GenericInstanceMethod genericTargetMethod)
                {
                    foreach (var arg in genericTargetMethod.GenericArguments)
                    {
                        var validatedTypes = new HashSet<TypeReference>();
                        if (IsOrHasReferenceTypeField(arg, validatedTypes, method, diagnosticMessages, out var fieldDescription))
                        {
                            string errorFieldPath = fieldDescription == null ? arg.Name : arg.Name + fieldDescription;
                            var message = $"You may not build a type {arg.Name} with {nameof(BlobBuilder.Construct)} as {errorFieldPath} is a reference or pointer.  Only non-reference types are allowed in Blobs.";
                            diagnosticMessages.Add(UserError.MakeError("ConstructBlobWithRefTypeViolation", message, method, instruction));
                        }
                    }
                }
                if (instruction.OpCode == OpCodes.Ldfld)
                {
                    var fieldReference = (FieldReference)instruction.Operand;
                    var tr = fieldReference.FieldType;
                    if (IsTypeRestrictedToBlobAssetStorage(tr))
                    {
                        var fancyName = FancyNameFor(fieldReference.FieldType);

                        string error =
                            $"ref {fancyName} yourVariable = ref your{fieldReference.DeclaringType.Name}.{fieldReference.Name}";

                        diagnosticMessages.Add(
                            UserError.MakeError("MayOnlyLiveInBlobStorageViolation",
                                $"You may only access .{fieldReference.Name} by (non-readonly) ref, as it may only live in blob storage. try `{error}`",
                                method, instruction));
                    }
                }

                if (instruction.OpCode == OpCodes.Ldobj)
                {
                    var tr = (TypeReference)instruction.Operand;
                    if (IsTypeRestrictedToBlobAssetStorage(tr))
                    {
                        var pushingInstruction = CecilHelpers.FindInstructionThatPushedArg(method, 0, instruction);

                        string error = $"ref {tr.Name} yourVariable = ref ...";
                        if (pushingInstruction.Operand is FieldReference fr)
                        {
                            var typeName = fr.DeclaringType.Name;
                            error = $"ref {tr.Name} yourVariable = ref your{typeName}.{fr.Name}";
                        }

                        diagnosticMessages.Add(
                            UserError.MakeError("MayOnlyLiveInBlobStorageViolation",
                                $"{tr.Name} may only live in blob storage. Access it by (non-readonly) ref instead: `{error}`", method,
                                instruction));
                    }
                }
            }

            return diagnosticMessages;
        }

        // Don't do a CheckedResolve here. If we somehow fail we don't want to block the user.
        static bool TryResolve(TypeReference tr, MethodDefinition method, List<DiagnosticMessage> diagnosticMessages, out TypeDefinition td)
        {
            td = tr.Resolve();
            if (td == null)
            {
                diagnosticMessages.Add(
                    UserError.MakeWarning("ResolveFailureWarning",
                        $"Unable to resolve type {tr.FullName} for verification.",
                        method, method.Body.Instructions.FirstOrDefault()));
                return false;
            }

            return true;
        }

        private static string FancyNameFor(TypeReference typeReference)
        {
            if (typeReference is GenericInstanceType git)
            {
                var sb = new StringBuilder();
                sb.Append(typeReference.Name.Split('`')[0]);
                sb.Append("<");
                bool first = true;
                foreach (var ga in git.GenericArguments)
                {
                    if (!first)
                        sb.Append(",");
                    sb.Append(FancyNameFor(ga));
                    first = false;
                }

                sb.Append(">");
                return sb.ToString();
            }

            return typeReference.Name;
        }

        static bool HasMayOnlyLiveInBlobStorageAttribute(TypeDefinition td)
        {
            if (!td.HasCustomAttributes)
                return false;
            foreach (var ca in td.CustomAttributes)
                if (ca.AttributeType.Name == nameof(MayOnlyLiveInBlobStorageAttribute))
                    return true;
            return false;
        }

        protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
        {
            return false;
        }
    }
}
