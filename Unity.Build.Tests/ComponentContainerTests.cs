using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Build.Tests
{
    class ComponentContainerTests
    {
        public interface ITestComponent { }
        public interface ITestInterface : ITestComponent { }

        struct ComponentA : ITestInterface
        {
            public int Integer;
            public float Float;
            public string String;
        }

        struct ComponentB : ITestInterface
        {
            public byte Byte;
            public double Double;
            public short Short;
        }

        class ComplexComponent : ITestComponent
        {
            public int Integer;
            public float Float;
            public string String = string.Empty;
            public ComponentA Nested;
            public List<int> ListInteger = new List<int>();
        }

        class InvalidComponent { }

        abstract class AbstractClass : ITestComponent
        {
            public int Integer;
        }

        class DerivedClass : AbstractClass
        {
            public float Float;
        }

        class TestComponentContainer : ComponentContainer<TestComponentContainer, ITestComponent> { }

        /// <summary>
        /// Verify that <see cref="ComponentContainer{ITestComponent}"/> can store complex components and get back the value.
        /// </summary>
        [Test]
        public void ComponentValues_AreValid()
        {
            var container = TestComponentContainer.CreateInstance();
            var component = new ComplexComponent
            {
                Integer = 1,
                Float = 123.456f,
                String = "test",
                Nested = new ComponentA
                {
                    Integer = 42
                },
                ListInteger = new List<int> { 1, 1, 2, 3, 5, 8, 13 }
            };
            container.SetComponent(component);

            var value = container.GetComponent<ComplexComponent>();
            Assert.That(value.Integer, Is.EqualTo(1));
            Assert.That(value.Float, Is.EqualTo(123.456f));
            Assert.That(value.String, Is.EqualTo("test"));
            Assert.That(value.Nested.Integer, Is.EqualTo(42));
            Assert.That(value.ListInteger, Is.EquivalentTo(new List<int> { 1, 1, 2, 3, 5, 8, 13 }));
        }

        /// <summary>
        /// Verify that <see cref="ComponentContainer{ITestComponent}"/> can inherit values from dependencies.
        /// </summary>
        [Test]
        public void ComponentInheritance()
        {
            var containerA = TestComponentContainer.CreateInstance();
            containerA.SetComponent(new ComponentA
            {
                Integer = 1,
                Float = 123.456f,
                String = "test"
            });

            var containerB = TestComponentContainer.CreateInstance();
            containerB.AddDependency(containerA);
            containerB.SetComponent(new ComponentB
            {
                Byte = 255,
                Double = 3.14159265358979323846,
                Short = 32767
            });

            Assert.That(containerB.IsComponentInherited<ComponentA>(), Is.True);
            Assert.That(containerB.GetComponent<ComponentA>(), Is.EqualTo(new ComponentA
            {
                Integer = 1,
                Float = 123.456f,
                String = "test"
            }));

            Assert.That(containerB.IsComponentInherited<ComponentB>(), Is.False);
            Assert.That(containerB.GetComponent<ComponentB>(), Is.EqualTo(new ComponentB
            {
                Byte = 255,
                Double = 3.14159265358979323846,
                Short = 32767
            }));
        }

        /// <summary>
        /// Verify that <see cref="ComponentContainer{ITestComponent}"/> can inherit values from multiple dependencies.
        /// </summary>
        [Test]
        public void ComponentInheritance_FromMultipleDependencies()
        {
            var containerA = TestComponentContainer.CreateInstance();
            containerA.SetComponent(new ComponentA
            {
                Integer = 1,
                Float = 123.456f,
                String = "test"
            });

            var containerB = TestComponentContainer.CreateInstance();
            containerB.AddDependency(containerA);
            containerB.SetComponent(new ComponentB
            {
                Byte = 255,
                Double = 3.14159265358979323846,
                Short = 32767
            });

            var containerC = TestComponentContainer.CreateInstance();
            containerC.SetComponent(new ComplexComponent
            {
                Integer = 1,
                Float = 123.456f,
                String = "test",
                Nested = new ComponentA
                {
                    Integer = 42
                },
                ListInteger = new List<int> { 1, 1, 2, 3, 5, 8, 13 }
            });

            var containerD = TestComponentContainer.CreateInstance();
            containerD.AddDependency(containerB);
            containerD.AddDependency(containerC);

            Assert.That(containerD.IsComponentInherited<ComponentA>(), Is.True);
            Assert.That(containerD.GetComponent<ComponentA>(), Is.EqualTo(new ComponentA
            {
                Integer = 1,
                Float = 123.456f,
                String = "test"
            }));

            Assert.That(containerD.IsComponentInherited<ComponentB>(), Is.True);
            Assert.That(containerD.GetComponent<ComponentB>(), Is.EqualTo(new ComponentB
            {
                Byte = 255,
                Double = 3.14159265358979323846,
                Short = 32767
            }));

            Assert.That(containerD.IsComponentInherited<ComplexComponent>(), Is.True);
            var complexComponent = containerD.GetComponent<ComplexComponent>();
            Assert.That(complexComponent.Integer, Is.EqualTo(1));
            Assert.That(complexComponent.Float, Is.EqualTo(123.456f));
            Assert.That(complexComponent.String, Is.EqualTo("test"));
            Assert.That(complexComponent.Nested.Integer, Is.EqualTo(42));
            Assert.That(complexComponent.ListInteger, Is.EquivalentTo(new List<int> { 1, 1, 2, 3, 5, 8, 13 }));
        }

        /// <summary>
        /// Verify that <see cref="ComponentContainer{ITestComponent}"/> can override values from dependencies.
        /// </summary>
        [Test]
        public void ComponentOverrides()
        {
            var containerA = TestComponentContainer.CreateInstance();
            containerA.SetComponent(new ComponentA
            {
                Integer = 1,
                Float = 123.456f,
                String = "test"
            });

            var containerB = TestComponentContainer.CreateInstance();
            containerB.AddDependency(containerA);
            containerB.SetComponent(new ComponentB
            {
                Byte = 255,
                Double = 3.14159265358979323846,
                Short = 32767
            });

            var component = containerB.GetComponent<ComponentA>();
            component.Integer = 2;
            containerB.SetComponent(component);

            Assert.That(containerB.IsComponentOverridden<ComponentA>(), Is.True);
            Assert.That(containerB.GetComponent<ComponentA>(), Is.EqualTo(new ComponentA
            {
                Integer = 2,
                Float = 123.456f,
                String = "test"
            }));

            Assert.That(containerB.IsComponentOverridden<ComponentB>(), Is.False);
            Assert.That(containerB.GetComponent<ComponentB>(), Is.EqualTo(new ComponentB
            {
                Byte = 255,
                Double = 3.14159265358979323846,
                Short = 32767
            }));
        }

        /// <summary>
        /// Verify that <see cref="ComponentContainer{ITestComponent}"/> can override values from multiple dependencies.
        /// </summary>
        [Test]
        public void ComponentOverrides_FromMultipleDependencies()
        {
            var containerA = TestComponentContainer.CreateInstance();
            containerA.SetComponent(new ComponentA { Integer = 1 });

            var containerB = TestComponentContainer.CreateInstance();
            containerB.AddDependency(containerA);

            var componentA = containerB.GetComponent<ComponentA>();
            componentA.Float = 123.456f;
            containerB.SetComponent(componentA);

            var containerC = TestComponentContainer.CreateInstance();
            containerC.AddDependency(containerB);

            componentA = containerC.GetComponent<ComponentA>();
            componentA.String = "test";
            containerC.SetComponent(componentA);

            var containerD = TestComponentContainer.CreateInstance();
            containerD.AddDependency(containerC);

            var value = containerD.GetComponent<ComponentA>();
            Assert.That(value.Integer, Is.EqualTo(1));
            Assert.That(value.Float, Is.EqualTo(123.456f));
            Assert.That(value.String, Is.EqualTo("test"));
        }

        /// <summary>
        /// Verify that ComponentContainer can serialize, deserialize and reserialize to JSON without losing any values.
        /// </summary>
        [Test]
        public void ComponentSerialization()
        {
            var container = TestComponentContainer.CreateInstance();
            container.SetComponent(new ComplexComponent
            {
                Integer = 1,
                Float = 123.456f,
                String = "test",
                Nested = new ComponentA
                {
                    Integer = 42
                },
                ListInteger = new List<int> { 1, 1, 2, 3, 5, 8, 13 }
            });

            var json = container.SerializeToJson();
            Assert.That(json.Length, Is.GreaterThan(3));

            var deserializedContainer = TestComponentContainer.CreateInstance();
            TestComponentContainer.DeserializeFromJson(deserializedContainer, json);

            var component = deserializedContainer.GetComponent<ComplexComponent>();
            Assert.That(component.Integer, Is.EqualTo(1));
            Assert.That(component.Float, Is.EqualTo(123.456f));
            Assert.That(component.String, Is.EqualTo("test"));
            Assert.That(component.Nested.Integer, Is.EqualTo(42));
            Assert.That(component.ListInteger, Is.EquivalentTo(new List<int> { 1, 1, 2, 3, 5, 8, 13 }));

            var reserializedJson = deserializedContainer.SerializeToJson();
            Assert.That(reserializedJson, Is.EqualTo(json));
        }

        [Test]
        public void DeserializeInvalidJson_ShouldNotThrowException()
        {
            var container = TestComponentContainer.CreateInstance();
            LogAssert.Expect(LogType.Error, $"Failed to deserialize memory container of type '{typeof(TestComponentContainer).FullName}':\nInput json was invalid. ExpectedType=[Value] ActualType=[EndObject] ActualChar=['}}'] at Line=[1] at Character=[47]");
            TestComponentContainer.DeserializeFromJson(container, "{\"Dependencies\": [], \"Components\": [{\"$type\": }, {\"$type\": }]}");
        }

        [Test]
        public void DeserializeInvalidComponent_ShouldNotResetEntireBuildSettings()
        {
            var container = TestComponentContainer.CreateInstance();
            LogAssert.Expect(LogType.Error, $"Failed to deserialize memory container of type '{typeof(TestComponentContainer).FullName}':\nSystem.InvalidOperationException: PropertyContainer.Construct failed to construct DstType=[{typeof(ITestComponent).FullName}]. Could not resolve type from TypeName=[Some.InvalidComponent.Name, Unknown.Assembly].");
            TestComponentContainer.DeserializeFromJson(container, $"{{\"Dependencies\": [], \"Components\": [{{\"$type\": \"{typeof(ComponentA).FullName}, {typeof(ComponentA).Assembly.GetName().Name}\"}}, {{\"$type\": \"Some.InvalidComponent.Name, Unknown.Assembly\"}}]}}");
            Assert.That(container.HasComponent<ComponentA>(), Is.True);
        }

        [Test]
        public void DeserializeInvalidDependency_ShouldNotResetEntireBuildSettings()
        {
            var container = TestComponentContainer.CreateInstance();
            TestComponentContainer.DeserializeFromJson(container, $"{{\"Dependencies\": [null, \"\", \"bleh\"], \"Components\": [{{\"$type\": \"{typeof(ComponentA).FullName}, {typeof(ComponentA).Assembly.GetName().Name}\"}}]}}");
            Assert.That(container.HasComponent<ComponentA>(), Is.True);
            Assert.That(container.Dependencies.Count, Is.Zero);
        }

        [Test]
        public void DeserializeMultipleTimes_ShouldNotAppendData()
        {
            var container = TestComponentContainer.CreateInstance();
            Assert.That(container.HasComponent<ComponentA>(), Is.False);
            Assert.That(container.Components.Count, Is.Zero);
            TestComponentContainer.DeserializeFromJson(container, $"{{\"Dependencies\": [], \"Components\": [{{\"$type\": \"{typeof(ComponentA).FullName}, {typeof(ComponentA).Assembly.GetName().Name}\"}}]}}");
            Assert.That(container.HasComponent<ComponentA>(), Is.True);
            Assert.That(container.Components.Count, Is.EqualTo(1));
            TestComponentContainer.DeserializeFromJson(container, $"{{\"Dependencies\": [], \"Components\": [{{\"$type\": \"{typeof(ComponentA).FullName}, {typeof(ComponentA).Assembly.GetName().Name}\"}}]}}");
            Assert.That(container.HasComponent<ComponentA>(), Is.True);
            Assert.That(container.Components.Count, Is.EqualTo(1));
        }

        [Test]
        public void CanQuery_InterfaceType()
        {
            var container = TestComponentContainer.CreateInstance();
            container.SetComponent(new ComponentA { Float = 123.456f, Integer = 42, String = "foo" });

            Assert.That(container.HasComponent(typeof(ITestInterface)));
            Assert.That(container.TryGetComponent(typeof(ITestInterface), out var value), Is.True);
            Assert.That(value, Is.EqualTo(new ComponentA { Float = 123.456f, Integer = 42, String = "foo" }));
            Assert.That(container.RemoveComponent(typeof(ITestInterface)), Is.True);
        }

        [Test]
        public void CanQuery_AbstractType()
        {
            var container = TestComponentContainer.CreateInstance();
            container.SetComponent(new DerivedClass { Integer = 2, Float = 654.321f });

            Assert.That(container.HasComponent(typeof(AbstractClass)));
            Assert.That(container.TryGetComponent(typeof(AbstractClass), out var value), Is.True);

            var instance = value as DerivedClass;
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.Integer, Is.EqualTo(2));
            Assert.That(instance.Float, Is.EqualTo(654.321f));
            Assert.That(container.RemoveComponent(typeof(AbstractClass)), Is.True);
        }

        [Test]
        public void CannotSet_NullType()
        {
            var container = TestComponentContainer.CreateInstance();
            Assert.Throws<ArgumentNullException>(() =>
            {
                container.SetComponent(null, new ComponentA());
            });
        }

        [Test]
        public void CannotSet_ObjectType()
        {
            var container = TestComponentContainer.CreateInstance();
            Assert.Throws<InvalidOperationException>(() =>
            {
                container.SetComponent(typeof(object), new ComponentA());
            });
        }

        [Test]
        public void CannotSet_InterfaceType()
        {
            var container = TestComponentContainer.CreateInstance();
            Assert.Throws<InvalidOperationException>(() =>
            {
                container.SetComponent(typeof(ITestInterface), new ComponentA());
            });
        }

        [Test]
        public void CannotSet_AbstractType()
        {
            var container = TestComponentContainer.CreateInstance();
            Assert.Throws<InvalidOperationException>(() =>
            {
                container.SetComponent(typeof(AbstractClass), new DerivedClass());
            });
        }

        [Test]
        public void MissingDependencies_DoesNotThrow()
        {
            var settings = TestComponentContainer.CreateInstance();
            settings.SetComponent(new ComplexComponent());

            // We cannot directly add `null` using AddDependency, so we add it to the underlying list to simulate a
            // missing dependency (either added through UI or missing asset).
            settings.Dependencies.Add(null);
            var missingDependency = TestComponentContainer.CreateInstance();
            missingDependency.SetComponent(new ComplexComponent());
            settings.AddDependency(missingDependency);
            UnityEngine.Object.DestroyImmediate(missingDependency);

            Assert.DoesNotThrow(() => settings.TryGetComponent<ComplexComponent>(out _));
            Assert.DoesNotThrow(() => settings.GetComponent<ComplexComponent>());
            Assert.DoesNotThrow(() => settings.GetDependencies());
            Assert.DoesNotThrow(() => settings.HasComponent<ComplexComponent>());
        }

        [Test]
        public void AddingDestoyedDependency_Throws()
        {
            var settings = TestComponentContainer.CreateInstance();
            var missingDependency = TestComponentContainer.CreateInstance();
            UnityEngine.Object.DestroyImmediate(missingDependency);
            Assert.Throws<ArgumentNullException>(() => settings.AddDependency(missingDependency));
        }

        [Test]
        public void HasComponent()
        {
            var container = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentA()));
            Assert.That(container.HasComponent<ComponentA>(), Is.True);
            Assert.That(container.HasComponent<ComponentB>(), Is.False);
            Assert.Throws<ArgumentNullException>(() => container.HasComponent(null));
            Assert.Throws<InvalidOperationException>(() => container.HasComponent(typeof(object)));
            Assert.Throws<InvalidOperationException>(() => container.HasComponent(typeof(InvalidComponent)));
        }

        [Test]
        public void IsComponentInherited()
        {
            var containerA = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentA()));
            var containerB = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentB()));

            containerA.AddDependency(containerB);

            Assert.That(containerA.IsComponentInherited<ComponentA>(), Is.False);
            Assert.That(containerA.IsComponentInherited<ComponentB>(), Is.True);

            Assert.That(containerB.IsComponentInherited<ComponentA>(), Is.False);
            Assert.That(containerB.IsComponentInherited<ComponentB>(), Is.False);

            Assert.Throws<ArgumentNullException>(() => containerA.IsComponentInherited(null));
            Assert.Throws<InvalidOperationException>(() => containerA.IsComponentInherited(typeof(object)));
            Assert.Throws<InvalidOperationException>(() => containerA.IsComponentInherited(typeof(InvalidComponent)));
        }

        [Test]
        public void IsComponentOverridden()
        {
            var containerA = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentA()));
            var containerB = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentA()));

            containerA.AddDependency(containerB);

            Assert.That(containerA.IsComponentOverridden<ComponentA>(), Is.True);
            Assert.That(containerB.IsComponentOverridden<ComponentA>(), Is.False);

            Assert.Throws<ArgumentNullException>(() => containerA.IsComponentOverridden(null));
            Assert.Throws<InvalidOperationException>(() => containerA.IsComponentOverridden(typeof(object)));
            Assert.Throws<InvalidOperationException>(() => containerA.IsComponentOverridden(typeof(InvalidComponent)));
        }

        [Test]
        public void GetComponent()
        {
            var container = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentA()));
            Assert.That(container.GetComponent<ComponentA>(), Is.Not.Null);
            Assert.Throws<InvalidOperationException>(() => container.GetComponent<ComponentB>());
            Assert.Throws<ArgumentNullException>(() => container.GetComponent(null));
            Assert.Throws<InvalidOperationException>(() => container.GetComponent(typeof(object)));
            Assert.Throws<InvalidOperationException>(() => container.GetComponent(typeof(InvalidComponent)));
        }

        [Test]
        public void TryGetComponent()
        {
            var container = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentA()));
            Assert.That(container.TryGetComponent<ComponentA>(out var _), Is.True);
            Assert.That(container.TryGetComponent<ComponentB>(out var _), Is.False);
            Assert.That(container.TryGetComponent(null, out var _), Is.False);
            Assert.That(container.TryGetComponent(typeof(object), out var _), Is.False);
            Assert.That(container.TryGetComponent(typeof(InvalidComponent), out var _), Is.False);
        }

        [Test]
        public void GetComponents()
        {
            var containerA = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentA()));
            var containerB = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentB()));
            var complexContainer = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComplexComponent()));

            containerA.AddDependency(containerB);
            containerB.AddDependency(complexContainer);

            var containerAComponents = containerA.GetComponents();
            Assert.That(containerAComponents.Count, Is.EqualTo(3));
            Assert.That(containerAComponents.Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComponentA), typeof(ComponentB), typeof(ComplexComponent) }));

            var containerBComponents = containerB.GetComponents();
            Assert.That(containerBComponents.Count, Is.EqualTo(2));
            Assert.That(containerBComponents.Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComponentB), typeof(ComplexComponent) }));

            var complexContainerComponents = complexContainer.GetComponents();
            Assert.That(complexContainerComponents.Count, Is.EqualTo(1));
            Assert.That(complexContainerComponents.Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComplexComponent) }));
        }

        [Test]
        public void GetComponents_WithType()
        {
            var containerA = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentA()));
            var containerB = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentB()));
            var complexContainer = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComplexComponent()));

            containerA.AddDependency(containerB);
            containerB.AddDependency(complexContainer);

            Assert.That(containerA.GetComponents<ComponentA>().Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComponentA) }));
            Assert.That(containerA.GetComponents<ComponentB>().Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComponentB) }));
            Assert.That(containerA.GetComponents<ComplexComponent>().Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComplexComponent) }));

            Assert.That(containerB.GetComponents<ComponentA>(), Is.Empty);
            Assert.That(containerB.GetComponents<ComponentB>().Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComponentB) }));
            Assert.That(containerB.GetComponents<ComplexComponent>().Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComplexComponent) }));

            Assert.That(complexContainer.GetComponents<ComponentA>(), Is.Empty);
            Assert.That(complexContainer.GetComponents<ComponentB>(), Is.Empty);
            Assert.That(complexContainer.GetComponents<ComplexComponent>().Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComplexComponent) }));

            Assert.That(containerA.GetComponents<ITestInterface>().Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComponentA), typeof(ComponentB) }));
            Assert.That(containerB.GetComponents<ITestInterface>().Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComponentB) }));
            Assert.That(complexContainer.GetComponents<ITestInterface>(), Is.Empty);
        }

        [Test]
        public void SetComponent()
        {
            var container = TestComponentContainer.CreateInstance(c => c.SetComponent(new ComponentA()));
            Assert.That(container.Components.Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(ComponentA) }));
            Assert.Throws<ArgumentNullException>(() => container.SetComponent(null, default));
            Assert.Throws<InvalidOperationException>(() => container.SetComponent(typeof(object), default));
            Assert.Throws<InvalidOperationException>(() => container.SetComponent(typeof(InvalidComponent), default));
            Assert.Throws<InvalidOperationException>(() => container.SetComponent(typeof(ITestInterface), default));
        }

        [Test]
        public void RemoveComponent()
        {
            var container = TestComponentContainer.CreateInstance();
            container.SetComponent(new ComponentA());
            container.SetComponent(new ComponentB());
            container.SetComponent(new ComplexComponent());

            Assert.That(container.HasComponent<ComponentA>(), Is.True);
            Assert.That(container.HasComponent<ComponentB>(), Is.True);
            Assert.That(container.HasComponent<ComplexComponent>(), Is.True);
            Assert.That(container.Components.Count, Is.EqualTo(3));

            Assert.That(container.RemoveComponent<ComplexComponent>(), Is.True);
            Assert.That(container.Components.Count, Is.EqualTo(2));

            Assert.That(container.RemoveComponent<DerivedClass>(), Is.False);
            Assert.That(container.Components.Count, Is.EqualTo(2));

            Assert.That(container.RemoveComponent<ITestInterface>(), Is.True);
            Assert.That(container.Components.Count, Is.EqualTo(0));

            Assert.Throws<ArgumentNullException>(() => container.RemoveComponent(null));
            Assert.Throws<InvalidOperationException>(() => container.RemoveComponent(typeof(object)));
            Assert.Throws<InvalidOperationException>(() => container.RemoveComponent(typeof(InvalidComponent)));
        }

        [Test]
        public void HasDependency()
        {
            var containerA = TestComponentContainer.CreateInstance();
            var containerB = TestComponentContainer.CreateInstance();
            var containerC = TestComponentContainer.CreateInstance();

            containerA.AddDependency(containerB);
            containerB.AddDependency(containerC);

            Assert.That(containerA.HasDependency(containerA), Is.False);
            Assert.That(containerA.HasDependency(containerB), Is.True);
            Assert.That(containerA.HasDependency(containerC), Is.True);

            Assert.That(containerB.HasDependency(containerA), Is.False);
            Assert.That(containerB.HasDependency(containerB), Is.False);
            Assert.That(containerB.HasDependency(containerC), Is.True);

            Assert.That(containerC.HasDependency(containerA), Is.False);
            Assert.That(containerC.HasDependency(containerB), Is.False);
            Assert.That(containerC.HasDependency(containerC), Is.False);

            Assert.That(containerA.HasDependency(null), Is.False);
        }

        [Test]
        public void AddDependency()
        {
            var containerA = TestComponentContainer.CreateInstance();
            var containerB = TestComponentContainer.CreateInstance();
            Assert.That(containerA.AddDependency(containerB), Is.True);
            Assert.That(containerA.AddDependency(containerB), Is.False);
            Assert.That(containerA.Dependencies, Is.EqualTo(new[] { containerB }));
            Assert.Throws<ArgumentNullException>(() => containerA.AddDependency(null));
        }

        [Test]
        public void AddDependency_CannotAddSelfDependency()
        {
            var container = TestComponentContainer.CreateInstance();
            Assert.That(container.AddDependency(container), Is.False);
            Assert.That(container.Dependencies.Count, Is.Zero);
        }

        [Test]
        public void AddDependency_CannotAddCircularDependency()
        {
            var containerA = TestComponentContainer.CreateInstance();
            var containerB = TestComponentContainer.CreateInstance();
            var containerC = TestComponentContainer.CreateInstance();

            Assert.That(containerA.AddDependency(containerB), Is.True);
            Assert.That(containerB.AddDependency(containerA), Is.False);
            Assert.That(containerB.AddDependency(containerC), Is.True);
            Assert.That(containerC.AddDependency(containerA), Is.False);
            Assert.That(containerC.AddDependency(containerB), Is.False);

            Assert.That(containerA.GetDependencies().Count, Is.EqualTo(2));
            Assert.That(containerB.GetDependencies().Count, Is.EqualTo(1));
            Assert.That(containerC.GetDependencies().Count, Is.Zero);
        }

        [Test]
        public void GetDependencies()
        {
            var containerA = TestComponentContainer.CreateInstance();
            var containerB = TestComponentContainer.CreateInstance();
            var containerC = TestComponentContainer.CreateInstance();

            containerA.AddDependency(containerB);
            containerB.AddDependency(containerC);

            Assert.That(containerA.GetDependencies(), Is.EqualTo(new[] { containerB, containerC }));
            Assert.That(containerB.GetDependencies(), Is.EqualTo(new[] { containerC }));
            Assert.That(containerC.GetDependencies(), Is.Empty);
        }

        [Test]
        public void RemoveDependency()
        {
            var containerA = TestComponentContainer.CreateInstance();
            var containerB = TestComponentContainer.CreateInstance();

            containerA.AddDependency(containerB);

            Assert.That(containerA.RemoveDependency(containerB), Is.True);
            Assert.That(containerA.RemoveDependency(containerB), Is.False);
            Assert.That(containerB.RemoveDependency(containerA), Is.False);
            Assert.Throws<ArgumentNullException>(() => containerA.RemoveDependency(null));
        }

        [Test]
        public void ClearDependencies()
        {
            var containerA = TestComponentContainer.CreateInstance();
            var containerB = TestComponentContainer.CreateInstance();
            var containerC = TestComponentContainer.CreateInstance();

            containerA.AddDependency(containerB);
            containerA.AddDependency(containerC);

            Assert.That(containerA.Dependencies.Count, Is.EqualTo(2));
            Assert.DoesNotThrow(() => containerA.ClearDependencies());
            Assert.That(containerA.Dependencies, Is.Empty);
            Assert.DoesNotThrow(() => containerA.ClearDependencies());
            Assert.That(containerA.Dependencies, Is.Empty);
        }
    }
}
