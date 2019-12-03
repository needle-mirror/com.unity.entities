using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Properties;

namespace Unity.Build.Tests
{
    class BuildStepTests
    {
        class PipelineComponent : IBuildPipelineComponent { public BuildPipeline Pipeline { get; set; } }
        class RequiredComponentA : IBuildSettingsComponent { }
        class RequiredComponentB : IBuildSettingsComponent { }
        class OptionalComponentA : IBuildSettingsComponent { }
        class OptionalComponentB : IBuildSettingsComponent { }
        class InvalidComponent { }

        class TestBuildStep : BuildStep
        {
            public override string Description => throw new NotImplementedException();
            public override Type[] RequiredComponents => new[] { typeof(RequiredComponentA), typeof(RequiredComponentB) };
            public override Type[] OptionalComponents => new[] { typeof(OptionalComponentA), typeof(OptionalComponentB) };
            public override BuildStepResult RunBuildStep(BuildContext context) => throw new NotImplementedException();
        }

        class TestData
        {
            public BuildStep BuildStep { get; }
            public BuildPipeline BuildPipeline { get; }
            public BuildSettings BuildSettings { get; }
            public BuildContext BuildContext { get; }

            public TestData(params Type[] components)
            {
                BuildStep = new TestBuildStep();
                BuildPipeline = BuildPipeline.CreateInstance(p => p.BuildSteps.Add(BuildStep));
                BuildSettings = BuildSettings.CreateInstance((s) =>
                {
                    s.SetComponent(new PipelineComponent { Pipeline = BuildPipeline });
                    foreach (var component in components)
                    {
                        s.SetComponent(component, TypeConstruction.Construct<IBuildSettingsComponent>(component));
                    }
                });
                BuildContext = new BuildContext(BuildPipeline, BuildSettings, null);
            }
        }

        [Test]
        public void HasRequiredComponent()
        {
            var data = new TestData(typeof(RequiredComponentA));

            Assert.That(data.BuildStep.HasRequiredComponent<RequiredComponentA>(data.BuildContext), Is.True);
            Assert.That(data.BuildStep.HasRequiredComponent<RequiredComponentB>(data.BuildContext), Is.False);
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.HasRequiredComponent<OptionalComponentA>(data.BuildContext));

            Assert.Throws<ArgumentNullException>(() => data.BuildStep.HasRequiredComponent(data.BuildContext, null));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.HasRequiredComponent(data.BuildContext, typeof(object)));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.HasRequiredComponent(data.BuildContext, typeof(InvalidComponent)));
        }

        [Test]
        public void GetRequiredComponent()
        {
            var data = new TestData(typeof(RequiredComponentA));

            Assert.That(data.BuildStep.GetRequiredComponent<RequiredComponentA>(data.BuildContext), Is.Not.Null);
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetRequiredComponent<RequiredComponentB>(data.BuildContext));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetRequiredComponent<OptionalComponentA>(data.BuildContext));

            Assert.Throws<ArgumentNullException>(() => data.BuildStep.GetRequiredComponent(data.BuildContext, null));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetRequiredComponent(data.BuildContext, typeof(object)));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetRequiredComponent(data.BuildContext, typeof(InvalidComponent)));
        }

        [Test]
        public void GetRequiredComponents()
        {
            var data = new TestData(typeof(RequiredComponentA), typeof(RequiredComponentB));
            IEnumerable<IBuildSettingsComponent> components = null;
            Assert.DoesNotThrow(() => components = data.BuildStep.GetRequiredComponents(data.BuildContext));
            Assert.That(components.Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(RequiredComponentA), typeof(RequiredComponentB) }));

            data = new TestData(typeof(RequiredComponentA));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetRequiredComponents(data.BuildContext));
        }

        [Test]
        public void GetRequiredComponents_WithType()
        {
            var data = new TestData(typeof(RequiredComponentA), typeof(RequiredComponentB));
            IEnumerable<IBuildSettingsComponent> components = null;
            Assert.DoesNotThrow(() => components = data.BuildStep.GetRequiredComponents<RequiredComponentA>(data.BuildContext));
            Assert.That(components.Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(RequiredComponentA) }));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetRequiredComponents<OptionalComponentA>(data.BuildContext));

            Assert.Throws<ArgumentNullException>(() => data.BuildStep.GetRequiredComponents(data.BuildContext, null));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetRequiredComponents(data.BuildContext, typeof(object)));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetRequiredComponents(data.BuildContext, typeof(InvalidComponent)));
        }

        [Test]
        public void HasOptionalComponent()
        {
            var data = new TestData(typeof(OptionalComponentA));

            Assert.That(data.BuildStep.HasOptionalComponent<OptionalComponentA>(data.BuildContext), Is.True);
            Assert.That(data.BuildStep.HasOptionalComponent<OptionalComponentB>(data.BuildContext), Is.False);
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.HasOptionalComponent<RequiredComponentA>(data.BuildContext));

            Assert.Throws<ArgumentNullException>(() => data.BuildStep.HasOptionalComponent(data.BuildContext, null));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.HasOptionalComponent(data.BuildContext, typeof(object)));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.HasOptionalComponent(data.BuildContext, typeof(InvalidComponent)));
        }

        [Test]
        public void GetOptionalComponent()
        {
            var data = new TestData(typeof(OptionalComponentA));

            Assert.That(data.BuildStep.GetOptionalComponent<OptionalComponentA>(data.BuildContext), Is.Not.Null);
            Assert.That(data.BuildStep.GetOptionalComponent<OptionalComponentB>(data.BuildContext), Is.Not.Null);
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetOptionalComponent<RequiredComponentA>(data.BuildContext));

            Assert.Throws<ArgumentNullException>(() => data.BuildStep.GetOptionalComponent(data.BuildContext, null));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetOptionalComponent(data.BuildContext, typeof(object)));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetOptionalComponent(data.BuildContext, typeof(InvalidComponent)));
        }

        [Test]
        public void GetOptionalComponents()
        {
            var data = new TestData(typeof(OptionalComponentA), typeof(OptionalComponentB));
            IEnumerable<IBuildSettingsComponent> components = null;
            Assert.DoesNotThrow(() => components = data.BuildStep.GetOptionalComponents(data.BuildContext));
            Assert.That(components.Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(OptionalComponentA), typeof(OptionalComponentB) }));

            data = new TestData(typeof(OptionalComponentA));
            Assert.DoesNotThrow(() => data.BuildStep.GetOptionalComponents(data.BuildContext));
        }

        [Test]
        public void GetOptionalComponents_WithType()
        {
            var data = new TestData(typeof(OptionalComponentA), typeof(OptionalComponentB));
            IEnumerable<IBuildSettingsComponent> components = null;
            Assert.DoesNotThrow(() => components = data.BuildStep.GetOptionalComponents<OptionalComponentA>(data.BuildContext));
            Assert.That(components.Select(c => c.GetType()), Is.EquivalentTo(new[] { typeof(OptionalComponentA) }));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetOptionalComponents<RequiredComponentA>(data.BuildContext));

            Assert.Throws<ArgumentNullException>(() => data.BuildStep.GetOptionalComponents(data.BuildContext, null));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetOptionalComponents(data.BuildContext, typeof(object)));
            Assert.Throws<InvalidOperationException>(() => data.BuildStep.GetOptionalComponents(data.BuildContext, typeof(InvalidComponent)));
        }
    }
}
