using NUnit.Framework;
using UnityEditor;

namespace Unity.Entities.Editor.Tests
{
    class SceneMapperTests : BaseTestFixture
    {
        SceneMapper m_SceneMapper;

        protected override string SceneName { get; } = nameof(SceneMapperTests);

        [SetUp]
        public void Setup()
        {
            m_SceneMapper = new SceneMapper();
            m_SceneMapper.Update();
        }

        [TearDown]
        public void Teardown()
        {
            m_SceneMapper?.Dispose();
        }

        [Test]
        public void GetSubSceneId_ReturnsRegularSubSceneId()
        {
            var (subSceneId, isDynamicSubScene) = m_SceneMapper.GetSubSceneId(SubScene.SceneGUID);
            Assert.That(subSceneId, Is.EqualTo(SubScene.gameObject.GetInstanceID()));
            Assert.That(isDynamicSubScene, Is.False);
        }

        [Test]
        public void GetSubSceneId_ReturnsDynamicSubSceneId()
        {
            Hash128 subSceneHash1 = GUID.Generate();
            Hash128 subSceneHash2 = GUID.Generate();

            Assert.That(m_SceneMapper.TryGetSceneOrSubSceneInstanceId(subSceneHash1, out _), Is.False);
            Assert.That(m_SceneMapper.TryGetSceneOrSubSceneInstanceId(subSceneHash2, out _), Is.False);

            Assert.That(m_SceneMapper.GetSubSceneId(subSceneHash1), Is.EqualTo((subSceneHash1.GetHashCode(), true)));
            Assert.That(m_SceneMapper.GetSubSceneId(subSceneHash2), Is.EqualTo((subSceneHash2.GetHashCode(), true)));
            Assert.That(m_SceneMapper.GetSubSceneId(subSceneHash1), Is.EqualTo((subSceneHash1.GetHashCode(), true)));
        }
    }
}
