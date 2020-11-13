using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Entities.SourceGen
{
    public class FieldDescription
    {
        public IFieldSymbol FieldSymbol { get; private set; }
        public bool IsValueType { get; private set; }

        public static FieldDescription From(VariableDeclaratorSyntax syntaxNode, GeneratorExecutionContext context)
        {
            IFieldSymbol fieldSymbol = (IFieldSymbol)context.Compilation.GetSemanticModel(syntaxNode.SyntaxTree).GetDeclaredSymbol(syntaxNode);

            return new FieldDescription
            {
                FieldSymbol = fieldSymbol,
                IsValueType = fieldSymbol.Type.IsValueType
            };
        }
    }
}
