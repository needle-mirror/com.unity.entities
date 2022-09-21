using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Editor
{
    [Serializable]
    class DataModeHandler : IDataModeHandlerAndDispatcher, IDisposable
    {
        readonly DataMode[] m_EditModeDataModes;
        readonly DataMode[] m_PlayModeDataModes;

        [SerializeField] int m_EditModeDataMode;
        [SerializeField] int m_PlayModeDataMode;

        public DataModeHandler(DataMode[] editModeDataModes, DataMode[] playModeDataModes)
        {
            m_EditModeDataModes = editModeDataModes;
            m_PlayModeDataModes = playModeDataModes;

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        void OnPlayModeStateChanged(PlayModeStateChange mode)
        {
            if (mode == PlayModeStateChange.EnteredPlayMode ||
                mode == PlayModeStateChange.EnteredEditMode)
            {
                dataModeChanged(CurrentMode);
            }
        }

        DataMode CurrentMode
        {
            get => EditorApplication.isPlaying ? m_PlayModeDataModes[m_PlayModeDataMode] : m_EditModeDataModes[m_EditModeDataMode];
            set
            {
                if (EditorApplication.isPlaying)
                    m_PlayModeDataMode = IndexOfMode(m_PlayModeDataModes, value);
                else
                    m_EditModeDataMode = IndexOfMode(m_EditModeDataModes, value);

                dataModeChanged(CurrentMode);
            }
        }

        int CurrentModeIndex => EditorApplication.isPlaying ? m_PlayModeDataMode : m_EditModeDataMode;

        public DataMode dataMode => CurrentMode;
        public  IReadOnlyList<DataMode> supportedDataModes => GetSupportedDataModesForContext();
        public bool IsDataModeSupported(DataMode mode) => IndexOfMode(GetSupportedDataModesForContext(), mode) >= 0;
        public void SwitchToNextDataMode()
        {
            var possibleDataModes = GetSupportedDataModesForContext();
            CurrentMode = possibleDataModes[(CurrentModeIndex + 1) % possibleDataModes.Length];
        }
        public void SwitchToDefaultDataMode() => CurrentMode = GetSupportedDataModesForContext()[0];
        public void SwitchToDataMode(DataMode mode) => CurrentMode = mode;

        DataMode[] GetSupportedDataModesForContext()
            => EditorApplication.isPlaying ? m_PlayModeDataModes : m_EditModeDataModes;

        static int IndexOfMode(DataMode[] modes, DataMode mode)
        {
            for (var i = 0; i < modes.Length; i++)
            {
                if (modes[i] == mode)
                    return i;
            }

            return -1;
        }

        public event Action<DataMode> dataModeChanged = delegate {  };

        public void Dispose()
            => EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }
}
