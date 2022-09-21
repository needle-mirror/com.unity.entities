using System;
using System.Collections.Generic;

// Represent a ComponentTypeHandle used by 2 different type
// of aspect fields: RefRO/RefRW or ComponentEnableRefRO/ComponentEnableRefRW
// the Ref field takes priority over ComponentEnableRef when declaring the ComponentTypeHandle
using DependentComponentTypeHandle =
    Unity.Entities.SourceGen.Aspect.DependentDotsPrimitive<
        Unity.Entities.SourceGen.Aspect.ComponentTypeHandle,
        Unity.Entities.SourceGen.Aspect.BinaryDependency<
            Unity.Entities.SourceGen.Aspect.ComponentTypeHandle,
            Unity.Entities.SourceGen.Aspect.ComponentRefField,
            Unity.Entities.SourceGen.Aspect.EnabledField
        >
    >;

namespace Unity.Entities.SourceGen.Aspect
{
    /// <summary>
    /// Description of a Type Handle for an aspect.
    /// </summary>
    public struct AspectTypeHandleDescription
    {
        public List<AspectField> BufferTypeHandle;
        public List<AspectField> Update;

        public Dictionary<string, DependentComponentTypeHandle> ComponentTypeHandles;
        public static AspectTypeHandleDescription Default => new AspectTypeHandleDescription
            {
                BufferTypeHandle = new List<AspectField>(),
                Update = new List<AspectField>(),
                ComponentTypeHandles = new Dictionary<string, DependentComponentTypeHandle>()
            };

        static DependentComponentTypeHandle NewCth(AspectField field) =>
            new DependentComponentTypeHandle(new ComponentTypeHandle(field.TypeName, field.IsReadOnly));

        public void AddFieldRequireCTH(ComponentRefField dependentField)
            => ComponentTypeHandles.AddOrSetValue(dependentField.TypeName, ()=> NewCth(dependentField), x =>
            {
                x.Dependencies.Primary = dependentField;
                return x;
            });

        public void AddFieldRequireCTH(EnabledField dependentField)
            => ComponentTypeHandles.AddOrSetValue(dependentField.TypeName, () => NewCth(dependentField), x =>
            {
                x.Dependencies.Secondary = dependentField;
                return x;
            });

        public string DeclareCode(ref Printer printer)
        {
            foreach (var lookup in ComponentTypeHandles.Values)
            {
                lookup.Declare(printer);
            }
            return printer.Result;
        }

        public string ConstructCode(ref Printer printer)
        {
            foreach (var lookup in ComponentTypeHandles.Values)
            {
                lookup.Construct(printer);
            }
            return printer.Result;
        }

        public string UpdateCode(ref Printer printer)
        {
            foreach (var lookup in ComponentTypeHandles.Values)
            {
                lookup.Update(printer);
            }
            return printer.Result;
        }

    }
}
