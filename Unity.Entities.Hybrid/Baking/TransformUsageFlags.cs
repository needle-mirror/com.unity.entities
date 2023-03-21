using System;

namespace Unity.Entities
{
    /// <summary>
    /// Controls how Transform components on GameObjects are converted to entity data. These flags help to
    /// reduce the number of unnecessary transform components in baked entities based on their intended use at runtime.
    /// The Dynamic flag will replicate as close as possible the GameObject data and structure.
    /// </summary>
    /// <remarks>
    /// These flags are used whenever an entity is requested during conversion, for example, via
    /// <see cref="IBaker.GetEntity"/>. Bakers for default GameObject components will add the appropriate
    /// TransformUsageFlags too. For example in the case of the baker for MeshRenderer, Renderable will be added as a
    /// TransformUsageFlag.
    ///
    /// Multiple bakers can indicate different TransformUsageFlags for the same entity. All those flags are combined
    /// together before the transform components are added to the entity. For example if a baker requests for an entity
    /// to be Dynamic and another baker requests for the same entity to be in WorldSpace, the entity will be considered
    /// to be Dynamic and in WorldSpace when the transform components are added to the entity.
    ///
    /// These are a few examples on how to use TransformUsageFlags can help to reduce the number of unnecessary
    /// transforms components on entities:
    ///
    /// Let's assume that we got a GameObject representing a building and that building contains a child GameObject that
    /// is a window (these entities are not going to move at runtime). If both entities are only marked as Renderable,
    /// then they will not need to be in a hierarchy and all their transform information can be combined in a
    /// <see cref="LocalToWorld"/> component (in WorldSpace). In this case both entities will only have a <see cref="LocalToWorld"/>
    /// component and they will not have <see cref="LocalTransform"/> and <see cref="Parent"/> unnecessarily.
    ///
    /// In another example, the same window GameObject could be part of a ship instead of a building. In that case,
    /// the ship will be marked as Dynamic and the window will still be Renderable. The window entity will end up
    /// with all the required transform components to follow the ship around when it sails (<see cref="LocalToWorld"/>,
    /// <see cref="LocalTransform"/> and <see cref="Parent"/>).
    ///
    /// A building GameObject could have a helicopter GameObject on the roof (as a child). In this case the building is
    /// still Renderable, but the helicopter will be marked as Dynamic as it can take off. The helicopter will have the
    /// transform components to be moved (<see cref="LocalToWorld"/>, <see cref="LocalTransform"/>), but it will not be
    /// parented to the building and their transform data will be in world space.
    ///
    /// In the case of a ship with a helicopter, both will be marked as dynamic and the helicopter entity will be a
    /// child of the ship, so it will follow the ship around when it sails. In this particular case when the helicopter
    /// takes off, the hierarchy will need to be broken manually at runtime and the transform data converted to world
    /// space.
    ///
    /// If the helicopter can shoot some bullets, the bullet entity prefab should be marked as Dynamic so it can be
    /// instantiated at the right position and moved.
    ///
    /// There is also a case where an Entity might be stripped out from the final world during baking. This will happen
    /// when there is no baker adding a TransformUsageFlag to it (TranformUsageFlags.None counts as adding a
    /// TransformUsageFlag). An example of this is a GameObject that is created in the editor hierarchy to group their
    /// children at authoring time for organizational purposes, but has no use at runtime. In this case, the children
    /// would be moved to world space. There is an exception to this stripping rule, if an entity that got no
    /// TransformUsageFlags has a Dynamic parent and Dynamic children, then that entity will be considered Dynamic as
    /// well and it will not be stripped out.
    ///</remarks>

    [Flags]
    public enum TransformUsageFlags : int
    {
        /// <summary>
        /// Use this flag to specify that no transform components are required. Unless someone else is requesting other
        /// flags, the entity will not have any transform related components and will not be part of a hierarchy.
        /// This does not affect its membership in any <see cref="LinkedEntityGroup"/> components that might be created
        /// based on the source GameObject hierarchy.
        /// </summary>
        None = 0,

        /// <summary>
        /// Use this flag in a baker to indicate that an entity requires the necessary transforms components to be
        /// rendered (<see cref="LocalToWorld"/>), but it does not require the transforms components needed to move the
        /// entity at runtime.
        /// Renderable entities will be placed in WorldSpace if none of their parents in the hierarchy is Dynamic.
        /// </summary>
        Renderable = 1,

        /// <summary>
        /// Use this flag in a baker to indicate that an entity requires the necessary transforms components to be
        /// moved at runtime (<see cref="LocalTransform"/>, <see cref="LocalToWorld"/>).
        /// Renderable children of a Dynamic entity will be treated as Dynamic as well and they will get a <see cref="Parent"/> component.
        /// A Dynamic usage also implies Renderable, therefore there is no need to indicate that a Dynamic entity is
        /// also Renderable.
        /// </summary>
        Dynamic = 1 << 1,

        /// <summary>
        /// Use this flag in a baker to indicate that an entity requires to be in WorldSpace, even if they got a Dynamic
        /// entity as a parent. This means that an entity will not have a <see cref="Parent"/> component and all their
        /// transform component data will be baked in world space.
        /// A WorldSpace usage implies Renderable but not Dynamic, but Dynamic can be used with the WorldSpace flag.
        /// </summary>
        WorldSpace = 1 << 2,

        /// <summary>
        /// Use this flag in a baker to indicate that an entity requires transform components to represent non uniform
        /// scale. For Dynamic entities, all the scale information will be stored in a <see cref="PostTransformMatrix"/> component.
        /// For Renderable only entities, the scale information will be combined into the <see cref="LocalToWorld"/> component.
        /// If a GameObject contains a non uniform scale, this flag will be considered implicitly for Renderable and
        /// Dynamic entities.
        /// </summary>
        NonUniformScale = 1 << 3,

        /// <summary>
        /// Use this flag to specify that you want to take full manual control over the transform conversion of an
        /// entity. This flag is an override: When it is set, all other flags will be ignored. The transform system will
        /// not add any transform related components to the entity. The entity will not have a parent, and it will not
        /// have any children attached to it.
        /// </summary>
        ManualOverride = 1 << 4,
    }

    [Flags]
    internal enum RuntimeTransformComponentFlags : int
    {
        None = 0,

        LocalToWorld = 1,
        LocalTransform = 1 << 1,
        RequestParent = 1 << 2,
        PostTransformMatrix = 1 << 3,

        ManualOverride =  1 << 4,
    }

    /// <summary>
    /// Stores a set of TransformUsageFlags as counters for each bit. This way TransformUsageFlags can be added and removed reliably.
    /// </summary>
    unsafe struct TransformUsageFlagCounters : IEquatable<TransformUsageFlagCounters>
    {
        const int Length = 5;
        fixed int Counts[Length];
        int       IsUsed;

        public void Add(TransformUsageFlags value)
        {
            IsUsed++;
            for (int i = 0; i != Length; i++)
                Counts[i] += ((int)value & (1 << i)) != 0 ? 1 : 0;
        }

        public void Remove(TransformUsageFlags value)
        {
            IsUsed--;
            for (int i = 0; i != Length; i++)
                Counts[i] -= ((int)value & (1 << i)) != 0 ? 1 : 0;
        }

        public void Add(in TransformUsageFlagCounters value)
        {
            IsUsed += value.IsUsed;
            for (int i = 0; i != Length; i++)
                Counts[i] += value.Counts[i];
        }

        public void Remove(in TransformUsageFlagCounters value)
        {
            IsUsed -= value.IsUsed;
            for (int i = 0; i != Length; i++)
                Counts[i] -= value.Counts[i];
        }

        public bool Equals(TransformUsageFlagCounters value)
        {
            if (IsUsed != value.IsUsed)
                return false;

            for (int i = 0; i != Length; i++)
            {
                if (Counts[i] != value.Counts[i])
                    return false;
            }

            return true;
        }

        public TransformUsageFlags Flags
        {
            get
            {
                int flags = 0;
                for (int i = 0; i != Length; i++)
                    flags |= Counts[i] != 0 ? 1 << i : 0;
                return (TransformUsageFlags) flags;
            }
        }

        /// <summary>
        /// TransformUsage.None is different from Unused
        /// TransformUsage.None simply means that no transforms are required (For example, a manager singleton)
        /// IsUnused means that there is no valid reference to this entity, hence the entity shouldn't exist in the game world.
        /// </summary>
        public bool IsUnused => IsUsed == 0;
    }
}
