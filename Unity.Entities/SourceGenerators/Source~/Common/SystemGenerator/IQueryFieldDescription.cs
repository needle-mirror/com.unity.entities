using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    public interface IQueryFieldDescription
    {
        public string EntityQueryFieldAssignment(string systemStateName, string generatedQueryFieldName);
        public FieldDeclarationSyntax GetFieldDeclarationSyntax(string generatedQueryFieldName);
    }
}
