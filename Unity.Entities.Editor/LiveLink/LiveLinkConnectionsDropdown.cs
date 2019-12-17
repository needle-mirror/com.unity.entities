using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine;
using UnityEngine.UIElements;
using PopupWindow = UnityEditor.PopupWindow;

namespace Unity.Entities.Editor
{
    class LiveLinkConnectionsDropdown : PopupWindowContent, IDisposable
    {
        readonly List<LiveLinkConnection> m_LinkConnections = new List<LiveLinkConnection>();
        bool m_IsOpen;

        public LiveLinkConnectionsDropdown()
        {
            EditorSceneLiveLinkToPlayerSendSystem.instance.LiveLinkPlayerConnected += OnPlayerConnected;
            EditorSceneLiveLinkToPlayerSendSystem.instance.LiveLinkPlayerDisconnected += OnPlayerDisconnected;

            foreach (var connectedPlayer in EditorConnection.instance.ConnectedPlayers)
            {
                var playerId = connectedPlayer.playerId;
                var buildSettingGuid = EditorSceneLiveLinkToPlayerSendSystem.instance.GetBuildSettingsGUIDForLiveLinkConnection(playerId);
                m_LinkConnections.Add(new LiveLinkConnection(playerId, connectedPlayer.name, LiveLinkConnectionStatus.Connected, buildSettingGuid));
            }
        }

        void OnPlayerDisconnected(int playerId)
        {
            m_LinkConnections.RemoveAll(x => x.PlayerId == playerId);
            LiveLinkToolbar.RepaintPlaybar(); // to repaint the dropdown button itself
        }

        void OnPlayerConnected(int playerId, Hash128 buildSettingsGuid)
        {
            var connectedPlayer = EditorConnection.instance.ConnectedPlayers.Find(x => x.playerId == playerId);
            if (connectedPlayer == null)
                return;

            var existingConnection = m_LinkConnections.FirstOrDefault(x => x.PlayerId == playerId);
            if (existingConnection != null)
                existingConnection.Reset(connectedPlayer.name, LiveLinkConnectionStatus.Connected, buildSettingsGuid);
            else
                m_LinkConnections.Add(new LiveLinkConnection(connectedPlayer.playerId, connectedPlayer.name, LiveLinkConnectionStatus.Connected, buildSettingsGuid));

            if (m_IsOpen)
            {
                GenerateUiFromState();
            }

            LiveLinkToolbar.RepaintPlaybar(); // to repaint the dropdown button itself
        }

        public void DrawDropdown()
        {
            var dropdownRect = new Rect(130, 0, 40, 22);
            var hasConnectedDevices = m_LinkConnections.Any(c => c.Status == LiveLinkConnectionStatus.Connected);
            var icon = hasConnectedDevices ? Icons.LiveLinkOn : Icons.LiveLink;
            icon.tooltip = hasConnectedDevices
                    ? "View linked devices."
                    : "No devices currently linked. Create a Live Link build to connect a device.";

            if (EditorGUI.DropdownButton(dropdownRect, icon, FocusType.Keyboard, LiveLinkStyles.Dropdown))
            {
                PopupWindow.Show(dropdownRect, this);
            }
        }

        public override Vector2 GetWindowSize() => SizeHelper.GetDropdownSize(m_LinkConnections.Count);

        public override void OnOpen()
        {
            m_IsOpen = true;
            GenerateUiFromState();
        }

        void GenerateUiFromState()
        {
            const string basePath = "Packages/com.unity.entities/Editor/LiveLink";
            var template = m_LinkConnections.Count == 0
                ? UIElementHelpers.LoadTemplate(basePath, "LiveLinkConnectionsDropdown.Empty", "LiveLinkConnectionsDropdown")
                : UIElementHelpers.LoadTemplate(basePath, "LiveLinkConnectionsDropdown");

            if (m_LinkConnections.Count > 0)
            {
                var placeholder = template.Q<VisualElement>("devices");
                var tpl = UIElementHelpers.LoadClonableTemplate(basePath, "LiveLinkConnectionsDropdown.ItemTemplate");
                foreach (var connection in m_LinkConnections)
                {
                    var item = tpl.GetNewInstance();
                    var icon = item.Q<Image>();
                    icon.AddToClassList(GetStatusClass(connection.Status));
                    icon.RegisterCallback<PointerDownEvent>(i => ToggleConnectionStatus(i, connection));
                    var label = item.Q<Label>();
                    label.text = connection.Name.Length <= SizeHelper.MaxCharCount ? connection.Name : connection.Name.Substring(0, SizeHelper.MaxCharCount) + "...";
                    label.tooltip = $"{connection.Name} - build setting: {connection.BuildSettingsName}";
                    placeholder.Add(item);
                }
            }

            var footer = UIElementHelpers.LoadTemplate(basePath, "LiveLinkConnectionsDropdown.Footer", "LiveLinkConnectionsDropdown");
            footer.Q<Button>("live-link-connections-dropdown__footer__build").SetEnabled(false);
            var resetButton = footer.Q<Button>("live-link-connections-dropdown__footer__reset");
            var clearButton = footer.Q<Button>("live-link-connections-dropdown__footer__clear");

            if (m_LinkConnections.Count > 0)
            {
                resetButton.clickable.clicked += LiveLinkCommands.ResetPlayer;
                clearButton.clickable.clicked += LiveLinkCommands.ClearLiveLinkCache;
            }
            else
            {
                resetButton.SetEnabled(false);
                clearButton.SetEnabled(false);
            }

            template.Add(footer);
            editorWindow.rootVisualElement.Clear();
            editorWindow.rootVisualElement.Add(template);
        }

        public override void OnClose()
        {
            m_IsOpen = false;
        }

        void ToggleConnectionStatus(PointerDownEvent e, LiveLinkConnection connection)
        {
            switch (connection.Status)
            {
                case LiveLinkConnectionStatus.Connected:
                    DisconnectPlayer(e, connection);
                    break;
                case LiveLinkConnectionStatus.SoftDisconnected:
                    ReconnectPlayer(e, connection);
                    break;
            }
        }

        static void DisconnectPlayer(EventBase e, LiveLinkConnection connection)
        {
            var previousCls = GetStatusClass(connection.Status);
            var target = (VisualElement) e.target;
            connection.Status = LiveLinkConnectionStatus.SoftDisconnected;
            target.RemoveFromClassList(previousCls);
            target.AddToClassList(GetStatusClass(connection.Status));

            EditorSceneLiveLinkToPlayerSendSystem.instance.DisableSendForPlayer(connection.PlayerId);

            LiveLinkToolbar.RepaintPlaybar(); // to repaint the dropdown button itself
        }

        static void ReconnectPlayer(EventBase e, LiveLinkConnection connection)
        {
            var target = (VisualElement) e.target;
            var previousCls = GetStatusClass(connection.Status);
            connection.Status = LiveLinkConnectionStatus.Reseting;
            target.RemoveFromClassList(previousCls);
            target.AddToClassList(GetStatusClass(connection.Status));

            EditorSceneLiveLinkToPlayerSendSystem.instance.ResetPlayer(connection.PlayerId);
        }

        static string GetStatusClass(LiveLinkConnectionStatus connectionStatus)
        {
            switch (connectionStatus)
            {
                case LiveLinkConnectionStatus.Error:
                    return "live-link-connections-dropdown__status--error";
                case LiveLinkConnectionStatus.Connected:
                    return "live-link-connections-dropdown__status--connected";
                case LiveLinkConnectionStatus.SoftDisconnected:
                    return "live-link-connections-dropdown__status--soft-disconnected";
                case LiveLinkConnectionStatus.Reseting:
                    return "live-link-connections-dropdown__status--soft-reseting";
                default:
                    return null;
            }
        }

        public override void OnGUI(Rect rect) { }

        public void Dispose()
        {
            EditorSceneLiveLinkToPlayerSendSystem.instance.LiveLinkPlayerConnected -= OnPlayerConnected;
            EditorSceneLiveLinkToPlayerSendSystem.instance.LiveLinkPlayerDisconnected -= OnPlayerDisconnected;
        }

        class LiveLinkConnection
        {
            public int PlayerId { get; }
            public string Name { get; private set; }
            public LiveLinkConnectionStatus Status { get; set; }
            public Hash128 BuildSettingsGuid { get; private set; }
            public string BuildSettingsName { get; private set; }
            public LiveLinkConnection(int playerId, string name, LiveLinkConnectionStatus status, Hash128 buildSettingsGuid)
            {
                PlayerId = playerId;
                Name = name;
                Status = status;
                BuildSettingsGuid = buildSettingsGuid;
                BuildSettingsName = buildSettingsGuid != default ? Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(buildSettingsGuid.ToString())) : "Unknown";
            }

            public void Reset(string name, LiveLinkConnectionStatus status, Hash128 buildSettingsGuid)
            {
                Name = name;
                Status = status;
                BuildSettingsGuid = buildSettingsGuid;
                BuildSettingsName = buildSettingsGuid != default ? Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(buildSettingsGuid.ToString())) : "Unknown";
            }
        }

        enum LiveLinkConnectionStatus
        {
            Connected,
            SoftDisconnected,
            Error,
            Reseting
        }
    }

    static class SizeHelper
    {
        static readonly Vector2 s_EmptyDropdownSize = new Vector2(205, 110);
        const int Width = 250;
        const int ItemHeight = 19;
        const int PaddingHeight = 110;

        internal const int MaxCharCount = 30;

        public static Vector2 GetDropdownSize(int itemCount)
            => itemCount == 0 ? s_EmptyDropdownSize : new Vector2(Width, itemCount * ItemHeight + PaddingHeight);
    }
}