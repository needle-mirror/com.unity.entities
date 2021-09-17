using System;
using NUnit.Framework;
using UnityEngine;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
    public class AuthoringComponentSourceGenNoErrorTests : SourceGenTests
    {
        [Test]
        public void IBufferElementData_WithMultipleFields()
        {
            const string source = @"
                [GenerateAuthoringComponent]
                public struct BufferElementData_MultipleFields : IBufferElementData
                {
                    public float MyFloat;
                    public bool MyBool;
                }";

            AssertProducesNoError(source);
        }

        [Test]
        public void IBufferElementData_WithStructLayoutAttribute()
        {
            const string source = @"
                [GenerateAuthoringComponent]
                [StructLayout(LayoutKind.Explicit)]
                public struct BufferElementData_StructLayoutAttribute : IBufferElementData
                {
                    [FieldOffset(4)]public float MyFloat;
                }";

            AssertProducesNoError(source);
        }

        protected override string[] DefaultUsings { get; } = { "System", "Unity.Entities", "System.Runtime.InteropServices" };
        protected override Type[] DefaultCompilationReferenceTypes { get; } =
        {
            typeof(GenerateAuthoringComponentAttribute),
            typeof(ConvertToEntity),
            typeof(GameObject),
            typeof(MonoBehaviour),
        };
    }
}
