using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System.IO;
using Unity.Entities.SourceGen.Common;
using static Unity.Entities.SourceGen.Common.SourceGenHelpers;

namespace Unity.Entities.SourceGen
{
    [Generator]
    public class AuthoringComponentGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            bool IsGeneratedAuthoringComponentAttribute(AttributeData data)
            {
                return data.AttributeClass.ContainingAssembly.Name == "Unity.Entities"
                       && data.AttributeClass.Name == "GenerateAuthoringComponentAttribute";
            }

            try
            {
                if (context.Compilation.Assembly.Name.Contains("CodeGen.Tests"))
                {
                    return;
                }

                var authoringComponentReceiver = (AuthoringComponentReceiver)context.SyntaxReceiver;

                if (!authoringComponentReceiver.CandidateSyntaxes.Any() || !context.Compilation.ReferencedAssemblyNames.Any(n => n.Name == "Unity.Entities.Hybrid"))
                {
                    // TODO: I believe it is valid to have GenerateAuthoringComponent but not reference entities (DocCodeSamples.Tests currently does this).
                    // We should probably throw a warning here though.
                    //throw new ArgumentException("Using the [GenerateAuthoringComponent] attribute requires a reference to the Unity.Entities.Hybrid assembly.");
                    return;
                }

                SourceGenHelpers.LogInfo($"Source generating assembly {context.Compilation.Assembly.Name} for authoring components...");
                var stopwatch = Stopwatch.StartNew();;

                foreach (var candidateSyntax in authoringComponentReceiver.CandidateSyntaxes)
                {
                    var semanticModel = context.Compilation.GetSemanticModel(candidateSyntax.SyntaxTree);
                    var candidateSymbol = semanticModel.GetDeclaredSymbol(candidateSyntax);
                    LogInfo($"Parsing authoring component {candidateSymbol.Name}");

                    if (!candidateSymbol.GetAttributes().Any(IsGeneratedAuthoringComponentAttribute))
                    {
                        continue;
                    }

                    var authoringComponent = new AuthoringComponent(candidateSyntax, candidateSymbol, context);

                    CheckValidity(authoringComponent);

                    var compilationUnit =
                        CompilationUnit().AddMembers(GenerateAuthoringTypeFrom(authoringComponent))
                                         .NormalizeWhitespace();

                    var generatedSourceHint = candidateSyntax.SyntaxTree.GetGeneratedSourceFileName(context.Compilation.Assembly);
                    var generatedSourceFullPath = candidateSyntax.SyntaxTree.GetGeneratedSourceFilePath(context.Compilation.Assembly);
                    var sourceTextForNewClass = compilationUnit.GetText(Encoding.UTF8);
                    sourceTextForNewClass = sourceTextForNewClass.WithInitialLineDirectiveToGeneratedSource(generatedSourceFullPath);
                    context.AddSource(generatedSourceHint, sourceTextForNewClass);

                    File.WriteAllLines(
                        generatedSourceFullPath,
                        sourceTextForNewClass.ToString().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
                }

                stopwatch.Stop();
                SourceGenHelpers.LogInfo($"TIME : AuthoringComponent : {context.Compilation.Assembly.Name} : {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception exception)
            {
                //context.WaitForDebuggerInAssembly();
                context.LogError("SGICE002", "Authoring Component", exception.ToString(), context.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
            }
        }

        static MemberDeclarationSyntax GenerateAuthoringTypeFrom(AuthoringComponent authoringComponent)
        {
            switch (authoringComponent.Interface)
            {
                case AuthoringComponentInterface.IComponentData:
                    return AuthoringComponentFactory.GenerateComponentDataAuthoring(authoringComponent)
                        .AddNamespaces(authoringComponent.NamespacesFromMostToLeastNested);
                case AuthoringComponentInterface.IBufferElementData:
                    return AuthoringComponentFactory.GenerateBufferingElementDataAuthoring(authoringComponent)
                        .AddNamespaces(authoringComponent.NamespacesFromMostToLeastNested);
                default:
                    return default;
            }
        }

        static void CheckValidity(AuthoringComponent authoringComponent)
        {
            switch (authoringComponent.Interface)
            {
                case AuthoringComponentInterface.IComponentData:
                    if (authoringComponent.FromValueType &&
                        authoringComponent.FieldDescriptions.Any(d => d.FieldType == FieldType.EntityArray))
                    {
                        throw new ArgumentException(
                            "Invalid use of Entity[] in a struct: IComponentData structs cannot contain managed types." +
                            "Either use an array that works in IComponentData structs (DynamicBuffer) or a IComponentData class.");
                    }
                    break;

                case AuthoringComponentInterface.IBufferElementData:
                    if (!authoringComponent.FromValueType)
                    {
                        throw new ArgumentException("IBufferElementData types must be structs.");
                    }

                    if (authoringComponent.FieldDescriptions.Any(d => d.FieldType == FieldType.NonEntityReferenceType || d.FieldType == FieldType.EntityArray))
                    {
                        throw new ArgumentException("IBufferElementData types may only contain blittable or primitive fields.");
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        "The [GenerateAuthoringComponent] attribute may only be used with types " +
                        "that implement either IBufferElementData or IComponentData.");
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new AuthoringComponentReceiver());
        }
    }
}
