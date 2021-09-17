using NUnit.Framework;
using Unity.Mathematics;
using static Unity.Entities.GameObjectConversionUtility;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities.Tests.Conversion
{
    // TODO : @elora
    // [ ] Make sure an error is thrown when trying to bind multiple authoring fields to a runtime field- error thrown on domain reload trying to register
    class BindingRegistryTests : ConversionTestFixtureBase
    {
        [Test]
        public void ManualConversion_BindingRegistry()
        {
            var go = CreateGameObject();
            go.AddComponent<BindingRegistryManualTestAuthoring>();

            var entity = ConvertGameObjectHierarchy(go, MakeDefaultSettings());

            var component = m_Manager.GetComponentData<BindingRegistryManualTestComponent>(entity);
            Assert.AreEqual(true, component.BindBool);
            Assert.AreEqual(5, component.BindInt);
            Assert.AreEqual(10.0f, component.BindFloat);

            Assert.IsTrue(BindingRegistry.HasBindings(typeof(BindingRegistryManualTestComponent)));
        }

        [Test]
        public void GenerateAuthoringConversion_BindingRegistry()
        {
            var bindingType = typeof(BindingRegistryAutoTestComponent);
            var floatName = nameof(BindingRegistryAutoTestComponent.BindFloat);
            var intName = nameof(BindingRegistryAutoTestComponent.BindInt);
            var boolName = nameof(BindingRegistryAutoTestComponent.BindBool);

            var go = CreateGameObject();
            var authoringType = GeneratedAuthoringComponentConversionTests.GetAuthoringComponentType<BindingRegistryAutoTestComponent>();
            var c = go.AddComponent(authoringType);
            authoringType.GetField(boolName).SetValue(c, true);
            authoringType.GetField(intName).SetValue(c, 5);
            authoringType.GetField(floatName).SetValue(c, 10.0f);

            var entity = ConvertGameObjectHierarchy(go, MakeDefaultSettings());

            var component = m_Manager.GetComponentData<BindingRegistryAutoTestComponent>(entity);
            Assert.AreEqual(true, component.BindBool);
            Assert.AreEqual(5, component.BindInt);
            Assert.AreEqual(10.0f, component.BindFloat);

            Assert.IsTrue(BindingRegistry.HasBindings(typeof(BindingRegistryAutoTestComponent)));

            var floatBinding = BindingRegistry.GetBinding(bindingType, floatName);
            Assert.NotNull(floatBinding.Item1, $"The field {floatName} could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, floatBinding.Item1);
            Assert.AreEqual(floatName, floatBinding.Item2);

            var intBinding = BindingRegistry.GetBinding(bindingType, intName);
            Assert.NotNull(intBinding.Item1, $"The field {intName} could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, intBinding.Item1);
            Assert.AreEqual(intName, intBinding.Item2);

            var boolBinding = BindingRegistry.GetBinding(bindingType, boolName);
            Assert.NotNull(boolBinding.Item1, $"The field {boolName} could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, boolBinding.Item1);
            Assert.AreEqual(boolName, boolBinding.Item2);
        }

        [Test]
        public void GenerateAuthoringConversion_MultipleIntFields_BindingRegistry()
        {
            var bindingType = typeof(BindingRegistryIntComponent);
            var int1Name = nameof(BindingRegistryIntComponent.Int1);
            var int2Name = nameof(BindingRegistryIntComponent.Int2);
            var int3Name = nameof(BindingRegistryIntComponent.Int3);
            var int4Name = nameof(BindingRegistryIntComponent.Int4);

            var go = CreateGameObject();
            var authoringType = GeneratedAuthoringComponentConversionTests.GetAuthoringComponentType<BindingRegistryIntComponent>();
            var c = go.AddComponent(authoringType);

            var int1 = 0;
            var int2 = new int2(0, 1);
            var int3 = new int3(0, 1, 2);
            var int4 = new int4(0, 1, 2, 3);
            authoringType.GetField(int1Name).SetValue(c, int1);
            authoringType.GetField(int2Name).SetValue(c, int2);
            authoringType.GetField(int3Name).SetValue(c, int3);
            authoringType.GetField(int4Name).SetValue(c, int4);

            var entity = ConvertGameObjectHierarchy(go, MakeDefaultSettings());

            var component = m_Manager.GetComponentData<BindingRegistryIntComponent>(entity);
            Assert.AreEqual(int1, component.Int1);
            Assert.AreEqual(int2, component.Int2);
            Assert.AreEqual(int3, component.Int3);
            Assert.AreEqual(int4, component.Int4);

            Assert.IsTrue(BindingRegistry.HasBindings(typeof(BindingRegistryIntComponent)));

            // int
            var int1Binding = BindingRegistry.GetBinding(bindingType, int1Name);
            Assert.NotNull(int1Binding.Item1, $"The field {int1Name} could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, int1Binding.Item1);
            Assert.AreEqual(int1Name, int1Binding.Item2);

            // int2
            var int2XBinding = BindingRegistry.GetBinding(bindingType,  int2Name+ ".x");
            Assert.NotNull(int2XBinding.Item1, $"The field {int2Name}.x could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, int2XBinding.Item1);
            Assert.AreEqual(authoringType.GetField(int2Name).Name + ".x", int2XBinding.Item2);

            var int2YBinding = BindingRegistry.GetBinding(bindingType, int2Name + ".y");
            Assert.NotNull(int2YBinding.Item1, $"The field {int2Name}.y could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, int2YBinding.Item1);
            Assert.AreEqual(authoringType.GetField(int2Name).Name + ".y", int2YBinding.Item2);

            // float3
            var int3XBinding = BindingRegistry.GetBinding(bindingType, int3Name+ ".x");
            Assert.NotNull(int3XBinding.Item1, $"The field {int3Name}.x could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, int3XBinding.Item1);
            Assert.AreEqual(authoringType.GetField(int3Name).Name + ".x", int3XBinding.Item2);

            var int3YBinding = BindingRegistry.GetBinding(bindingType, int3Name + ".y");
            Assert.NotNull(int3YBinding.Item1, $"The field {int3Name}.y could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, int3YBinding.Item1);
            Assert.AreEqual(authoringType.GetField(int3Name).Name + ".y", int3YBinding.Item2);

            var int3ZBinding = BindingRegistry.GetBinding(bindingType, int3Name + ".z");
            Assert.NotNull(int3ZBinding.Item1, $"The field {int3Name}.z could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, int3ZBinding.Item1);
            Assert.AreEqual(authoringType.GetField(int3Name).Name + ".z", int3ZBinding.Item2);

            // float4
            var int4XBinding = BindingRegistry.GetBinding(bindingType, int4Name + ".x");
            Assert.NotNull(int4XBinding.Item1, $"The field {int4Name}.x could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, int4XBinding.Item1);
            Assert.AreEqual(authoringType.GetField(int4Name).Name + ".x", int4XBinding.Item2);

            var int4YBinding = BindingRegistry.GetBinding(bindingType, int4Name + ".y");
            Assert.NotNull(int4YBinding.Item1, $"The field {int4Name}.y could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, int4YBinding.Item1);
            Assert.AreEqual(authoringType.GetField(int4Name).Name + ".y", int4YBinding.Item2);

            var int4ZBinding = BindingRegistry.GetBinding(bindingType, int4Name + ".z");
            Assert.NotNull(int4ZBinding.Item1, $"The field {int4Name}.z could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, int4ZBinding.Item1);
            Assert.AreEqual(authoringType.GetField(int4Name).Name + ".z", int4ZBinding.Item2);

            var float4WBinding = BindingRegistry.GetBinding(bindingType, int4Name + ".w");
            Assert.NotNull(float4WBinding.Item1, $"The field {int4Name}.w could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float4WBinding.Item1);
            Assert.AreEqual(authoringType.GetField(int4Name).Name + ".w", float4WBinding.Item2);
        }

        [Test]
        public void GenerateAuthoringConversion_MultipleFloatFields_BindingRegistry()
        {
            var bindingType = typeof(BindingRegistryFloatComponent);
            var float1Name = nameof(BindingRegistryFloatComponent.Float1);
            var float2Name = nameof(BindingRegistryFloatComponent.Float2);
            var float3Name = nameof(BindingRegistryFloatComponent.Float3);
            var float4Name = nameof(BindingRegistryFloatComponent.Float4);

            var go = CreateGameObject();
            var authoringType = GeneratedAuthoringComponentConversionTests.GetAuthoringComponentType<BindingRegistryFloatComponent>();
            var c = go.AddComponent(authoringType);

            var float1 = 1.0f;
            var float2 = new float2(2.0f, 2.1f);
            var float3 = new float3(3.0f, 3.1f, 3.2f);
            var float4 = new float4(4.0f, 4.1f, 4.2f, 4.3f);
            authoringType.GetField(float1Name).SetValue(c, float1);
            authoringType.GetField(float2Name).SetValue(c, float2);
            authoringType.GetField(float3Name).SetValue(c, float3);
            authoringType.GetField(float4Name).SetValue(c, float4);

            var entity = ConvertGameObjectHierarchy(go, MakeDefaultSettings());

            var component = m_Manager.GetComponentData<BindingRegistryFloatComponent>(entity);
            Assert.AreEqual(float1, component.Float1);
            Assert.AreEqual(float2, component.Float2);
            Assert.AreEqual(float3, component.Float3);
            Assert.AreEqual(float4, component.Float4);

            Assert.IsTrue(BindingRegistry.HasBindings(bindingType));

            //float
            var float1Binding = BindingRegistry.GetBinding(bindingType, float1Name);
            Assert.NotNull(float1Binding.Item1, $"The field {float1Name} could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float1Binding.Item1);
            Assert.AreEqual(float1Name, float1Binding.Item2);

            // float2
            var float2XBinding = BindingRegistry.GetBinding(bindingType,  float2Name+ ".x");
            Assert.NotNull(float2XBinding.Item1, $"The field {float2Name}.x could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float2XBinding.Item1);
            Assert.AreEqual(authoringType.GetField(float2Name).Name + ".x", float2XBinding.Item2);

            var float2YBinding = BindingRegistry.GetBinding(bindingType, float2Name + ".y");
            Assert.NotNull(float2YBinding.Item1, $"The field {float2Name}.y could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float2YBinding.Item1);
            Assert.AreEqual(authoringType.GetField(float2Name).Name + ".y", float2YBinding.Item2);

            // float3
            var float3XBinding = BindingRegistry.GetBinding(bindingType, float3Name+ ".x");
            Assert.NotNull(float3XBinding.Item1, $"The field {float3Name}.x could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float3XBinding.Item1);
            Assert.AreEqual(authoringType.GetField(float3Name).Name + ".x", float3XBinding.Item2);

            var float3YBinding = BindingRegistry.GetBinding(bindingType, float3Name + ".y");
            Assert.NotNull(float3YBinding.Item1, $"The field {float3Name}.y could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float3YBinding.Item1);
            Assert.AreEqual(authoringType.GetField(float3Name).Name + ".y", float3YBinding.Item2);

            var float3ZBinding = BindingRegistry.GetBinding(bindingType, float3Name + ".z");
            Assert.NotNull(float3ZBinding.Item1, $"The field {float3Name}.z could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float3ZBinding.Item1);
            Assert.AreEqual(authoringType.GetField(float3Name).Name + ".z", float3ZBinding.Item2);

            // float4
            var float4XBinding = BindingRegistry.GetBinding(bindingType, float4Name + ".x");
            Assert.NotNull(float4XBinding.Item1, $"The field {float4Name}.x could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float4XBinding.Item1);
            Assert.AreEqual(authoringType.GetField(float4Name).Name + ".x", float4XBinding.Item2);

            var float4YBinding = BindingRegistry.GetBinding(bindingType, float4Name + ".y");
            Assert.NotNull(float4YBinding.Item1, $"The field {float4Name}.y could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float4YBinding.Item1);
            Assert.AreEqual(authoringType.GetField(float4Name).Name + ".y", float4YBinding.Item2);

            var float4ZBinding = BindingRegistry.GetBinding(bindingType, float4Name + ".z");
            Assert.NotNull(float4ZBinding.Item1, $"The field {float4Name}.z could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float4ZBinding.Item1);
            Assert.AreEqual(authoringType.GetField(float4Name).Name + ".z", float4ZBinding.Item2);

            var float4WBinding = BindingRegistry.GetBinding(bindingType, float4Name + ".w");
            Assert.NotNull(float4WBinding.Item1, $"The field {float4Name}.w could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, float4WBinding.Item1);
            Assert.AreEqual(authoringType.GetField(float4Name).Name + ".w", float4WBinding.Item2);
        }

        [Test]
        public void GenerateAuthoringConversion_MultipleBoolFields_BindingRegistry()
        {
            var bindingType = typeof(BindingRegistryBoolComponent);
            var bool1Name = nameof(BindingRegistryBoolComponent.Bool1);
            var bool2Name = nameof(BindingRegistryBoolComponent.Bool2);
            var bool3Name = nameof(BindingRegistryBoolComponent.Bool3);
            var bool4Name = nameof(BindingRegistryBoolComponent.Bool4);

            var go = CreateGameObject();
            var authoringType = GeneratedAuthoringComponentConversionTests.GetAuthoringComponentType<BindingRegistryBoolComponent>();
            var c = go.AddComponent(authoringType);

            var bool1 = true;
            var bool2 = new bool2(true, false);
            var bool3 = new bool3(true, false, true);
            var bool4 = new bool4(true, false, true, false);
            authoringType.GetField(bool1Name).SetValue(c, bool1);
            authoringType.GetField(bool2Name).SetValue(c, bool2);
            authoringType.GetField(bool3Name).SetValue(c, bool3);
            authoringType.GetField(bool4Name).SetValue(c, bool4);

            var entity = ConvertGameObjectHierarchy(go, MakeDefaultSettings());

            var component = m_Manager.GetComponentData<BindingRegistryBoolComponent>(entity);
            Assert.AreEqual(bool1, component.Bool1);
            Assert.AreEqual(bool2, component.Bool2);
            Assert.AreEqual(bool3, component.Bool3);
            Assert.AreEqual(bool4, component.Bool4);

            Assert.IsTrue(BindingRegistry.HasBindings(typeof(BindingRegistryBoolComponent)));

            // Bool
            var bool1Binding = BindingRegistry.GetBinding(bindingType, bool1Name);
            Assert.NotNull(bool1Binding.Item1, $"The field {bool1Name} could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, bool1Binding.Item1);
            Assert.AreEqual(authoringType.GetField(bool1Name).Name, bool1Binding.Item2);

            // Bool2
            var bool2XBinding = BindingRegistry.GetBinding(bindingType, bool2Name + ".x");
            Assert.NotNull(bool2XBinding.Item1, $"The field {bool2Name}.x could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, bool2XBinding.Item1);
            Assert.AreEqual(authoringType.GetField(bool2Name).Name + ".x", bool2XBinding.Item2);

            var bool2YBinding = BindingRegistry.GetBinding(bindingType, bool2Name + ".y");
            Assert.NotNull(bool2YBinding.Item1, $"The field {bool2Name}.y could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, bool2YBinding.Item1);
            Assert.AreEqual(authoringType.GetField(bool2Name).Name + ".y", bool2YBinding.Item2);

            // Bool 3
            var bool3XBinding = BindingRegistry.GetBinding(bindingType, bool3Name + ".x");
            Assert.NotNull(bool3XBinding.Item1, $"The field {bool3Name}.x could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, bool3XBinding.Item1);
            Assert.AreEqual(authoringType.GetField(bool3Name).Name + ".x", bool3XBinding.Item2);

            var bool3YBinding = BindingRegistry.GetBinding(bindingType, bool3Name + ".y");
            Assert.NotNull(bool3YBinding.Item1, $"The field {bool3Name}.y could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, bool3YBinding.Item1);
            Assert.AreEqual(authoringType.GetField(bool3Name).Name + ".y", bool3YBinding.Item2);

            var bool3ZBinding = BindingRegistry.GetBinding(bindingType, bool3Name + ".z");
            Assert.NotNull(bool3ZBinding.Item1, $"The field {bool3Name}.z could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, bool3ZBinding.Item1);
            Assert.AreEqual(authoringType.GetField(bool3Name).Name + ".z", bool3ZBinding.Item2);

            // Bool 4
            var bool4XBinding = BindingRegistry.GetBinding(bindingType, bool4Name + ".x");
            Assert.NotNull(bool4XBinding.Item1, $"The field {bool4Name}.x could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, bool4XBinding.Item1);
            Assert.AreEqual(authoringType.GetField(bool4Name).Name + ".x", bool4XBinding.Item2);

            var bool4YBinding = BindingRegistry.GetBinding(bindingType, bool4Name + ".y");
            Assert.NotNull(bool4YBinding.Item1, $"The field {bool4Name}.y could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, bool4YBinding.Item1);
            Assert.AreEqual(authoringType.GetField(bool4Name).Name + ".y", bool4YBinding.Item2);

            var bool4ZBinding = BindingRegistry.GetBinding(bindingType, bool4Name + ".z");
            Assert.NotNull(bool4ZBinding.Item1, $"The field {bool4Name}.z could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, bool4ZBinding.Item1);
            Assert.AreEqual(authoringType.GetField(bool4Name).Name + ".z", bool4ZBinding.Item2);

            var bool4WBinding = BindingRegistry.GetBinding(bindingType, bool4Name + ".w");
            Assert.NotNull(bool4WBinding.Item1, $"The field {bool4Name}.w could not be found in the BindingRegistry.");
            Assert.AreEqual(authoringType, bool4WBinding.Item1);
            Assert.AreEqual(authoringType.GetField(bool4Name).Name + ".w", bool4WBinding.Item2);
        }

        [Test]
        public void BindSingleField_BindingRegistry()
        {
            var go = CreateGameObject();
            go.AddComponent<BindingRegistryField1TestAuthoring>();

            var entity = ConvertGameObjectHierarchy(go, MakeDefaultSettings());

            Assert.IsTrue(m_Manager.HasComponent<BindingRegistryFieldTestComponent>(entity), "BindingRegistryFieldTestComponent was not added to the entity during conversion");

            var floatBinding = BindingRegistry.GetBinding(typeof(BindingRegistryFieldTestComponent), nameof(BindingRegistryFieldTestComponent.BindFloat2) + ".x");
            Assert.NotNull(floatBinding, "The field BindFloat2.x could not be found in the BindingRegistry.");
            Assert.AreEqual(typeof(BindingRegistryField1TestAuthoring), floatBinding.Item1);
            Assert.AreEqual(nameof(BindingRegistryField1TestAuthoring.FloatField), floatBinding.Item2);
        }

        [Test]
        public void BindMultipleFields_BindingRegistry()
        {
            var go = CreateGameObject();
            go.AddComponent<BindingRegistryField1TestAuthoring>();
            go.AddComponent<BindingRegistryField2TestAuthoring>();

            var entity = ConvertGameObjectHierarchy(go, MakeDefaultSettings());

            Assert.IsTrue(m_Manager.HasComponent<BindingRegistryFieldTestComponent>(entity), "BindingRegistryFieldTestComponent was not added to the entity during conversion");

            var floatXBinding = BindingRegistry.GetBinding(typeof(BindingRegistryFieldTestComponent), nameof(BindingRegistryFieldTestComponent.BindFloat2) + ".x");
            Assert.NotNull(floatXBinding, "The field BindFloat2.x could not be found in the BindingRegistry.");
            Assert.AreEqual(typeof(BindingRegistryField1TestAuthoring), floatXBinding.Item1);
            Assert.AreEqual(nameof(BindingRegistryField1TestAuthoring.FloatField), floatXBinding.Item2);

            var floatYBinding = BindingRegistry.GetBinding(typeof(BindingRegistryFieldTestComponent), nameof(BindingRegistryFieldTestComponent.BindFloat2) + ".y");
            Assert.NotNull(floatYBinding, "The field BindFloat2.y could not be found in the BindingRegistry.");
            Assert.AreEqual(typeof(BindingRegistryField2TestAuthoring), floatYBinding.Item1);
            Assert.AreEqual(nameof(BindingRegistryField2TestAuthoring.FloatField), floatYBinding.Item2);
        }

        struct BindingRegistryBufferElement : IBufferElementData
        {
            public int Int1;
        }

        [Test]
        public void GetBinding_NotAssignableFromTypeThrows_BindingRegistry()
        {
            var go = CreateGameObject();
            go.AddComponent<BindingRegistryField1TestAuthoring>();
            go.AddComponent<BindingRegistryField2TestAuthoring>();

            var entity = ConvertGameObjectHierarchy(go, MakeDefaultSettings());

            Assert.IsTrue(m_Manager.HasComponent<BindingRegistryFieldTestComponent>(entity), "BindingRegistryFieldTestComponent was not added to the entity during conversion");

            Assert.Throws<UnityEngine.Assertions.AssertionException>( () =>BindingRegistry.GetBinding(typeof(BindingRegistryBufferElement), nameof(BindingRegistryBufferElement.Int1)));
        }

        [Test]
        public void GetBinding_FieldNotFoundReturnsNull_BindingRegistry()
        {
            var go = CreateGameObject();
            go.AddComponent<BindingRegistryField1TestAuthoring>();

            var entity = ConvertGameObjectHierarchy(go, MakeDefaultSettings());

            Assert.IsTrue(m_Manager.HasComponent<BindingRegistryFieldTestComponent>(entity), "BindingRegistryFieldTestComponent was not added to the entity during conversion");

            var floatXBinding = BindingRegistry.GetBinding(typeof(BindingRegistryFieldTestComponent), "BindFloat.x");
            Assert.Null(floatXBinding.Item1);
            Assert.AreEqual("", floatXBinding.Item2);
        }
    }
}
