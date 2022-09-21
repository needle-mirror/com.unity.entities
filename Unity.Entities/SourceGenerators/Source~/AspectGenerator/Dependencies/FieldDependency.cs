using System;
using System.Runtime.CompilerServices;

namespace Unity.Entities.SourceGen.Aspect
{
    /// <summary>
    /// An AspectField will implement this interface to declare it's dependency on a
    /// DOTS primitive such as 'ComponentLookup` or 'ComponentTypeHandle'
    /// </summary>
    /// <remarks>This interface is only used as a generic type constrain.</remarks>
    public interface IFieldDependency<TDotsPrimitive>
        where TDotsPrimitive : IDotsPrimitiveSyntax
    {
        /// <summary>
        /// Will be set to the AspectField that is responsible for declaring the
        /// DOTS primitive when dependencies are resolved.
        /// All dependent fields of that DOTS primitive will refer to the same declaring AspectField
        /// </summary>
        AspectField DeclaringField { get; set; }

    }

    /// <summary>
    /// A 'DependentDotsPrimitive' implements its type of dependencies
    /// through this interface
    /// </summary>
    /// <remarks>This interface is only used as a generic type constrain.</remarks>
    public interface IDotsPrimitiveDependency
    {
        AspectField DeclaringField { get; }
        void Resolve();
    }

    /// <summary>
    /// Very simple 2-fields dependency.
    /// </summary>
    /// <typeparam name="TDotsPrimitive"></typeparam>
    /// <typeparam name="TPrimaryChild">Primary child will be the first pick for DeclaringField when resolving</typeparam>
    /// <typeparam name="TSecondaryChild">If the primary child is not set, the secondary becomes the DeclaringField when resolving</typeparam>
    public struct BinaryDependency<TDotsPrimitive, TPrimaryChild, TSecondaryChild> : IDotsPrimitiveDependency
        where TDotsPrimitive : IDotsPrimitiveSyntax
        where TPrimaryChild : AspectField, IFieldDependency<TDotsPrimitive>
        where TSecondaryChild : AspectField, IFieldDependency<TDotsPrimitive>
    {
        public TPrimaryChild Primary;
        public TSecondaryChild Secondary;
        public AspectField DeclaringField
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var resolved = Primary != null ? (AspectField)Primary : Secondary;
                if (resolved == null)
                {
                    throw new InvalidOperationException(
                        "Failed to resolve field dependencies between a ComponentData and a ComponentEnabled. This is a bug in the source generator. " +
                        "Most likely reason being no ComponentLookup were added to the AspectDescription.Lookup for the requested component typename");
                }
                return resolved;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Resolve()
        {
            var resolved = DeclaringField;
            if (Secondary != null)
                Secondary.DeclaringField = resolved;
            if (Primary != null)
                Primary.DeclaringField = resolved;
        }
    }
}
