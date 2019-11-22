using Unity.Serialization.Json;

namespace Unity.Build
{
    internal class BuildJsonVisitor : JsonVisitor
    {
        public BuildJsonVisitor()
        {
            AddAdapter(new BuildJsonVisitorAdapter(this));
        }

        protected override string GetTypeInfo<TProperty, TContainer, TValue>(TProperty property, ref TContainer container, ref TValue value)
        {
            if (value != null)
            {
                var type = value.GetType();
                return $"{type}, {type.Assembly.GetName().Name}";
            }
            return null;
        }
    }
}
