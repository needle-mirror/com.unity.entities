using System.Collections.Generic;
using Unity.Entities.Content;
using UnityEditor;
using UnityEngine.Analytics;


namespace Unity.Scenes.Editor
{
    internal static class EntitySceneBuildAnalytics
    {
        private const string VendorKey = "unity.entitySceneAnalytics";
        private const string BuildEvent = "entitySceneBuildContentEvent";
        private static HashSet<string> registeredEvents = new HashSet<string>();

        internal struct BuildData
        {
            public int NumberOfContentArchives;
            public int NumberOfWeakReferences;
            public int NumberOfSubScenes;
            public int NumberOfAssets;
            public int NumberOfContentFilesBuilt;
            public int NumberOfAssetsInSubScenes;
            public bool IsUsingContentArchives;
        }

        private static bool RegisterEvent(string eventName)
        {
            bool eventSuccessfullyRegistered = false;
            UnityEngine.Analytics.AnalyticsResult registerEvent = EditorAnalytics.RegisterEventWithLimit(eventName, 100, 100, VendorKey);
            if (registerEvent == UnityEngine.Analytics.AnalyticsResult.Ok)
            {
                registeredEvents.Add(eventName);
                eventSuccessfullyRegistered = true;
            }
            return eventSuccessfullyRegistered;
        }

        private static bool EventIsRegistered(string eventName)
        {
            return registeredEvents.Contains(eventName);
        }


        internal static void ReportBuildEvent(IRuntimeCatalogDataSource buildResultsCatalogDataSource, int numberOfAssetsInSubscenes, int numberOfWeakAssetReferences, int numberOfSubScenesInBuild, int numberOfAssets, bool isUsingContentArchives)
        {
            if (!EditorAnalytics.enabled)
                return;

            if (!EventIsRegistered(BuildEvent))
                if (!RegisterEvent(BuildEvent))
                    return;

            int numberOfContentFiles = 0;
            int numberOfContentArchives = 0;

            if (buildResultsCatalogDataSource != null)
            {
                var archiveIds = buildResultsCatalogDataSource.GetArchiveIds();
                foreach (var archiveId in archiveIds)
                {
                    if (archiveId.IsValid)
                    {
                        numberOfContentArchives++;
                        var fileIds = buildResultsCatalogDataSource.GetFileIds(archiveId);

                        foreach (var fileId in fileIds)
                            if (fileId.IsValid)
                                numberOfContentFiles++;
                    }
                }
            }

            BuildData data = new BuildData()
            {
                NumberOfAssets = numberOfAssets,
                NumberOfContentArchives = numberOfContentArchives,
                NumberOfWeakReferences = numberOfWeakAssetReferences,
                NumberOfSubScenes = numberOfSubScenesInBuild,
                NumberOfContentFilesBuilt = numberOfContentFiles,
                NumberOfAssetsInSubScenes = numberOfAssetsInSubscenes,
                IsUsingContentArchives = isUsingContentArchives
            };
            EditorAnalytics.SendEventWithLimit(BuildEvent, data);
        }
    }
}

