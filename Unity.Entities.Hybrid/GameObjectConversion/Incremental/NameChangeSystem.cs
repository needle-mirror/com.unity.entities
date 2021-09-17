#if !DOTS_DISABLE_DEBUG_NAMES
using Unity.Entities.Conversion;

namespace Unity.Entities
{
    [UpdateInGroup(typeof(ConversionSetupGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    partial class NameChangeSystem : SystemBase
    {
        IncrementalChangesSystem _incremental;
        GameObjectConversionMappingSystem _mapping;

        protected override void OnCreate()
        {
            base.OnCreate();

            _incremental = World.GetExistingSystem<IncrementalChangesSystem>();
            _mapping = World.GetExistingSystem<GameObjectConversionMappingSystem>();
        }

        protected override void OnUpdate()
        {
            if (!_mapping.AssignName)
                return;

            for (var i = 0; i < _incremental.IncomingChanges.ChangedGameObjects.Count; i++)
            {
                var gameObject = _incremental.IncomingChanges.ChangedGameObjects[i];
                var entity = _mapping.TryGetPrimaryEntity(gameObject);
                if (entity == Entity.Null)
                    continue;

                _mapping.DstEntityManager.GetName(entity, out var name);
                if (name.CompareTo(gameObject.name) != 0)
                    _mapping.DstEntityManager.SetName(entity, gameObject.name);
            }
        }
    }
}
#endif
