using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEditor;
using System;
using System.Linq;
using UnityEditor.Events;
using Unity.Serialization.Json;
using Unity.Properties;

namespace Unity.Build.Common
{
    internal class BuildQueue : ScriptableSingleton<BuildQueue>
    {
        [System.Serializable]
        internal class OnAllBuildsCompletedEvent : UnityEvent<BuildPipelineResult[]>
        {
        }

        [Serializable]
        internal class QueuedBuild
        {
            [SerializeField]
            internal BuildTarget requiredActiveTarget;

            [SerializeField]
            internal string buildSettingsGuid;

            [SerializeField]
            internal bool buildFinished;

            [SerializeField]
            internal string buildPipelineResult;
        }

        /// <summary>
        /// Sort builds, so less active target switching would occur.
        /// Build targets matching NoTarget (for ex., Dots) or active target, will come first. The others will follow
        /// </summary>
        internal class BuildStorter
        {
            BuildTarget m_CurrentActiveTarget;

            internal BuildStorter(BuildTarget currentActiveTarget)
            {
                m_CurrentActiveTarget = currentActiveTarget;
            }
            internal int Compare(QueuedBuild x, QueuedBuild y)
            {
                if (x.requiredActiveTarget == y.requiredActiveTarget)
                    return 0;

                if (x.requiredActiveTarget == m_CurrentActiveTarget || x.requiredActiveTarget == BuildTarget.NoTarget)
                    return -1;
                if (y.requiredActiveTarget == m_CurrentActiveTarget || y.requiredActiveTarget == BuildTarget.NoTarget)
                    return 1;

                return x.requiredActiveTarget.CompareTo(y.requiredActiveTarget);
            }
        }

        [SerializeField]
        List<QueuedBuild> m_QueueBuilds;

        [SerializeField]
        BuildTarget m_OriginalBuildTarget;

        [SerializeField]
        OnAllBuildsCompletedEvent m_OnAllBuildsCompletedEvent;

        List<QueuedBuild> m_PrepareQueueBuilds;

        public void OnEnable()
        {
            if (m_QueueBuilds == null)
                m_QueueBuilds = new List<QueuedBuild>();

            if (m_QueueBuilds.Count == 0)
            {
                Clear();
                return;
            }

            // Can't start builds right away, because BuildSettings might not be loaded yet, meaning OnEnable in ComponentContainer might not be called yet
            EditorApplication.delayCall += ProcessBuilds;
        }

        public void Clear()
        {
            m_OriginalBuildTarget = BuildTarget.NoTarget;
            if (m_PrepareQueueBuilds != null)
                m_PrepareQueueBuilds.Clear();
            if (m_QueueBuilds != null)
                m_QueueBuilds.Clear();
            m_OnAllBuildsCompletedEvent = null;
        }

        private static BuildSettings ToBuildSettings(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                throw new Exception("No valid build settings provided");

            var path = AssetDatabase.GUIDToAssetPath(guid);
            return AssetDatabase.LoadAssetAtPath<BuildSettings>(path);
        }

        private BuildTarget GetRequiredEditorTarget(BuildSettings buildSettings)
        {
            var classicBuildProfile = buildSettings.GetComponent<IBuildPipelineComponent>() as ClassicBuildProfile;
            if (classicBuildProfile != null)
                return classicBuildProfile.Target;
            return BuildTarget.NoTarget;
        }

        public void QueueBuild(BuildSettings buildSettings, BuildPipelineResult buildPipelineResult)
        {
            if (m_PrepareQueueBuilds == null)
                m_PrepareQueueBuilds = new List<QueuedBuild>();

            if (buildSettings == null)
                throw new ArgumentNullException(nameof(buildSettings));
            var b = new QueuedBuild();
            b.requiredActiveTarget = GetRequiredEditorTarget(buildSettings);
            b.buildSettingsGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(buildSettings));

            if (m_QueueBuilds.Count > 0)
                buildPipelineResult = BuildPipelineResult.Failure(buildSettings.GetBuildPipeline(), buildSettings, "Can't queue builds while executing build.");

            // If the build failed in previous step, don't execute it
            if (buildPipelineResult != null && buildPipelineResult.Failed)
                b.buildFinished = true;
            else
                b.buildFinished = false;

            b.buildPipelineResult = buildPipelineResult != null ? JsonSerialization.Serialize(buildPipelineResult) : string.Empty;

            m_PrepareQueueBuilds.Add(b);
        }

        public void FlushBuilds(UnityAction<BuildPipelineResult[]> onAllBuildsCompleted)
        {
            if (m_PrepareQueueBuilds == null || m_PrepareQueueBuilds.Count == 0)
                return;
            m_QueueBuilds.Clear();
            m_QueueBuilds.AddRange(m_PrepareQueueBuilds);
            m_PrepareQueueBuilds = null;

            m_OriginalBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            m_OnAllBuildsCompletedEvent = new OnAllBuildsCompletedEvent();
            if (onAllBuildsCompleted != null)
            {
                UnityEventTools.AddPersistentListener(m_OnAllBuildsCompletedEvent, onAllBuildsCompleted);
                m_OnAllBuildsCompletedEvent.SetPersistentListenerState(m_OnAllBuildsCompletedEvent.GetPersistentEventCount() - 1, UnityEventCallState.EditorAndRuntime);
            }
            var sorter = new BuildStorter(m_OriginalBuildTarget);
            m_QueueBuilds.Sort(sorter.Compare);
            ProcessBuilds();
        }

        private QueuedBuild GetNextUnfinishedBuild()
        {
            foreach (var b in m_QueueBuilds)
            {
                if (!b.buildFinished)
                    return b;
            }
            return null;
        }

        private void ProcessBuilds()
        {
            EditorApplication.delayCall -= ProcessBuilds;

            if (m_OriginalBuildTarget <= 0)
            {
                var invalidTarget = m_OriginalBuildTarget;
                Clear();
                throw new Exception($"Original build target is invalid: {invalidTarget}");
            }

            // Editor is compiling, wait until other frame
            if (EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += ProcessBuilds;
                return;
            }

            QueuedBuild currentBuild = GetNextUnfinishedBuild();

            while (currentBuild != null)
            {
                var t = currentBuild.requiredActiveTarget;
                var b = ToBuildSettings(currentBuild.buildSettingsGuid);

                if (t == BuildTarget.NoTarget || t == EditorUserBuildSettings.activeBuildTarget)
                {
                    currentBuild.buildPipelineResult = JsonSerialization.Serialize(b.Build());
                    currentBuild.buildFinished = true;
                }
                else
                {
                    if (!EditorUserBuildSettings.SwitchActiveBuildTarget(UnityEditor.BuildPipeline.GetBuildTargetGroup(t), t))
                    {
                        m_QueueBuilds.Clear();
                        throw new Exception($"Failed to switch active build target to {t}");
                    }
                    else
                    {
                        // Show dialog before actual build dialog, this way it's clear what's happening
                        EditorUtility.DisplayProgressBar("Hold on...", $"Switching to {t}", 0.0f);
                        return;
                    }
                }

                currentBuild = GetNextUnfinishedBuild();
            } 


            // No more builds to run?
            if (currentBuild == null)
            {
                // We're done
                if (m_OriginalBuildTarget == EditorUserBuildSettings.activeBuildTarget)
                {
                    EditorUtility.ClearProgressBar();
                    m_OnAllBuildsCompletedEvent.Invoke(m_QueueBuilds.Select(m =>
                    {
                        var buildPipelineResult = TypeConstruction.Construct<BuildPipelineResult>();
                        JsonSerialization.DeserializeFromString<BuildPipelineResult>(m.buildPipelineResult, ref buildPipelineResult);
                        return buildPipelineResult;
                    }).ToArray());
                    Clear();
                }
                else
                {
                    EditorUtility.DisplayProgressBar("Hold on...", $"Switching to original build target {m_OriginalBuildTarget}", 0.0f);
                    // Restore original build target
                    EditorUserBuildSettings.SwitchActiveBuildTarget(UnityEditor.BuildPipeline.GetBuildTargetGroup(m_OriginalBuildTarget), m_OriginalBuildTarget);
                }
                return;
            }
        }
    }
}