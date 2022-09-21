using System;
using NUnit.Framework;
using Unity.Burst;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
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
