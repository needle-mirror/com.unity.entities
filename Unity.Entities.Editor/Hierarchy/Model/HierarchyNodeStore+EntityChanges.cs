using System;
using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Unity.Entities.Editor
{
    partial struct HierarchyNodeStore
    {
        /// <summary>
        /// Represents a batch of changes to be integrated in to the dynamic hierarchy model.
        /// </summary>
        unsafe struct HierarchyEntityChangesBatch
        {
            /// <summary>
            /// !!IMPORTANT!! This is the order we will process elements for the batch.
            /// </summary>
            enum ChangeType
            {
                RemovedSceneReferencesWithoutSceneTags = 0,
                RemovedSceneTagsWithSceneReferences,
                RemovedSceneReferences,
                RemovedSceneTagsWithoutParents,
                RemovedParents,
                DestroyedEntities,
                CreatedEntities,
                AddedSceneReferences,
                AddedSceneReferencesWithoutSceneTags,
                AddedSceneTagWithSceneReferences,
                AddedSceneTagWithoutParents,
                AddedParents,
                
                // Used to compute array lengths.
                Count
            }

            readonly struct Range
            {
                public readonly int Start;
                public readonly int Count;

                public Range(int start, int count)
                {
                    Start = start;
                    Count = count;
                }
            }

            readonly HierarchyEntityChanges m_Changes;
            readonly int m_BatchStart;
            readonly int m_BatchCount;
            
#pragma warning disable 649
            fixed int m_Offset[(int) ChangeType.Count];
            fixed int m_Length[(int) ChangeType.Count];
#pragma warning restore 649

            public NativeArray<Entity> CreatedEntities => SubArray(m_Changes.CreatedEntities, GetRange(ChangeType.CreatedEntities));
            public NativeArray<Entity> DestroyedEntities => SubArray(m_Changes.DestroyedEntities, GetRange(ChangeType.DestroyedEntities));
            
            public NativeArray<Entity> AddedSceneReferencesEntities=> SubArray(m_Changes.AddedSceneReferenceEntities, GetRange(ChangeType.AddedSceneReferences));
            public NativeArray<Entity> RemovedSceneReferencesEntities=> SubArray(m_Changes.RemovedSceneReferenceEntities, GetRange(ChangeType.RemovedSceneReferences));
            
            public NativeArray<Entity> AddedSceneReferencesWithoutSceneTag => SubArray(m_Changes.AddedSceneReferenceWithoutSceneTagEntities, GetRange(ChangeType.AddedSceneReferencesWithoutSceneTags));
            public NativeArray<Entity> RemovedSceneReferencesWithoutSceneTag=> SubArray(m_Changes.RemovedSceneReferenceWithoutSceneTagEntities, GetRange(ChangeType.RemovedSceneReferencesWithoutSceneTags));
            
            public NativeArray<Entity> AddedParentEntities=> SubArray(m_Changes.AddedParentEntities, GetRange(ChangeType.AddedParents));
            public NativeArray<Entity> RemovedParentEntities=> SubArray(m_Changes.RemovedParentEntities, GetRange(ChangeType.RemovedParents));
            public NativeArray<Parent> AddedParentComponents=> SubArray(m_Changes.AddedParentComponents, GetRange(ChangeType.AddedParents));
            public NativeArray<SceneTag> AddedParentSceneTagForNullParentComponents => SubArray(m_Changes.AddedParentSceneTagForNullParentComponents, GetRange(ChangeType.AddedParents));
            
            public NativeArray<Entity> AddedSceneTagWithoutParentEntities=> SubArray(m_Changes.AddedSceneTagWithoutParentEntities, GetRange(ChangeType.AddedSceneTagWithoutParents));
            public NativeArray<Entity> RemovedSceneTagWithoutParentEntities=> SubArray(m_Changes.RemovedSceneTagWithoutParentEntities, GetRange(ChangeType.RemovedSceneTagsWithoutParents));
            public NativeArray<SceneTag> AddedSceneTagWithoutParentComponents => SubArray(m_Changes.AddedSceneTagWithoutParentComponents, GetRange(ChangeType.AddedSceneTagWithoutParents));
            
            public NativeArray<Entity> AddedSceneTagWithSceneReferenceEntities=> SubArray(m_Changes.AddedSceneTagWithSceneReferenceEntities, GetRange(ChangeType.AddedSceneTagWithSceneReferences));
            public NativeArray<Entity> RemovedSceneTagWithSceneReferenceEntities=> SubArray(m_Changes.RemovedSceneTagWithSceneReferenceEntities, GetRange(ChangeType.RemovedSceneTagsWithSceneReferences));
            public NativeArray<SceneTag> AddedSceneTagWithSceneReferenceComponents=> SubArray(m_Changes.AddedSceneTagWithSceneReferenceComponents, GetRange(ChangeType.AddedSceneTagWithSceneReferences));

            public HierarchyEntityChangesBatch(HierarchyEntityChanges changes, int batchStart, int batchCount)
            {
                m_Changes = changes;
                m_BatchStart = batchStart;
                m_BatchCount = batchCount;
                
                m_Length[(int) ChangeType.RemovedSceneReferencesWithoutSceneTags] = m_Changes.RemovedSceneReferenceWithoutSceneTagEntities.Length;
                m_Length[(int) ChangeType.RemovedSceneTagsWithSceneReferences] = m_Changes.RemovedSceneTagWithSceneReferenceEntities.Length;
                m_Length[(int) ChangeType.RemovedSceneReferences] = m_Changes.RemovedSceneReferenceEntities.Length;
                m_Length[(int) ChangeType.RemovedSceneTagsWithoutParents] = m_Changes.RemovedSceneTagWithoutParentEntities.Length;
                m_Length[(int) ChangeType.RemovedParents] = m_Changes.RemovedParentEntities.Length;
                m_Length[(int) ChangeType.DestroyedEntities] = m_Changes.DestroyedEntities.Length;
                m_Length[(int) ChangeType.CreatedEntities] = m_Changes.CreatedEntities.Length;
                m_Length[(int) ChangeType.AddedSceneReferences] = m_Changes.AddedSceneReferenceEntities.Length;
                m_Length[(int) ChangeType.AddedSceneReferencesWithoutSceneTags] = m_Changes.AddedSceneReferenceWithoutSceneTagEntities.Length;
                m_Length[(int) ChangeType.AddedSceneTagWithSceneReferences] = m_Changes.AddedSceneTagWithSceneReferenceEntities.Length;
                m_Length[(int) ChangeType.AddedSceneTagWithoutParents] = m_Changes.AddedSceneTagWithoutParentEntities.Length;
                m_Length[(int) ChangeType.AddedParents] = m_Changes.AddedParentEntities.Length;

                var offset = 0;
                for (var i = 0; i < (int) ChangeType.Count; i++)
                {
                    m_Offset[i] = offset;
                    offset += m_Length[i];
                }
            }

            Range GetRange(ChangeType type) 
            {
                var offset = m_Offset[(int) type];
                var length = m_Length[(int) type];
                var start = math.clamp(m_BatchStart - offset, 0, length);
                var count = math.clamp(m_BatchStart - offset + m_BatchCount - start, 0, length - start);
                return new Range(start, count);
            }

            static NativeArray<T> SubArray<T>(NativeList<T> list, Range range) where T : unmanaged
            {
                if (range.Count == 0) return default;
                return list.AsArray().GetSubArray(range.Start, range.Count);
            }
        }

        public struct IntegrateEntityChangesEnumerator : IEnumerator
        {
            enum Step
            {
                UpdateMapping,
                IntegrateChanges,
                UpdateVersion,
                Complete
            }
            
            readonly HierarchyNodeStore m_Hierarchy;
            readonly World m_World;
            readonly HierarchyEntityChanges m_Changes;

            Step m_Step;
            
            int m_TotalCount;
            int m_BatchStart;
            int m_BatchCount;
            
            public object Current => null;

            public void Reset() => throw new InvalidOperationException($"{nameof(IntegrateEntityChangesEnumerator)} can not be reset. Instead a new instance should be used with a new change set.");

            public float Progress => m_TotalCount > 0 ? m_BatchStart / (float) m_TotalCount : 0;

            public IntegrateEntityChangesEnumerator(HierarchyNodeStore hierarchy, World world, HierarchyEntityChanges changes, int batchSize)
            {
                m_Hierarchy = hierarchy;
                m_World = world;
                m_Changes = changes;
                m_TotalCount = changes.GetChangeCount();
                m_BatchStart = 0;
                m_BatchCount = batchSize > 0 ? batchSize : m_TotalCount;
                m_Step = Step.UpdateMapping;
                
                m_Hierarchy.m_Nodes.ResizeEntityCapacity(world.EntityManager.EntityCapacity);
            }

            public bool MoveNext()
            {
                switch (m_Step)
                {
                    case Step.UpdateMapping:
                    {
                        UpdateMapping();
                        m_Step = Step.IntegrateChanges;
                        return true;
                    }
                    
                    case Step.IntegrateChanges:
                    {
                        unsafe
                        {
                            new IntegrateEntityChangesJob
                            {
                                EntityDataAccess = m_World.EntityManager.GetCheckedEntityDataAccess(),
                                Batch = new HierarchyEntityChangesBatch(m_Changes, m_BatchStart, m_BatchCount),
                                Hierarchy = m_Hierarchy
                            }.Run();
                        }

                        m_BatchStart += m_BatchCount;

                        if (m_BatchStart >= m_TotalCount)
                            m_Step = Step.UpdateVersion;

                        return true;
                    }

                    case Step.UpdateVersion:
                    {
                        m_Hierarchy.UpdateChangeVersion(HierarchyNodeHandle.Root);
                        m_Step = Step.Complete;
                        return false;
                    }
                    case Step.Complete:
                    {
                        return false;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            
            void UpdateMapping()
            {
                for (var i = 0; i < m_Changes.AddedSceneReferenceWithoutSceneTagEntities.Length; i++)
                {
                    // Try to resolve which Unity Scene this sub-scene belongs to. This is used during integration to place the node under the correct parent.
                    if (m_World.EntityManager.HasComponent<Scenes.SubScene>(m_Changes.AddedSceneReferenceWithoutSceneTagEntities[i]))
                    {
                        var subSceneComponent = m_World.EntityManager.GetComponentObject<Scenes.SubScene>(m_Changes.AddedSceneReferenceWithoutSceneTagEntities[i]);

                        if (subSceneComponent.SceneAsset != null && !subSceneComponent.gameObject.scene.isSubScene)
                            m_Hierarchy.m_SceneReferenceEntityToScene.Add(m_Changes.AddedSceneReferenceWithoutSceneTagEntities[i], subSceneComponent.gameObject.scene);
                    }
                }

                for (var i = 0; i < m_Changes.RemovedSceneReferenceWithoutSceneTagEntities.Length; i++)
                {
                    m_Hierarchy.m_SceneReferenceEntityToScene.Remove(m_Changes.RemovedSceneReferenceWithoutSceneTagEntities[i]);
                }
            }
        }
        
        /// <summary>
        /// Integrates the given <see cref="HierarchyEntityChanges"/> set in to this hierarchy.
        /// </summary>
        /// <param name="world">The world being operated on.</param>
        /// <param name="changes">The entity changes to apply.</param>
        /// <returns>The scheduled job handle.</returns>
        public void IntegrateEntityChanges(World world, HierarchyEntityChanges changes)
        {
            var enumerator = CreateIntegrateEntityChangesEnumerator(world, changes, changes.GetChangeCount());
            while (enumerator.MoveNext()) { }
        }

        /// <summary>
        /// Creates an enumerator which will integrate the given entity changes over several ticks.
        /// </summary>
        /// <param name="world">The world being operated on.</param>
        /// <param name="changes">The entity changes to apply.</param>
        /// <param name="batchSize">The number of changes to integrate per tick.</param>
        /// <returns>An enumerator which can be ticked.</returns>
        public IntegrateEntityChangesEnumerator CreateIntegrateEntityChangesEnumerator(World world, HierarchyEntityChanges changes, int batchSize)
        {
            return new IntegrateEntityChangesEnumerator(this, world, changes, batchSize);
        }

        [BurstCompile]
        unsafe struct IntegrateEntityChangesJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public EntityDataAccess* EntityDataAccess;
            
            public HierarchyEntityChangesBatch Batch;
            public HierarchyNodeStore Hierarchy;

            public void Execute()
            {
                var removedSceneReferencesWithoutSceneTag = Batch.RemovedSceneReferencesWithoutSceneTag;
                var removedSceneReferencesEntities = Batch.RemovedSceneReferencesEntities;
                var removedSceneTagWithSceneReferenceEntities = Batch.RemovedSceneTagWithSceneReferenceEntities;
                var removedSceneTagWithoutParentEntities = Batch.RemovedSceneTagWithoutParentEntities;
                var removedParentEntities = Batch.RemovedParentEntities;
                var destroyedEntities = Batch.DestroyedEntities;
                var createdEntities = Batch.CreatedEntities;
                var addedSceneReferencesEntities = Batch.AddedSceneReferencesEntities;
                var addedSceneReferencesWithoutSceneTag = Batch.AddedSceneReferencesWithoutSceneTag;
                var addedSceneTagWithSceneReferenceEntities = Batch.AddedSceneTagWithSceneReferenceEntities;
                var addedSceneTagWithSceneReferenceComponents = Batch.AddedSceneTagWithSceneReferenceComponents;
                var addedSceneTagWithoutParentEntities = Batch.AddedSceneTagWithoutParentEntities;
                var addedSceneTagWithoutParentComponents = Batch.AddedSceneTagWithoutParentComponents;
                var addedParentEntities = Batch.AddedParentEntities;
                var addedParentComponents = Batch.AddedParentComponents;
                var addedParentSceneTagForNullParentComponents = Batch.AddedParentSceneTagForNullParentComponents;
                
                for (var i = 0; i < removedSceneReferencesWithoutSceneTag.Length; i++)
                    Hierarchy.SetParent(HierarchyNodeHandle.FromSubScene(removedSceneReferencesEntities[i]), HierarchyNodeHandle.Root);
                
                for (var i = 0; i < removedSceneTagWithSceneReferenceEntities.Length; i++)
                    Hierarchy.SetParent(HierarchyNodeHandle.FromSubScene(removedSceneTagWithSceneReferenceEntities[i]), HierarchyNodeHandle.Root);

                for (var i = 0; i < removedSceneReferencesEntities.Length; i++)
                    Hierarchy.RemoveNode(HierarchyNodeHandle.FromSubScene(removedSceneReferencesEntities[i]));

                for (var i = 0; i < removedSceneTagWithoutParentEntities.Length; i++)
                    Hierarchy.SetParent(HierarchyNodeHandle.FromEntity(removedSceneTagWithoutParentEntities[i]), HierarchyNodeHandle.Root);
                
                for (var i = 0; i < removedParentEntities.Length; i++)
                    Hierarchy.SetParent(HierarchyNodeHandle.FromEntity(removedParentEntities[i]), HierarchyNodeHandle.Root);

                for (var i = 0; i < destroyedEntities.Length; i++)
                    Hierarchy.RemoveNode(HierarchyNodeHandle.FromEntity(destroyedEntities[i]));

                for (var i = 0; i < createdEntities.Length; i++)
                    Hierarchy.m_Nodes.ValueByEntity.SetValueDefaultUnchecked(createdEntities[i]);

                for (var i = 0; i < addedSceneReferencesEntities.Length; i++)
                    Hierarchy.AddNode(HierarchyNodeHandle.FromSubScene(addedSceneReferencesEntities[i]));

                for (var i = 0; i < addedSceneReferencesWithoutSceneTag.Length; i++)
                {
                    if (Hierarchy.m_SceneReferenceEntityToScene.TryGetValue(addedSceneReferencesWithoutSceneTag[i], out var scene))
                        Hierarchy.SetParent(HierarchyNodeHandle.FromSubScene(addedSceneReferencesWithoutSceneTag[i]), HierarchyNodeHandle.FromScene(scene));
                }
                
                for (var i = 0; i < addedSceneTagWithSceneReferenceEntities.Length; i++)
                {
                    if (addedSceneTagWithSceneReferenceComponents[i].SceneEntity == Entity.Null)
                    {
                        // Something unexpected with the ECS data model. Just drop this node at the root.
                        Hierarchy.SetParent(HierarchyNodeHandle.FromSubScene(addedSceneTagWithSceneReferenceEntities[i]), HierarchyNodeHandle.Root);
                        continue;
                    }
                    
                    var subSceneNode = HierarchyNodeHandle.FromSubScene(EntityDataAccess, addedSceneTagWithSceneReferenceComponents[i]);
                    Hierarchy.SetParent(HierarchyNodeHandle.FromSubScene(addedSceneTagWithSceneReferenceEntities[i]), subSceneNode);
                }
                
                for (var i = 0; i < addedSceneTagWithoutParentEntities.Length; i++)
                {
                    if (addedSceneTagWithoutParentComponents[i].SceneEntity == Entity.Null)
                    {
                        // Something unexpected with the ECS data model. Just drop this node at the root.
                        Hierarchy.SetParent(HierarchyNodeHandle.FromEntity(addedSceneTagWithoutParentEntities[i]), HierarchyNodeHandle.Root);
                        continue;
                    }
                    
                    var subSceneNode = HierarchyNodeHandle.FromSubScene(EntityDataAccess, addedSceneTagWithoutParentComponents[i]);
                    Hierarchy.SetParent(HierarchyNodeHandle.FromEntity(addedSceneTagWithoutParentEntities[i]), Hierarchy.Exists(subSceneNode) ? subSceneNode : HierarchyNodeHandle.Root);
                }
                
                for (var i = 0; i < addedParentEntities.Length; i++)
                {
                    if (addedParentComponents[i].Value == Entity.Null)
                    {
                        // The 'Parent.Value' is null. Try to place this entity under it's sub-scene instead.
                        if (addedParentSceneTagForNullParentComponents[i].SceneEntity == Entity.Null)
                        {
                            // The sub-scene is also missing. Drop this node at the root.
                            Hierarchy.SetParent(HierarchyNodeHandle.FromEntity(addedParentEntities[i]), HierarchyNodeHandle.Root);
                            continue;
                        }
                        
                        var subSceneNode = HierarchyNodeHandle.FromSubScene(EntityDataAccess, addedParentSceneTagForNullParentComponents[i]);
                        Hierarchy.SetParent(HierarchyNodeHandle.FromEntity(addedParentEntities[i]), subSceneNode);
                        continue;
                    }
                    
                    Hierarchy.SetParent(HierarchyNodeHandle.FromEntity(addedParentEntities[i]), HierarchyNodeHandle.FromEntity(addedParentComponents[i].Value));
                }
            }
        }
    }
}