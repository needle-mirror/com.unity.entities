using System;
using NUnit.Framework;
using Unity.Burst;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
    [TestFixture]
    public class SystemBaseErrorTests : SourceGenTests
    {
        protected override Type[] DefaultCompilationReferenceTypes { get; } =
        {
            typeof(BurstCompileAttribute),
            typeof(SystemBase),
        };

        protected override string[] DefaultUsings { get; } =
        {
            "System", "Unity.Burst", "Unity.Entities"
        };

        [Test]
        public void DC0058_SystemBaseDerivedClass_WithoutPartialKeyword()
        {
            const string source = @"
                public class NoPartialKeyword : SystemBase
                {
                    protected override void OnUpdate() {}
                }";
            AssertProducesError(source, "DC0058", "NoPartialKeyword");
        }
    }
}
