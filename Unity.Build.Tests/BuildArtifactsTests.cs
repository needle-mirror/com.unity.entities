using NUnit.Framework;
using System;

namespace Unity.Build.Tests
{
    class BuildArtifactsTests
    {
        BuildPipeline m_BuildPipeline;
        BuildSettings m_BuildSettings;

        class TestArtifacts : IBuildArtifact { }
        class TestArtifacts2 : IBuildArtifact { }

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            m_BuildPipeline = BuildPipeline.CreateInstance(pipeline => pipeline.name = "TestPipeline");
            m_BuildSettings = BuildSettings.CreateInstance(settings => settings.name = "TestSettings");
        }

        [Test]
        public void Store_Throws_WhenNullResultPassed()
        {
            Assert.Throws<ArgumentNullException>(() => BuildArtifacts.Store(null, new IBuildArtifact[] { }));
        }

        [Test]
        public void Store_Throws_WhenNullBuildSettingsPassed()
        {
            var result = BuildPipelineResult.Success(m_BuildPipeline, null);
            Assert.Throws<ArgumentNullException>(() => BuildArtifacts.Store(result, new IBuildArtifact[] { }));
        }

        [Test]
        public void Store_Throws_WhenNullArtifactsPassed()
        {
            var result = BuildPipelineResult.Success(m_BuildPipeline, m_BuildSettings);
            Assert.Throws<ArgumentNullException>(() => BuildArtifacts.Store(result, null));
        }

        [Test]
        public void GetBuildArtifact()
        {
            var result = BuildPipelineResult.Success(m_BuildPipeline, m_BuildSettings);
            BuildArtifacts.Store(result, new[] { new TestArtifacts() });
            Assert.That(BuildArtifacts.GetBuildArtifact<TestArtifacts>(m_BuildSettings), Is.Not.Null);
        }

        [Test]
        public void GetBuildArtifact_ReturnNull_WithWrongType()
        {
            var result = BuildPipelineResult.Success(m_BuildPipeline, m_BuildSettings);
            BuildArtifacts.Store(result, new[] { new TestArtifacts() });
            Assert.That(BuildArtifacts.GetBuildArtifact<TestArtifacts2>(m_BuildSettings), Is.Null);
        }

        [Test]
        public void GetBuildArtifact_DoesNotThrow_WhenNullBuildSettingsPassed()
        {
            Assert.DoesNotThrow(() => BuildArtifacts.GetBuildArtifact<IBuildArtifact>(null));
        }

        [Test]
        public void GetBuildResult()
        {
            BuildArtifacts.Store(BuildPipelineResult.Success(m_BuildPipeline, m_BuildSettings), new IBuildArtifact[] { });

            var result = BuildArtifacts.GetBuildResult(m_BuildSettings);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Succeeded, Is.True);
        }
    }
}
