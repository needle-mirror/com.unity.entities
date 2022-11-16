using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Baking;

namespace Unity.Entities
{
    /// <summary>
    /// Stores the state of the baker (Added components, Created entities and all recorded dependencies)
    /// The purpose of it is to be able to revert the changes when the baker needs to run again because a dependency changed
    /// or if the component / game object was destroyed.
    /// </summary>
    internal struct BakerState : IDisposable
    {
        internal UnsafeList<TypeIndex>                 AddedComponents;
        internal UnsafeList<Entity>                    Entities;

#if UNITY_EDITOR
        internal UnsafeParallelHashSet<int>                    ReferencedPrefabs;
#endif
        internal UnsafeParallelHashSet<(uint, Hash128)>        ReferencedBlobAssets;

        internal BakeDependencies.RecordedDependencies Dependencies;
        internal BakerEntityUsage                      Usage;

        public BakerState(Entity entity, Allocator allocator)
        {
            AddedComponents = new UnsafeList<TypeIndex>(0, allocator);
            Entities = new UnsafeList<Entity>(1, allocator);
            Entities.Add(entity);
#if UNITY_EDITOR
            ReferencedPrefabs = new UnsafeParallelHashSet<int>(1, allocator);
#endif
            ReferencedBlobAssets = new UnsafeParallelHashSet<(uint, Hash128)>(1, allocator);
            Dependencies = new BakeDependencies.RecordedDependencies(0, allocator);

            Usage = new BakerEntityUsage(entity, 0, allocator);
        }

        public void Revert(EntityCommandBuffer ecb, Entity newPrimaryEntity, ref UnsafeParallelHashMap<Entity, TransformUsageFlagCounters> transformUsage, BlobAssetStore blobAssetStore, ref bool dirtyEntityUsage, ref BakedEntityData bakedEntityData)
        {
            var oldPrimaryEntity = Entities[0];

            // Revert components on primary entity
            foreach (var typeIndex in AddedComponents)
                ecb.RemoveComponent(oldPrimaryEntity, ComponentType.FromTypeIndex(typeIndex));

            // Destroy entities except the first one, since thats always the primary entity
            for (int i = 1; i < Entities.Length; i++)
            {
                transformUsage.Remove(Entities[i]);
                ecb.DestroyEntity(Entities[i]);
                dirtyEntityUsage = true;
            }

#if UNITY_EDITOR
            var enumeratorPrefabs = ReferencedPrefabs.GetEnumerator();
            while (enumeratorPrefabs.MoveNext())
            {
                var prefabInstanceId = enumeratorPrefabs.Current;
                bakedEntityData.RemovePrefabRef(prefabInstanceId);
            }
            enumeratorPrefabs.Dispose();
            ReferencedPrefabs.Clear();
#endif

            var enumeratorBlob = ReferencedBlobAssets.GetEnumerator();
            while (enumeratorBlob.MoveNext())
            {
                var blobHash = enumeratorBlob.Current;
                blobAssetStore.TryRemove(blobHash.Item2, blobHash.Item1, true);
            }
            enumeratorBlob.Dispose();
            ReferencedBlobAssets.Clear();

            AddedComponents.Clear();
            Entities.Resize(1, NativeArrayOptions.UninitializedMemory);
            Entities[0] = newPrimaryEntity;
        }

        public void Dispose()
        {
            AddedComponents.Dispose();
            Entities.Dispose();
            Dependencies.Dispose();
            ReferencedBlobAssets.Dispose();

#if UNITY_EDITOR
            ReferencedPrefabs.Dispose();
#endif
        }

        public UnsafeList<Entity> GetEntities()
        {
            return Entities;
        }

        public Entity GetPrimaryEntity()
        {
            return Entities[0];
        }
    }
}
