using System;

namespace Unity.Entities
{
    /// <summary>
    /// Controls how Transform components on GameObjects are converted to entity data. 
    /// </summary>
    /// <remarks>
    /// Use these flags to optimize hierarchies by removing GameObjects from them at bake time.
    ///
    /// These flags are used whenever an entity is requested during conversion, for example, via
    /// <see cref="IBaker.GetEntity"/>.
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
        /// Use this flag to specify that you are going to read and write the local and global transform of the entity.
        /// This flag mirrors the behavior of GameObject's most closely and should be used if in doubt.
        /// </summary>
        Default = 1,

        /// <summary>
        /// Use this flag to specify that you are going to continuously override the global transform data of this
        /// entity. If an entity has this flag, it will not have a parent.
        /// </summary>
        WriteGlobalTransform = 1 << 1,

        /// <summary>
        /// Use this flag to specify that you are reading from the entity's global transform. If you only read from an
        /// entity and all of its parent, their hierarchy may be flattened.
        /// </summary>
        ReadGlobalTransform = 1 << 2,

        /// <summary>
        /// Use this flag to specify that you are reading from the entity's residual transform. By default, the
        /// transform system only supports uniform scaling. Non-uniform scaling will be baked into a residual transform
        /// matrix that describes the difference between the local-to-world matrix from the GameObject and the
        /// local-to-world matrix computed using uniform scaling. The residual transform is not going to be updated
        /// after conversion and will only be present when this flag is set.
        /// </summary>
        ReadResidualTransform = 1 << 3,

        /// <summary>
        /// /Use this flag to specify that you are reading from an entity's local-to-world matrix. If this is not set,
        /// no local-to-world matrix will be computed for the entity.
        /// </summary>
        ReadLocalToWorld = 1 << 4,

        /// <summary>
        /// Use this flag to specify that you want to take full manual control over the transform conversion of an
        /// entity. This flag is an override: When it is set, all other flags will be ignored. The transform system will
        /// not add any transform related components to the entity. The entity will not have a parent, and it will not
        /// have any children attached to it.
        /// This is different from None, because None will result in removing any previously added transform components during incremental baking.
        /// </summary>
        ManualOverride = 1 << 5,

        /// <summary>
        /// This combined flag can be used as a mask to check whether anyone is writing to the transform of an entity.
        /// </summary>
        WriteFlags = Default | WriteGlobalTransform,
    }

    /// <summary>
    /// Stores a set of TransformUsageFlags as counters for each bit. This way TransformUsageFlags can be added and removed reliably.
    /// for example 3 entities might reference ReadLocalToWorld, then after some changes, 2 entities stop referencing the entity.
    /// The counter knows that we still have a ReadLocalToWorld usage exactly once.
    /// </summary>
    unsafe struct TransformUsageFlagCounters : IEquatable<TransformUsageFlagCounters>
    {
        const int Length = 6;
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
