namespace Unity.Entities.SourceGen.SystemGenerator.Common
{
    public interface IQueryFieldDescription
    {
        public string EntityQueryFieldAssignment(string generatedQueryFieldName);
        public string GetFieldDeclaration(string generatedQueryFieldName, bool forcePublic = false);
    }
}
