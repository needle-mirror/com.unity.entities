using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    public readonly struct Archetype : IEquatable<Archetype>
    {
        public readonly EntityQueryOptions Options;
        public readonly IReadOnlyCollection<Query> All;
        public readonly IReadOnlyCollection<Query> Any;
        public readonly IReadOnlyCollection<Query> None;

        public Archetype(
            IReadOnlyCollection<Query> all,
            IReadOnlyCollection<Query> any,
            IReadOnlyCollection<Query> none,
            EntityQueryOptions options = default)
        {
            All = all;
            Any = any;
            None = none;
            Options = options;
        }

        public bool Equals(Archetype other) =>
            Options == other.Options
            && All.SequenceEqual(other.All)
            && Any.SequenceEqual(other.Any)
            && None.SequenceEqual(other.None);

        public override bool Equals(object obj) => obj is Archetype other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 19;

                foreach (var all in All)
                    hash = hash * 31 + all.GetHashCode();
                foreach (var any in Any)
                    hash = hash * 31 + any.GetHashCode();
                foreach (var none in None)
                    hash = hash * 31 + none.GetHashCode();
                hash = hash * 31 + ((int)Options).GetHashCode();

                return hash;
            }
        }
        public static bool operator ==(Archetype left, Archetype right) => left.Equals(right);
        public static bool operator !=(Archetype left, Archetype right) => !left.Equals(right);
    }
}
