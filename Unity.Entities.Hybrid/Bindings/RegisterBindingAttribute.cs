using System;

namespace Unity.Entities
{

    /// <summary>
    /// Creates a table association between the Type specified and the runtime field of an IComponentData,
    /// accessible via the BindingRegistry
    ///
    /// Only primitive types of int, bool, and float, in addition to Unity.Mathematics variants of these primitives
    /// (e.g. int2, float4) will be added to the BindingRegistry. Other non-compatible types will be silently ignored
    /// if this attribute is applied to it.
    ///
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = true)]
    public class RegisterBindingAttribute : Attribute
    {
        //order of constructors matter. Will break ILPP if changed.
        public RegisterBindingAttribute(Type runtimeComponent, string runtimeField)
        {
            ComponentType = runtimeComponent;
            ComponentField = runtimeField;
            Generated = false;
        }

        public  RegisterBindingAttribute(Type runtimeComponent, string runtimeField, bool generated)
        {
            ComponentType = runtimeComponent;
            ComponentField = runtimeField;
            Generated = generated;
        }

        internal bool Generated { get;  }
        public Type ComponentType { get; }
        public string ComponentField { get; }
    }
}
