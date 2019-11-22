using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using PropertyAttribute = Unity.Properties.PropertyAttribute;
using System.Linq;
using System;

namespace Unity.Build.Common.Tests
{
    public class BuildPipelineExtensionTests
    {
        const string k_TestRootFolder = "Assets/Tests/BuildPipelineExtensions/";
        const string k_TestBuildBuildpipelineAssetPath = k_TestRootFolder + "TestBuildPipeline.buildpipeline";
        const string k_TestBuildSettings32AssetPath = k_TestRootFolder + "TestBuildSettings32.buildsettings";
        const string k_TestBuildSettings64AssetPath = k_TestRootFolder + "TestBuildSettings64.buildsettings";
        const string k_TestsContainer = k_TestRootFolder + "Container.asset";

        class TestBuildProfileComponent : IBuildPipelineComponent
        {
            [Property] public BuildPipeline Pipeline { get; set; }
        }

        [BuildStep(flags = BuildStepAttribute.Flags.Hidden)]
        sealed class TestBuildStepSuccess : BuildStep
        {
            public override Type[] OptionalComponents => new[] { typeof(OutputBuildDirectory) };
            public override string Description => nameof(TestBuildStepSuccess);
            public override BuildStepResult RunBuildStep(BuildContext context)
            {
                Directory.CreateDirectory(this.GetOutputBuildDirectory(context));
                var path = Path.Combine(this.GetOutputBuildDirectory(context), "Result.txt");
                File.WriteAllText(path, "success");
                return Success();
            }
        }

        // Note: To survive domain reload, the settings have to fields and serializables
        [SerializeField]
        BuildSettings m_SettingsWindows64;
        [SerializeField]
        BuildSettings m_SettingsWindows32;
        [SerializeField]
        BuildPipeline m_BuildPipeline;
        [SerializeField]
        UnityEditor.BuildTarget m_OriginalBuildTarget;
        [SerializeField]
        BuildTargetGroup m_OriginalBuildTargetGroup;

        [SerializeField]
        ResultContainer m_Container;

        private BuildTarget Standalone32Target
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                        return BuildTarget.StandaloneWindows;
                    case RuntimePlatform.OSXEditor:
                        // No 32 bit target on OSX
                        return BuildTarget.StandaloneOSX;
                    default:
                        throw new NotImplementedException("Please implement for " + Application.platform);
                }
            }
        }

        private BuildTarget Standalone64Target
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                        return BuildTarget.StandaloneWindows64;
                    case RuntimePlatform.OSXEditor:
                        return BuildTarget.StandaloneOSX;
                    default:
                        throw new NotImplementedException("Please implement for " + Application.platform);
                }
            }
        }

        [TearDown]
        public void Teardown()
        {
            BuildPipelineExtensions.CancelBuildAsync();
            AssetDatabase.DeleteAsset(k_TestRootFolder);
        }

        /// <summary>
        /// Test build case where Editor target switch is required
        /// Note: Currently we can only effectively test this on Windows, because we have there 32 & 64 bit targets, thus we'll need target switch.
        ///       On OSX, there's only 64 bit target, so no real target switch will be required
        /// </summary>
        /// <returns></returns>
        [UnityTest]
        public IEnumerator CanBuildMultipleBuildsWithActiveTargetSwitch()
        {
            m_BuildPipeline = BuildPipeline.CreateAsset(k_TestBuildBuildpipelineAssetPath,
                (p) =>
                {
                    p.BuildSteps.Add(new TestBuildStepSuccess());
                });

            m_SettingsWindows32 = BuildSettings.CreateAsset(k_TestBuildSettings32AssetPath, (bs) =>
            {
                bs.SetComponent(new ClassicBuildProfile()
                {
                    Target = Standalone32Target,
                    Pipeline = m_BuildPipeline
                }); 
            });

            m_SettingsWindows64 = BuildSettings.CreateAsset(k_TestBuildSettings64AssetPath, (bs) =>
            {
                bs.SetComponent(new ClassicBuildProfile()
                {
                    Target = Standalone64Target,
                    Pipeline = m_BuildPipeline
                });
            });

            m_Container = ResultContainer.CreateInstance<ResultContainer>();
            m_Container.Results = null;
            m_Container.Completed = false;
            AssetDatabase.CreateAsset(m_Container, k_TestsContainer);
            AssetDatabase.ImportAsset(k_TestsContainer, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            m_Container = AssetDatabase.LoadAssetAtPath<ResultContainer>(k_TestsContainer);

            m_OriginalBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            m_OriginalBuildTargetGroup = UnityEditor.BuildPipeline.GetBuildTargetGroup(m_OriginalBuildTarget);

            // Leave this for testing purposes
            //if (EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, UnityEditor.BuildTarget.Android))
            //    yield return new RecompileScripts(false);

            var serttingsToBuilds = new[] { m_SettingsWindows32, m_SettingsWindows64 };
            BuildPipelineExtensions.BuildAsync(new BuildBatchDescription()
            {
                BuildItems = serttingsToBuilds.Select(m => new BuildBatchItem() { BuildSettings = m }).ToArray(),
                OnBuildCompleted = m_Container.SetCompleted
            });

            while (m_Container.Completed == false)
            {
                yield return new RecompileScripts(false);
            }
 
            Assert.IsTrue(EditorUserBuildSettings.activeBuildTarget == m_OriginalBuildTarget);
            Assert.IsTrue(m_Container.Results != null);
            Assert.IsTrue(m_Container.Results.Contains(m_SettingsWindows32.name + ", Success"));
            Assert.IsTrue(m_Container.Results.Contains(m_SettingsWindows64.name + ", Success"));
        }
    }
}
