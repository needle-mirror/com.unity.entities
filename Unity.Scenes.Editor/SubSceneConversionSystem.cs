using System.IO;
using Unity.Entities;

namespace Unity.Scenes.Editor
{
    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    internal partial class SubSceneConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((SubScene subscene) =>
            {
                var entity = GetPrimaryEntity(subscene);
                DstEntityManager.AddComponentData(entity, new SceneReference() {SceneGUID = subscene.SceneGUID});

                if (subscene.AutoLoadScene)
                {
                    DstEntityManager.AddComponentData(entity,
                        new RequestSceneLoaded());
                }
            }).WithStructuralChanges().Run();
        }
    }

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
