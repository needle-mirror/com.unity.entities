using Unity.Entities;
using UnityEngine;

namespace Unity.Scenes
{
    /// <summary>
    /// The group of systems responsible for loading and unloading scenes.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation,
        WorldSystemFilterFlags.Default | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public class SceneSystemGroup : ComponentSystemGroup
    {
    }
}
