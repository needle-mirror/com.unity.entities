using Unity.Entities;
using UnityEngine;

namespace Unity.Scenes.Editor
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(SceneSystemGroup))]
    class LiveConversionEditorSystemGroup : ComponentSystemGroup
    {
    }
}
