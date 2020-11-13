using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    enum LambdaJobKind
    {
        Entities,
        Job
    }

    enum ContainingSystemType
    {
        JobComponentSystem,
        SystemBase
    }

    enum ScheduleMode
    {
        Schedule,
        ScheduleParallel,
        Run
    }

    enum FloatMode
    {
        Default,
        Strict,
        Deterministic,
        Fast,
    }

    enum FloatPrecision
    {
        Standard,
        High,
        Medium,
        Low,
    }

    [Flags]
    public enum EntityQueryOptions
    {
        Default = 0,
        IncludePrefab = 1,
        IncludeDisabled = 2,
        FilterWriteGroup = 4,
    }

    class LambdaJobDescription
    {
        public ClassDeclarationSyntax DeclaringType { get; private set; }
        public MethodDeclarationSyntax ContainingMethod { get; private set; }
        public InvocationExpressionSyntax ContainingInvocationExpression { get; private set; }
        internal ParenthesizedLambdaExpressionSyntax OriginalLambdaExpression;
        public readonly List<LambdaCapturedVariableDescription> VariablesCaptured = new List<LambdaCapturedVariableDescription>();
        public readonly List<LambdaCapturedVariableDescription> VariablesCapturedOnlyByLocals = new List<LambdaCapturedVariableDescription>();
        public readonly List<LambdaCapturedVariableDescription> DisposeOnJobCompletionVariables = new List<LambdaCapturedVariableDescription>();

        public string Name { get; internal set; }
        public string QueryName { get => $"{Name}_Query"; }
        public string JobStructName { get => $"{Name}_Job"; }
        public string ExecuteInSystemMethodName { get => $"{Name}_Execute"; }
        public string LambdaBodyMethodName { get => $"{Name}_LambdaBody"; }
        bool CanContainReferenceTypes { get => !UsesBurst && ScheduleMode == ScheduleMode.Run; }
        internal bool NeedsJobDelegateFields { get => ScheduleMode == ScheduleMode.Run && (UsesBurst || LambdaJobKind == LambdaJobKind.Job); }
        internal bool NeedsEntityInQueryIndex { get => LambdaParameters.OfType<LambdaParamDescription_EntityInQueryIndex>().Any(); }

        public LambdaJobKind LambdaJobKind { get; private set; }
        public ContainingSystemType ContainingSystemType  { get; private set; }
        public ScheduleMode ScheduleMode { get; private set; }
        public bool UsesBurst { get; private set; }
        public FloatMode FloatMode { get; private set; }
        public FloatPrecision FloatPrecision { get; private set; }
        public EntityQueryOptions EntityQueryOptions { get; private set; }
        public bool BurstSynchronousCompilation { get; private set; }
        public bool WithStructuralChanges { get; private set; }
        public bool WithStructuralChangesAndLambdaBodyInSystem { get; private set; }
        public ArgumentSyntax DependencyArgument { get; private set; }
        public List<LambdaParamDescription> LambdaParameters { get; }
        public List<INamedTypeSymbol> WithAllTypes { get; private set; }
        public List<INamedTypeSymbol> WithAnyTypes { get; private set; }
        public List<INamedTypeSymbol> WithNoneTypes { get; private set; }
        public List<INamedTypeSymbol> WithChangeFilterTypes { get; private set; }
        public List<INamedTypeSymbol> WithSharedComponentFilterTypes { get; private set; }
        public List<ArgumentSyntax> WithSharedComponentFilterArgumentSyntaxes { get; private set; }
        public List<ArgumentSyntax> WithStoreEntityQueryInFieldArgumentSyntaxes { get; private set; }
        public ArgumentSyntax       WithFilter_EntityArray { get; private set; }
        Dictionary<string, List<InvocationExpressionSyntax>> MethodInvocations { get; set; }
        public SemanticModel Model { get; private set; }
        public GeneratorExecutionContext Context { get; private set; }
#if !GENERIC_ENTITIES_FOREACH_SUPPORT
        public bool HasGenericParameters { get => false; }
#else
        public bool HasGenericParameters { get => LambdaParameters.OfType<LambdaParamDescription_Generic>().Any(); }
#endif
        public bool HasManagedParameters { get => LambdaParameters.OfType<LambdaParamDescription_ManagedComponent>().Any(); }
        public bool HasSharedComponentParameters { get => LambdaParameters.OfType<LambdaParamDescription_SharedComponent>().Any(); }

        public BlockSyntax RewrittenLambdaBody { get; private set; }
        public List<DataFromEntityFieldDescription> AdditionalFields { get; private set; }
        public List<MethodDeclarationSyntax> MethodsForLocalFunctions { get; private set; }

        public LambdaJobDescription()
        {
            UsesBurst = true;
            FloatMode = FloatMode.Default;
            FloatPrecision = FloatPrecision.Standard;
            EntityQueryOptions = EntityQueryOptions.Default;
            BurstSynchronousCompilation = false;
            LambdaParameters = new List<LambdaParamDescription>();
            WithAllTypes = new List<INamedTypeSymbol>();
            WithAnyTypes = new List<INamedTypeSymbol>();
            WithChangeFilterTypes = new List<INamedTypeSymbol>();
            WithSharedComponentFilterTypes = new List<INamedTypeSymbol>();
            WithSharedComponentFilterArgumentSyntaxes = new List<ArgumentSyntax>();
            WithStoreEntityQueryInFieldArgumentSyntaxes = new List<ArgumentSyntax>();
            AdditionalFields = new List<DataFromEntityFieldDescription>();
            MethodsForLocalFunctions = new List<MethodDeclarationSyntax>();
        }

        List<INamedTypeSymbol> AllTypeArgumentSymbolsOfMethod(string methodName)
        {
            var result = new List<INamedTypeSymbol>();
            if (!MethodInvocations.ContainsKey(methodName))
                return result;

            foreach (var methodInvocation in MethodInvocations[methodName])
            {
                var symbol = (IMethodSymbol)ModelExtensions.GetSymbolInfo(Model, methodInvocation).Symbol;
                foreach (var argumentType in symbol.TypeArguments.OfType<INamedTypeSymbol>())
                {
                    if (argumentType.IsGenericType)
                        Context.LogError("DC0025", "WithNameNotWithLiteral", $"Type {argumentType.Name} cannot be used with {methodName} as generic types and parameters are not allowed", methodInvocation.GetLocation());
                    // TODO: make sure argument type is valid for lambda provider type
                    //if (!LambdaParamaterValueProviderInformation.IsTypeValidForEntityQuery(argumentTypeDefinition))
                    //    UserError.DC0025($"Type {argumentType.Name} cannot be used with {m.MethodName} as it is not a supported component type", descriptionConstruction.ContainingMethod, m.InstructionInvokingMethod).Throw();
                    result.Add(argumentType);
                }
            }
            return result;
        }

        List<ArgumentSyntax> AllArgumentSyntaxesOfMethod(string methodName)
        {
            var result = new List<ArgumentSyntax>();
            if (!MethodInvocations.TryGetValue(methodName, out var invocations))
                return result;

            foreach (var methodInvocation in invocations)
                result.AddRange(methodInvocation.ArgumentList.Arguments);
            return result;
        }

        ArgumentSyntax SingleOptionalArgumentSyntaxOfMethod(string methodName)
        {
            if (!MethodInvocations.TryGetValue(methodName, out var invocations))
                return null;

            foreach (var methodInvocation in invocations)
            {
                var res = methodInvocation.ArgumentList.Arguments.First();
                if (res != null)
                    return res;
            }

            return null;
        }

        public static LambdaJobDescription From(IdentifierNameSyntax node, GeneratorExecutionContext context, int lambdaJobIndexInSystem)
        {
            var declaringType = node.Ancestors().OfType<ClassDeclarationSyntax>().First();
            LambdaJobDescription result = new LambdaJobDescription()
            {
                Name = $"{declaringType.Identifier}_LambdaJob_{lambdaJobIndexInSystem}",
                Context = context,
                Model = context.Compilation.GetSemanticModel(node.SyntaxTree),
                DeclaringType = declaringType,
                ContainingMethod = node.Ancestors().OfType<MethodDeclarationSyntax>().First(),
                MethodInvocations = node.GetMethodInvocations(),
                LambdaJobKind = node.Identifier.ValueText == "Job" ? LambdaJobKind.Job : LambdaJobKind.Entities
            };

            // Quick check to ensure this is a valid Entities.ForEach or Job.WithCode
            if ((result.LambdaJobKind == LambdaJobKind.Entities && !result.MethodInvocations.ContainsKey("ForEach")) ||
                (result.LambdaJobKind == LambdaJobKind.Job && !result.MethodInvocations.ContainsKey("WithCode")))
                return null;

            var candidateContainingTypeSymbol = result.Model.GetSymbolInfo(node).Symbol?.ContainingType;
            if (candidateContainingTypeSymbol.Is("Unity.Entities.SystemBase"))
                result.ContainingSystemType = ContainingSystemType.SystemBase;
            else if (candidateContainingTypeSymbol.Is("Unity.Entities.JobComponentSystem"))
                result.ContainingSystemType = ContainingSystemType.JobComponentSystem;
            else
                throw new InvalidOperationException($"Invalid system type for lambda job {candidateContainingTypeSymbol.ToFullName()}");

            if (result.MethodInvocations.ContainsKey("Schedule"))
            {
                result.ScheduleMode = ScheduleMode.Schedule;
                result.DependencyArgument = result.MethodInvocations["Schedule"].First().ArgumentList.Arguments.FirstOrDefault();
            }
            else if (result.MethodInvocations.ContainsKey("ScheduleParallel"))
            {
                result.ScheduleMode = ScheduleMode.ScheduleParallel;
                result.DependencyArgument = result.MethodInvocations["ScheduleParallel"].First().ArgumentList.Arguments.FirstOrDefault();
            }
            else if (result.MethodInvocations.ContainsKey("Run"))
                result.ScheduleMode = ScheduleMode.Run;
            else
                context.LogError("SG0001", "LambdaJob", "Could not parse scheduling mode from lambda job invocations", node.GetLocation());

            result.ContainingInvocationExpression = result.MethodInvocations[result.ScheduleMode.ToString()].FirstOrDefault();

            if (result.MethodInvocations.ContainsKey("WithName"))
            {
                var invocation = result.MethodInvocations["WithName"].First();
                var literalArgument = invocation.ArgumentList.Arguments.FirstOrDefault()?.DescendantNodes().OfType<LiteralExpressionSyntax>().FirstOrDefault();
                if (literalArgument != null)
                    result.Name = literalArgument.Token.ValueText;
                else
                    context.LogError("SG0001", "WithNameNotWithLiteral", "WithName must be used with a string literal as an argument", invocation.GetLocation());
            }

            if (result.MethodInvocations.ContainsKey("WithBurst"))
            {
                result.UsesBurst = true;

                var invocation = result.MethodInvocations["WithBurst"].First();

                // handle both named and unnamed arguments
                var argIndex = 0;
                foreach (var argument in invocation.ArgumentList.Arguments)
                {
                    var argumentName = argument.DescendantNodes().OfType<NameColonSyntax>().FirstOrDefault()?.Name;
                    if (argumentName != null)
                    {
                        if (argumentName.Identifier.ValueText.Contains("floatMode"))
                            argIndex = 0;
                        else if (argumentName.Identifier.ValueText.Contains("floatPrecision"))
                            argIndex = 1;
                        else if (argumentName.Identifier.ValueText.Contains("synchronousCompilation"))
                            argIndex = 2;
                    }

                    var argValue = argument.Expression.ToString();
                    switch (argIndex)
                    {
                        case 0:
                            if (SourceGenHelpers.TryParseQualifiedEnumValue(argValue, out FloatMode floatMode))
                                result.FloatMode = floatMode;
                            else
                                context.LogError("SG0001", "WithBurst", "WithBurst floatMode argument must be used with an enum values", invocation.GetLocation());
                            break;

                        case 1:
                            if (SourceGenHelpers.TryParseQualifiedEnumValue(argValue, out FloatPrecision floatPrecision))
                                result.FloatPrecision = floatPrecision;
                            else
                                context.LogError("SG0001", "WithBurst", "WithBurst floatPrecision argument must be used with an enum values", invocation.GetLocation());
                            break;

                        case 2:
                            if (bool.TryParse(argValue, out bool synchronousCompilation))
                                result.BurstSynchronousCompilation = synchronousCompilation;
                            else
                                context.LogError("SG0001", "WithBurst", "WithBurst synchronousCompilation argument must be used with a boolean literal", invocation.GetLocation());
                            break;
                    }

                    argIndex++;
                }
            }
            else if (result.MethodInvocations.ContainsKey("WithStructuralChanges"))
            {
                result.UsesBurst = false;
                result.WithStructuralChanges = true;
            }
            else if (result.MethodInvocations.ContainsKey("WithoutBurst"))
                result.UsesBurst = false;

            if (result.MethodInvocations.TryGetValue("WithEntityQueryOptions", out var withEntityQueryOptionsInvocations))
            {
                foreach (var invocation in withEntityQueryOptionsInvocations)
                {
                    var entityQueryOptionArgument = invocation.ArgumentList.Arguments.ElementAtOrDefault(0);
                    if (entityQueryOptionArgument != null)
                    {
                        if (SourceGenHelpers.TryParseQualifiedEnumValue(entityQueryOptionArgument.ToString(), out EntityQueryOptions option))
                            result.EntityQueryOptions |= option;
                        else
                            context.LogError("SG0001", "WithEntityQueryOptions", "WithEntityQueryOptions must be used with a EntityQueryOption value as the argument", invocation.GetLocation());
                    }
                }
            }

            // Parse non-lambda param EntityQuery parameters
            result.WithAllTypes = result.AllTypeArgumentSymbolsOfMethod("WithAll");
            result.WithAnyTypes = result.AllTypeArgumentSymbolsOfMethod("WithAny");
            result.WithNoneTypes = result.AllTypeArgumentSymbolsOfMethod("WithNone");
            result.WithChangeFilterTypes = result.AllTypeArgumentSymbolsOfMethod("WithChangeFilter");
            result.WithSharedComponentFilterTypes = result.AllTypeArgumentSymbolsOfMethod("WithSharedComponentFilter");
            result.WithSharedComponentFilterArgumentSyntaxes = result.AllArgumentSyntaxesOfMethod("WithSharedComponentFilter");
            result.WithStoreEntityQueryInFieldArgumentSyntaxes = result.AllArgumentSyntaxesOfMethod("WithStoreEntityQueryInField");
            result.WithFilter_EntityArray = result.SingleOptionalArgumentSyntaxOfMethod("WithFilter");

            // Parse lambda arguments and lambda body
            InvocationExpressionSyntax methodInvocationWithLambdaExpression = null;
            if (result.LambdaJobKind == LambdaJobKind.Entities)
                methodInvocationWithLambdaExpression = result.MethodInvocations["ForEach"].FirstOrDefault();
            if (result.LambdaJobKind == LambdaJobKind.Job)
                methodInvocationWithLambdaExpression = result.MethodInvocations["WithCode"].FirstOrDefault();

            if (methodInvocationWithLambdaExpression != null)
            {
                var lambdaExpression = methodInvocationWithLambdaExpression.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>().FirstOrDefault();
                var parameterList = lambdaExpression?.DescendantNodes().OfType<ParameterListSyntax>().FirstOrDefault();
                var parameters = parameterList?.DescendantNodes().OfType<ParameterSyntax>();
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        var symbol = (IParameterSymbol)ModelExtensions.GetDeclaredSymbol(result.Model, param);
                        result.LambdaParameters.Add(LambdaParamDescription.From(param, symbol));
                    }
                }

                result.OriginalLambdaExpression = lambdaExpression;

                // Discover captured variables
                // this must not include parameters as they can be captured by inner lambdas
                // or variables declared inside of lambda (marked as captured if they are used by local methods inside lambda)
                DataFlowAnalysis dataFlowAnalysis = result.Model.AnalyzeDataFlow(lambdaExpression);
                if (dataFlowAnalysis.Succeeded)
                {
                    foreach (var capturedVar in dataFlowAnalysis.CapturedInside)
                    {
                        if (result.LambdaParameters.All(param => param.Symbol.Name != capturedVar.Name))
                        {
                            if (dataFlowAnalysis.VariablesDeclared.Contains(capturedVar))
                                result.VariablesCapturedOnlyByLocals.Add(new LambdaCapturedVariableDescription(capturedVar));
                            else
                                result.VariablesCaptured.Add(new LambdaCapturedVariableDescription(capturedVar));
                        }
                    }
                }
                result.WithStructuralChangesAndLambdaBodyInSystem = result.WithStructuralChanges && result.VariablesCaptured.All(variable => variable.IsThis);

                // If we are also using any managed components or doing structural changes, we also need to capture this
                if ((result.HasManagedParameters || result.HasSharedComponentParameters || result.WithStructuralChanges)
                    && result.VariablesCaptured.All(variable => !variable.IsThis))
                {
                    var thisSymbol = result.Model.GetDeclaredSymbol(result.DeclaringType);
                    result.VariablesCaptured.Add(new LambdaCapturedVariableDescription(thisSymbol, true));
                }
            }

            // Apply attributes to captured variables
            foreach (var entityAttribute in LambdaCapturedVariableDescription.AttributesDescriptions)
            {
                foreach (var argumentSyntax in result.AllArgumentSyntaxesOfMethod(entityAttribute.MethodName))
                {
                    var expression = argumentSyntax.Expression;
                    if (expression is IdentifierNameSyntax identifier)
                    {
                        var capturedVariable = result.VariablesCaptured.FirstOrDefault(var => var.Symbol.Name == identifier.Identifier.Text);
                        if (capturedVariable != null && entityAttribute.CheckAttributeApplicable())
                            capturedVariable.Attributes.Add(entityAttribute.AttributeName);
                        // We don't current error-out if you try to apply an attribute (except `WithDisposeOnCompletion`) to a variable that isn't capture.
                        // Perhaps we should, but commenting out for now to ensure compatibility with current Entities.ForEach code.
                        /*
                        else
                            context.LogError("SGICE002", entityAttribute.MethodName, $"Cannot find captured variable {identifier.Identifier.Text} for {entityAttribute.MethodName}", argumentSyntax.GetLocation());
                        */
                    }
                    else
                        context.LogError("SGICE003", entityAttribute.MethodName, $"{entityAttribute.MethodName} must be used with a local identifier", argumentSyntax.GetLocation());
                }
            }

            // Either add DeallocateOnJobCompletion attributes to variables or add to list of variables that need to be disposed
            // (depending of if they support DeallocateOnJobCompletion and if we are running as a job)
            foreach (var argumentSyntax in result.AllArgumentSyntaxesOfMethod("WithDisposeOnCompletion"))
            {
                var expression = argumentSyntax.Expression;
                if (expression is IdentifierNameSyntax identifier)
                {
                    var capturedVariable = result.VariablesCaptured.FirstOrDefault(var => var.Symbol.Name == identifier.Identifier.Text);
                    if (capturedVariable != null)
                    {
                        if (result.ScheduleMode != ScheduleMode.Run && capturedVariable.SupportsDeallocateOnJobCompletion())
                            capturedVariable.Attributes.Add("DeallocateOnJobCompletion");
                        else
                            result.DisposeOnJobCompletionVariables.Add(capturedVariable);
                    }
                    else
                        context.LogError("SGICE002", "WithDisposeOnCompletion", $"Cannot find captured variable {identifier.Identifier.Text} for WithDisposeOnCompletion", argumentSyntax.GetLocation());
                }
                else
                    context.LogError("SGICE003", "WithDisposeOnCompletion", $"WithDisposeOnCompletion must be used with a local identifier", argumentSyntax.GetLocation());
            }

            // Rewrite lambda body and get additional fields that are needed if lambda body is not emitted into system
            if (result.WithStructuralChangesAndLambdaBodyInSystem)
                result.RewrittenLambdaBody = result.OriginalLambdaExpression.ToBlockSyntax();
            else
            {
                SyntaxNode rewrittenLambdaExpression;
                (rewrittenLambdaExpression, result.AdditionalFields, result.MethodsForLocalFunctions) = LambdaBodyRewriter.Rewrite(result);
                result.RewrittenLambdaBody = ((ParenthesizedLambdaExpressionSyntax)rewrittenLambdaExpression).ToBlockSyntax();
            }

            // Check to see if we have any references to __this in our rewritten lambda body and we can't contain reference types
            // if there is none remove the capture this reference
            if (!result.CanContainReferenceTypes &&
                result.RewrittenLambdaBody.DescendantNodes().OfType<SimpleNameSyntax>().All(syntax => syntax.Identifier.ToString() != "__this"))
                result.VariablesCaptured.RemoveAll(variable => variable.IsThis);

            // Also need to make sure we reference this if we are emitting lambda back into the system
            if (result.WithStructuralChangesAndLambdaBodyInSystem && result.VariablesCaptured.All(variable => !variable.IsThis))
            {
                var thisSymbol = result.Model.GetDeclaredSymbol(result.DeclaringType);
                result.VariablesCaptured.Add(new LambdaCapturedVariableDescription(thisSymbol, true));
            }

            // TODO: throw compile errors on malformed descriptions here!
            return result;
        }
    }
}
