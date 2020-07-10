using System;
using System.Collections.Generic;
using System.Text;
using Unity.Properties;
using Unity.Properties.Internal;

namespace Unity.Entities
{
    [Flags]
    enum ComponentPropertyType
    {
        None = 0,
        Component = 1 << 1,
        SharedComponent = 1 << 2,
        ChunkComponent = 1 << 3,
        HybridComponent = 1 << 4,
        Tag = 1 << 5,
        Buffer = 1 << 6,
        All = Component | SharedComponent | ChunkComponent | HybridComponent | Tag | Buffer
    }

    interface IComponentProperty : IProperty<EntityContainer>
    {
        int TypeIndex { get; }
        ComponentPropertyType Type { get; }
    }

    public readonly struct EntityContainer
    {
        static EntityContainer()
        {
            PropertyBagStore.AddPropertyBag(new EntityContainerPropertyBag());
        }

        public readonly EntityManager EntityManager;
        public readonly Entity Entity;
        public readonly bool IsReadOnly;

        public int GetComponentCount() => EntityManager.GetComponentCount(Entity);

        public EntityContainer(EntityManager entityManager, Entity entity, bool readOnly = true)
        {
            EntityManager = entityManager;
            Entity = entity;
            IsReadOnly = readOnly;
        }
    }

    class EntityContainerPropertyBag : PropertyBag<EntityContainer>, IPropertyNameable<EntityContainer>, IPropertyIndexable<EntityContainer>
    {
        class ComponentPropertyConstructor : IContainerTypeVisitor
        {
            public int TypeIndex { private get; set; }
            public bool IsReadOnly { private get; set; }

            public IComponentProperty Property { get; private set; }

            void IContainerTypeVisitor.Visit<TComponent>()
            {
                IComponentProperty CreateInstance(Type propertyType)
                    => (IComponentProperty)Activator.CreateInstance(propertyType.MakeGenericType(typeof(TComponent)), TypeIndex, IsReadOnly);

                var type = typeof(TComponent);
                if (typeof(IComponentData).IsAssignableFrom(type))
                {
                    if (TypeManager.IsChunkComponent(TypeIndex))
#if !UNITY_DISABLE_MANAGED_COMPONENTS
                        Property = CreateInstance(type.IsValueType
                        ? typeof(StructChunkComponentProperty<>)
                        : typeof(ClassChunkComponentProperty<>));
#else
                        Property = CreateInstance(typeof(StructChunkComponentProperty<>));
#endif
#if !UNITY_DISABLE_MANAGED_COMPONENTS
                    else if (TypeManager.IsManagedComponent(TypeIndex))
                        Property = CreateInstance(typeof(ClassComponentProperty<>));
#endif
                    else
                        Property = CreateInstance(typeof(StructComponentProperty<>));
                }
                else if (typeof(ISharedComponentData).IsAssignableFrom(type))
                    Property = CreateInstance(typeof(SharedComponentProperty<>));
                else if (typeof(IBufferElementData).IsAssignableFrom(type))
                    Property = CreateInstance(typeof(DynamicBufferProperty<>));
                else if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                    Property = CreateInstance(typeof(ManagedComponentProperty<>));
                else
                    throw new InvalidOperationException();
            }
        }

        abstract class ComponentProperty<TComponent> : Property<EntityContainer, TComponent>, IComponentProperty
        {
            public override string Name { get; } = SanitizedPropertyName(Properties.Editor.TypeUtility.GetTypeDisplayName(typeof(TComponent)));
            public override bool IsReadOnly { get; }
            public int TypeIndex { get; }
            public abstract ComponentPropertyType Type { get; }

            public ComponentProperty(int typeIndex, bool isReadOnly)
            {
                TypeIndex = typeIndex;
                IsReadOnly = isReadOnly;
            }

            public override TComponent GetValue(ref EntityContainer container)
            {
                return IsZeroSize ? default : DoGetValue(ref container);
            }

            public override void SetValue(ref EntityContainer container, TComponent value)
            {
                if (IsReadOnly) throw new NotSupportedException("Property is ReadOnly");

                if (IsZeroSize)
                    return;

                DoSetValue(ref container, value);
            }

            protected abstract TComponent DoGetValue(ref EntityContainer container);
            protected abstract void DoSetValue(ref EntityContainer container, TComponent value);
            protected abstract bool IsZeroSize { get; }
        }

        class SharedComponentProperty<TComponent> : ComponentProperty<TComponent> where TComponent : struct, ISharedComponentData
        {
            protected override bool IsZeroSize { get; } = false;
            public override ComponentPropertyType Type { get; } = ComponentPropertyType.SharedComponent;

            public SharedComponentProperty(int typeIndex, bool isReadOnly) : base(typeIndex, isReadOnly)
            {
            }

            protected override TComponent DoGetValue(ref EntityContainer container)
            {
                var entityManager = container.EntityManager;
                return entityManager.GetSharedComponentData<TComponent>(container.Entity);
            }

            protected override void DoSetValue(ref EntityContainer container, TComponent value)
            {
                var entityManager = container.EntityManager;
                entityManager.SetSharedComponentData(container.Entity, value);
            }
        }
        class StructComponentProperty<TComponent> : ComponentProperty<TComponent>
            where TComponent : struct, IComponentData
        {
            protected override bool IsZeroSize { get; } = TypeManager.IsZeroSized(TypeManager.GetTypeIndex<TComponent>());
            public override ComponentPropertyType Type => IsZeroSize ? ComponentPropertyType.Tag : ComponentPropertyType.Component;

            public StructComponentProperty(int typeIndex, bool isReadOnly) : base(typeIndex, isReadOnly)
            {
            }

            protected override TComponent DoGetValue(ref EntityContainer container)
            {
                var entityManager = container.EntityManager;
                return entityManager.GetComponentData<TComponent>(container.Entity);
            }

            protected override void DoSetValue(ref EntityContainer container, TComponent value)
            {
                var entityManager = container.EntityManager;
                entityManager.SetComponentData(container.Entity, value);
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        class ClassComponentProperty<TComponent> : ComponentProperty<TComponent>
            where TComponent : class, IComponentData
        {
            protected override bool IsZeroSize { get; } = TypeManager.IsZeroSized(TypeManager.GetTypeIndex<TComponent>());
            public override ComponentPropertyType Type => IsZeroSize ? ComponentPropertyType.Tag : ComponentPropertyType.Component;

            public ClassComponentProperty(int typeIndex, bool isReadOnly) : base(typeIndex, isReadOnly)
            {
            }

            protected override TComponent DoGetValue(ref EntityContainer container)
            {
                var entityManager = container.EntityManager;
                return entityManager.GetComponentData<TComponent>(container.Entity);
            }

            protected override void DoSetValue(ref EntityContainer container, TComponent value)
            {
                var entityManager = container.EntityManager;
                entityManager.SetComponentData(container.Entity, value);
            }
        }
#endif

        class ManagedComponentProperty<TComponent> : ComponentProperty<TComponent>
            where TComponent : UnityEngine.Object
        {
            protected override bool IsZeroSize { get; } = TypeManager.IsZeroSized(TypeManager.GetTypeIndex<TComponent>());
            public override ComponentPropertyType Type => IsZeroSize ? ComponentPropertyType.Tag : ComponentPropertyType.HybridComponent;

            public ManagedComponentProperty(int typeIndex, bool isReadOnly) : base(typeIndex, isReadOnly)
            {
            }

            protected override TComponent DoGetValue(ref EntityContainer container)
            {
                var entityManager = container.EntityManager;
                return entityManager.GetComponentObject<TComponent>(container.Entity);
            }

            protected override void DoSetValue(ref EntityContainer container, TComponent value)
            {
                var entityManager = container.EntityManager;
                entityManager.SetComponentObject(container.Entity, typeof(TComponent), value);
            }
        }

        class StructChunkComponentProperty<TComponent> : ComponentProperty<TComponent>
            where TComponent : struct, IComponentData
        {
            protected override bool IsZeroSize { get; } = TypeManager.IsZeroSized(TypeManager.GetTypeIndex<TComponent>());
            public override ComponentPropertyType Type => IsZeroSize ? ComponentPropertyType.Tag : ComponentPropertyType.ChunkComponent;

            public StructChunkComponentProperty(int typeIndex, bool isReadOnly) : base(typeIndex, isReadOnly)
            {
            }

            protected override TComponent DoGetValue(ref EntityContainer container)
            {
                var entityManager = container.EntityManager;
                return entityManager.GetChunkComponentData<TComponent>(container.Entity);
            }

            protected override void DoSetValue(ref EntityContainer container, TComponent value)
            {
                var entityManager = container.EntityManager;
                entityManager.SetChunkComponentData(entityManager.GetChunk(container.Entity), value);
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        class ClassChunkComponentProperty<TComponent> : ComponentProperty<TComponent>
            where TComponent : class, IComponentData
        {
            protected override bool IsZeroSize { get; } = TypeManager.IsZeroSized(TypeManager.GetTypeIndex<TComponent>());
            public override ComponentPropertyType Type => IsZeroSize ? ComponentPropertyType.Tag : ComponentPropertyType.ChunkComponent;

            public ClassChunkComponentProperty(int typeIndex, bool isReadOnly) : base(typeIndex, isReadOnly)
            {
            }

            protected override TComponent DoGetValue(ref EntityContainer container)
            {
                var entityManager = container.EntityManager;
                return entityManager.GetChunkComponentData<TComponent>(container.Entity);
            }

            protected override void DoSetValue(ref EntityContainer container, TComponent value)
            {
                var entityManager = container.EntityManager;
                entityManager.SetChunkComponentData(entityManager.GetChunk(container.Entity), value);
            }
        }
#endif

        class DynamicBufferProperty<TElement> : ComponentProperty<DynamicBufferContainer<TElement>>
            where TElement : struct, IBufferElementData
        {
            public override string Name => SanitizedPropertyName(Properties.Editor.TypeUtility.GetTypeDisplayName(typeof(TElement)));
            protected override bool IsZeroSize { get; } = TypeManager.IsZeroSized(TypeManager.GetTypeIndex<TElement>());
            public override ComponentPropertyType Type { get; } = ComponentPropertyType.Buffer;

            public DynamicBufferProperty(int typeIndex, bool isReadOnly) : base(typeIndex, isReadOnly)
            {
            }

            protected override DynamicBufferContainer<TElement> DoGetValue(ref EntityContainer container)
            {
                return new DynamicBufferContainer<TElement>(container, TypeIndex, IsReadOnly);
            }

            protected override void DoSetValue(ref EntityContainer container, DynamicBufferContainer<TElement> value)
            {
                // Nothing to do here, the container already proxies the data.
            }
        }

        readonly Dictionary<int, IComponentProperty> m_ReadOnlyPropertyCache = new Dictionary<int, IComponentProperty>();
        readonly Dictionary<int, IComponentProperty> m_ReadWritePropertyCache = new Dictionary<int, IComponentProperty>();

        readonly ComponentPropertyConstructor m_ComponentPropertyConstructor = new ComponentPropertyConstructor();

        internal override IEnumerable<IProperty<EntityContainer>> GetProperties(ref EntityContainer container)
        {
            return EnumerateProperties(container);
        }

        IEnumerable<IProperty<EntityContainer>> EnumerateProperties(EntityContainer container)
        {
            var count = container.GetComponentCount();
            var entityManager = container.EntityManager;
            for (var i = 0; i < count; i++)
            {
                var typeIndex = entityManager.GetComponentTypeIndex(container.Entity, i);
                var property = GetOrCreatePropertyForType(typeIndex, container.IsReadOnly);
                yield return property;
            }
        }

        bool IPropertyNameable<EntityContainer>.TryGetProperty(ref EntityContainer container, string name, out IProperty<EntityContainer> property)
        {
            foreach (var p in EnumerateProperties(container))
            {
                if (p.Name != name) continue;
                property = p;
                return true;
            }

            property = null;
            return false;
        }

        bool IPropertyIndexable<EntityContainer>.TryGetProperty(ref EntityContainer container, int index, out IProperty<EntityContainer> property)
        {
            var entityManager = container.EntityManager;
            property = GetOrCreatePropertyForType(entityManager.GetComponentTypeIndex(container.Entity, index), container.IsReadOnly);
            return true;
        }

        IComponentProperty GetOrCreatePropertyForType(int typeIndex, bool isReadOnly)
        {
            var cache = isReadOnly ? m_ReadOnlyPropertyCache : m_ReadWritePropertyCache;

            if (cache.TryGetValue(typeIndex, out var property))
                return property;

            m_ComponentPropertyConstructor.TypeIndex = typeIndex;
            m_ComponentPropertyConstructor.IsReadOnly = isReadOnly;
            PropertyBagStore.GetPropertyBag(TypeManager.GetType(typeIndex)).Accept(m_ComponentPropertyConstructor);
            cache.Add(typeIndex, m_ComponentPropertyConstructor.Property);
            return m_ComponentPropertyConstructor.Property;
        }

        static string SanitizedPropertyName(string typeDisplayName)
        {
            return typeDisplayName.Replace(".", "_");
        }
    }
}
