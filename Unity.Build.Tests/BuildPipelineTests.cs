using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Build.Tests
{
    [TestFixture]
    class BuildPipelineTests
    {
        class TestBuildArtifacts : IBuildArtifact
        {
            public List<string> BuildStepsRan = new List<string>();
            public List<string> CleanupStepsRan = new List<string>();
        }

        [BuildStep(flags = BuildStepAttribute.Flags.Hidden)]
        sealed class TestBuildStep1 : BuildStep
        {
            readonly bool m_IsEnabled;

            public class Data
            {
                public string Value;
            }

            public TestBuildStep1(bool enabled = true)
            {
                m_IsEnabled = enabled;
            }

            public override string Description => nameof(TestBuildStep1);

            public override bool IsEnabled(BuildContext context) => m_IsEnabled;

            public override BuildStepResult RunBuildStep(BuildContext context)
            {
                context.GetOrCreateValue<TestBuildArtifacts>().BuildStepsRan.Add(nameof(TestBuildStep1));
                return context.GetValue<Data>().Value == nameof(TestBuildStep1) ? Success() : Failure(nameof(TestBuildStep1));
            }

            public override BuildStepResult CleanupBuildStep(BuildContext context)
            {
                context.GetOrCreateValue<TestBuildArtifacts>().CleanupStepsRan.Add(nameof(TestBuildStep1));
                return context.GetValue<Data>().Value == nameof(TestBuildStep1) ? Success() : Failure(nameof(TestBuildStep1));
            }
        }

        [BuildStep(flags = BuildStepAttribute.Flags.Hidden)]
        sealed class TestBuildStep2 : BuildStep
        {
            readonly bool m_IsEnabled;

            public class Data
            {
                public string Value;
            }

            public TestBuildStep2(bool enabled = true)
            {
                m_IsEnabled = enabled;
            }

            public override string Description => nameof(TestBuildStep2);

            public override bool IsEnabled(BuildContext context) => m_IsEnabled;

            public override BuildStepResult RunBuildStep(BuildContext context)
            {
                context.GetOrCreateValue<TestBuildArtifacts>().BuildStepsRan.Add(nameof(TestBuildStep2));
                return context.GetValue<Data>().Value == nameof(TestBuildStep2) ? Success() : Failure(nameof(TestBuildStep2));
            }

            public override BuildStepResult CleanupBuildStep(BuildContext context)
            {
                context.GetOrCreateValue<TestBuildArtifacts>().CleanupStepsRan.Add(nameof(TestBuildStep2));
                return context.GetValue<Data>().Value == nameof(TestBuildStep2) ? Success() : Failure(nameof(TestBuildStep2));
            }
        }

        [BuildStep(flags = BuildStepAttribute.Flags.Hidden)]
        sealed class TestBuildStep3 : BuildStep
        {
            readonly bool m_IsEnabled;

            public class Data
            {
                public string Value;
            }

            public TestBuildStep3(bool enabled = true)
            {
                m_IsEnabled = enabled;
            }

            public override string Description => nameof(TestBuildStep3);

            public override bool IsEnabled(BuildContext context) => m_IsEnabled;

            public override BuildStepResult RunBuildStep(BuildContext context)
            {
                context.GetOrCreateValue<TestBuildArtifacts>().BuildStepsRan.Add(nameof(TestBuildStep3));
                return context.GetValue<Data>().Value == nameof(TestBuildStep3) ? Success() : Failure(nameof(TestBuildStep3));
            }

            public override BuildStepResult CleanupBuildStep(BuildContext context)
            {
                context.GetOrCreateValue<TestBuildArtifacts>().CleanupStepsRan.Add(nameof(TestBuildStep3));
                return context.GetValue<Data>().Value == nameof(TestBuildStep3) ? Success() : Failure(nameof(TestBuildStep3));
            }
        }

        [Test]
        public void WhenBuildPipelineSucceeded_AllBuildStepsAndCleanupStepsRan()
        {
            var pipeline = BuildPipeline.CreateInstance((p) =>
            {
                p.BuildSteps.Add(new TestBuildStep1());
                p.BuildSteps.Add(new TestBuildStep2());
                p.BuildSteps.Add(new TestBuildStep3());
            });
            var settings = BuildSettings.CreateInstance();
            var result = pipeline.Build(settings, mutator: (context) =>
            {
                context.SetValue(new TestBuildStep1.Data { Value = nameof(TestBuildStep1) });
                context.SetValue(new TestBuildStep2.Data { Value = nameof(TestBuildStep2) });
                context.SetValue(new TestBuildStep3.Data { Value = nameof(TestBuildStep3) });
            });

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.BuildStepsResults.Any(r => r.Failed), Is.False);

            var artifacts = BuildArtifacts.GetBuildArtifact<TestBuildArtifacts>(settings);
            Assert.That(artifacts, Is.Not.Null);
            Assert.That(artifacts.BuildStepsRan, Is.EqualTo(new List<string> { nameof(TestBuildStep1), nameof(TestBuildStep2), nameof(TestBuildStep3) }));
            Assert.That(artifacts.CleanupStepsRan, Is.EqualTo(new List<string> { nameof(TestBuildStep3), nameof(TestBuildStep2), nameof(TestBuildStep1) }));
        }

        [Test]
        public void WhenBuildPipelineFails_BuildStepsStopAtFailure_CleanupStepsOnlyRanIfBuildStepRan()
        {
            var pipeline = BuildPipeline.CreateInstance((p) =>
            {
                p.BuildSteps.Add(new TestBuildStep1());
                p.BuildSteps.Add(new TestBuildStep2());
                p.BuildSteps.Add(new TestBuildStep3());
            });
            var settings = BuildSettings.CreateInstance();
            var result = pipeline.Build(settings, mutator: (context) =>
            {
                context.SetValue(new TestBuildStep1.Data { Value = nameof(TestBuildStep1) });
                // Here we make TestStep2 fails by not providing its data
                context.SetValue(new TestBuildStep3.Data { Value = nameof(TestBuildStep3) });
            });
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.BuildStepsResults.Any(r => r.Failed), Is.True);

            var artifacts = BuildArtifacts.GetBuildArtifact<TestBuildArtifacts>(settings);
            Assert.That(artifacts, Is.Not.Null);
            Assert.That(artifacts.BuildStepsRan, Is.EqualTo(new List<string> { nameof(TestBuildStep1), nameof(TestBuildStep2) }));
            Assert.That(artifacts.CleanupStepsRan, Is.EqualTo(new List<string> { nameof(TestBuildStep2), nameof(TestBuildStep1) }));
        }

        [Test]
        public void WhenNestedBuildPipelineFails_ParentBuildPipelineFails()
        {
            var nestedPipeline = BuildPipeline.CreateInstance((p) =>
            {
                p.BuildSteps.Add(new TestBuildStep1());
                p.BuildSteps.Add(new TestBuildStep2());
                p.BuildSteps.Add(new TestBuildStep3());
            });
            var pipeline = BuildPipeline.CreateInstance(p => p.BuildSteps.Add(nestedPipeline));
            var settings = BuildSettings.CreateInstance();
            var result = pipeline.Build(settings, mutator: (context) =>
            {
                context.SetValue(new TestBuildStep1.Data { Value = nameof(TestBuildStep1) });
                // Here we make TestStep2 fails by not providing its data
                context.SetValue(new TestBuildStep3.Data { Value = nameof(TestBuildStep3) });
            });
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.BuildStepsResults.Any(r => r.Failed), Is.True);

            var artifacts = BuildArtifacts.GetBuildArtifact<TestBuildArtifacts>(settings);
            Assert.That(artifacts, Is.Not.Null);
            Assert.That(artifacts.BuildStepsRan, Is.EqualTo(new List<string> { nameof(TestBuildStep1), nameof(TestBuildStep2) }));
            Assert.That(artifacts.CleanupStepsRan, Is.EqualTo(new List<string> { nameof(TestBuildStep2), nameof(TestBuildStep1) }));
        }

        [Test]
        public void DisabledBuildSteps_DoesNotRun()
        {
            var pipeline = BuildPipeline.CreateInstance((p) =>
            {
                p.BuildSteps.Add(new TestBuildStep1());
                p.BuildSteps.Add(new TestBuildStep2(enabled: false));
                p.BuildSteps.Add(new TestBuildStep3());
            });
            var settings = BuildSettings.CreateInstance();
            var result = pipeline.Build(settings, mutator: (context) =>
            {
                context.SetValue(new TestBuildStep1.Data { Value = nameof(TestBuildStep1) });
                context.SetValue(new TestBuildStep2.Data { Value = nameof(TestBuildStep2) });
                context.SetValue(new TestBuildStep3.Data { Value = nameof(TestBuildStep3) });
            });

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.BuildStepsResults.Any(r => r.Failed), Is.False);

            var artifacts = BuildArtifacts.GetBuildArtifact<TestBuildArtifacts>(settings);
            Assert.That(artifacts, Is.Not.Null);
            Assert.That(artifacts.BuildStepsRan, Is.EqualTo(new List<string> { nameof(TestBuildStep1), nameof(TestBuildStep3) }));
            Assert.That(artifacts.CleanupStepsRan, Is.EqualTo(new List<string> { nameof(TestBuildStep3), nameof(TestBuildStep1) }));
        }
    }
}
