using System;
using UnityEditor;
using UnityEngine.Analytics;
using UnityEditor.Analytics;

namespace Unity.Entities.Editor
{
    [InitializeOnLoad]
    static class Analytics
    {
        // NOTE: Don't change names or numbers here because
        // these names and numbers are used in the cloud
        // analytics tables and any change would invalidate
        // previously recorded data. Should only add new enums.
        public enum Window
        {
            Unknown = 0,
            Inspector = 1,
            Hierarchy = 2,
            Components = 3,
            Systems = 4,
            Query = 5,
            Archetypes = 6,
            Journaling = 7,
            Profiler = 8
        }

        // NOTE: Don't change names or numbers here because
        // these names and numbers are used in the cloud
        // analytics tables and any change would invalidate
        // previously recorded data. Should only add new enums.
        public enum EventType
        {
            WindowOpen = 0,
            WindowFocus = 1,
            ProfilerModuleCreate = 2,
            InspectorTabFocus = 3,
            RelationshipGoTo = 4,
            DataModeSwitch = 5,
            DataModeManualSwitch = 6
        }

        // NOTE: Don't change existing fields here because
        // these existing fields are used in the cloud
        // analytics tables and any change would invalidate
        // previously recorded data. Should only add new fields.
        [Serializable]
        public struct EventPayload
        {
            public string window_name;
            public string event_type;
            public string context;
        };

        // Constants
        // NOTE: Don't change existing strings here because
        // these existing strings are used in the cloud
        // analytics tables and any change would invalidate
        // previously recorded data. Should only add new strings.
        public const string MemoryProfilerModuleName = "Memory";
        public const string StructuralChangesProfilerModuleName = "StructuralChanges";
        public const string AspectsTabName = "Aspects";
        public const string ComponentsTabName = "Components";
        public const string RelationshipsTabName = "Relationships";
        public const string GoToEntityDestination = "Entity";
        public const string GoToComponentDestination = "Component";
        public const string GoToSystemDestination = "System";

        const string k_VendorKey = "unity.entities";
        const int k_MaxEventsPerHour = 1000;
        const int k_MaxNumberOfElements = 1000;

        static bool s_EditorEventRegistered = false;
        const string k_EditorEventName = "uEntitiesEditorUsage";

        static bool EnableEditorAnalytics()
        {
            AnalyticsResult result = EditorAnalytics.RegisterEventWithLimit(k_EditorEventName, k_MaxEventsPerHour, k_MaxNumberOfElements, k_VendorKey, 2);
            if (result == AnalyticsResult.Ok)
                s_EditorEventRegistered = true;

            return s_EditorEventRegistered;
        }

        public static void SendEditorEvent(Window window, EventType eventType, string context = default)
        {
            // The event shouldn't be able to report if this is disabled but
            // if we know we're not going to report lets early out and not waste
            // time gathering all the data
            if (!EditorAnalytics.enabled)
                return;

            if (!EnableEditorAnalytics())
                return;

            var data = new EventPayload()
            {
                window_name = window.ToString(),
                event_type = eventType.ToString(),
                context = context
            };

            EditorAnalytics.SendEventWithLimit(k_EditorEventName, data, 2);
        }
    }
}
