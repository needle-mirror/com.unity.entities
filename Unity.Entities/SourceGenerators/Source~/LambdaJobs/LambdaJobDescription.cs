using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGenerator;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    public class LambdaJobDescription
    {
        public struct BurstSettings
        {
            public BurstFloatMode BurstFloatMode;
            public BurstFloatPrecision BurstFloatPrecision;
            public bool SynchronousCompilation;
        }

        public SemanticModel SemanticModel { get; }
        public SyntaxNode SyntaxNode { get; }

        public ISymbol SystemTypeSymbol { get; }
        public SystemType SystemType { get; }
        public string SystemStateParameterName { get; }
        public bool InStructSystem { get => SystemType == SystemType.ISystem; }
        public bool IsInSystemBase { get => SystemType == SystemType.SystemBase || SystemType == SystemType.ISystem; }

        public SystemGeneratorContext SystemGeneratorContext { get; }
        public TypeDeclarationSyntax DeclaringSystemType { get ; }
        public MethodDeclarationSyntax ContainingMethod { get; }
        public InvocationExpressionSyntax ContainingInvocationExpression { get; }
        public Dictionary<string, List<InvocationExpressionSyntax>> MethodInvocations { get; }
        public (bool IsEnabled, BurstSettings Settings) Burst { get; }
        public (ScheduleMode Mode, ArgumentSyntax DependencyArgument) Schedule { get; }

        public List<ArgumentSyntax> WithStoreEntityQueryInFieldArgumentSyntaxes { get; }
        public List<INamedTypeSymbol> WithAllTypes { get; }
        public List<INamedTypeSymbol> WithNoneTypes { get ; }
        public List<INamedTypeSymbol> WithAnyTypes { get ; }
        public List<INamedTypeSymbol> WithChangeFilterTypes { get ; }
        public List<INamedTypeSymbol> WithSharedComponentFilterTypes { get ; }
        public List<ArgumentSyntax> WithSharedComponentFilterArgumentSyntaxes { get; }
        public List<ArgumentSyntax> WithScheduleGranularityArgumentSyntaxes { get; }
        public Location Location { get; }
        public EntityQueryOptions EntityQueryOptions { get; }

        public string Name { get; }
        public bool Success { get; internal set; }

        public string EntityQueryFieldName => $"{Name}_Query";
        public string ExecuteInSystemMethodName => $"{Name}_Execute";

        bool CanContainReferenceTypes => !Burst.IsEnabled && Schedule.Mode == ScheduleMode.Run;

        internal readonly List<LambdaParamDescription> ExecuteMethodParamDescriptions = new List<LambdaParamDescription>();

        // Only throw this exception if the parsing cannot continue, in most cases we should try to continue and collect all valid errors
        protected internal class InvalidDescriptionException : Exception { }

        public ParenthesizedLambdaExpressionSyntax OriginalLambdaExpression;

        public readonly List<LambdaCapturedVariableDescription> VariablesCaptured = new List<LambdaCapturedVariableDescription>();
        public readonly List<LambdaCapturedVariableDescription> VariablesCapturedOnlyByLocals = new List<LambdaCapturedVariableDescription>();
        public readonly List<LambdaCapturedVariableDescription> DisposeOnJobCompletionVariables = new List<LambdaCapturedVariableDescription>();
        public readonly List<(string Name, ISymbol Symbol)> AdditionalVariablesCapturedForScheduling = new List<(string, ISymbol)>();

        public readonly bool WithStructuralChanges;
        public readonly LambdaJobKind LambdaJobKind;

        public readonly ArgumentSyntax WithFilterEntityArray;

        internal readonly List<DataFromEntityFieldDescription> AdditionalFields;
        public readonly List<MethodDeclarationSyntax> MethodsForLocalFunctions;
        public readonly BlockSyntax RewrittenLambdaBody;

        public readonly bool WithStructuralChangesAndLambdaBodyInSystem;
        internal readonly List<LambdaParamDescription> LambdaParameters;
        public readonly List<(LocalFunctionStatementSyntax localFunction, bool onlyUsedInLambda)> LocalFunctionUsedInLambda = new List<(LocalFunctionStatementSyntax, bool)>();

        public string JobStructName => $"{Name}_Job";
        public string LambdaBodyMethodName => $"{Name}_LambdaBody";
        public bool NeedsJobFunctionPointers => Schedule.Mode == ScheduleMode.Run && (Burst.IsEnabled || LambdaJobKind == LambdaJobKind.Job);
        public bool NeedsEntityInQueryIndex => LambdaParameters.OfType<LambdaParamDescription_EntityInQueryIndex>().Any();
        public bool IsForDOTSRuntime => SystemGeneratorContext.PreprocessorSymbolNames.Contains("UNITY_DOTSRUNTIME");
        public bool SafetyChecksEnabled => SystemGeneratorContext.PreprocessorSymbolNames.Contains("ENABLE_UNITY_COLLECTIONS_CHECKS");
        public bool DOTSRuntimeProfilerEnabled => SystemGeneratorContext.PreprocessorSymbolNames.Contains("ENABLE_DOTSRUNTIME_PROFILER");
        public bool NeedsUnsafe => ContainingMethod.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.UnsafeKeyword)) ||
                                   OriginalLambdaExpression.Ancestors().OfType<UnsafeStatementSyntax>().Any();

        bool HasManagedParameters => LambdaParameters.OfType<LambdaParamDescription_ManagedComponent>().Any();
        bool HasSharedComponentParameters => LambdaParameters.OfType<LambdaParamDescription_SharedComponent>().Any();

        public bool IsForEditor => SystemGeneratorContext.PreprocessorSymbolNames.Contains("UNITY_EDITOR");
        public bool DevelopmentBuildEnabled => SystemGeneratorContext.PreprocessorSymbolNames.Contains("DEVELOPMENT_BUILD");
        public bool HasJournalingEnabled => (IsForEditor || DevelopmentBuildEnabled) && !SystemGeneratorContext.PreprocessorSymbolNames.Contains("DISABLE_ENTITIES_JOURNALING");
        public bool HasJournalingRecordableParameters => HasJournalingRecordableChunkParameters || HasJournalingRecordableEntityParameters;
        public bool HasJournalingRecordableChunkParameters => !WithStructuralChanges && LambdaParameters.Any(p => !string.IsNullOrEmpty(p.EntitiesJournaling_RecordChunkSetComponent()));
        public bool HasJournalingRecordableEntityParameters => WithStructuralChanges && LambdaParameters.Any(p => !string.IsNullOrEmpty(p.EntitiesJournaling_RecordEntitySetComponent()));

        public LambdaJobDescription(
            SystemGeneratorContext systemGeneratorContext,
            LambdaJobsCandidate candidate,
            TypeDeclarationSyntax declaringType,
            MethodDeclarationSyntax containingMethod,
            SemanticModel semanticModel,
            int id)
        {
            try
            {
                Success = true;
                SystemGeneratorContext = systemGeneratorContext;
                SyntaxNode = candidate.SyntaxNode;
                Location = SyntaxNode.GetLocation();
                SemanticModel = semanticModel;

                DeclaringSystemType = declaringType;
                SystemTypeSymbol = SemanticModel.GetDeclaredSymbol(DeclaringSystemType);
                ContainingMethod = containingMethod;

                MethodInvocations = candidate.MethodInvocations;

                Burst = GetBurstSettings();
                Schedule = GetScheduleModeAndDependencyArgument();

                ContainingInvocationExpression = MethodInvocations[Schedule.Mode.ToString()].FirstOrDefault();

                WithAllTypes = AllTypeArgumentSymbolsOfMethod("WithAll");
                WithAnyTypes = AllTypeArgumentSymbolsOfMethod("WithAny");
                WithNoneTypes = AllTypeArgumentSymbolsOfMethod("WithNone");

                WithChangeFilterTypes = AllTypeArgumentSymbolsOfMethod("WithChangeFilter");
                WithSharedComponentFilterTypes = AllTypeArgumentSymbolsOfMethod("WithSharedComponentFilter");
                WithSharedComponentFilterArgumentSyntaxes = AllArgumentSyntaxesOfMethod("WithSharedComponentFilter");
                WithStoreEntityQueryInFieldArgumentSyntaxes = AllArgumentSyntaxesOfMethod("WithStoreEntityQueryInField");
                WithScheduleGranularityArgumentSyntaxes = AllArgumentSyntaxesOfMethod("WithScheduleGranularity");

                if (SystemTypeSymbol is INamedTypeSymbol namedSystemTypeSymbol)
                {
                    if (namedSystemTypeSymbol.Is("Unity.Entities.SystemBase"))
                        SystemType = SystemType.SystemBase;
                    else if (namedSystemTypeSymbol.InheritsFromInterface("Unity.Entities.ISystem"))
                    {
                        SystemType = SystemType.ISystem;

                        // Make sure we our top-level MemberAccessExpressionSyntax is not "Entities" (we should be accessing via a SystemState parameter)
                        if (!(SyntaxNode.Parent is MemberAccessExpressionSyntax parentMemberAccessExpression) ||
                            parentMemberAccessExpression.Expression.ToString() == "Entities")
                        {
                            LambdaJobsErrors.DC0072(SystemGeneratorContext, SyntaxNode.GetLocation());
                            Success = false;
                            throw new InvalidDescriptionException();
                        }

                        // Get name of SystemState parameter, needed for further codegen as we need to reuse this parameter
                        SystemStateParameterName = parentMemberAccessExpression.Expression.ToString();

                        // Do quick check here for invocations not permitted in ISystem and error out if so
                        var invalidISystemInvocations = new[] {"WithoutBurst", "WithSharedComponentFilter", "WithStructuralChanges"};
                        var invalidInvocationFound = false;
                        foreach (var invalidInvocation in MethodInvocations.Where(invocation => invalidISystemInvocations.Contains(invocation.Key)))
                        {
                            LambdaJobsErrors.DC0071(SystemGeneratorContext, SyntaxNode.GetLocation(), invalidInvocation.Key);
                            invalidInvocationFound = true;
                        }
                        if (invalidInvocationFound)
                        {
                            Success = false;
                            throw new InvalidDescriptionException();
                        }
                    }
                    else
                        throw new InvalidOperationException($"Invalid system type for lambda job {namedSystemTypeSymbol.ToFullName()}");
                }
                else
                    throw new InvalidOperationException($"Unable to find system type for {DeclaringSystemType}");

                EntityQueryOptions = GetEntityQueryOptions();

                (bool IsEnabled, BurstSettings Settings) GetBurstSettings()
                {
                    if (MethodInvocations.ContainsKey("WithoutBurst") || MethodInvocations.ContainsKey("WithStructuralChanges"))
                        return (false, default);

                    if (!MethodInvocations.TryGetValue("WithBurst", out var burstInvocations))
                        return (true, default);

                    var burstFloatMode = BurstFloatMode.Default;
                    var burstFloatPrecision = BurstFloatPrecision.Standard;
                    var synchronousCompilation = false;
                    var invocation = burstInvocations.First();

                    // handle both named and unnamed arguments
                    var argIndex = 0;
                    var invalidBurstArg = false;
                    foreach (var argument in invocation.ArgumentList.Arguments)
                    {
                        var argumentName = argument.DescendantNodes().OfType<NameColonSyntax>().FirstOrDefault()?.Name;
                        if (argumentName != null)
                        {
                            argIndex = argumentName.Identifier.ValueText switch
                            {
                                "floatMode" => 0,
                                "floatPrecision" => 1,
                                "synchronousCompilation" => 2,
                                _ => argIndex
                            };
                        }

                        var argValue = argument.Expression.ToString();
                        switch (argIndex)
                        {
                            case 0:
                                if (SourceGenHelpers.TryParseQualifiedEnumValue(argValue, out BurstFloatMode mode))
                                    burstFloatMode = mode;
                                else
                                    invalidBurstArg = true;
                                break;
                            case 1:
                                if (SourceGenHelpers.TryParseQualifiedEnumValue(argValue, out BurstFloatPrecision precision))
                                    burstFloatPrecision = precision;
                                else
                                    invalidBurstArg = true;
                                break;
                            case 2:
                                if (bool.TryParse(argValue, out var synchronous))
                                    synchronousCompilation = synchronous;
                                else
                                    invalidBurstArg = true;
                                break;
                        }

                        argIndex++;
                    }

                    if (invalidBurstArg)
                    {
                        LambdaJobsErrors.DC0008(SystemGeneratorContext, invocation.GetLocation(), "WithBurst");
                        Success = false;
                    }

                    return
                        (true,
                            new BurstSettings
                            {
                                SynchronousCompilation = synchronousCompilation,
                                BurstFloatMode = burstFloatMode,
                                BurstFloatPrecision = burstFloatPrecision
                            });
                }

                EntityQueryOptions GetEntityQueryOptions()
                {
                    var options = EntityQueryOptions.Default;

                    if (!MethodInvocations.TryGetValue("WithEntityQueryOptions", out var invocations))
                    {
                        return options;
                    }

                    foreach (var invocation in invocations)
                    {
                        var entityQueryOptionArgument = invocation.ArgumentList.Arguments.ElementAtOrDefault(0);
                        if (entityQueryOptionArgument == null)
                        {
                            continue;
                        }

                        if (SourceGenHelpers.TryParseQualifiedEnumValue(entityQueryOptionArgument.ToString(), out EntityQueryOptions option))
                        {
                            options |= option;
                        }

                        else
                        {
                            // !!! Need a test for this error
                            SystemGeneratorErrors.DC0064(SystemGeneratorContext, invocation.GetLocation());
                        }
                    }
                    return options;
                }

                (ScheduleMode Mode, ArgumentSyntax Dependency) GetScheduleModeAndDependencyArgument()
                {
                    if (MethodInvocations.ContainsKey("Run"))
                        return (ScheduleMode.Run, default);
                    else if (MethodInvocations.TryGetValue("Schedule", out var schedulingInvocations))
                        return (ScheduleMode.Schedule, schedulingInvocations.First().ArgumentList.Arguments.SingleOrDefault());
                    else if (MethodInvocations.TryGetValue("ScheduleParallel", out var schedulingParallelInvocations))
                        return (ScheduleMode.ScheduleParallel, schedulingParallelInvocations.First().ArgumentList.Arguments.SingleOrDefault());
                    else
                    {
                        LambdaJobsErrors.DC0011(SystemGeneratorContext, Location);
                        Success = false;
                        throw new InvalidDescriptionException();
                    }
                }

                LambdaParameters = new List<LambdaParamDescription>();
                AdditionalFields = new List<DataFromEntityFieldDescription>();
                MethodsForLocalFunctions = new List<MethodDeclarationSyntax>();

                Name = GetName( $"{DeclaringSystemType.Identifier}_LambdaJob_{id}");
                LambdaJobKind = candidate.LambdaJobKind;
                WithStructuralChanges = MethodInvocations.ContainsKey("WithStructuralChanges");
                WithFilterEntityArray = SingleOptionalArgumentSyntaxOfMethod("WithFilter");

                var methodInvocationWithLambdaExpression = LambdaJobKind switch
                {
                    LambdaJobKind.Entities => MethodInvocations["ForEach"].FirstOrDefault(),
                    LambdaJobKind.Job => MethodInvocations["WithCode"].FirstOrDefault(),
                    _ => throw new InvalidOperationException("LambdaJob does not include required ForEach or WithCode invocation.")
                };

                // Check to see if the lambda expression is not valid, need to do this before we continue analyzing lambda
                var lambdaArgument = methodInvocationWithLambdaExpression.ArgumentList.Arguments.FirstOrDefault();
                if (lambdaArgument.Expression is ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpressionSyntax)
                {
                    OriginalLambdaExpression = parenthesizedLambdaExpressionSyntax;
                }
                else
                {
                    LambdaJobsErrors.DC0044(SystemGeneratorContext, Location);
                    throw new InvalidDescriptionException();
                }

                var parameterList = OriginalLambdaExpression?.DescendantNodes().OfType<ParameterListSyntax>().FirstOrDefault();
                var parameters = parameterList?.DescendantNodes().OfType<ParameterSyntax>();

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        var paramDescription = LambdaParamDescription.From(SystemGeneratorContext, param);
                        if (paramDescription != null)
                        {
                            paramDescription.JobName = Name;
                            LambdaParameters.Add(paramDescription);
                        }
                        else
                            Success = false;
                    }
                }

                ExecuteMethodParamDescriptions.AddRange(LambdaParameters);

				var lambdaSyntax = OriginalLambdaExpression.Block ?? (SyntaxNode) OriginalLambdaExpression.ExpressionBody;

				// Can early out of a lot of analysis if we are only dealing with identifiers that are lambda params
				// (no captured variables or additional method calls)
                var hasNonParameterIdentifier = lambdaSyntax.DescendantNodes().OfType<IdentifierNameSyntax>().Any(identifier =>
                    LambdaParameters.All(param => param.Name != identifier.Identifier.ToString()));

				if (hasNonParameterIdentifier)
				{
				    // Check to see if we have a local method declaration in our parent block, if so we will need to move that into the job struct
                    // (and capture variables that it uses)
                    var localFunctionStatementsInContainingMethodButNotLambda =
                        ContainingMethod.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Where(statement =>
                            !lambdaSyntax.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Contains(statement));
                    if (localFunctionStatementsInContainingMethodButNotLambda.Any())
                    {
                        var invocationsInLambda = lambdaSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>();
                        var invocationsInContainingMethodButNotLambda = ContainingMethod.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(invocation => !invocationsInLambda.Contains(invocation));

                        static bool InvocationsContainsInvocationOfMethod(IEnumerable<InvocationExpressionSyntax> invocations,  string methodName) =>
                            invocations.Any(invocation => invocation.Expression is IdentifierNameSyntax invocationMethodIdentifier &&
                                                          invocationMethodIdentifier.Identifier.ToString() == methodName);

                        foreach (var localFunction in localFunctionStatementsInContainingMethodButNotLambda)
                        {
                            if (InvocationsContainsInvocationOfMethod(invocationsInLambda, localFunction.Identifier.ToString()))
                            {
                                var onlyUsedInLambda = !InvocationsContainsInvocationOfMethod(invocationsInContainingMethodButNotLambda, localFunction.Identifier.ToString());
                                LocalFunctionUsedInLambda.Add((localFunction, onlyUsedInLambda));
                            }
                        }
                    }

                    // Discover captured variables
                    // this must not include parameters as they can be captured by inner lambdas
                    // or variables declared inside of lambda (marked as captured if they are used by local methods inside lambda)
                    var syntaxesToAnalyze = LocalFunctionUsedInLambda.Select(tuple => tuple.localFunction).Concat(new[] {lambdaSyntax});
                    foreach (var analyzeSyntax in syntaxesToAnalyze)
                    {
                        var dataFlowAnalysis = SemanticModel.AnalyzeDataFlow(analyzeSyntax);
                        if (dataFlowAnalysis.Succeeded)
                        {
                            foreach (var capturedVariable in dataFlowAnalysis.CapturedInside)
                            {
                                // Make sure not already captured or a lambda param
                                if (LambdaParameters.All(param => param.Name != capturedVariable.Name) &&
                                    VariablesCapturedOnlyByLocals.All(param => param.Symbol.Name != capturedVariable.Name) &&
                                    VariablesCaptured.All(param => param.Symbol.Name != capturedVariable.Name))
                                {
                                    var capturedVariableDescription = new LambdaCapturedVariableDescription(capturedVariable);
                                    if (dataFlowAnalysis.VariablesDeclared.Contains(capturedVariable))
                                        VariablesCapturedOnlyByLocals.Add(capturedVariableDescription);
                                    else
                                        VariablesCaptured.Add(capturedVariableDescription);

                                    if (Schedule.Mode != ScheduleMode.Run && !capturedVariableDescription.IsNativeContainer &&
                                        dataFlowAnalysis.AlwaysAssigned.Contains(capturedVariable) && dataFlowAnalysis.DataFlowsOut.Contains(capturedVariable))
                                    {
                                        LambdaJobsErrors.DC0013(SystemGeneratorContext, Location, capturedVariable.Name);
                                        Success = false;
                                    }
                                }
                            }
                        }
				    }
				}

                WithStructuralChangesAndLambdaBodyInSystem = WithStructuralChanges && VariablesCaptured.All(variable => variable.IsThis);

                // If we are also using any managed components or doing structural changes, we also need to capture this
                if ((HasManagedParameters || HasSharedComponentParameters || WithStructuralChanges)
                    && VariablesCaptured.All(variable => !variable.IsThis))
                {
                    VariablesCaptured.Add(new LambdaCapturedVariableDescription(SystemTypeSymbol, true));
                }

                // Also captured any variables used in expressions that construct shared component filters
                foreach (var sharedComponentFilterArgumentSyntax in WithSharedComponentFilterArgumentSyntaxes)
                {
                    foreach (var identifier in sharedComponentFilterArgumentSyntax.DescendantNodes().OfType<IdentifierNameSyntax>())
                    {
                        var identifierSymbol = ModelExtensions.GetSymbolInfo(SemanticModel, identifier);
                        if (identifierSymbol.Symbol is ILocalSymbol || identifierSymbol.Symbol is IParameterSymbol &&
                            VariablesCaptured.All(variable => variable.OriginalVariableName != identifier.Identifier.Text))
                            AdditionalVariablesCapturedForScheduling.Add((identifier.Identifier.Text, identifierSymbol.Symbol));
                    }
                }

                foreach (var entityAttribute in LambdaCapturedVariableDescription.AttributesDescriptions)
                {
                    foreach (var argumentSyntax in AllArgumentSyntaxesOfMethod(entityAttribute.MethodName))
                    {
                        var expression = argumentSyntax.Expression;
                        if (expression is IdentifierNameSyntax identifier)
                        {
                            var capturedVariable = VariablesCaptured.FirstOrDefault(v => v.Symbol.Name == identifier.Identifier.Text);
                            if (capturedVariable != null)
                            {
                                if (entityAttribute.CheckAttributeApplicable(SystemGeneratorContext, SemanticModel, capturedVariable))
                                    capturedVariable.Attributes.Add(entityAttribute.AttributeName);
                                else
                                    Success = false;
                            }
                            else
                            {
                                LambdaJobsErrors.DC0012(SystemGeneratorContext, argumentSyntax.GetLocation(), identifier.ToString(), entityAttribute.MethodName);
                                Success = false;
                            }
                        }
                        else
                        {
                            LambdaJobsErrors.DC0012(SystemGeneratorContext, argumentSyntax.GetLocation(), expression.ToString(), entityAttribute.MethodName);
                            Success = false;
                        }
                    }
                }

                // Either add DeallocateOnJobCompletion attributes to variables or add to list of variables that need to be disposed
                // (depending of if they support DeallocateOnJobCompletion and if we are running as a job)
                foreach (var argumentSyntax in AllArgumentSyntaxesOfMethod("WithDisposeOnCompletion"))
                {
                    var expression = argumentSyntax.Expression;
                    if (expression is IdentifierNameSyntax identifier)
                    {
                        var capturedVariable = VariablesCaptured.FirstOrDefault(var => var.Symbol.Name == identifier.Identifier.Text);
                        if (capturedVariable != null)
                        {
                            if (Schedule.Mode != ScheduleMode.Run && capturedVariable.SupportsDeallocateOnJobCompletion())
                            {
                                capturedVariable.Attributes.Add("Unity.Collections.DeallocateOnJobCompletion");
                            }
                            else
                            {
                                DisposeOnJobCompletionVariables.Add(capturedVariable);
                            }
                        }
                        else
                        {
                            LambdaJobsErrors.DC0012(SystemGeneratorContext, argumentSyntax.GetLocation(), identifier.ToString(), "WithDisposeOnCompletion");
                            Success = false;
                        }
                    }
                    else
                    {
                        LambdaJobsErrors.DC0012(SystemGeneratorContext, expression.GetLocation(), expression.ToString(), "WithDisposeOnCompletion");
                        Success = false;
                    }
                }

                // Rewrite lambda body and get additional fields that are needed if lambda body is not emitted into system
                if (WithStructuralChangesAndLambdaBodyInSystem || !hasNonParameterIdentifier)
                {
                    RewrittenLambdaBody = OriginalLambdaExpression.ToBlockSyntax();
                }
                else
                {
                    SyntaxNode rewrittenLambdaExpression;
                    (rewrittenLambdaExpression, AdditionalFields, MethodsForLocalFunctions) = LambdaBodyRewriter.Rewrite(this);
                    RewrittenLambdaBody = ((ParenthesizedLambdaExpressionSyntax) rewrittenLambdaExpression).ToBlockSyntax();
                }

                // Remove source that has been been disable with preprocessor directives (but remains as disabled text)
                RewrittenLambdaBody = (BlockSyntax)new DisabledTextTriviaRemover().Visit(RewrittenLambdaBody);

                // Check to see if we have any references to __this in our rewritten lambda body and we can't contain reference types
                // if there is none remove the capture this reference
                if (!CanContainReferenceTypes
                    && RewrittenLambdaBody
                        .DescendantNodes()
                        .OfType<SimpleNameSyntax>()
                        .All(syntax => syntax.Identifier.ToString() != "__this"))
                {
                    VariablesCaptured.RemoveAll(variable => variable.IsThis);
                }

                // Also need to make sure we reference this if we are emitting lambda back into the system
                if (WithStructuralChangesAndLambdaBodyInSystem && VariablesCaptured.All(variable => !variable.IsThis))
                    VariablesCaptured.Add(new LambdaCapturedVariableDescription(SystemTypeSymbol, true));

                this.Verify();
            }
            catch (InvalidDescriptionException)
            {
                Success = false;
            }
        }
        ArgumentSyntax SingleOptionalArgumentSyntaxOfMethod(string methodName)
        {
            return
                !MethodInvocations.TryGetValue(methodName, out var invocations)
                    ? null
                    : invocations.Select(methodInvocation => methodInvocation.ArgumentList.Arguments.First()).FirstOrDefault(arg => arg != null);
        }

        // In the case of some copied source (lambda bodies in particular), we need to ensure that we remove any disabled text.
        // If we emit this back as source, the metadata marking the source as disabled text will be ignored and the it will include the
        // disabled text as valid source.
        class DisabledTextTriviaRemover : CSharpSyntaxRewriter
        {
            static bool IsTriviaToRemove(SyntaxTrivia syntaxTrivia) =>
                (syntaxTrivia.Kind() == SyntaxKind.DisabledTextTrivia ||
                 syntaxTrivia.Kind() == SyntaxKind.IfDirectiveTrivia ||
                 syntaxTrivia.Kind() == SyntaxKind.ElseDirectiveTrivia ||
                 syntaxTrivia.Kind() == SyntaxKind.ElifDirectiveTrivia ||
                 syntaxTrivia.Kind() == SyntaxKind.EndIfDirectiveTrivia);

            public override SyntaxToken VisitToken(SyntaxToken token)
            {
                return base.VisitToken(token)
                    .WithLeadingTrivia(token.LeadingTrivia.Where(trivia => !IsTriviaToRemove(trivia)))
                    .WithTrailingTrivia(token.TrailingTrivia.Where(trivia => !IsTriviaToRemove(trivia)));
            }
        }

        List<ArgumentSyntax> AllArgumentSyntaxesOfMethod(string methodName)
        {
            var result = new List<ArgumentSyntax>();
            if (!MethodInvocations.ContainsKey(methodName))
            {
                return result;
            }
            foreach (var methodInvocation in MethodInvocations[methodName])
            {
                result.AddRange(methodInvocation.ArgumentList.Arguments);
            }
            return result;
        }

        List<INamedTypeSymbol> AllTypeArgumentSymbolsOfMethod(string methodName)
        {
            var result = new List<INamedTypeSymbol>();
            if (!MethodInvocations.ContainsKey(methodName))
            {
                return result;
            }

            foreach (var methodInvocation in MethodInvocations[methodName])
            {
                var symbol = (IMethodSymbol)SemanticModel.GetSymbolInfo(methodInvocation).Symbol;

                // We can fail to get the symbol here, in that case we don't have access to the type
                // this will be reported by Roslyn with
                if (symbol == null)
                {
                    Success = false;
                    continue;
                }

                foreach (var argumentType in symbol.TypeArguments.OfType<ITypeParameterSymbol>())
                {
                    LambdaJobsErrors.DC0051(SystemGeneratorContext, Location, argumentType.Name, methodName);
                    Success = false;
                }

                foreach (var argumentType in symbol.TypeArguments.OfType<INamedTypeSymbol>())
                {
                    if (argumentType.IsGenericType)
                    {
                        LambdaJobsErrors.DC0051(SystemGeneratorContext, Location, argumentType.Name, methodName);
                        Success = false;
                        continue;
                    }

                    result.Add(argumentType);
                }
            }

            return result;
        }

        string GetName(string defaultName)
        {
            if (!MethodInvocations.TryGetValue("WithName", out var withNameInvocations))
                return defaultName;

            var invocation = withNameInvocations.First();
            var literalArgument = invocation.ArgumentList.Arguments.FirstOrDefault()?.DescendantNodes().OfType<LiteralExpressionSyntax>().FirstOrDefault();

            if (literalArgument == null)
            {
                LambdaJobsErrors.DC0008(SystemGeneratorContext, invocation.GetLocation(), "WithName");
                Success = false;
                return defaultName;
            }

            var customName = literalArgument.Token.ValueText;
            if (!customName.IsValidLambdaName())
            {
                LambdaJobsErrors.DC0043(SystemGeneratorContext, Location, customName);
                return defaultName;
            }

            return literalArgument.Token.ValueText;
        }
    }
}
