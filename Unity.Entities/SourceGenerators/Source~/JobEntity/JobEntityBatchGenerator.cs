using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using static Unity.Entities.SourceGen.JobEntityDescription;

namespace Unity.Entities.SourceGen
{
    public static class JobEntityBatchTypeGenerator
    {
        public static StructDeclarationSyntax GenerateFrom(JobEntityDescription jobEntityDescription)
        {
            string jobEntityBatchCode =
                $@"public struct {jobEntityDescription.GeneratedJobEntityBatchTypeName} : Unity.Entities.IJobEntityBatch
                   {{
                       public {jobEntityDescription.DeclaringTypeFullyQualifiedName} __JobData;
                       {jobEntityDescription.OnUpdateMethodParameters.Select(p => p.BatchFieldDeclaration).SeparateByNewLine()}

                       public unsafe void Execute(Unity.Entities.ArchetypeChunk batch, int batchIndex)
                       {{
                           {CreateNativeArrayUnsafePointers(jobEntityDescription.OnUpdateMethodParameters).SeparateByNewLine()}
                           for (int i = 0; i < batch.Count; i++)
                           {{
                               __JobData.OnUpdate({CreateJobOnUpdateArguments(jobEntityDescription.OnUpdateMethodParameters).SeparateByComma()});
                           }}
                       }}
                   }}";

            return (StructDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(jobEntityBatchCode);

            IEnumerable<string> CreateNativeArrayUnsafePointers(IEnumerable<OnUpdateMethodParameter> onUpdateMethodParameters)
            {
                return
                    onUpdateMethodParameters.Select(p =>
                        !p.IsReadOnly
                            ? $"var {p.NativeArrayPointerName} = ({p.FullyQualifiedTypeName}*)batch.GetNativeArray({p.BatchFieldName}).GetUnsafePtr();"
                            : $"var {p.NativeArrayPointerName} = ({p.FullyQualifiedTypeName}*)batch.GetNativeArray({p.BatchFieldName}).GetUnsafeReadOnlyPtr();");
            }

            IEnumerable<string> CreateJobOnUpdateArguments(IEnumerable<OnUpdateMethodParameter> onUpdateMethodParameters)
            {
                foreach (var parameter in onUpdateMethodParameters)
                {
                    if (parameter.IsReadOnly)
                    {
                        yield return $"in {parameter.NativeArrayPointerName}[i]";
                    }
                    else
                    {
                        yield return $"ref {parameter.NativeArrayPointerName}[i]";
                    }
                }
            }
        }
    }
}
