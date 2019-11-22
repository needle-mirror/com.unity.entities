using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using PropertyAttribute = Unity.Properties.PropertyAttribute;

namespace Unity.Build.Common.Tests
{
    public class BuildQueueTests
    {
        [Test]
        public void CanSortBuildsCorrectly()
        {
            var sorterActiveTargetAndroid = new BuildQueue.BuildStorter(BuildTarget.Android);
            var sorterActiveTargetStandaloneWindows = new BuildQueue.BuildStorter(BuildTarget.StandaloneWindows);

            var builds = new List<BuildQueue.QueuedBuild>(new []
            {
                new BuildQueue.QueuedBuild(){ requiredActiveTarget = BuildTarget.StandaloneWindows},
                new BuildQueue.QueuedBuild(){ requiredActiveTarget = BuildTarget.NoTarget},
                new BuildQueue.QueuedBuild(){ requiredActiveTarget = BuildTarget.iOS},
                new BuildQueue.QueuedBuild(){ requiredActiveTarget = BuildTarget.Android},
            });

            builds.Sort(sorterActiveTargetAndroid.Compare);

            Assert.That(builds[0].requiredActiveTarget == BuildTarget.NoTarget || builds[0].requiredActiveTarget == BuildTarget.Android, Is.True);
            Assert.That(builds[2].requiredActiveTarget == BuildTarget.StandaloneWindows, Is.True);
            Assert.That(builds[3].requiredActiveTarget == BuildTarget.iOS, Is.True);

            builds.Sort(sorterActiveTargetStandaloneWindows.Compare);

            Assert.That(builds[0].requiredActiveTarget == BuildTarget.NoTarget || builds[0].requiredActiveTarget == BuildTarget.StandaloneWindows, Is.True);
            Assert.That(builds[2].requiredActiveTarget == BuildTarget.iOS, Is.True);
            Assert.That(builds[3].requiredActiveTarget == BuildTarget.Android, Is.True);
        }

    }
}
