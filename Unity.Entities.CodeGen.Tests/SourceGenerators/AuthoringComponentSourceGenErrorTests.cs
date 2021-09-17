using System;
using NUnit.Framework;
using UnityEngine;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
    [TestFixture]
    public class AuthoringComponentSourceGenErrorTests : SourceGenTests
    {
        protected override string[] DefaultUsings { get; } = { "System", "Unity.Entities", "Unity.Collections", "System.Runtime.InteropServices" };
        protected override Type[] DefaultCompilationReferenceTypes { get; } =
        {
            typeof(GenerateAuthoringComponentAttribute),
            typeof(ConvertToEntity),
            typeof(GameObject),
            typeof(MonoBehaviour),
        };

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void DC0030_ManagedIComponentData_WithoutDefaultConstructor()
        {
            const string source = @"
                [GenerateAuthoringComponent]
                public class RotationSpeed_ForEach : IComponentData
                {
                    public float RadiansPerSecond;

                    public RotationSpeed_ForEach(float radiansPerSecond)
                    {
                        RadiansPerSecond = radiansPerSecond;
                    }
                }";
            AssertProducesError(source, "DC0030", "RotationSpeed_ForEach");
        }
#endif

        [Test]
        public void DC0040_IBufferElementData_WithNonBlittableField()
        {
            const string source = @"
                [GenerateAuthoringComponent]
                public struct NonBlittableField_BufferElementData : IBufferElementData
                {
                    public string NonBlittableString;
                }";

            AssertProducesError(source, "DC0040", "NonBlittableString", "NonBlittableField_BufferElementData");
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void DC0041_IBufferElementData_ManagedType()
        {
            const string source = @"
                [GenerateAuthoringComponent]
                public class ManagedType_BufferElementData : IBufferElementData
                {
                    public float MyFloat;
                }";

            AssertProducesError(source, "DC0041", "ManagedType_BufferElementData");
        }
#endif

        [Test]
        public void DC3003_GenerateAuthoringComponent_WithoutAnyInterface()
        {
            const string source = @"
                [GenerateAuthoringComponent]
                public struct GenerateAuthoringComponent_NoInterface
                {
                    public float FloatField;
                }";

            AssertProducesError(source, "DC3003", "GenerateAuthoringComponent_NoInterface");
        }

        [Test]
        public void DC3003_GenerateAuthoringComponent_WithInvalidInterface()
        {
            const string source = @"
                [GenerateAuthoringComponent]
                public struct GenerateAuthoringComponent_InvalidInterface : IDisposable
                {
                    public void Dispose() { }
                }";

            AssertProducesError(source, "DC3003", "GenerateAuthoringComponent_InvalidInterface");
        }

        [Test]
        public void DC0060_IComponentDataStruct_WithEntityArray()
        {
            const string source = @"
                [GenerateAuthoringComponent]
                public struct ComponentDataStruct_WithEntityArray : IComponentData
                {
                    public Entity[] MyEntities;
                }";

            AssertProducesError(source, "DC0060", "ComponentDataStruct_WithEntityArray");
        }
    }
}
