using System;
using Mono.Cecil;
using Unity.Entities.CodeGen.Tests;
using static Unity.Entities.Hybrid.CodeGen.AuthoringComponentPostProcessor;

namespace Unity.Entities.Hybrid.CodeGen.Tests
{
    public abstract class AuthoringComponentIntegrationTest : IntegrationTest
    {
        protected override string ExpectedPath =>
            "Packages/com.unity.entities/Unity.Entities.Hybrid.CodeGen.Tests/AuthoringComponent/IntegrationTests";

        protected void RunAuthoringComponentDataPostprocessingTest(Type type)
        {
            RunPostprocessingTest(CreateComponentDataAuthoringType(TypeDefinitionFor(type)));
        }

        protected void RunAuthoringBufferElementDataPostprocessingTest(Type type)
        {
            RunPostprocessingTest(CreateBufferElementDataAuthoringType(TypeDefinitionFor(type)));
        }
    }
}
