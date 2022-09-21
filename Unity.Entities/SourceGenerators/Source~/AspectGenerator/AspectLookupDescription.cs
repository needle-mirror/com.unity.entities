using System;
using System.Collections.Generic;

// Represent a ComponentLookup used by 2 different type
// of aspect fields: RefRO/RefRW or ComponentEnableRefRO/ComponentEnableRefRW
// the Ref field takes priority over ComponentEnableRef when declaring the ComponentLookup
using DependentComponentLookup =
    Unity.Entities.SourceGen.Aspect.DependentDotsPrimitive<
           Unity.Entities.SourceGen.Aspect.ComponentLookup,
           Unity.Entities.SourceGen.Aspect.BinaryDependency<
                   Unity.Entities.SourceGen.Aspect.ComponentLookup,
                   Unity.Entities.SourceGen.Aspect.ComponentRefField,
                   Unity.Entities.SourceGen.Aspect.EnabledField
           >
    >;

namespace Unity.Entities.SourceGen.Aspect
{
    /// <summary>
    /// Description of the Entity lookup feature of aspect.
    /// </summary>
    public struct AspectLookupDescription
    {
        public List<AspectField> BufferLookup;
        public List<AspectField> Update;
        public Dictionary<string, DependentComponentLookup> ComponentLookups;

        public static AspectLookupDescription Default => new AspectLookupDescription
        {
            ComponentLookups = new Dictionary<string, DependentComponentLookup>(),
            BufferLookup = new List<AspectField>(),
            Update = new List<AspectField>()
        };

        static DependentComponentLookup NewComponentLookup(AspectField field) =>
            new DependentComponentLookup(new ComponentLookup(field.TypeName, field.IsReadOnly));

        public void AddFieldRequireComponentLookup(ComponentRefField dependentField)
            => ComponentLookups.AddOrSetValue(dependentField.TypeName, () => NewComponentLookup(dependentField), x =>
            {
                x.Dependencies.Primary = dependentField;
                return x;
            });

        public void AddFieldRequireComponentLookup(EnabledField dependentField)
            => ComponentLookups.AddOrSetValue(dependentField.TypeName, () => NewComponentLookup(dependentField), x =>
            {
                x.Dependencies.Secondary = dependentField;
                return x;
            });

        public string DeclareCode(ref Printer printer)
        {
            foreach (var lookup in ComponentLookups.Values)
            {
                lookup.Declare(printer);
            }
            return printer.Result;
        }

        public string ConstructCode(ref Printer printer)
        {
            foreach (var lookup in ComponentLookups.Values)
            {
                lookup.Construct(printer);
            }
            return printer.Result;
        }

        public string UpdateCode(ref Printer printer)
        {
            foreach (var lookup in ComponentLookups.Values)
            {
                lookup.Update(printer);
            }
            return printer.Result;
        }
    }

}
