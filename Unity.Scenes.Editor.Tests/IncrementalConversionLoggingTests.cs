using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Conversion;
using Unity.Entities.Hybrid.Tests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes.Editor.Tests
{
    [Serializable]
    [TestFixture]
    class IncrementalConversionLoggingTests
    {
        [SerializeField] TestWithEditorLiveConversion m_EditorLiveConversion;
        [SerializeField] bool m_debugLoggingWasEnabled;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!m_EditorLiveConversion.OneTimeSetUp())
                return;
            SceneManager.SetActiveScene(EditorSceneManager.NewScene(NewSceneSetup.EmptyScene));

            m_debugLoggingWasEnabled = LiveConversionSettings.IsDebugLoggingEnabled;
            LiveConversionSettings.IsDebugLoggingEnabled = true;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_EditorLiveConversion.OneTimeTearDown();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            LiveConversionSettings.IsDebugLoggingEnabled = m_debugLoggingWasEnabled;
        }

        [SetUp]
        public void SetUp() => m_EditorLiveConversion.SetUp();

        [UnityTest]
        public IEnumerator IncrementalConversion_WithSingleObject_LogsAreCorrect()
        {
            var subScene = m_EditorLiveConversion.CreateEmptySubScene("TestSubScene", true);
            SceneManager.SetActiveScene(subScene.EditingScene);

            var w = m_EditorLiveConversion.GetLiveConversionWorldForEditMode();
            yield return m_EditorLiveConversion.UpdateEditorAndWorld(w);
            {
                var go = new GameObject("TestGameObject");

                Undo.RegisterCreatedObjectUndo(go, "Test Create");
                yield return m_EditorLiveConversion.UpdateEditorAndWorld(w);

                LogAssert.Expect(LogType.Log, new Regex("Reconverting 1 GameObjects"));
                Undo.RegisterCompleteObjectUndo(go, "Test Change GameObject");
                yield return m_EditorLiveConversion.UpdateEditorAndWorld(w);

                LogAssert.Expect(LogType.Log, new Regex("Reconverting 1 GameObjects"));
            }
        }
    }
}
