using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [TestFixture]
    public class TransformUpdateTests : ECSTestsFixture
    {
        public interface IComponentMatrixData
        {
            float4x4 Value { get; set; }
        }

        struct ParentTransform : IComponentData
        {
            public Entity Value;
        }

        struct LocalToWorldMatrix : IComponentData, IComponentMatrixData
        {
            public float4x4 Value { get; set; }
        }

        struct Position : IComponentData
        {
            public float3 Value;
        }

        struct Heading : IComponentData
        {
            public float3 Value;
        }

        struct Rotation : IComponentData
        {
            public quaternion Value;
        }

        struct Scale : IComponentData
        {
            public float3 Value;
        }

        struct UniformScale : IComponentData
        {
            public float Value;
        }

        //
        // Managed by system:
        //

        // Change system does not provide previous value, so need to store.
        private struct PreviousParentTransform : ISystemStateComponentData
        {
            public Entity Value;
        }

        private struct TransformRoot : ISystemStateComponentData
        {
            public Entity Value;
        }

        private struct LocalToParentMatrix : IComponentMatrixData, ISystemStateComponentData
        {
            public float4x4 Value { get; set; }
        }

        [DisableAutoCreation]
        [ComponentSystemPatch]
        public class TransformPatch : JobComponentSystem
        {
            private uint LastSystemVersion = 0;
            private int LastPositionVersion = 0;
            private int LastRotationVersion = 0;
            private int LastHeadingVersion = 0;
            private int LastScaleVersion = 0;
            private int LastUniformScaleVersion = 0;
            private int LastParentTransformVersion = 0;

            private NativeQueue<Entity> RootChangedQueue;
            private NativeMultiHashMap<Entity, Entity> TransformChildHashMap;
            private ComponentDataFromEntity<LocalToWorldMatrix> LocalToWorldMatrices;
            private ComponentDataFromEntity<LocalToParentMatrix> LocalToParentMatrices;
            private EntityCommandBuffer PostUpdateCommands;

            public TransformPatch()
            {
                TransformChildHashMap = new NativeMultiHashMap<Entity, Entity>(1024, Allocator.Persistent);
                RootChangedQueue = new NativeQueue<Entity>(Allocator.Persistent);
            }

            public void UpdateAddedParentTransforms()
            {
                var chunks = m_EntityManager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    new ComponentType[] {typeof(PreviousParentTransform)}, // none
                    new ComponentType[] {typeof(ParentTransform)}, // all
                    Allocator.Temp);

                if (chunks.Length == 0)
                {
                    chunks.Dispose();
                    return;
                }

                var transformRoots = EntityManager.GetComponentDataFromEntity<TransformRoot>();
                var transformParentType = EntityManager.GetArchetypeChunkComponentType<ParentTransform>(true);
                var entityType = EntityManager.GetArchetypeChunkEntityType(true);

                Debug.Log(string.Format("New ParentTransform = {0}", chunks.EntityCount));

                for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var parentCount = chunk.Count;

                    var chunkParentTransforms = chunk.GetNativeArray(transformParentType);
                    var chunkEntities = chunk.GetNativeArray(entityType);

                    for (int i = 0; i < parentCount; i++)
                    {
                        var childEntity = chunkEntities[i];
                        var parentEntity = chunkParentTransforms[i].Value;
                        var rootEntity = parentEntity;

                        if (transformRoots.Exists(rootEntity))
                        {
                            rootEntity = transformRoots[rootEntity].Value;
                        }

                        RootChangedQueue.Enqueue(rootEntity);
                        PostUpdateCommands.AddComponent(childEntity, new PreviousParentTransform());
                        PostUpdateCommands.AddComponent(childEntity, new TransformRoot {Value = rootEntity});

                        // Separately call SetComponent so that change is tracked.
                        PostUpdateCommands.SetComponent(childEntity,
                            new PreviousParentTransform {Value = parentEntity});
                        TransformChildHashMap.Add(parentEntity, childEntity);
                    }
                }

                chunks.Dispose();
            }

            public void UpdateChangedParentTransforms()
            {
                var chunks = EntityManager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(ParentTransform), typeof(PreviousParentTransform)}, // all
                    Allocator.Temp);

                if (chunks.Length == 0)
                {
                    chunks.Dispose();
                    return;
                }

                var transformRoots = EntityManager.GetComponentDataFromEntity<TransformRoot>();
                var transformParentType = EntityManager.GetArchetypeChunkComponentType<ParentTransform>(true);
                var transformPreviousParentType =
                    EntityManager.GetArchetypeChunkComponentType<PreviousParentTransform>(true);
                var entityType = EntityManager.GetArchetypeChunkEntityType(true);

                for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];

                    if (chunk.GetComponentVersion(transformParentType) <= GlobalSystemVersion)
                        continue;

                    var childCount = chunk.Count;
                    var chunkParentTransforms = chunk.GetNativeArray(transformParentType);
                    var chunkPreviousParentTransforms = chunk.GetNativeArray(transformPreviousParentType);
                    var chunkEntities = chunk.GetNativeArray(entityType);

                    for (int i = 0; i < childCount; i++)
                    {
                        var childEntity = chunkEntities[i];
                        var parentEntity = chunkParentTransforms[i].Value;
                        var previousParentEntity = chunkPreviousParentTransforms[i].Value;

                        UpdateParent(transformRoots, parentEntity, previousParentEntity, childEntity);
                    }
                }

                chunks.Dispose();
            }

            private void RemoveParentChildMap(Entity parentEntity, Entity childEntity)
            {
                NativeMultiHashMapIterator<Entity> it;
                Entity foundChild;
                if (!TransformChildHashMap.TryGetFirstValue(parentEntity, out foundChild, out it))
                {
                    throw new System.InvalidOperationException(
                        string.Format("ParentTransform not found in Hierarchy hashmap"));
                }

                do
                {
                    if (foundChild == childEntity)
                    {
                        TransformChildHashMap.Remove(it);
                        return;
                    }
                } while (TransformChildHashMap.TryGetNextValue(out foundChild, ref it));

                throw new System.InvalidOperationException(
                    string.Format("ParentTransform not found in Hierarchy hashmap"));
            }

            private void UpdateParent(ComponentDataFromEntity<TransformRoot> transformRoots, Entity parentEntity,
                Entity previousParentEntity, Entity childEntity)
            {
                if (parentEntity == previousParentEntity)
                    return;

                RemoveParentChildMap(previousParentEntity, childEntity);

                var rootEntity = parentEntity;
                if (transformRoots.Exists(rootEntity))
                {
                    rootEntity = transformRoots[rootEntity].Value;
                }

                RootChangedQueue.Enqueue(rootEntity);
                PostUpdateCommands.SetComponent(childEntity, new TransformRoot {Value = rootEntity});
                PostUpdateCommands.SetComponent(childEntity, new PreviousParentTransform {Value = parentEntity});
                TransformChildHashMap.Add(parentEntity, childEntity);
            }

            public void UpdateChangedRootPRS()
            {
                var chunks = EntityManager.CreateArchetypeChunkArray(
                    new ComponentType[]
                        {typeof(Position), typeof(Rotation), typeof(Heading), typeof(Scale), typeof(UniformScale)},
                    new ComponentType[] {typeof(TransformRoot)}, // none
                    Array.Empty<ComponentType>(), // all
                    Allocator.Temp);

                if (chunks.Length == 0)
                {
                    chunks.Dispose();
                    return;
                }

                var entityType = EntityManager.GetArchetypeChunkEntityType(true);

                for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var updated = UpdateChunkLocal<LocalToWorldMatrix>(chunk);
                    if (updated)
                    {
                        var entities = chunk.GetNativeArray(entityType);
                        var count = chunk.Count;
                        for (int i = 0; i < count; i++)
                        {
                            RootChangedQueue.Enqueue(entities[i]);
                        }
                    }
                }

                chunks.Dispose();
            }

            public void UpdateChangedChildPRS()
            {
                var chunks = EntityManager.CreateArchetypeChunkArray(
                    new ComponentType[]
                        {typeof(Position), typeof(Rotation), typeof(Heading), typeof(Scale), typeof(UniformScale)},
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(TransformRoot), typeof(LocalToParentMatrix)}, // all
                    Allocator.Temp);

                if (chunks.Length == 0)
                {
                    chunks.Dispose();
                    return;
                }

                var transformRootType = EntityManager.GetArchetypeChunkComponentType<TransformRoot>(true);

                for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var updated = UpdateChunkLocal<LocalToParentMatrix>(chunk);
                    if (updated)
                    {
                        var transformRoots = chunk.GetNativeArray(transformRootType);
                        var count = chunk.Count;
                        for (int i = 0; i < count; i++)
                        {
                            RootChangedQueue.Enqueue(transformRoots[i].Value);
                        }
                    }
                }

                chunks.Dispose();
            }

            private bool UpdateChunkLocal<T>(ArchetypeChunk chunk)
                where T : struct, IComponentMatrixData, IComponentData
            {
                var positionType = EntityManager.GetArchetypeChunkComponentType<Position>(true);
                var rotationType = EntityManager.GetArchetypeChunkComponentType<Rotation>(true);
                var headingType = EntityManager.GetArchetypeChunkComponentType<Heading>(true);
                var scaleType = EntityManager.GetArchetypeChunkComponentType<Scale>(true);
                var uniformScaleType = EntityManager.GetArchetypeChunkComponentType<UniformScale>(true);
                var localToParentType = EntityManager.GetArchetypeChunkComponentType<T>(false);

                var positionChanged =
                    ChangeVersionUtility.DidChange(chunk.GetComponentVersion(positionType), LastSystemVersion);
                var rotationChanged =
                    ChangeVersionUtility.DidChange(chunk.GetComponentVersion(rotationType), LastSystemVersion);
                var headingChanged =
                    ChangeVersionUtility.DidChange(chunk.GetComponentVersion(headingType), LastSystemVersion);
                var scaleChanged =
                    ChangeVersionUtility.DidChange(chunk.GetComponentVersion(scaleType), LastSystemVersion);
                var uniformScaleChanged =
                    ChangeVersionUtility.DidChange(chunk.GetComponentVersion(uniformScaleType), LastSystemVersion);
                var changed = positionChanged || rotationChanged || headingChanged || scaleChanged ||
                              uniformScaleChanged;

                if (!changed)
                    return false;

                var positions = chunk.GetNativeArray(positionType);
                var rotations = chunk.GetNativeArray(rotationType);
                var headings = chunk.GetNativeArray(headingType);
                var scales = chunk.GetNativeArray(scaleType);
                var uniformScales = chunk.GetNativeArray(uniformScaleType);
                var localToParents = chunk.GetNativeArray(localToParentType);

                var existPosition = positions.Length > 0;
                var existRotation = rotations.Length > 0;
                var existHeading = headings.Length > 0;
                var existUniformScale = uniformScales.Length > 0;
                var existScale = scales.Length > 0;

                var count = chunk.Count;

                if (existPosition && (!existRotation) && (!existHeading) && (!existUniformScale) && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.translate(positions[i].Value);
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if ((!existPosition) && existRotation && (!existHeading) && (!existUniformScale) && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.rottrans(rotations[i].Value, new float3());
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if (existPosition && existRotation && (!existHeading) && (!existUniformScale) && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.rottrans(rotations[i].Value, positions[i].Value);
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if ((!existPosition) && (!existRotation) && existHeading && (!existUniformScale) && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.lookRotationToMatrix(new float3(), headings[i].Value, math.up());
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if (existPosition && (!existRotation) && existHeading && (!existUniformScale) && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent =
                            math.lookRotationToMatrix(positions[i].Value, headings[i].Value, math.up());
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if ((!existPosition) && (!existRotation) && (!existHeading) && existUniformScale && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.scale(uniformScales[i].Value);
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if (existPosition && (!existRotation) && (!existHeading) && existUniformScale && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.mul(math.translate(positions[i].Value),
                            math.scale(uniformScales[i].Value));
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if ((!existPosition) && existRotation && (!existHeading) && existUniformScale && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.mul(math.rottrans(rotations[i].Value, new float3()),
                            math.scale(uniformScales[i].Value));
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if (existPosition && existRotation && (!existHeading) && existUniformScale && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.mul(math.rottrans(rotations[i].Value, positions[i].Value),
                            math.scale(uniformScales[i].Value));
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if ((!existPosition) && (!existRotation) && existHeading && existUniformScale && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent =
                            math.mul(math.lookRotationToMatrix(new float3(), headings[i].Value, math.up()),
                                math.scale(uniformScales[i].Value));
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if (existPosition && (!existRotation) && existHeading && existUniformScale && (!existScale))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent =
                            math.mul(math.lookRotationToMatrix(positions[i].Value, headings[i].Value, math.up()),
                                math.scale(uniformScales[i].Value));
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if ((!existPosition) && (!existRotation) && (!existHeading) && (!existUniformScale) && existScale)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.scale(scales[i].Value);
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if (existPosition && (!existRotation) && (!existHeading) && (!existUniformScale) && existScale)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent =
                            math.mul(math.translate(positions[i].Value), math.scale(scales[i].Value));
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if ((!existPosition) && existRotation && (!existHeading) && (!existUniformScale) && existScale)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.mul(math.rottrans(rotations[i].Value, new float3()),
                            math.scale(scales[i].Value));
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if (existPosition && existRotation && (!existHeading) && (!existUniformScale) && existScale)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent = math.mul(math.rottrans(rotations[i].Value, positions[i].Value),
                            math.scale(scales[i].Value));
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if ((!existPosition) && (!existRotation) && existHeading && (!existUniformScale) && existScale)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent =
                            math.mul(math.lookRotationToMatrix(new float3(), headings[i].Value, math.up()),
                                math.scale(scales[i].Value));
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else if (existPosition && (!existRotation) && existHeading && (!existUniformScale) && existScale)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float4x4 localToParent =
                            math.mul(math.lookRotationToMatrix(positions[i].Value, headings[i].Value, math.up()),
                                math.scale(scales[i].Value));
                        localToParents[i] = new T {Value = localToParent};
                    }
                }

                else
                {
                    throw new System.InvalidOperationException("Invalid combination of transform components");
                }

                return true;
            }

            private void UpdateHierarchy(Entity entity, float4x4 parentToWorld)
            {
                var localToParent = LocalToParentMatrices[entity].Value;
                var localToWorld = math.mul(parentToWorld, localToParent);
                
                LocalToWorldMatrices[entity] = new LocalToWorldMatrix
                {
                    Value = localToWorld
                };

                NativeMultiHashMapIterator<Entity> it;
                Entity child;

                if (!TransformChildHashMap.TryGetFirstValue(entity, out child, out it))
                {
                    return;
                }

                do
                {
                    UpdateHierarchy(child, localToWorld);
                } while (TransformChildHashMap.TryGetNextValue(out child, ref it));
            }

            private void UpdateRoot(Entity rootEntity)
            {
                var localToWorld = LocalToWorldMatrices[rootEntity].Value;

                NativeMultiHashMapIterator<Entity> it;
                Entity child;

                if (!TransformChildHashMap.TryGetFirstValue(rootEntity, out child, out it))
                {
                    return;
                }

                do
                {
                    UpdateHierarchy(child, localToWorld);
                } while (TransformChildHashMap.TryGetNextValue(out child, ref it));
            }

            public void UpdateRoots()
            {
                var changedLength = RootChangedQueue.Count;
                if (changedLength == 0)
                    return;

                LocalToWorldMatrices = EntityManager.GetComponentDataFromEntity<LocalToWorldMatrix>(false);
                LocalToParentMatrices = EntityManager.GetComponentDataFromEntity<LocalToParentMatrix>(false);

                var rootDirty = new NativeHashMap<Entity, int>(changedLength, Allocator.Temp);
                var rootChanged = new NativeArray<Entity>(changedLength, Allocator.Temp);
                var rootDirtyCount = 0;

                // Remove duplicate roots
                for (int i = 0; i < changedLength; i++)
                {
                    var rootEntity = RootChangedQueue.Dequeue();
                    int dirtyIndex;
                    if (!rootDirty.TryGetValue(rootEntity, out dirtyIndex))
                    {
                        rootChanged[rootDirtyCount] = rootEntity;
                        rootDirty.TryAdd(rootEntity, rootDirtyCount);
                        rootDirtyCount++;
                    }
                }

                for (int i = 0; i < rootDirtyCount; i++)
                {
                    var rootEntity = rootChanged[i];
                    UpdateRoot(rootEntity);
                }

                rootChanged.Dispose();
                rootDirty.Dispose();
            }

            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                // #todo When delete ParentTransform
                // #todo When add new ParentTransform, recalc local space

                var positionVersion = EntityManager.GetComponentOrderVersion<Position>();
                var rotationVersion = EntityManager.GetComponentOrderVersion<Rotation>();
                var headingVersion = EntityManager.GetComponentOrderVersion<Heading>();
                var scaleVersion = EntityManager.GetComponentOrderVersion<Scale>();
                var uniformScaleVersion = EntityManager.GetComponentOrderVersion<UniformScale>();
                var parentTransformVersion = EntityManager.GetComponentOrderVersion<ParentTransform>();

                var positionChange = positionVersion != LastPositionVersion;
                var rotationChange = rotationVersion != LastRotationVersion;
                var headingChange = headingVersion != LastHeadingVersion;
                var scaleChange = scaleVersion != LastScaleVersion;
                var uniformScaleChange = uniformScaleVersion != LastUniformScaleVersion;
                var parentTransformChange = parentTransformVersion != LastParentTransformVersion;
                var possibleChange = positionChange || rotationChange || headingChange || scaleChange || uniformScaleChange || parentTransformChange;
                
                if (!possibleChange)
                    return inputDeps;

                LastPositionVersion = positionVersion;
                LastRotationVersion = rotationVersion;
                LastHeadingVersion = headingVersion;
                LastScaleVersion = scaleVersion;
                LastUniformScaleVersion = uniformScaleVersion;
                LastParentTransformVersion = parentTransformVersion;
                PostUpdateCommands = new EntityCommandBuffer(Allocator.Persistent);

                Debug.Log(string.Format("Transform Patch: {0}", GlobalSystemVersion));

                RootChangedQueue.Clear();

                // Stage-0
                //   - ParentTransform changed
                //   - Hash(parent->children)
                //   - Set TransformRoot
                //   - UpdateQueue(root) 
                UpdateAddedParentTransforms();
                UpdateChangedParentTransforms();

                // Stage-1
                // - root, PRS changed.
                UpdateChangedRootPRS();

                // Stage-2
                // - child, PRS changed.
                // - UpdateQueue(child->root)
                UpdateChangedChildPRS();

                // Stage-3
                // - UpdateRoot: LocalToWorldMatrix
                // - UpdateSubHierarchy: LocalToWorldMatrix
                UpdateRoots();

                // Complete
                PostUpdateCommands.Playback(EntityManager);
                PostUpdateCommands.Dispose();
                LastSystemVersion = GlobalSystemVersion;

                return inputDeps;
            }

            protected override void OnDestroyManager()
            {
                TransformChildHashMap.Dispose();
                RootChangedQueue.Dispose();
                PostUpdateCommands.Dispose();
            }
        }

        [DisableAutoCreation]
        public class TestTransformSetup : ComponentSystem
        {
            public NativeArray<Entity> AllEntities;
            public int UpdateStep;

            protected override void OnDestroyManager()
            {
                AllEntities.Dispose();
            }

            protected override void OnUpdate()
            {
                Debug.Log(string.Format("Update {0}", UpdateStep));
                switch (UpdateStep)
                {
                    case 0:
                        Update0();
                        break;
                    case 1:
                        Update1();
                        break;
                    case 2:
                        Update2();
                        break;
                }
            }

            public void UpdateCase(int step)
            {
                UpdateStep = step;
                Update();
            }

            void Update0()
            {
                int count = 4;

                AllEntities = new NativeArray<Entity>(count, Allocator.Persistent);

                AllEntities[0] = EntityManager.CreateEntity(
                    typeof(LocalToWorldMatrix),
                    typeof(Position),
                    typeof(Heading));
                var parentEntity = AllEntities[0];

                for (int i = 1; i < count; i++)
                {
                    AllEntities[i] = EntityManager.CreateEntity(
                        typeof(ParentTransform),
                        typeof(LocalToWorldMatrix),
                        typeof(LocalToParentMatrix),
                        typeof(Position),
                        typeof(Heading));
                    EntityManager.SetComponentData(AllEntities[i], new ParentTransform {Value = parentEntity});
                    parentEntity = AllEntities[i];
                }
            }

            void Update1()
            {
                var count = AllEntities.Length;

                for (int i = 1; i < count; i++)
                {
                    EntityManager.SetComponentData(AllEntities[i], new ParentTransform {Value = AllEntities[0]});
                }
            }

            void Update2()
            {
                var count = AllEntities.Length;

                for (int i = 1; i < count; i++)
                {
                    EntityManager.RemoveComponents(AllEntities[i]);
                    /*
                    float theta = 2 * Mathf.PI * ((float) i / count);
                    float x = math.cos(theta);
                    float z = math.sin(theta);
                    EntityManager.SetComponentData(AllEntities[i], new Heading {Value = new float3(x, 0.0f, z)});
                    */
                }
            }
        }

        // Capture reparenting changes to DAG
        [Test]
        public void TRA_CatchChangesToParentTransform()
        {
            var testTransformSetup = World.CreateManager<TestTransformSetup>();
            var transformPatch = World.CreateManager<TransformPatch>();

            testTransformSetup.UpdateCase(0);
            transformPatch.Update();
            testTransformSetup.UpdateCase(1);
            transformPatch.Update();

            var rootEntity = testTransformSetup.AllEntities[0];
            var entityCount = testTransformSetup.AllEntities.Length;
            {

                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(ParentTransform)}, // all
                    Allocator.Temp);
                var transformParentType = m_Manager.GetArchetypeChunkComponentType<ParentTransform>(true);

                var rootChildCount = 0;
                for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var parentCount = chunk.Count;

                    var chunkParentTransforms = chunk.GetNativeArray(transformParentType);
                    for (int i = 0; i < parentCount; i++)
                    {
                        if (chunkParentTransforms[i].Value == rootEntity)
                        {
                            rootChildCount++;
                        }
                    }
                }

                chunks.Dispose();
                Assert.AreEqual(entityCount - 1, rootChildCount);
            }

            testTransformSetup.UpdateCase(2);
            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    new ComponentType[] {typeof(ParentTransform)}, // none
                    new ComponentType[] {typeof(PreviousParentTransform)}, // all
                    Allocator.Temp);
                
                Assert.AreEqual(entityCount-1,chunks.EntityCount);

                chunks.Dispose();
            }

        }
    }
}
