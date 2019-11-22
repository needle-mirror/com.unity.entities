using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;
using PropertyAttribute = Unity.Properties.PropertyAttribute;
using UnityEngine.Events;

namespace Unity.Build.Common
{
    public struct BuildBatchItem
    {
        public BuildSettings BuildSettings;
        public Action<BuildContext> Mutator;
    }

    public struct BuildBatchDescription
    {
        public BuildBatchItem[] BuildItems;
        public UnityAction<BuildPipelineResult[]> OnBuildCompleted;
    }
}
