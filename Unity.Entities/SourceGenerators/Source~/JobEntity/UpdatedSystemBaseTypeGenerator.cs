using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Unity.Entities.SourceGen
{
    internal static class UpdatedSystemBaseTypeGenerator
    {
        public static ClassDeclarationSyntax GenerateFrom(SystemBaseDescription systemBaseDescription)
        {
            var emptyClassWithSameIdentifierBaseListAndModifiers =
                ClassDeclaration(systemBaseDescription.DeclaringType.Identifier)
                    .WithBaseList(systemBaseDescription.DeclaringType.BaseList)
                    .WithModifiers(systemBaseDescription.DeclaringType.Modifiers);

            MethodDeclarationSyntax rewrittenOnUpdateMethod = GetOnUpdateMethodReplacement(systemBaseDescription);

            var nonOverrideModifiers =
                new SyntaxTokenList(rewrittenOnUpdateMethod.Modifiers.Where(m => !m.IsKind( SyntaxKind.OverrideKeyword)));

            return
                emptyClassWithSameIdentifierBaseListAndModifiers
                    .AddMembers(OnUpdateEntityQuery())
                    .AddMembers(
                        rewrittenOnUpdateMethod
                            .WithIdentifier(
                                SyntaxFactory.Identifier($"{systemBaseDescription.OriginalSystemBaseOnUpdateMethod.Identifier.Text}_Patched"))
                            .WithModifiers(nonOverrideModifiers))
                    .AddMembers(OnCreateForCompiler(systemBaseDescription));
        }

        private static MemberDeclarationSyntax OnCreateForCompiler(SystemBaseDescription systemBaseDescription)
        {
            string onCreateForCompiler =
                $@"{systemBaseDescription.GetAccessModifiers()} override void OnCreateForCompiler()
                   {{
                       __OnUpdateQuery = GetEntityQuery(
                            new Unity.Entities.EntityQueryDesc()
                            {{
                                 All = new Unity.Entities.ComponentType[]
                                 {{
                                     {CreateEntityQueryContents().SeparateByComma()}
                                 }}
                            }});
                   }}";

            return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(onCreateForCompiler);

            IEnumerable<string> CreateEntityQueryContents()
            {
                return
                    systemBaseDescription.JobEntityData.UserWrittenJobEntity.OnUpdateMethodParameters
                        .Select(p =>
                            !p.IsReadOnly
                                ? $"Unity.Entities.ComponentType.ReadWrite<{p.FullyQualifiedTypeName}>()"
                                : $"Unity.Entities.ComponentType.ReadOnly<{p.FullyQualifiedTypeName}>()");
            }
        }

        private static MethodDeclarationSyntax GetOnUpdateMethodReplacement(SystemBaseDescription systemBaseDescription)
        {
            var onUpdateInvocation = systemBaseDescription.EntitiesOnUpdateInvocation;

            string replacement =
                $@"override void OnUpdate()
                   {{
                       {CreateJobEntityVariableDeclaration(systemBaseDescription.EntitiesOnUpdateInvocation)}
                       Dependency = new {systemBaseDescription.JobEntityData.GeneratedIJobEntityBatchType.Identifier.Text}
                       {{
                           __JobData = {onUpdateInvocation.JobEntityVariableName},
                           {systemBaseDescription.JobEntityData.UserWrittenJobEntity.OnUpdateMethodParameters
                               .Select(p =>
                                   p.IsReadOnly
                                       ? $"{p.BatchFieldName} = GetComponentTypeHandle<{p.FullyQualifiedTypeName}>(isReadOnly: true)"
                                       : $"{p.BatchFieldName} = GetComponentTypeHandle<{p.FullyQualifiedTypeName}>()")
                               .SeparateByCommaAndNewLine()}
                       }}
                       .ScheduleParallel(__OnUpdateQuery, dependsOn: Dependency);
                   }}";

            return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(replacement);
        }

        private static string CreateJobEntityVariableDeclaration(SystemBaseDescription.EntitiesOnUpdate entitiesOnUpdateInvocation)
        {
            if (entitiesOnUpdateInvocation.HasFieldAssignmentsDuringJobEntityCreation)
            {
                return
                    $@"var {entitiesOnUpdateInvocation.JobEntityVariableName} = new {entitiesOnUpdateInvocation.JobEntityTypeName}
                      {{
                          {entitiesOnUpdateInvocation.FieldAssignmentsInJobEntity.Select(f => $"{f.Left} = {f.Right}").SeparateByCommaAndNewLine()}
                      }};";
            }
            return
                $@"var {entitiesOnUpdateInvocation.JobEntityVariableName} = new {entitiesOnUpdateInvocation.JobEntityTypeName}()";
        }

        private static FieldDeclarationSyntax OnUpdateEntityQuery()
        {
            const string queryField = "Unity.Entities.EntityQuery __OnUpdateQuery;";
            return (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(queryField);
        }
    }
}
