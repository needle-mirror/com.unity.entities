using Microsoft.CodeAnalysis;

namespace Unity.Entities.SourceGen.Common
{
    public interface ISourceGenerationDescription
    {
        GeneratorExecutionContext Context { get; }
    }
}
