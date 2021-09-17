using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    static class LambdaJobsVerification
    {
        public static void Verify(this LambdaJobDescription description)
        {
            // If our lambda expression is badly formed, early out as we will likely flag other false errors (capturing of this)
            if (!VerifyLambdaExpression(description))
            {
                description.Success = false;
                return;
            }

            if ((description.Burst.IsEnabled || description.Schedule.Mode != ScheduleMode.Run) && description.VariablesCaptured.Any(var => var.Type.IsReferenceType))
            {
                foreach (var variable in description.VariablesCaptured.Where(var => var.Type.IsReferenceType))
                    LambdaJobsErrors.DC0004(description.SystemGeneratorContext, description.Location, variable.OriginalVariableName);
                description.Success = false;
            }

            if (description.LambdaJobKind == LambdaJobKind.Job && description.WithStructuralChanges)
            {
                LambdaJobsErrors.DC0057(description.SystemGeneratorContext, description.Location);
                description.Success = false;
            }

            if (!VerifyDC0073(description))
            {
                description.Success = false;
            }
        }

        static bool VerifyLambdaExpression(LambdaJobDescription description)
        {
            var success = true;
            foreach (var invocationExpression in description.OriginalLambdaExpression.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                // Check if any structural changes are made inside
                if ((!description.WithStructuralChanges || description.Schedule.Mode != ScheduleMode.Run) &&
                    invocationExpression.DoesPerformStructuralChange(description.SemanticModel))
                {
                    LambdaJobsErrors.DC0027(description.SystemGeneratorContext, invocationExpression.GetLocation());
                    success = false;
                }
                if (invocationExpression.CheckIsForEach(description.SemanticModel))
                {
                    LambdaJobsErrors.DC0029(description.SystemGeneratorContext, invocationExpression.GetLocation());
                    success = false;
                }
            }

            foreach (var methodInvocations in description.MethodInvocations)
            {
                if (methodInvocations.Key != "ForEach" && methodInvocations.Key != "WithCode")
                {
                    foreach (var methodInvocation in methodInvocations.Value.Where(methodInvocation => methodInvocation.ContainsDynamicCode()))
                    {
                        LambdaJobsErrors.DC0010(description.SystemGeneratorContext, methodInvocation.GetLocation(), methodInvocations.Key);
                        description.Success = false;
                    }
                }
            }

            foreach (var methods in description.MethodInvocations.Where(methods => methods.Value.Count > 1))
            {
                var methodInvocationSymbol = description.SemanticModel.GetSymbolInfo(methods.Value.First()).Symbol;
                if (!methodInvocationSymbol.HasAttribute(
                    "Unity.Entities.LambdaJobDescriptionConstructionMethods.AllowMultipleInvocationsAttribute"))
                {
                    LambdaJobsErrors.DC0009(description.SystemGeneratorContext, description.Location, methods.Key);
                    description.Success = false;
                }
            }

            foreach (var storeInQuerySyntax in description.WithStoreEntityQueryInFieldArgumentSyntaxes)
            {
                if (!(storeInQuerySyntax.Expression is IdentifierNameSyntax storeInQueryIdentifier) || !(description.SemanticModel.GetSymbolInfo(storeInQueryIdentifier).Symbol is IFieldSymbol))
                {
                    LambdaJobsErrors.DC0031(description.SystemGeneratorContext, description.Location);
                    description.Success = false;
                }
            }

            if (description.Burst.IsEnabled || description.Schedule.Mode == ScheduleMode.Schedule || description.Schedule.Mode == ScheduleMode.ScheduleParallel)
            {
                foreach (var param in description.ExecuteMethodParamDescriptions.OfType<IManagedComponentParamDescription>())
                {
                    LambdaJobsErrors.DC0223(description.SystemGeneratorContext, description.Location, param.Name, description.IsInSystemBase, true);
                    description.Success = false;
                }
                foreach (var param in description.ExecuteMethodParamDescriptions.OfType<ISharedComponentParamDescription>())
                {
                    LambdaJobsErrors.DC0223(description.SystemGeneratorContext, description.Location, param.TypeSymbol.Name, description.IsInSystemBase, false);
                    description.Success = false;
                }
            }

            var duplicateTypes =
                description
                    .ExecuteMethodParamDescriptions
                    .Where(desc => !(desc is IAllowDuplicateTypes))
                    .FindDuplicatesBy(desc => desc.TypeSymbol.ToFullName());

            foreach (var duplicate in duplicateTypes)
            {
                LambdaJobsErrors.DC0070(
                    description.SystemGeneratorContext,
                    description.Location,
                    duplicate.TypeSymbol);

                description.Success = false;
            }

            foreach (var param in description.ExecuteMethodParamDescriptions.OfType<IComponentParamDescription>().Where(param => string.IsNullOrEmpty(param.Syntax.GetModifierString())))
            {
                LambdaJobsErrors.DC0055(description.SystemGeneratorContext, description.Location, param.Name);
            }

            foreach (var param in description.ExecuteMethodParamDescriptions.OfType<IManagedComponentParamDescription>().Where(param => param.IsByRef()))
            {
                if (param.IsByRef())
                {
                    LambdaJobsErrors.DC0024(description.SystemGeneratorContext, description.Location, param.Name);
                    description.Success = false;
                }
            }

            foreach (var param in description.ExecuteMethodParamDescriptions.OfType<ISharedComponentParamDescription>().Where(param => param.IsByRef()))
            {
                LambdaJobsErrors.DC0020(description.SystemGeneratorContext, description.Location, param.TypeSymbol.Name);
                description.Success = false;
            }

            foreach (var param in description.ExecuteMethodParamDescriptions.OfType<IDynamicBufferParamDescription>())
            {
                if (param.TypeSymbol is INamedTypeSymbol namedTypeSymbol)
                {
                    foreach (var typeArg in namedTypeSymbol.TypeArguments.OfType<INamedTypeSymbol>().Where(
                        typeArg => typeArg is INamedTypeSymbol namedTypeArg && namedTypeArg.TypeArguments.Any()))
                    {
                        LambdaJobsErrors.DC0050(description.SystemGeneratorContext, description.Location, typeArg.Name);
                        description.Success = false;
                    }
                }
            }

            description.Success &= VerifyWithTypes(description, description.WithAllTypes, "WithAll");
            description.Success &= VerifyWithTypes(description, description.WithAnyTypes, "WithAny");
            description.Success &= VerifyWithTypes(description, description.WithNoneTypes, "WithNone");

            // Check WithNone for conflicting types
            description.Success &= VerifyConflictingTypes(description, description.WithNoneTypes, description.WithAllTypes, "WithNone", "WithAll");
            description.Success &= VerifyConflictingTypes(description, description.WithNoneTypes, description.WithAnyTypes, "WithNone", "WithAny");
            description.Success &= VerifyConflictingTypes(
                description,
                description.WithNoneTypes,
                description.ExecuteMethodParamDescriptions.Select(param => (INamedTypeSymbol)param.TypeSymbol).ToList(),
                "WithNone",
                "lambda parameter");

            // Check WithAny for conflicting types
            description.Success &= VerifyConflictingTypes(description, description.WithAnyTypes, description.WithAllTypes, "WithAny", "WithAll");
            description.Success &= VerifyConflictingTypes(
                description,
                description.WithAnyTypes,
                description.ExecuteMethodParamDescriptions.Select(param => (INamedTypeSymbol)param.TypeSymbol).ToList(),
                "WithAny",
                "lambda parameter");

            var containingSystemTypeSymbol = (INamedTypeSymbol)description.SemanticModel.GetDeclaredSymbol(description.DeclaringSystemType);
            if (containingSystemTypeSymbol.GetMembers().OfType<IMethodSymbol>().Any(method => method.Name == "OnCreateForCompiler"))
            {
                LambdaJobsErrors.DC0025(description.SystemGeneratorContext, description.Location, containingSystemTypeSymbol.Name);
                description.Success = false;
            }

            if (containingSystemTypeSymbol.TypeParameters.Any())
            {
                LambdaJobsErrors.DC0053(description.SystemGeneratorContext, description.Location, containingSystemTypeSymbol.Name);
                description.Success = false;
            }

            var containingMethodSymbol = description.SemanticModel.GetDeclaredSymbol(description.ContainingMethod);
            if (containingMethodSymbol is IMethodSymbol methodSymbol && methodSymbol.TypeArguments.Any())
            {
                LambdaJobsErrors.DC0054(description.SystemGeneratorContext, description.Location, methodSymbol.Name);
                description.Success = false;
            }

            return success;
        }

        static bool VerifyWithTypes(LambdaJobDescription description, IEnumerable<INamedTypeSymbol> withTypes, string methodName)
        {
            var success = true;
            foreach (var withType in withTypes)
            {
                if (!(withType.InheritsFromInterface("Unity.Entities.IComponentData") ||
                      withType.InheritsFromInterface("Unity.Entities.ISharedComponentData") ||
                      withType.InheritsFromInterface("Unity.Entities.IBufferElementData") ||
                      withType.Is("UnityEngine.Object")))
                {
                    LambdaJobsErrors.DC0052(description.SystemGeneratorContext, description.Location, withType.Name, methodName);
                    success = false;
                }

                if (description.WithSharedComponentFilterTypes.Any(sharedComponentFilterType=>sharedComponentFilterType.ToFullName() == withType.ToFullName()))
                {
                    LambdaJobsErrors.DC0026(description.SystemGeneratorContext, description.Location, withType.ToFullName());
                    description.Success = false;
                }
            }

            return success;
        }

        static bool VerifyConflictingTypes(LambdaJobDescription description,
            IEnumerable<INamedTypeSymbol> typeGroup1, IReadOnlyCollection<INamedTypeSymbol> typeGroup2, string typeGroup1Name, string typeGroup2Name)
        {
            var success = true;

            foreach (var matchingType in typeGroup1.Where(x => typeGroup2.Any(y => x.ToFullName() == y.ToFullName())))
            {
                LambdaJobsErrors.DC0056(description.SystemGeneratorContext, description.Location, typeGroup1Name, typeGroup2Name, matchingType.Name);
                success = false;
            }

            return success;
        }

        static bool VerifyDC0073(LambdaJobDescription description)
        {
            if ( description.WithScheduleGranularityArgumentSyntaxes.Count > 0
                && description.Schedule.Mode != ScheduleMode.ScheduleParallel)
            {
                LambdaJobsErrors.DC0073(description.SystemGeneratorContext, description.Location);
                return false;
            }
            return true;
        }
    }
}
