using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Properties;
using Unity.Properties.Internal;

namespace Unity.Entities
{
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
        interface IComponentProperty : IProperty<EntityContainer>
        {
        }
        
        class ComponentPropertyConstructor : IContainerTypeVisitor
        {
            public int TypeIndex { private get; set; }
            public bool IsReadOnly { private get; set; }
            
            public IComponentProperty Property { get; private set; }
            
            void IContainerTypeVisitor.Visit<TComponent>()
            {
                if (TypeManager.IsBuffer(TypeIndex))
                {
                    Property = new DynamicBufferProperty<TComponent>(TypeIndex, IsReadOnly);
                }
                else
                {
                    Property = new ComponentProperty<TComponent>(TypeIndex, IsReadOnly);
                }
            }
        }
        
        unsafe class ComponentProperty<TComponent> : Property<EntityContainer, TComponent>, IComponentProperty
        {
            readonly int m_TypeIndex;

            public override string Name => GetTypeName(typeof(TComponent));
            public override bool IsReadOnly { get; }

            public ComponentProperty(int typeIndex, bool isReadOnly)
            {
                m_TypeIndex = typeIndex;
                IsReadOnly = isReadOnly;
            }
            
            public override TComponent GetValue(ref EntityContainer container)
            {
                if (TypeManager.IsSharedComponent(m_TypeIndex))
                    return (TComponent) container.EntityManager.GetSharedComponentData(container.Entity, m_TypeIndex);

                if (TypeManager.IsManagedComponent(m_TypeIndex))
                    return container.EntityManager.GetComponentObject<TComponent>(container.Entity);
                
                return TypeManager.GetTypeInfo(m_TypeIndex).IsZeroSized ? default : Unsafe.AsRef<TComponent>(container.EntityManager.GetComponentDataRawRO(container.Entity, m_TypeIndex));
            }
            
            public override void SetValue(ref EntityContainer container, TComponent value)
            {
                if (IsReadOnly) throw new NotSupportedException("Property is ReadOnly");
                throw new NotImplementedException();
            }
        }
        
        unsafe class DynamicBufferProperty<TElement> : Property<EntityContainer, DynamicBufferContainer<TElement>>, IComponentProperty
        {
            readonly int m_TypeIndex;

            public override string Name => GetTypeName(typeof(TElement));
            public override bool IsReadOnly { get; }

            public DynamicBufferProperty(int typeIndex, bool isReadOnly)
            {
                m_TypeIndex = typeIndex;
                IsReadOnly = isReadOnly;
            }
            
            public override DynamicBufferContainer<TElement> GetValue(ref EntityContainer container)
            {
                if (IsReadOnly)
                {
                    return new DynamicBufferContainer<TElement>((BufferHeader*) container.EntityManager.GetComponentDataRawRO(container.Entity, m_TypeIndex), IsReadOnly);
                }
                else
                {
                    return new DynamicBufferContainer<TElement>((BufferHeader*) container.EntityManager.GetComponentDataRawRW(container.Entity, m_TypeIndex), IsReadOnly);
                }
            }
            
            public override void SetValue(ref EntityContainer container, DynamicBufferContainer<TElement> value)
            {
                if (IsReadOnly) throw new NotSupportedException("Property is ReadOnly");
                throw new NotImplementedException();
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

            for (var i = 0; i < count; i++)
            {
                var typeIndex = container.EntityManager.GetComponentTypeIndex(container.Entity, i);
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
            property = GetOrCreatePropertyForType(container.EntityManager.GetComponentTypeIndex(container.Entity, index), container.IsReadOnly);
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
            
        static string GetTypeName(Type type)
        {
            var index = 0;
            return GetTypeName(type, type.GetGenericArguments(), ref index);
        }

        static string GetTypeName(Type type, IReadOnlyList<Type> args, ref int argIndex)
        {
            var name = type.Name;

            if (type.IsGenericParameter)
            {
                return name;
            }
            
            if (type.IsNested)
            {
                name = $"{GetTypeName(type.DeclaringType, args, ref argIndex)}.{name}";
            }
            
            if (type.IsGenericType)
            {
                var tickIndex = name.IndexOf('`');
                
                if (tickIndex > -1)
                    name = name.Remove(tickIndex);
                
                var genericTypes = type.GetGenericArguments();
                var genericTypeNames = new StringBuilder();
                
                for (var i = 0; i < genericTypes.Length && argIndex < args.Count; i++, argIndex++)
                {
                    if (i != 0) genericTypeNames.Append(", ");
                    genericTypeNames.Append(GetTypeName(args[argIndex]));
                }

                if (genericTypeNames.Length > 0)
                {
                    name = $"{name}<{genericTypeNames}>";
                }
            }

            return name;
        }
    }
}
