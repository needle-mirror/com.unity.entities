using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    class SingletonEntityQuery : IEquatable<SingletonEntityQuery>
    {
        public string EntityQueryFieldName { get; set; }
        public ClassDeclarationSyntax SystemBaseType { get; set; }
        public string FullyQualifiedSingletonTypeName { get; set; }
        public SingletonAccessType AccessType { get; set; }

        public bool Equals(SingletonEntityQuery other)
        {
            return
                other != null
                && Equals(SystemBaseType, other.SystemBaseType)
                && FullyQualifiedSingletonTypeName == other?.FullyQualifiedSingletonTypeName
                && IsAccessTypeCompatible(other.AccessType);
        }

        public override bool Equals(object obj)
        {
            return obj is SingletonEntityQuery other && Equals(other);
        }

        public override int GetHashCode()
        {
            int accessType = AccessType == SingletonAccessType.Set ? 0 : 1;
            unchecked
            {
                var hashCode = (SystemBaseType != null ? SystemBaseType.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ SourceGenHelpers.GetStableHashCode(FullyQualifiedSingletonTypeName);
                hashCode = (hashCode * 397) ^ accessType;
                return hashCode;
            }
        }

        bool IsAccessTypeCompatible(SingletonAccessType otherType)
        {
            switch (AccessType)
            {
                case SingletonAccessType.GetSingleton:
                case SingletonAccessType.GetSingletonEntity:
                    return otherType == SingletonAccessType.GetSingleton || otherType == SingletonAccessType.GetSingletonEntity;
                default:
                    return otherType == SingletonAccessType.Set;
            }
        }
    }
}
