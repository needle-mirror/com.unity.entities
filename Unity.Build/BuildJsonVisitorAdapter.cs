using Unity.Properties;
using Unity.Serialization;
using Unity.Serialization.Json;
using UnityEditor;

namespace Unity.Build
{
    internal class BuildJsonVisitorAdapter : JsonVisitorAdapter,
        IVisitAdapter<IBuildStep>,
        IVisitAdapter<IRunStep>
    {
        [InitializeOnLoadMethod]
        static void Initialize()
        {
            // NOTE: Types conversion registrations have to happen in InitializeOnLoadMethod,
            // otherwise they could be registered too late and some conversions would fail silently.
            TypeConversion.Register<SerializedStringView, IBuildStep>(view => BuildStep.Deserialize(view.ToString()));
            TypeConversion.Register<SerializedStringView, IRunStep>(view => RunStep.Deserialize(view.ToString()));
        }

        public BuildJsonVisitorAdapter(JsonVisitor visitor) : base(visitor) { }

        public VisitStatus Visit<TProperty, TContainer>(IPropertyVisitor visitor, TProperty property, ref TContainer container, ref IBuildStep value, ref ChangeTracker changeTracker)
            where TProperty : IProperty<TContainer, IBuildStep>
        {
            AppendJsonString(property, BuildStep.Serialize(value));
            return VisitStatus.Override;
        }

        public VisitStatus Visit<TProperty, TContainer>(IPropertyVisitor visitor, TProperty property, ref TContainer container, ref IRunStep value, ref ChangeTracker changeTracker)
            where TProperty : IProperty<TContainer, IRunStep>
        {
            AppendJsonString(property, RunStep.Serialize(value));
            return VisitStatus.Override;
        }
    }
}
