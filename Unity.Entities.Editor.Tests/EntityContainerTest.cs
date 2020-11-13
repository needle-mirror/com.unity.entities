using System;
using NUnit.Framework;
using Unity.Entities.Tests;
using Unity.Properties;
using Unity.Properties.Adapters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Entities.Editor.Tests
{
    [TestFixture]
    class EntityContainerTest : ECSTestsFixture
    {
        [Flags]
        enum Category
        {
            None = 0,
            StructData = 1,
            ClassData = 2,
            StructChunkData = 4,
            ClassChunkData = 8,
            BufferData = 16,
            ManagedData = 32,
            SharedData = 64
        }

        struct StructComponentData : IComponentData
        {
            public Category Category;
            public float FloatValue;
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        class ClassComponentData : IComponentData
        {
            public Category Category;
            public float FloatValue;
        }

        class ClassComponentData2 : IComponentData
        {
            public Category Category;
            public float FloatValue;
        }
#endif

        struct StructChunkData : IComponentData
        {
            public int Value;
            public Category Category;
            public float FloatValue;
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        class ClassChunkData : IComponentData
        {
            public Category Category;
            public GameObject GameObject;
            public float FloatValue;
        }
#endif

        struct SharedComponentData : ISharedComponentData
        {
            public Category Category;
            public float FloatValue;
        }

        struct BufferElement : IBufferElementData
        {
            public Category Category;
            public int Value;
            public float FloatValue;
        }

        class TestComponentCategoryVisitor : PropertyVisitor,
            IVisit<StructComponentData>,
            IVisit<StructChunkData>,
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            IVisit<ClassComponentData>,
            IVisit<ClassComponentData2>,
            IVisit<ClassChunkData>,
#endif
            IVisit<SharedComponentData>,
            IVisit<BufferElement>,
            IVisit<DynamicBufferContainer<BufferElement>>,
            IVisit<Transform>,
            IVisit<GameObject>
        {
            public GameObject GameObject { private get; set; }

            public TestComponentCategoryVisitor()
            {
                AddAdapter(this);
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, StructComponentData> property, ref TContainer container, ref StructComponentData data)
            {
                Assert.That(data.Category, Is.EqualTo(Category.StructData));
                return VisitStatus.Stop;
            }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            public VisitStatus Visit<TContainer>(Property<TContainer, ClassComponentData> property, ref TContainer container, ref ClassComponentData data)
            {
                Assert.That(data.Category, Is.EqualTo(Category.ClassData));
                return VisitStatus.Stop;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, ClassComponentData2> property, ref TContainer container, ref ClassComponentData2 data)
            {
                Assert.That(data.Category, Is.EqualTo(Category.ClassData));
                return VisitStatus.Stop;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, ClassChunkData> property, ref TContainer container, ref ClassChunkData data)
            {
                Assert.That(data.Category, Is.EqualTo(Category.ClassChunkData));
                Assert.That(data.GameObject, Is.EqualTo(GameObject));
                return VisitStatus.Stop;
            }

#endif

            public VisitStatus Visit<TContainer>(Property<TContainer, SharedComponentData> property, ref TContainer container, ref SharedComponentData data)
            {
                Assert.That(data.Category, Is.EqualTo(Category.SharedData));
                return VisitStatus.Stop;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, Transform> property, ref TContainer container, ref Transform transform)
            {
                Assert.That(transform.localPosition, Is.EqualTo(Vector3.right));
                Assert.That(transform.localScale, Is.EqualTo(Vector3.up));
                Assert.That(transform.localRotation, Is.EqualTo(Quaternion.Euler(15, 30, 45)));
                return VisitStatus.Stop;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, StructChunkData> property, ref TContainer container, ref StructChunkData data)
            {
                Assert.That(data.Value, Is.EqualTo(25));
                Assert.That(data.Category, Is.EqualTo(Category.StructChunkData));
                return VisitStatus.Stop;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, DynamicBufferContainer<BufferElement>> property, ref TContainer container, ref DynamicBufferContainer<BufferElement> data)
            {
                for (var i = 0; i < data.Count; ++i)
                    Assert.That(data[i].Value, Is.EqualTo(i));
                return VisitStatus.Unhandled;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, BufferElement> property, ref TContainer container, ref BufferElement data)
            {
                Assert.That(data.Category, Is.EqualTo(Category.BufferData));
                return VisitStatus.Stop;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, GameObject> property, ref TContainer container, ref GameObject value)
            {
                Assert.That(value, Is.EqualTo(GameObject));
                return VisitStatus.Stop;
            }
        }

        class TestDataWriteBackVisitor : PropertyVisitor,
            IVisit<StructComponentData>,
            IVisit<StructChunkData>,
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            IVisit<ClassComponentData>,
            IVisit<ClassComponentData2>,
            IVisit<ClassChunkData>,
#endif
            IVisit<SharedComponentData>,
            IVisit<BufferElement>,
            IVisit<DynamicBufferContainer<BufferElement>>
        {
            public GameObject GameObject { private get; set; }
            public bool Read { get; set; }
            public float Value { get; }
            public Category Category { get; }

            public TestDataWriteBackVisitor(bool read, float value, Category category)
            {
                Read = read;
                Value = value;
                Category = category;
                AddAdapter(this);
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, StructComponentData> property, ref TContainer container, ref StructComponentData data)
            {
                if (!Category.HasFlag(Category.StructData))
                    return VisitStatus.Stop;
                if (Read)
                    Assert.That(data.FloatValue, Is.EqualTo(Value));
                else
                    data.FloatValue = Value;
                return VisitStatus.Stop;
            }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            public VisitStatus Visit<TContainer>(Property<TContainer, ClassComponentData> property, ref TContainer container, ref ClassComponentData data)
            {
                if (!Category.HasFlag(Category.ClassData))
                    return VisitStatus.Stop;
                if (Read)
                    Assert.That(data.FloatValue, Is.EqualTo(Value));
                else
                    data.FloatValue = Value;
                return VisitStatus.Stop;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, ClassComponentData2> property, ref TContainer container, ref ClassComponentData2 data)
            {
                if (!Category.HasFlag(Category.ClassData))
                    return VisitStatus.Stop;
                if (Read)
                    Assert.That(data.FloatValue, Is.EqualTo(Value));
                else
                    data.FloatValue = Value;
                return VisitStatus.Stop;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, ClassChunkData> property, ref TContainer container, ref ClassChunkData data)
            {
                if (!Category.HasFlag(Category.ClassChunkData))
                    return VisitStatus.Stop;
                if (Read)
                    Assert.That(data.FloatValue, Is.EqualTo(Value));
                else
                    data.FloatValue = Value;
                return VisitStatus.Stop;
            }
#endif

            public VisitStatus Visit<TContainer>(Property<TContainer, SharedComponentData> property, ref TContainer container, ref SharedComponentData data)
            {
                if (!Category.HasFlag(Category.SharedData))
                    return VisitStatus.Stop;
                if (Read)
                    Assert.That(data.FloatValue, Is.EqualTo(Value));
                else
                    data.FloatValue = Value;
                return VisitStatus.Stop;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, StructChunkData> property, ref TContainer container, ref StructChunkData data)
            {
                if (!Category.HasFlag(Category.StructChunkData))
                    return VisitStatus.Stop;
                if (Read)
                    Assert.That(data.FloatValue, Is.EqualTo(Value));
                else
                    data.FloatValue = Value;
                return VisitStatus.Stop;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, DynamicBufferContainer<BufferElement>> property, ref TContainer container, ref DynamicBufferContainer<BufferElement> data)
            {
                if (!Category.HasFlag(Category.BufferData))
                    return VisitStatus.Stop;
                if (Read)
                    Assert.That(data.Count, Is.EqualTo(51));
                else
                    data.Add( new BufferElement{ FloatValue = data.Count * Value });

                return VisitStatus.Unhandled;
            }

            public VisitStatus Visit<TContainer>(Property<TContainer, BufferElement> property, ref TContainer container, ref BufferElement data)
            {
                if (!Category.HasFlag(Category.BufferData))
                    return VisitStatus.Stop;
                if (Read)
                    Assert.That(data.FloatValue, Is.EqualTo(Value * (property as IListElementProperty).Index));
                else
                    data.FloatValue = Value * (property as IListElementProperty).Index;
                return VisitStatus.Stop;
            }
        }

        class InvalidEntityVisitor : PropertyVisitor
        {
            protected override void VisitProperty<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value)
            {
                throw new InvalidOperationException();
            }
        }

        GameObject _gameObject;

        public override void Setup()
        {
            base.Setup();
            _gameObject = new GameObject();
        }

        public override void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
            base.TearDown();
        }

        [Test]
        public void EntityContainer_WhenVisited_ReturnsTheCorrectValuesForAllComponentTypes()
        {
            var entity = m_Manager.CreateEntity(typeof(StructComponentData),
#if !UNITY_DISABLE_MANAGED_COMPONENTS
                typeof(ClassComponentData),
#endif
                typeof(SharedComponentData));

            m_Manager.AddChunkComponentData<StructChunkData>(entity);
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            m_Manager.AddChunkComponentData<ClassChunkData>(entity);
#endif

            _gameObject.transform.localPosition = Vector3.right;
            _gameObject.transform.localScale = Vector3.up;
            _gameObject.transform.localRotation = Quaternion.Euler(15, 30, 45);

            m_Manager.AddComponentObject(entity, _gameObject.transform);
            m_Manager.AddComponentObject(entity, _gameObject);
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            m_Manager.AddComponentObject(entity, new ClassComponentData2 { Category = Category.ClassData });
#endif

            var buffer = m_Manager.AddBuffer<BufferElement>(entity);
            for (var i = 0; i < 50; ++i)
                buffer.Add(new BufferElement {Category = Category.BufferData, Value = i});

            m_Manager.SetSharedComponentData(entity, new SharedComponentData { Category = Category.SharedData });
            m_Manager.SetChunkComponentData(m_Manager.GetChunk(entity), new StructChunkData { Value = 25, Category = Category.StructChunkData });
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            m_Manager.SetChunkComponentData(m_Manager.GetChunk(entity), new ClassChunkData { GameObject = _gameObject, Category = Category.ClassChunkData });
            m_Manager.SetComponentData(entity, new ClassComponentData { Category = Category.ClassData });
#endif
            m_Manager.SetComponentData(entity, new StructComponentData { Category = Category.StructData });
            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, true), new TestComponentCategoryVisitor { GameObject = _gameObject});
        }

         [Test]
        public void EntityContainer_WhenVisited_CanReadAndWriteData()
        {
            var entity = m_Manager.CreateEntity(typeof(StructComponentData),
#if !UNITY_DISABLE_MANAGED_COMPONENTS
                typeof(ClassComponentData),
#endif
                typeof(SharedComponentData));

            m_Manager.AddChunkComponentData<StructChunkData>(entity);
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            m_Manager.AddChunkComponentData<ClassChunkData>(entity);
#endif

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            m_Manager.AddComponentObject(entity, new ClassComponentData2 { Category = Category.ClassData });
#endif

            var buffer = m_Manager.AddBuffer<BufferElement>(entity);
            for (var i = 0; i < 50; ++i)
                buffer.Add(new BufferElement {Category = Category.BufferData, FloatValue = i});

            m_Manager.SetSharedComponentData(entity, new SharedComponentData { Category = Category.SharedData });
            m_Manager.SetChunkComponentData(m_Manager.GetChunk(entity), new StructChunkData { Value = 25, Category = Category.StructChunkData });
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            m_Manager.SetChunkComponentData(m_Manager.GetChunk(entity), new ClassChunkData { GameObject = _gameObject, Category = Category.ClassChunkData });
            m_Manager.SetComponentData(entity, new ClassComponentData { Category = Category.ClassData });
#endif
            m_Manager.SetComponentData(entity, new StructComponentData { Category = Category.StructData });

            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(false, 25, Category.StructChunkData));
            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(true, 25, Category.StructChunkData));
            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(false, 25, Category.ClassChunkData));
            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(true, 25, Category.ClassChunkData));

            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(false, 25, Category.StructData));
            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(true, 25, Category.StructData));
            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(false, 25, Category.ClassData));
            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(true, 25, Category.ClassData));

            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(false, 25, Category.SharedData));
            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(true, 25, Category.SharedData));

            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(false, 25, Category.BufferData));
            PropertyContainer.Visit(new EntityContainer(m_Manager, entity, false), new TestDataWriteBackVisitor(true, 25, Category.BufferData));
        }

        [Test]
        public void EntityContainer_WhenVisitingAnInvalidEntity_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => PropertyContainer.Visit(new EntityContainer(m_Manager, Entity.Null, false), new InvalidEntityVisitor()));

            var entity = m_Manager.CreateEntity(typeof(StructComponentData), typeof(BufferElement));
            var container = new EntityContainer(m_Manager, entity, false);

            // Validate that we are actually visiting something.
            Assert.Throws<InvalidOperationException>(() => PropertyContainer.Visit(container, new InvalidEntityVisitor()));

            // We can find the correct path and this will not throw.
            Assert.That(PropertyContainer.IsPathValid(ref container, new PropertyPath($"{nameof(EntityContainerTest)}_{nameof(StructComponentData)}")), Is.True);
            Assert.That(PropertyContainer.IsPathValid(ref container, new PropertyPath($"{nameof(EntityContainerTest)}_{nameof(BufferElement)}")), Is.True);

            m_Manager.DestroyEntity(entity);

            Assert.DoesNotThrow(() => PropertyContainer.Visit(container, new InvalidEntityVisitor()));

            // We cannot find the correct path anymore and this will not throw.
            Assert.That(PropertyContainer.IsPathValid(ref container, new PropertyPath($"{nameof(EntityContainerTest)}_{nameof(StructComponentData)}")), Is.False);
            Assert.That(PropertyContainer.IsPathValid(ref container, new PropertyPath($"{nameof(EntityContainerTest)}_{nameof(BufferElement)}")), Is.False);
        }
    }
}
