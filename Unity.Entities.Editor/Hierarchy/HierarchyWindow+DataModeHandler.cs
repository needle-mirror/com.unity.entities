using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Editor
{
    partial class HierarchyWindow : IDataModeHandlerAndDispatcher
    {
        [SerializeField] DataModeHandler m_DataModeHandler = new DataModeHandler(new DataMode[] { DataMode.Authoring, DataMode.Runtime }, new DataMode[] { DataMode.Mixed, DataMode.Runtime, DataMode.Authoring });

        bool IDataModeHandler.IsDataModeSupported(DataMode mode) => m_DataModeHandler.IsDataModeSupported(mode);
        void IDataModeHandler.SwitchToNextDataMode()
        {
            m_DataModeHandler.SwitchToNextDataMode();
            Analytics.SendEditorEvent(Analytics.Window.Hierarchy, Analytics.EventType.DataModeManualSwitch, m_DataModeHandler.dataMode.ToString());
        }
        void IDataModeHandler.SwitchToDataMode(DataMode mode)
        {
            Analytics.SendEditorEvent(Analytics.Window.Hierarchy, Analytics.EventType.DataModeManualSwitch, mode.ToString());
            m_DataModeHandler.SwitchToDataMode(mode);
        }
        void IDataModeHandler.SwitchToDefaultDataMode() => m_DataModeHandler.SwitchToDefaultDataMode();
        DataMode IDataModeHandler.dataMode => m_DataModeHandler.dataMode;
        IReadOnlyList<DataMode> IDataModeHandler.supportedDataModes => m_DataModeHandler.supportedDataModes;
        event Action<DataMode> IDataModeHandlerAndDispatcher.dataModeChanged
        {
            add => m_DataModeHandler.dataModeChanged += value;
            remove => m_DataModeHandler.dataModeChanged -= value;
        }
    }
}
