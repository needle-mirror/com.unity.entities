using System;
using Microsoft.CodeAnalysis;

namespace Unity.Entities.SourceGen.Common
{
    public class SyntaxTreeInfo : IEquatable<SyntaxTreeInfo>
    {
        public SyntaxTree Tree { get; set; }
        public bool IsSourceGenerationSuccessful { get; set; }

        public bool Equals(SyntaxTreeInfo other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Tree, other.Tree);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((SyntaxTreeInfo)obj);
        }

        public override int GetHashCode() => Tree != null ? Tree.GetHashCode() : 0;
        public static bool operator ==(SyntaxTreeInfo left, SyntaxTreeInfo right) => Equals(left, right);
        public static bool operator !=(SyntaxTreeInfo left, SyntaxTreeInfo right) => !Equals(left, right);
    }
}
