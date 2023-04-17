#if UNITY_EDITOR
#if ENABLE_CLOUD_SERVICES_ANALYTICS
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Analytics;

namespace Unity.Entities
{
    [InitializeOnLoad]
    static class BakingAnalytics
    {
        static bool s_EventsRegistered = false;
        const int k_MaxEventsPerHour = 1000;
        const int k_MaxNumberOfElements = 1000;
        const string k_VendorKey = "unity.entities";

        const string k_EventNameComplexity = "subSceneComplexity";
        const string k_EventNameIncremental = "incrementalBaking";
        const string k_EventNameOpen = "openSubScene";
        const string k_EventNameImporter = "backgroundImporter";

        static ProjectComplexityData s_ProjectComplexityData;
        static NativeList<TypeIndex> s_BakeTypeIndices;
        static IReadOnlyList<System.Type> s_BakingSystemTypes;

        public static IReadOnlyList<System.Type> BakingSystemTypes
        {
            set { s_BakingSystemTypes = value; }
        }

        public static void LogBakerTypeIndex(TypeIndex bakeTypeIndex)
        {
            s_BakeTypeIndices.Add(bakeTypeIndex);
        }

        public static void LogBlobAssetCount(TypeIndex blobAssetCount)
        {
            s_ProjectComplexityData.blob_assets_count = blobAssetCount;
        }

        public static void LogPrefabCount(TypeIndex prefabCount)
        {
            s_ProjectComplexityData.prefabs_count = prefabCount;
        }


        static BakingAnalytics()
        {
            s_BakeTypeIndices = new NativeList<TypeIndex>(Allocator.Persistent);// de-allocate
            s_ProjectComplexityData = new ProjectComplexityData()
            {
                default_components_count = 0,
                custom_bakers_count = 0,
                custom_baking_systems_count = 0,
                blob_assets_count = 0,
                prefabs_count = 0,
            };

            AppDomain.CurrentDomain.DomainUnload += (_, __) => { s_BakeTypeIndices.Dispose(); };
        }

        static bool EnableAnalytics()
        {
            if (!s_EventsRegistered)
            {
                AnalyticsResult resultComplexity = EditorAnalytics.RegisterEventWithLimit(k_EventNameComplexity, k_MaxEventsPerHour,
                    k_MaxNumberOfElements, k_VendorKey);
                AnalyticsResult resultIncremental = EditorAnalytics.RegisterEventWithLimit(k_EventNameIncremental, k_MaxEventsPerHour,
                    k_MaxNumberOfElements, k_VendorKey);
                AnalyticsResult resultOpen = EditorAnalytics.RegisterEventWithLimit(k_EventNameOpen, k_MaxEventsPerHour,
                    k_MaxNumberOfElements, k_VendorKey);
                AnalyticsResult resultImporter = EditorAnalytics.RegisterEventWithLimit(k_EventNameImporter, k_MaxEventsPerHour,
                    k_MaxNumberOfElements, k_VendorKey);

                if (resultComplexity == AnalyticsResult.Ok ||resultIncremental == AnalyticsResult.Ok ||
                    resultOpen == AnalyticsResult.Ok || resultImporter == AnalyticsResult.Ok)
                    s_EventsRegistered = true;
            }

            return s_EventsRegistered;
        }

        public enum EventType
        {
            IncrementalBaking,
            OpeningSubScene,
            BackgroundImporter
        }

        public static void SendAnalyticsEvent(float elapsedMs, EventType eventType)
        {
            //The event shouldn't be able to report if this is disabled but if we know we're not going to report
            //Lets early out and not waste time gathering all the data
            if (!EditorAnalytics.enabled)
                return;

            if (!EnableAnalytics())
                return;

            if (eventType == EventType.IncrementalBaking)
            {
                SendIncrementalBakingPerformanceEvents(elapsedMs);
            }else if (eventType == EventType.OpeningSubScene)
            {
                SendComplexityEvent();
                SendOpenSubScenePerformanceEvents(elapsedMs);
            }
            else
            {
                SendBackgroundImporterPerformanceEvents(elapsedMs);
            }

            s_BakeTypeIndices.Clear();
            s_ProjectComplexityData.Clear();
        }


        static void SendComplexityEvent()
        {
            int defaultComponentCount = 0;
            int customBakerCount = 0;
            int customBakingSystemCount = 0;

            for (int i = 0; i < s_BakeTypeIndices.Length; i++)
            {
                var bakeTypeIndex = s_BakeTypeIndices[i];
                var bakers = BakerDataUtility.GetBakers(bakeTypeIndex);

                // If the Component is custom
                var assembly = TypeManager.GetType(bakeTypeIndex).Assembly;

                // Check if the Component/Bakers are from a Unity Assembly or a Custom one
                bool isUnityAssembly;
                if (BakerDataUtility._BakersByAssembly.TryGetValue(assembly, out var assemblyData))
                    isUnityAssembly = assemblyData.IsUnityAssembly;
                else
                    isUnityAssembly = assembly.GetName().Name.StartsWith("Unity.") || assembly.GetName().Name.StartsWith("UnityEngine.");

                // Log the Component/Bakers according to Assembly
                if (isUnityAssembly)
                    defaultComponentCount++;
                else
                    customBakerCount += bakers.Length;
            }


            for (int i = 0; i < s_BakingSystemTypes.Count; i++)
            {
                // If the BakingSystem is custom
                var assembly = s_BakingSystemTypes[i].Assembly;

                // Check if the BakingSystem is from a Unity Assembly or a Custom one
                bool isUnityAssembly;
                if (BakerDataUtility._BakersByAssembly.TryGetValue(assembly, out var assemblyData))
                    isUnityAssembly = assemblyData.IsUnityAssembly;
                else
                    isUnityAssembly = assembly.GetName().Name.StartsWith("Unity.") || assembly.GetName().Name.StartsWith("UnityEngine.");

                // Log the BakingSystem according to Assembly
                if (!isUnityAssembly)
                    customBakingSystemCount++;
            }

            s_ProjectComplexityData.default_components_count = defaultComponentCount;
            s_ProjectComplexityData.custom_bakers_count = customBakerCount;
            s_ProjectComplexityData.custom_baking_systems_count = customBakingSystemCount;

            // collect max every playmode enter, send when project is closed
            EditorAnalytics.SendEventWithLimit(k_EventNameComplexity, s_ProjectComplexityData);
        }

        static void SendIncrementalBakingPerformanceEvents(float elapsedMs)
        {
            EditorAnalytics.SendEventWithLimit(k_EventNameIncremental, new PerformanceData(){elapsedMs = elapsedMs});
        }
        static void SendOpenSubScenePerformanceEvents(float elapsedMs)
        {
            EditorAnalytics.SendEventWithLimit(k_EventNameOpen, new PerformanceData(){elapsedMs = elapsedMs});
        }
        static void SendBackgroundImporterPerformanceEvents(float elapsedMs)
        {
            EditorAnalytics.SendEventWithLimit(k_EventNameImporter, new PerformanceData(){elapsedMs = elapsedMs});
        }

        struct ProjectComplexityData
        {
            public int default_components_count;
            public int custom_bakers_count;
            public int custom_baking_systems_count;
            public int blob_assets_count;
            public int prefabs_count;

            internal void Print()
            {
                UnityEngine.Debug.Log($"default_components_count = {default_components_count}, " +
                    $"custom_bakers_count = {custom_bakers_count}, " +
                    $"custom_baking_systems_count = {custom_baking_systems_count}, " +
                    $"blob_assets_count = {blob_assets_count}, " +
                    $"prefabs_count = {prefabs_count}, ");
            }

            public void Clear()
            {
                default_components_count = 0;
                custom_bakers_count = 0;
                custom_baking_systems_count = 0;
                blob_assets_count = 0;
                prefabs_count = 0;
            }
        }

        struct PerformanceData
        {
            public float elapsedMs;
        }
    }
}
#endif // ENABLE_CLOUD_SERVICES_ANALYTICS
#endif // UNITY_EDITOR
