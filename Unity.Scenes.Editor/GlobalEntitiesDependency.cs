using System;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    // Used to force rebuilding of all entity scenes (subscenes) or livelink assets
    // When used, this creates an asset that is dependency on either entity scene importers or livelink asset importers
    // These assets should be committed with the project so that it is compatible with the cache server
    internal class GlobalEntitiesDependency : ScriptableObject
    {
        [SerializeField]
        internal Hash128 cacheGUID;
    }
}
