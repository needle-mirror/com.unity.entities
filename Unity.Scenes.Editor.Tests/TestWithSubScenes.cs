using System;
using UnityEditor.SceneManagement;

namespace Unity.Scenes.Editor.Tests
{
    [Serializable]
    struct TestWithSubScenes
    {
        public void Setup()
        {
        }

        public void TearDown()
        {
            SceneWithBuildConfigurationGUIDs.ClearBuildSettingsCache();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
    }
}
