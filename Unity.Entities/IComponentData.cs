using System;

namespace Unity.Entities
{
    /// <summary>
    /// This interface marks structs as 'unmanaged components' and classes as 'managed components'.
    /// </summary>
    /// <remarks>
    /// See https://docs.unity3d.com/Packages/com.unity.entities@latest/index.html?subfolder=/manual/ecs_components.html
    /// </remarks>
    public interface IComponentData
    {
    }

    /// <summary>
    /// An interface for creating structs that can be stored in a <see cref="DynamicBuffer{T}"/>.
    /// </summary>
    /// <remarks>
    /// See [Dynamic Buffers](xref:ecs-dynamic-buffers) for additional information.
    /// </remarks>
    public interface IBufferElementData
    {
    }

    /// <summary>
    /// Specifies the maximum number of elements to store inside a chunk.
    /// </summary>
    /// <remarks>
    /// Use this attribute on the declaration of your IBufferElementData subtype:
    ///
    /// <code>
    /// [InternalBufferCapacity(10)]
    /// public struct FloatBufferElement : IBufferElementData
    /// {
    ///     public float Value;
    /// }
    /// </code>
    ///
    /// All <see cref="DynamicBuffer{T}"/> with this type of element store the specified number of elements inside the
    /// chunk along with other component types in the same archetype. When the number of elements in the buffer exceeds
    /// this limit, the entire buffer is moved outside the chunk.
    ///
    /// [DefaultBufferCapacityNumerator](xref:Unity.Entities.TypeManager.DefaultBufferCapacityNumerator) defines
    /// the default number of elements.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Struct)]
    public class InternalBufferCapacityAttribute : Attribute
    {
        /// <summary>
        /// The number of elements stored inside the chunk.
        /// </summary>
        public readonly int Capacity;

        /// <summary>
        /// The number of elements stored inside the chunk.
        /// </summary>
        /// <param name="capacity"></param>
        public InternalBufferCapacityAttribute(int capacity)
        {
            Capacity = capacity;
        }
    }

    /// <summary>
    /// Specifies the maximum number of components of a type that can be stored in the same chunk.
    /// </summary>
    /// <remarks>Place this attribute on the declaration of a component, such as <see cref="IComponentData"/>, to
    /// limit the number of entities with that component which can be stored in a single chunk. Note that the actual
    /// limit on the number of entities in a chunk can be smaller, based on the actual size of all the components in the
    /// same <see cref="EntityArchetype"/> as the component defining this limit.
    ///
    /// If an archetype contains more than one component type specifying a chunk capacity limit, then the lowest limit
    /// is used.</remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class MaximumChunkCapacityAttribute : Attribute
    {
        /// <summary>
        /// The maximum number of entities having this component type in an <see cref="ArchetypeChunk"/>.
        /// </summary>
        public readonly int Capacity;

        /// <summary>
        /// The maximum number of entities having this component type in an <see cref="ArchetypeChunk"/>.
        /// </summary>
        /// <param name="capacity"></param>
        public MaximumChunkCapacityAttribute(int capacity)
        {
            Capacity = capacity;
        }
    }

    /// <summary>
    /// States that a component type is serializable.
    /// </summary>
    /// <remarks>
    /// By default, ECS does not support storing pointer types in chunks. Apply this attribute to a component declaration
    /// to allow the use of pointers as fields in the component.
    ///
    /// Note that ECS does not perform any pre- or post-serialization processing to maintain pointer validity. When
    /// using this attribute, your code assumes responsibility for handling pointer serialization and deserialization.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class ChunkSerializableAttribute : Attribute
    {
    }

    // [TODO: Document shared components with Jobs...]
    /// <summary>
    /// An interface for a component type whose value is shared across all entities with the same value.
    /// </summary>
    /// <remarks>
    /// See https://docs.unity3d.com/Packages/com.unity.entities@latest/index.html?subfolder=/manual/shared_component_data.html
    /// </remarks>
    public interface ISharedComponentData
    {
    }

    /// <summary>
    /// An interface for a component type that stores system-specific data.
    /// </summary>
    /// <remarks>
    /// See [System State Components](xref:ecs-system-state-component-data) for additional information.
    /// </remarks>
    public interface ISystemStateComponentData : IComponentData
    {
    }

    /// <summary>
    /// An interface for a component type that stores system-specific data in a buffer.
    /// </summary>
    /// <seealso cref="ISystemStateComponentData"/>
    /// <seealso cref="IBufferElementData"/>
    public interface ISystemStateBufferElementData : IBufferElementData
    {
    }

    /// <summary>
    /// An interface for a component type that stores shared system-specific data.
    /// </summary>
    /// <seealso cref="ISystemStateComponentData"/>
    /// <seealso cref="ISharedComponentData"/>
    public interface ISystemStateSharedComponentData : ISharedComponentData
    {
    }

    /// <summary>
    /// An interface for a component type which allows the component to be Enabled and Disabled
    /// </summary>
    /// <remarks>
    /// This interface is marked as "internal" during development of the feature. It will be made public when the feature is complete.
    /// </remarks>
    internal interface IEnableableComponent
    {
    }

    /// <summary>
    /// Disables the entity.
    /// </summary>
    /// <remarks> By default, an <see cref="EntityQuery"/> ignores all entities that have a Disabled component. You
    /// can override this default behavior by setting the <see cref="EntityQueryOptions.IncludeDisabled"/> flag of the
    /// <see cref="EntityQueryDesc"/> object used to create the query. When using the EntityQueryBuilder class
    /// in a ComponentSystem, set this flag by calling the <see cref="EntityQueryBuilder.With(EntityQueryOptions)"/>
    /// function.</remarks>
    public struct Disabled : IComponentData
    {
    }

    /// <summary>
    /// Marks the entity as a prefab, which implicitly disables the entity.
    /// </summary>
    /// <remarks> By default, an <see cref="EntityQuery"/> ignores all entities that have a Prefab component. You
    /// can override this default behavior by setting the <see cref="EntityQueryOptions.IncludePrefab"/> flag of the
    /// <see cref="EntityQueryDesc"/> object used to create the query. When using the EntityQueryBuilder class
    /// in a ComponentSystem, set this flag by calling the <see cref="EntityQueryBuilder.With(EntityQueryOptions)"/>
    /// function.</remarks>
    public struct Prefab : IComponentData
    {
    }

    /// <summary>
    /// Marks the entity as an asset, which is used for the Export phase of GameObject conversion.
    /// </summary>
    public struct Asset : IComponentData
    {
    }

    /// <summary>
    /// The LinkedEntityGroup buffer makes the entity be the root of a set of connected entities.
    /// </summary>
    /// <remarks>
    /// Referenced Prefabs automatically add a LinkedEntityGroup with the complete child hierarchy.
    /// EntityManager.Instantiate uses LinkedEntityGroup to instantiate the whole set of entities automatically.
    /// EntityManager.SetEnabled uses LinkedEntityGroup to enable the whole set of entities.
    /// </remarks>
    public struct LinkedEntityGroup : IBufferElementData
    {
        /// <summary>
        /// A child entity.
        /// </summary>
        public Entity Value;

        /// <summary>
        /// Provides implicit conversion of an <see cref="Entity"/> to a LinkedEntityGroup element.
        /// </summary>
        /// <param name="e">The entity to convert</param>
        /// <returns>A new buffer element.</returns>
        public static implicit operator LinkedEntityGroup(Entity e)
        {
            return new LinkedEntityGroup {Value = e};
        }
    }

    /// <summary>
    /// A Unity-defined shared component assigned to all entities in the same subscene.
    /// </summary>
    [Serializable]
    public struct SceneTag : ISharedComponentData, IEquatable<SceneTag>
    {
        /// <summary>
        /// The root entity of the subscene.
        /// </summary>
        public Entity  SceneEntity;

        /// <summary>
        /// A unique hash code for comparison.
        /// </summary>
        /// <returns>The scene entity has code.</returns>
        public override int GetHashCode()
        {
            return SceneEntity.GetHashCode();
        }

        /// <summary>
        /// Two SceneTags are equal if they have the same root subscene entity.
        /// </summary>
        /// <param name="other">The other SceneTag.</param>
        /// <returns>True if both SceneTags refer to the same Subscene. False, otherwise.</returns>
        public bool Equals(SceneTag other)
        {
            return SceneEntity == other.SceneEntity;
        }

        /// <summary>
        /// A string for logging.
        /// </summary>
        /// <returns>A string identifying the root subscene entity.</returns>
        public override string ToString()
        {
            return $"SubSceneTag: {SceneEntity}";
        }
    }
}
