#if !USE_IMPROVED_DATAMODE
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Entities.Editor.Tests
{
#if DOTSHIERARCHY_ENABLE_DATAMODES
    [Serializable]
    sealed class DataModeHandlerTests
    {
        static readonly DataMode[] m_EditModeDataModes = { DataMode.Authoring, DataMode.Runtime };
        static readonly DataMode[] m_PlayModeDataModes = { DataMode.Mixed, DataMode.Runtime, DataMode.Authoring };

        [SerializeField] DataModeHandler m_DataModeHandler;
        [SerializeField] bool m_DataModeChangedEventFired;
        [SerializeField] DataMode m_LastDataModeInEvent;

        [SetUp]
        public void SetUp()
        {
            m_DataModeHandler ??= new DataModeHandler(m_EditModeDataModes, m_PlayModeDataModes);
            m_DataModeHandler.dataModeChanged += OnDataModeChanged;
        }

        void OnDataModeChanged(DataMode mode)
        {
            m_DataModeChangedEventFired = true;
            m_LastDataModeInEvent = mode;
        }

        [TearDown]
        public void TearDown()
        {
            m_DataModeHandler.dataModeChanged -= OnDataModeChanged;
            m_DataModeHandler = null;
            m_DataModeChangedEventFired = false;
            m_LastDataModeInEvent = default;
        }

        void AssertThatEventHasNotBeenFired()
        {
            Assert.That(m_DataModeChangedEventFired, Is.False);
            Assert.That(m_LastDataModeInEvent, Is.EqualTo((DataMode)default));
        }

        void AssertThatEventHasBeenFiredWithArgument(DataMode expectedDataModeInEvent)
        {
            Assert.That(m_DataModeChangedEventFired, Is.True);
            Assert.That(m_LastDataModeInEvent, Is.EqualTo(expectedDataModeInEvent));

            m_DataModeChangedEventFired = false;
            m_LastDataModeInEvent = default;
        }

        [Test]
        public void EditMode_ModeSwitching()
        {
            Assert.That(EditorApplication.isPlaying, Is.False);
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Authoring), $"Initial data mode in edit mode must be {DataMode.Authoring}");
            AssertThatEventHasNotBeenFired();

            m_DataModeHandler.SwitchToNextDataMode();
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Runtime), $"Next data mode after default in edit mode must be {DataMode.Runtime}");
            AssertThatEventHasBeenFiredWithArgument(DataMode.Runtime);

            m_DataModeHandler.SwitchToNextDataMode();
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Authoring), $"Next data mode after {DataMode.Runtime} in edit mode must be {DataMode.Authoring}");
            AssertThatEventHasBeenFiredWithArgument(DataMode.Authoring);

            m_DataModeHandler.SwitchToDefaultDataMode();
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Authoring), $"Default data mode in edit mode must be {DataMode.Authoring}");
            AssertThatEventHasBeenFiredWithArgument(DataMode.Authoring);

            Assert.That(m_DataModeHandler.IsDataModeSupported(DataMode.Authoring), Is.True);
            Assert.That(m_DataModeHandler.IsDataModeSupported(DataMode.Runtime), Is.True);
            Assert.That(m_DataModeHandler.IsDataModeSupported(DataMode.Mixed), Is.False);
        }

        [UnityTest]
        public IEnumerator PlayMode_ModeSwitching()
        {
            Assert.That(EditorApplication.isPlaying, Is.False);

            yield return new EnterPlayMode();

            Assert.That(EditorApplication.isPlaying, Is.True);
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Mixed), $"Initial data mode in play mode must be {DataMode.Mixed}");
            // We can only assert this when enter playmode doesn't trigger a domain reload because
            // the enter play mode change event is triggered before we get the chance to register to the event.
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0)
                AssertThatEventHasBeenFiredWithArgument(DataMode.Mixed);

            m_DataModeHandler.SwitchToNextDataMode();
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Runtime), $"Next data mode after default in play mode must be {DataMode.Runtime}");
            AssertThatEventHasBeenFiredWithArgument(DataMode.Runtime);

            m_DataModeHandler.SwitchToNextDataMode();
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Authoring), $"Next data mode after {DataMode.Runtime} in play mode must be {DataMode.Authoring}");
            AssertThatEventHasBeenFiredWithArgument(DataMode.Authoring);

            m_DataModeHandler.SwitchToNextDataMode();
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Mixed), $"Next data mode after {DataMode.Authoring} in play mode must be {DataMode.Mixed}");
            AssertThatEventHasBeenFiredWithArgument(DataMode.Mixed);

            m_DataModeHandler.SwitchToDefaultDataMode();
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Mixed), $"Default data mode in play mode must be {DataMode.Mixed}");
            AssertThatEventHasBeenFiredWithArgument(DataMode.Mixed);

            Assert.That(m_DataModeHandler.IsDataModeSupported(DataMode.Authoring), Is.True);
            Assert.That(m_DataModeHandler.IsDataModeSupported(DataMode.Runtime), Is.True);
            Assert.That(m_DataModeHandler.IsDataModeSupported(DataMode.Mixed), Is.True);
        }

        [UnityTest]
        public IEnumerator AlternateBetweenPlayModeEditMode_ReuseLastSelectedMode()
        {
            m_DataModeHandler.SwitchToDataMode(DataMode.Runtime);
            yield return new EnterPlayMode();
            m_DataModeHandler.SwitchToDataMode(DataMode.Authoring);
            yield return new ExitPlayMode();

            Assert.That(EditorApplication.isPlaying, Is.False);
            AssertThatEventHasBeenFiredWithArgument(DataMode.Runtime);
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Runtime));

            yield return new EnterPlayMode();

            Assert.That(EditorApplication.isPlaying, Is.True);
            // We can only assert this when enter playmode doesn't trigger a domain reload because
            // the enter play mode change event is triggered before we get the chance to register to the event.
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0)
                AssertThatEventHasBeenFiredWithArgument(DataMode.Authoring);
            Assert.That(m_DataModeHandler.dataMode, Is.EqualTo(DataMode.Authoring));
        }
    }
#endif //DOTSHIERARCHY_ENABLE_DATAMODES
}
#endif
