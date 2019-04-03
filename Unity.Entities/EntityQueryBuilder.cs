using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;

namespace Unity.Entities
{
    public partial struct EntityQueryBuilder
    {
        // TODO: add ReadOnly support for Any/All

        ComponentSystem m_System;
        ResizableArray64Byte<int> m_Any, m_None, m_All;
        ComponentGroup m_Group;

        internal EntityQueryBuilder(ComponentSystem system)
        {
            m_System = system;
            m_Any    = new ResizableArray64Byte<int>();
            m_None   = new ResizableArray64Byte<int>();
            m_All    = new ResizableArray64Byte<int>();
            m_Group  = null;
        }

        // this is a specialized function intended only for validation that builders are hashing and getting cached
        // correctly without unexpected collisions. "Equals" is hard to truly validate because the type may not
        // fully be constructed yet due to ForEach not getting called yet.
        internal bool ShallowEquals(ref EntityQueryBuilder other)
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!ReferenceEquals(m_System, other.m_System))
                throw new InvalidOperationException($"Suspicious comparison of {nameof(EntityQueryBuilder)}s with different {nameof(ComponentSystem)}s");
            #endif

            return
                m_Any .Equals(ref other.m_Any)  &&
                m_None.Equals(ref other.m_None) &&
                m_All .Equals(ref other.m_All)  &&
                ReferenceEquals(m_Group, other.m_Group);
        }

        public override int GetHashCode() =>
            throw new InvalidOperationException("Hashing implies storage, but this type should only live on the stack in user code");
        public override bool Equals(object obj) =>
            throw new InvalidOperationException("Calling this function is a sign of inadvertent boxing");

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void ValidateHasNoGroup() => ThrowIfInvalidMixing(m_Group != null);

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void ValidateHasNoSpec() => ThrowIfInvalidMixing(m_Any.Length != 0 || m_None.Length != 0 || m_All.Length != 0);

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void ThrowIfInvalidMixing(bool throwIfTrue)
        {
            if (throwIfTrue)
                throw new InvalidOperationException($"Cannot mix {nameof(WithAny)}/{nameof(WithNone)}/{nameof(WithAll)} and {nameof(With)}({nameof(ComponentGroup)})");
        }

        public EntityQueryBuilder With(ComponentGroup componentGroup)
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (componentGroup == null)
                throw new ArgumentNullException(nameof(componentGroup));
            if (m_Group != null)
                throw new InvalidOperationException($"{nameof(ComponentGroup)} has already been set");
            ValidateHasNoSpec();
            #endif

            m_Group = componentGroup;
            return this;
        }

        EntityArchetypeQuery ToEntityArchetypeQuery(int delegateTypeCount)
        {
            ComponentType[] ToComponentTypes(ref ResizableArray64Byte<int> typeIndices, ComponentType.AccessMode mode, int extraCapacity = 0)
            {
                var length = typeIndices.Length + extraCapacity;
                if (length == 0)
                    return Array.Empty<ComponentType>();

                var types = new ComponentType[length];
                for (var i = 0; i < typeIndices.Length; ++i)
                    types[i] = new ComponentType { TypeIndex = typeIndices[i], AccessModeType = mode };

                return types;
            }

            return new EntityArchetypeQuery
            {
                Any  = ToComponentTypes(ref m_Any,  ComponentType.AccessMode.ReadWrite),
                None = ToComponentTypes(ref m_None, ComponentType.AccessMode.ReadOnly),
                All  = ToComponentTypes(ref m_All,  ComponentType.AccessMode.ReadWrite, delegateTypeCount),
            };
        }

        public EntityArchetypeQuery ToEntityArchetypeQuery() =>
            ToEntityArchetypeQuery(0);

        public ComponentGroup ToComponentGroup() =>
            m_Group ?? (m_Group = m_System.GetComponentGroup(ToEntityArchetypeQuery()));

        // see EntityQueryBuilder.tt for the template that is converted into EntityQueryBuilder.gen.cs,
        // which contains ForEach and other generated methods.

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        EntityManager.InsideForEach InsideForEach() =>
            new EntityManager.InsideForEach(m_System.EntityManager);
        #endif

        unsafe ComponentGroup ResolveComponentGroup(int* delegateTypeIndices, int delegateTypeCount)
        {
            var hash
                = (uint)m_Any .GetHashCode() * 0xEA928FF9
                ^ (uint)m_None.GetHashCode() * 0x4B772F25
                ^ (uint)m_All .GetHashCode() * 0xBAEE8991
                ^ math.hash(delegateTypeIndices, sizeof(int) * delegateTypeCount);

            var cache = m_System.GetOrCreateEntityQueryCache();
            var found = cache.FindQueryInCache(hash);

            if (found < 0)
            {
                // base query from builder spec, but reserve some extra room for the types detected from the delegate
                var eaq = ToEntityArchetypeQuery(delegateTypeCount);

                // now fill out the extra types
                for (var i = 0 ; i < delegateTypeCount; ++i)
                    eaq.All[i + m_All.Length] = ComponentType.FromTypeIndex(delegateTypeIndices[i]);

                var group = m_System.GetComponentGroup(eaq);

                #if ENABLE_UNITY_COLLECTIONS_CHECKS
                found = cache.CreateCachedQuery(hash, group, ref this, delegateTypeIndices, delegateTypeCount);
                #else
                found = cache.CreateCachedQuery(hash, group);
                #endif
            }
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            else
            {
                cache.ValidateMatchesCache(found, ref this, delegateTypeIndices, delegateTypeCount);

                // TODO: also validate that m_Group spec matches m_Any/All/None and delegateTypeIndices
            }
            #endif

            return cache.GetCachedQuery(found);
        }
    }
}
