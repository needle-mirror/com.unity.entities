using System;
using System.Linq;
using NUnit.Framework;
using Unity.Jobs;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
    // We suppress the CS0282 warning around partial structs with fields in the case of source-generated types
    // (with our PartialStructWarningSuppressor), but we pass through other warnings not around our specific types.
    [TestFixture]
    public class PartialStructWarningSuppressorTests : SourceGenTests
    {
        protected override Type[] DefaultCompilationReferenceTypes { get; } =
        {
            typeof(SystemBase),
            typeof(JobHandle),
        };

        protected override string[] DefaultUsings { get; } =
        {
            "System", "Unity.Entities"
        };

        [Test]
        public void NoError_PartialISystemStructWithField()
        {
            const string source =
                @"
                public partial struct SomeSystemBase : ISystem
                {
                    public float _someField;

                    public void OnCreate(ref SystemState state) {}
                    public void OnDestroy(ref SystemState state) {}
                    public void OnUpdate(ref SystemState state) {}
                }";

            AssertProducesNoError(source, null, true);
        }

        [Test]
        public void NoError_PartialIJobEntityStructWithField()
        {
            const string source =
                @"
                public partial struct SomeEntityJob : IJobEntity
                {
                    public float _someField;

                    public void Execute(Entity entity)
                    {
                    }
                }";

            AssertProducesNoError(source, null, true);
        }

        [Test]
        public void NoError_PartialSomeOtherStructWithField()
        {
            const string source =
                @"
                public partial struct SomeStruct
                {
                    public float _someField;
                }
                public partial struct SomeStruct
                {
                    public float _yetAnotherField;
                }";

            AssertProducesError(source, "CS0282", Enumerable.Empty<string>(), null, true);
        }
    }
}
