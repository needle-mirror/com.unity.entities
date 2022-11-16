using System.IO;
using Unity.Entities;

namespace Unity.Scenes.Editor
{
    internal class SubSceneBaker : Baker<SubScene>
    {
        public override void Bake(SubScene authoring)
        {
            AddComponent(new SceneReference() {SceneGUID = authoring.SceneGUID});
            if (authoring.AutoLoadScene)
            {
                AddComponent(new RequestSceneLoaded());
            }
        }
    }
}
