using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities.Tests
{
    [TestFixture]
    class ArchetypeChunkArrayTest : ECSTestsFixture
    {
        public Entity CreateEntity(int value, int sharedValue)
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestSharedComp), typeof(EcsIntElement));
            m_Manager.SetComponentData(entity, new EcsTestData(value));
            m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(sharedValue));
            var buffer = m_Manager.GetBuffer<EcsIntElement>(entity);
            buffer.ResizeUninitialized(value);
            for (int i = 0; i < value; ++i)
            {
                buffer[i] = i;
            }
            return entity;
        }

        public Entity CreateEntity2(int value, int sharedValue)
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData2), typeof(EcsTestSharedComp));
            m_Manager.SetComponentData(entity, new EcsTestData2(value));
            m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(sharedValue));
            return entity;
        }

        void CreateEntities(int count)
        {
            for (int i = 0; i != count; i++)
                CreateEntity(i, i % 7);
        }

        void CreateEntities2(int count)
        {
            for (int i = 0; i != count; i++)
                CreateEntity2(i, i % 7);
        }

        void CreateMixedEntities(int count)
        {
            for (int i = 0; i != count; i++)
            {
                if ((i & 1) == 0)
                    CreateEntity(i, i % 7);
                else
                    CreateEntity2(-i, i % 7);
            }
        }

        struct ChangeMixedValues : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            public ComponentTypeHandle<EcsTestData> ecsTestData;
            public ComponentTypeHandle<EcsTestData2> ecsTestData2;

            public void Execute(int chunkIndex)
            {
                var chunk = chunks[chunkIndex];
                var chunkCount = chunk.Count;
                var chunkEcsTestData = chunk.GetNativeArray(ecsTestData);
                var chunkEcsTestData2 = chunk.GetNativeArray(ecsTestData2);

                if (chunkEcsTestData.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        chunkEcsTestData[i] = new EcsTestData(chunkEcsTestData[i].value + 100);
                    }
                }
                else if (chunkEcsTestData2.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        chunkEcsTestData2[i] = new EcsTestData2(chunkEcsTestData2[i].value0 - 1000);
                    }
                }
            }
        }

        [Test]
        public void ACS_WriteMixed()
        {
            CreateMixedEntities(64);

            var query = new EntityQueryDesc
            {
                Any = new ComponentType[] {typeof(EcsTestData2), typeof(EcsTestData)}, // any
                None = Array.Empty<ComponentType>(), // none
                All = Array.Empty<ComponentType>(), // all
            };
            var group = m_Manager.CreateEntityQuery(query);
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            Assert.AreEqual(14, chunks.Length);

            var ecsTestData = m_Manager.GetComponentTypeHandle<EcsTestData>(false);
            var ecsTestData2 = m_Manager.GetComponentTypeHandle<EcsTestData2>(false);
            var changeValuesJobs = new ChangeMixedValues
            {
                chunks = chunks,
                ecsTestData = ecsTestData,
                ecsTestData2 = ecsTestData2,
            };

            var collectValuesJobHandle = changeValuesJobs.Schedule(chunks.Length, 64);
            collectValuesJobHandle.Complete();

            ulong foundValues = 0;
            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var chunkCount = chunk.Count;

                Assert.AreEqual(4, math.ceilpow2(chunkCount - 1));

                var chunkEcsTestData = chunk.GetNativeArray(ecsTestData);
                var chunkEcsTestData2 = chunk.GetNativeArray(ecsTestData2);
                if (chunkEcsTestData.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        foundValues |= (ulong)1 << (chunkEcsTestData[i].value - 100);
                    }
                }
                else if (chunkEcsTestData2.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        foundValues |= (ulong)1 << (-chunkEcsTestData2[i].value0 - 1000);
                    }
                }
            }

            foundValues++;
            Assert.AreEqual(0, foundValues);

            chunks.Dispose();
        }

        struct ChangeMixedValuesSharedFilter : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            public ComponentTypeHandle<EcsTestData> ecsTestData;
            public ComponentTypeHandle<EcsTestData2> ecsTestData2;
            [ReadOnly] public SharedComponentTypeHandle<EcsTestSharedComp> ecsTestSharedData;
            public int sharedFilterIndex;

            public void Execute(int chunkIndex)
            {
                var chunk = chunks[chunkIndex];
                var chunkCount = chunk.Count;
                var chunkEcsTestData = chunk.GetNativeArray(ecsTestData);
                var chunkEcsTestData2 = chunk.GetNativeArray(ecsTestData2);
                var chunkEcsSharedDataIndex = chunk.GetSharedComponentIndex(ecsTestSharedData);

                if (chunkEcsSharedDataIndex != sharedFilterIndex)
                    return;

                if (chunkEcsTestData.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        chunkEcsTestData[i] = new EcsTestData(chunkEcsTestData[i].value + 100);
                    }
                }
                else if (chunkEcsTestData2.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        chunkEcsTestData2[i] = new EcsTestData2(chunkEcsTestData2[i].value0 - 1000);
                    }
                }
            }
        }

        [Test]
        public void ACS_WriteMixedFilterShared()
        {
            CreateMixedEntities(64);

            Assert.AreEqual(1, m_Manager.GlobalSystemVersion);

            // Only update shared value == 1
            var unique = new List<EcsTestSharedComp>(0);
            m_Manager.GetAllUniqueSharedComponentData(unique);
            var sharedFilterValue = 1;
            var sharedFilterIndex = -1;
            for (int i = 0; i < unique.Count; i++)
            {
                if (unique[i].value == sharedFilterValue)
                {
                    sharedFilterIndex = i;
                    break;
                }
            }

            var group = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                Any = new ComponentType[] {typeof(EcsTestData2), typeof(EcsTestData)}, // any
                None = Array.Empty<ComponentType>(), // none
                All = new ComponentType[] {typeof(EcsTestSharedComp)}, // all
            });
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            Assert.AreEqual(14, chunks.Length);

            var ecsTestData = m_Manager.GetComponentTypeHandle<EcsTestData>(false);
            var ecsTestData2 = m_Manager.GetComponentTypeHandle<EcsTestData2>(false);
            var ecsTestSharedData = m_Manager.GetSharedComponentTypeHandle<EcsTestSharedComp>();
            var changeValuesJobs = new ChangeMixedValuesSharedFilter
            {
                chunks = chunks,
                ecsTestData = ecsTestData,
                ecsTestData2 = ecsTestData2,
                ecsTestSharedData = ecsTestSharedData,
                sharedFilterIndex = sharedFilterIndex
            };

            var collectValuesJobHandle = changeValuesJobs.Schedule(chunks.Length, 64);
            collectValuesJobHandle.Complete();

            ulong foundValues = 0;
            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var chunkCount = chunk.Count;

                Assert.AreEqual(4, math.ceilpow2(chunkCount - 1));

                var chunkEcsSharedDataIndex = chunk.GetSharedComponentIndex(ecsTestSharedData);

                var chunkEcsTestData = chunk.GetNativeArray(ecsTestData);
                var chunkEcsTestData2 = chunk.GetNativeArray(ecsTestData2);
                if (chunkEcsTestData.Length > 0)
                {
                    var chunkEcsTestDataVersion = chunk.GetChangeVersion(ecsTestData);

                    Assert.AreEqual(1, chunkEcsTestDataVersion);

                    for (int i = 0; i < chunkCount; i++)
                    {
                        if (chunkEcsSharedDataIndex == sharedFilterIndex)
                        {
                            foundValues |= (ulong)1 << (chunkEcsTestData[i].value - 100);
                        }
                        else
                        {
                            foundValues |= (ulong)1 << (chunkEcsTestData[i].value);
                        }
                    }
                }
                else if (chunkEcsTestData2.Length > 0)
                {
                    var chunkEcsTestData2Version = chunk.GetChangeVersion(ecsTestData2);

                    Assert.AreEqual(1, chunkEcsTestData2Version);

                    for (int i = 0; i < chunkCount; i++)
                    {
                        if (chunkEcsSharedDataIndex == sharedFilterIndex)
                        {
                            foundValues |= (ulong)1 << (-chunkEcsTestData2[i].value0 - 1000);
                        }
                        else
                        {
                            foundValues |= (ulong)1 << (-chunkEcsTestData2[i].value0);
                        }
                    }
                }
            }

            foundValues++;
            Assert.AreEqual(0, foundValues);

            chunks.Dispose();
        }

        [Test]
        public void ACS_Buffers()
        {
            CreateEntities(128);

            var group = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsIntElement>());
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            var intElements = m_Manager.GetBufferTypeHandle<EcsIntElement>(false);

            for (int i = 0; i < chunks.Length; ++i)
            {
                var chunk = chunks[i];
                var accessor = chunk.GetBufferAccessor(intElements);

                for (int k = 0; k < accessor.Length; ++k)
                {
                    var buffer = accessor[i];

                    for (int n = 0; n < buffer.Length; ++n)
                    {
                        if (buffer[n] != n)
                            Assert.Fail("buffer element does not have the expected value");
                    }
                }
            }

            chunks.Dispose();
        }

        class BumpChunkBufferTypeVersionSystem : ComponentSystem
        {
            struct UpdateChunks : IJobParallelFor
            {
                public NativeArray<ArchetypeChunk> Chunks;
                public BufferTypeHandle<EcsIntElement> EcsIntElements;

                public void Execute(int chunkIndex)
                {
                    var chunk = Chunks[chunkIndex];
                    var ecsBufferAccessor = chunk.GetBufferAccessor(EcsIntElements);
                    for (int i = 0; i < ecsBufferAccessor.Length; ++i)
                    {
                        var buffer = ecsBufferAccessor[i];
                        if (buffer.Length > 0)
                        {
                            buffer[0] += 1;
                        }
                    }
                }
            }

            EntityQuery m_Group;

            protected override void OnCreate()
            {
                m_Group = GetEntityQuery(typeof(EcsIntElement));
            }

            protected override void OnUpdate()
            {
                var chunks = m_Group.CreateArchetypeChunkArray(Allocator.TempJob);
                var ecsIntElements = GetBufferTypeHandle<EcsIntElement>();
                var updateChunksJob = new UpdateChunks
                {
                    Chunks = chunks,
                    EcsIntElements = ecsIntElements
                };
                var updateChunksJobHandle = updateChunksJob.Schedule(chunks.Length, 32);
                updateChunksJobHandle.Complete();

                chunks.Dispose();
            }
        }

        [Test]
        public void ACS_BufferHas()
        {
            CreateEntities(128);

            var group = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsIntElement>());
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            var intElements = m_Manager.GetBufferTypeHandle<EcsIntElement>(false);
            var missingElements = m_Manager.GetBufferTypeHandle<EcsComplexEntityRefElement>(false);

            for (int i = 0; i < chunks.Length; ++i)
            {
                var chunk = chunks[i];

                // Test Has<T>()
                bool hasIntElements = chunk.Has(intElements);
                Assert.IsTrue(hasIntElements, "Has(EcsIntElement) should be true");
                bool hasMissingElements = chunk.Has(missingElements);
                Assert.IsFalse(hasMissingElements, "Has(EcsComplexEntityRefElement) should be false");
            }

            chunks.Dispose();
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void ACS_ManagedComponentHas()
        {
            CreateEntities(128);

            var group = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestManagedComponent>());
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            var strComponents = m_Manager.GetComponentTypeHandle<EcsTestManagedComponent>(false);
            var missingComponents = m_Manager.GetComponentTypeHandle<EcsTestData>(false);

            for (int i = 0; i < chunks.Length; ++i)
            {
                var chunk = chunks[i];

                // Test Has<T>()
                bool hasstrComponents = chunk.Has(strComponents);
                Assert.IsTrue(hasstrComponents, "Has(EcsTestManagedComponent) should be true");
                bool hasMissingElements = chunk.Has(missingComponents);
                Assert.IsFalse(hasMissingElements, "Has(EcsComplexEntityRefElement) should be false");
            }

            chunks.Dispose();
        }

#endif

        [Test]
        public void ACS_BufferVersions()
        {
            CreateEntities(128);

            var group = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsIntElement>());
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            var intElements = m_Manager.GetBufferTypeHandle<EcsIntElement>(false);
            uint[] chunkBufferVersions = new uint[chunks.Length];

            for (int i = 0; i < chunks.Length; ++i)
            {
                var chunk = chunks[i];

                // Test DidChange() before modifications
                chunkBufferVersions[i] = chunk.GetChangeVersion(intElements);
                bool beforeDidChange = chunk.DidChange(intElements, chunkBufferVersions[i]);
                Assert.IsFalse(beforeDidChange, "DidChange() is true before modifications");
                uint beforeVersion = chunk.GetChangeVersion(intElements);
                Assert.AreEqual(chunkBufferVersions[i], beforeVersion, "version mismatch before modifications");
            }

            // Run system to bump chunk versions
            var bumpChunkBufferTypeVersionSystem = World.CreateSystem<BumpChunkBufferTypeVersionSystem>();
            bumpChunkBufferTypeVersionSystem.Update();

            // Check versions after modifications
            for (int i = 0; i < chunks.Length; ++i)
            {
                var chunk = chunks[i];

                uint afterVersion = chunk.GetChangeVersion(intElements);
                Assert.AreNotEqual(chunkBufferVersions[i], afterVersion, "version match after modifications");
                bool afterDidAddChange = chunk.DidChange(intElements, chunkBufferVersions[i]);
                Assert.IsTrue(afterDidAddChange, "DidChange() is false after modifications");
            }

            chunks.Dispose();
        }

        [Test]
        public void ACS_BuffersRO()
        {
            CreateEntities(128);

            var group = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsIntElement>());
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();
            var intElements = m_Manager.GetBufferTypeHandle<EcsIntElement>(true);

            var chunk = chunks[0];
            var accessor = chunk.GetBufferAccessor(intElements);
            var buffer = accessor[0];

            Assert.Throws<InvalidOperationException>(() => buffer.Add(12));

            chunks.Dispose();
        }

        [Test]
        public void ACS_ChunkArchetypeTypesMatch()
        {
            var entityTypes = new ComponentType[] {typeof(EcsTestData), typeof(EcsTestSharedComp), typeof(EcsIntElement)};

            CreateEntities(128);

            var group = m_Manager.CreateEntityQuery(entityTypes);
            using (var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                foreach (var chunk in chunks)
                {
                    var archetype = chunk.Archetype;
                    var chunkTypes = archetype.GetComponentTypes().ToArray();
                    foreach (var type in entityTypes)
                    {
                        Assert.Contains(type, chunkTypes);
                    }

                    Assert.AreEqual(entityTypes.Length, entityTypes.Length);
                }
            }

            group.Dispose();
        }

        [MaximumChunkCapacity(3)]
        struct Max3Capacity : IComponentData {}

        [Test]
        public void MaximumChunkCapacityIsRespected()
        {
            for (int i = 0; i != 4; i++)
                m_Manager.CreateEntity(typeof(Max3Capacity));

            var group = m_Manager.CreateEntityQuery(typeof(Max3Capacity));
            using (var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                Assert.AreEqual(2, chunks.Length);
                Assert.AreEqual(3, chunks[0].Capacity);

                Assert.AreEqual(3, chunks[0].Count);
                Assert.AreEqual(1, chunks[1].Count);
            }

            group.Dispose();
        }

        [Test]
        public void ACS_DynamicComponentDataArrayReinterpret()
        {
            CreateMixedEntities(64);

            var query = new EntityQueryDesc
            {
                Any = new ComponentType[] { typeof(EcsTestData2), typeof(EcsTestData) }, // any
                None = Array.Empty<ComponentType>(), // none
                All = Array.Empty<ComponentType>(), // all
            };
            var group = m_Manager.CreateEntityQuery(query);
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            Assert.AreEqual(14, chunks.Length);

            var ecsTestData = m_Manager.GetComponentTypeHandle<EcsTestData>(false);
            var ecsTestData2 = m_Manager.GetComponentTypeHandle<EcsTestData2>(false);
            var changeValuesJobs = new ChangeMixedValues
            {
                chunks = chunks,
                ecsTestData = ecsTestData,
                ecsTestData2 = ecsTestData2,
            };

            var collectValuesJobHandle = changeValuesJobs.Schedule(chunks.Length, 64);
            collectValuesJobHandle.Complete();

            var ecsTestDataDynamic = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsTestData)));
            var ecsTestDataDynamic2 = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsTestData2)));

            ulong foundValues = 0;
            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var chunkCount = chunk.Count;

                Assert.AreEqual(4, math.ceilpow2(chunkCount - 1));

                var chunkEcsTestData = chunk.GetDynamicComponentDataArrayReinterpret<int>(ecsTestDataDynamic, UnsafeUtility.SizeOf<int>());
                var chunkEcsTestData2 = chunk.GetDynamicComponentDataArrayReinterpret<int2>(ecsTestDataDynamic2, UnsafeUtility.SizeOf<int2>());
                if (chunkEcsTestData.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        foundValues |= (ulong)1 << (chunkEcsTestData[i] - 100);
                    }
                }
                else if (chunkEcsTestData2.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        Assert.AreEqual(chunkEcsTestData2[i].x, chunkEcsTestData2[i].y);
                        foundValues |= (ulong)1 << (-chunkEcsTestData2[i].x - 1000);
                    }
                }
            }

            foundValues++;
            Assert.AreEqual(0, foundValues);

            chunks.Dispose();
        }


        class UntypedBufferSystemBumpVersion : ComponentSystem
        {
            struct UpdateChunks : IJobParallelFor
            {
                public NativeArray<ArchetypeChunk> Chunks;
                public DynamicComponentTypeHandle ElementHandle;

                public void Execute(int chunkIndex)
                {
                    var chunk = Chunks[chunkIndex];
                    var ecsBufferAccessor = chunk.GetUntypedBufferAccessor(ref ElementHandle);
                    for (int i = 0; i < ecsBufferAccessor.Length; ++i)
                    {
                        unsafe
                        {
                            var buffer = ecsBufferAccessor.GetUnsafeReadOnlyPtrAndLength(i, out var len);
                            for(int e=0;e<len;++e)
                            {
                                ((int*) buffer)[e] = 0xBADF00D;
                            }
                        }
                    }
                }
            }
            protected override void OnUpdate()
            {
                var query = GetEntityQuery(typeof(EcsIntElement));
                var chunks = query.CreateArchetypeChunkArray(Allocator.TempJob);
                var updateChunksJob = new UpdateChunks
                {
                    Chunks = chunks,
                    ElementHandle = GetDynamicComponentTypeHandle(ComponentType.ReadWrite(typeof(EcsIntElement)))
                };
                var updateChunksJobHandle = updateChunksJob.Schedule(chunks.Length, 32);
                updateChunksJobHandle.Complete();
                chunks.Dispose();
            }
        }

        [Test]
        public void ACS_UntypedBuffers()
        {
            CreateEntities(128);

            var group = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsIntElement>());
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            var roComponentType = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsIntElement)));
            for (int i = 0; i < chunks.Length; ++i)
            {
                var chunk = chunks[i];
                var accessor = chunk.GetUntypedBufferAccessor(ref roComponentType);
                Assert.IsTrue(accessor.Length != 0);
                Assert.IsTrue(accessor.ElementSize == UnsafeUtility.SizeOf<EcsIntElement>());
                for (int k = 0; k < accessor.Length; ++k)
                {
                    unsafe
                    {
                        var bufferPtr = (byte*) accessor.GetUnsafeReadOnlyPtrAndLength(k, out var length);
                        Assert.IsTrue(bufferPtr != null);
                        //Verify all accessors return the same values
                        Assert.IsTrue(bufferPtr == accessor.GetUnsafeReadOnlyPtr(k));
                        Assert.AreEqual(length, accessor.GetBufferLength(k));

                        //Check value contents by either case to int or EcsIntElement. Is also an example of proper api use
                        var bufferData = (int*) bufferPtr;
                        for (int n = 0; n < length; ++n)
                            Assert.AreEqual(n, bufferData[n], "buffer element does not have the expected value");

                        var elementSize = UnsafeUtility.SizeOf<EcsIntElement>();
                        for (int n = 0; n < length; ++n)
                        {
                            var ecsElement = UnsafeUtility.AsRef<EcsIntElement>((void*) bufferPtr);
                            Assert.AreEqual(n, ecsElement.Value, "buffer element does not have the expected value");
                            bufferPtr += elementSize;
                        }

                        //This should throw, since is a readonly handle
                        Assert.Throws<InvalidOperationException>(() =>
                        {
                            accessor.ResizeUninitialized(k, length + 1);
                        });
                    }
                }

                // Test resize
                var rwComponentType = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(typeof(EcsIntElement)));
                accessor = chunk.GetUntypedBufferAccessor(ref rwComponentType);
                for (int k = 0; k < accessor.Length; ++k)
                {
                    unsafe
                    {
                        //Increase the buffer size.
                        var oldLength = accessor.GetBufferLength(k);
                        var newLength = oldLength + 16;
                        accessor.ResizeUninitialized(k, newLength);
                        var bufferPtr = (byte*) accessor.GetUnsafePtrAndLength(k, out var length);

                        Assert.IsTrue(bufferPtr != null);
                        Assert.AreEqual(newLength, length);
                        Assert.GreaterOrEqual(accessor.GetBufferCapacity(k), newLength);
                        //Check content in range 0 - oldLen are the same
                        var bufferData = (int*) bufferPtr;
                        for (int n = 0; n < oldLength; ++n)
                            Assert.AreEqual(n, bufferData[n], "buffer element does not have the expected value");

                        //Shrink the buffer size. Check capacity is still the same
                        var oldCapacity = accessor.GetBufferCapacity(k);
                        accessor.ResizeUninitialized(k, 10);
                        Assert.IsTrue(accessor.GetUnsafePtr(k) != null);
                        Assert.AreEqual(10, accessor.GetBufferLength(k));
                        Assert.AreEqual(oldCapacity, accessor.GetBufferCapacity(k));
                        //Check content in range 0 - 10 are still the same
                        var toCheck = oldLength < 10 ? oldLength : 10;
                        bufferData = (int*) bufferPtr;
                        for (int n = 0; n < toCheck; ++n)
                            Assert.AreEqual(n, bufferData[n], "buffer element does not have the expected value");
                    }
                }
            }

            chunks.Dispose();
        }

        [Test]
        public void ACS_UntypedBuffersBumpVersion()
        {
            CreateEntities(128);

            var group = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsIntElement>());
            var chunks = group.CreateArchetypeChunkArray(Allocator.Temp);
            group.Dispose();

            var versions = new NativeArray<uint>(chunks.Length, Allocator.Temp);
            var roComponentType = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsIntElement)));
            for (int i = 0; i < chunks.Length; ++i)
                versions[i] = chunks[i].GetChangeVersion(roComponentType);

            var system = World.CreateSystem<UntypedBufferSystemBumpVersion>();
            system.Update();

            for (int i = 0; i < chunks.Length; ++i)
            {
                var newVersion = chunks[i].GetChangeVersion(roComponentType);
                Assert.AreNotEqual(versions[i], newVersion);
                bool afterDidAddChange = chunks[i].DidChange(roComponentType, versions[i]);
                Assert.IsTrue(afterDidAddChange, "DidChange() is false after modifications");
            }
        }

        struct UntypedBufferResizeJob : IJobEntityBatch
        {
            public DynamicComponentTypeHandle typeHandle;
            public int size;
            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var accessor = batchInChunk.GetUntypedBufferAccessor(ref typeHandle);
                for (int i = 0; i < batchInChunk.Count; ++i)
                {
                    //Resize and invalidate the secondary version.
                    accessor.ResizeUninitialized(i, size);
                }
            }
        }

        [Test,DotsRuntimeFixme]
        public void ACS_UntypedBuffersInvalidationWorks()
        {
            var entity = m_Manager.CreateEntity();
            var bufferRW = m_Manager.AddBuffer<EcsIntElement>(entity);
            bufferRW.Length = 1;

            var bufferFromEntity = m_Manager.GetBufferFromEntity<EcsIntElement>(true);
            var bufferRO = bufferFromEntity[entity];
            //grab also an alias
            var array = bufferRO.AsNativeArray();
            var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsIntElement>());
            var typeHandle = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(typeof(EcsIntElement)));
            //Resize buffer with a job (on the main thread)
            new UntypedBufferResizeJob {
                typeHandle = typeHandle,
                size = 5
            }.Run(query);
            //old buffer references are still valid here. So no throw or errors
            Assert.AreEqual(5, bufferRO.Length);
            Assert.AreEqual(5, bufferRW.Length);
            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < bufferRO.Length; ++i)  {
                    var o = bufferRO[i];
                }
            });
            //But arrays should be ivalidated
#if UNITY_2020_2_OR_NEWER
            Assert.Throws<ObjectDisposedException>(() =>
#else
            Assert.Throws<InvalidOperationException>(()=>
#endif
            {
                var el = array[0];
            });

            //Let's now run the job using schedule and check that is not possible to invalidate the buffer from the main
            //thread if they are assigned to to a job
            var jobHandle = new UntypedBufferResizeJob {
                typeHandle = typeHandle,
                size = 10
            }.Schedule(query);
            Assert.Throws<InvalidOperationException>(() => { bufferRW.Add(new EcsIntElement()); });
            Assert.Throws<InvalidOperationException>(() => { bufferRW.Length = 2; });
            jobHandle.Complete();
        }

        [Test]
        public void ACS_UntypeBufferAccessorIncorrectUse()
        {
            CreateEntities(128);

            var group = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsIntElement>());
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            var bufferType = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsIntElement)));
            var componentType = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsTestData)));

            for (int i = 0; i < chunks.Length; ++i)
            {
                var chunk = chunks[i];
                //Fail: cannot use GetDynamicComponentDataArrayReinterpret with IBufferElementData types
                Assert.Throws<ArgumentException>(() =>
                {
                    chunk.GetDynamicComponentDataArrayReinterpret<EcsIntElement>(bufferType, UnsafeUtility.SizeOf<EcsIntElement>());
                });

                //Fail: cannot use GetUntypedBufferAccessor with IComponentData types
                Assert.Throws<ArgumentException>(() =>
                {
                    chunk.GetUntypedBufferAccessor(ref componentType);
                });
            }

            chunks.Dispose();
        }

        [Test]
        public void ACS_DynamicTypeHas()
        {
            CreateEntities(128);

            var group = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsIntElement>());
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            var ecsTestData = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsTestData)));
            var missingElements = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsTestData3)));

            for (int i = 0; i < chunks.Length; ++i)
            {
                var chunk = chunks[i];

                // Test Has(DynamicComponentTypeHandle)
                bool hasEcsTestData = chunk.Has(ecsTestData);
                Assert.IsTrue(hasEcsTestData, "Has(EcsTestData) should be true");
                bool hasMissingElements = chunk.Has(missingElements);
                Assert.IsFalse(hasMissingElements, "Has(EcsTestData3) should be false");
            }

            chunks.Dispose();
        }

        [Test]
        public void ACS_DynamicComponentDataArrayReinterpretIncorrectUse()
        {
            CreateEntities2(128);

            var group = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData2>());
            var chunks = group.CreateArchetypeChunkArray(Allocator.TempJob);
            group.Dispose();

            var ecsTestData = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsTestData2)));

            for (int i = 0; i < chunks.Length; ++i)
            {
                var chunk = chunks[i];

                // Fail: not expected size
                Assert.Throws<InvalidOperationException>(() =>
                {
                    var chunkEcsTestData = chunk.GetDynamicComponentDataArrayReinterpret<int>(ecsTestData, UnsafeUtility.SizeOf<int>());
                });

                // If (Count * sizeof(int2)) % sizeof(int3) == 0 -> the test fail because the types can be aliased in that case.
                if ((chunk.Count * UnsafeUtility.SizeOf<int2>() % UnsafeUtility.SizeOf<int3>()) != 0)
                {
                    // Fail: not dividable by size
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        var chunkEcsTestData = chunk.GetDynamicComponentDataArrayReinterpret<int3>(ecsTestData, UnsafeUtility.SizeOf<int2>());
                    });
                }
                else
                {
                    Assert.DoesNotThrow(()=>
                    {
                        var chunkEcsTestData = chunk.GetDynamicComponentDataArrayReinterpret<int3>(ecsTestData, UnsafeUtility.SizeOf<int2>());
                    });
                }

            }

            chunks.Dispose();
        }

        // These tests require:
        // - JobsDebugger support for static safety IDs (added in 2020.1)
        // - Asserting throws
#if !UNITY_DOTSRUNTIME
        struct UseComponentTypeHandle : IJob
        {
            public Unity.Entities.ComponentTypeHandle<EcsTestData> ecsTestData;
            public void Execute()
            {
            }
        }

        [Test,DotsRuntimeFixme]
        public void ComponentTypeHandle_UseAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            var chunkComponentType = m_Manager.GetComponentTypeHandle<EcsTestData>(true);

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates ComponentTypeHandle

            var chunk = m_Manager.GetChunk(entity);
            Assert.That(() => { chunk.GetChunkComponentData(chunkComponentType); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<ObjectDisposedException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "ComponentTypeHandle<Unity.Entities.Tests.EcsTestData> which has been invalidated by a structural change."));
        }

        [Test,DotsRuntimeFixme]
        public void ComponentTypeHandle_UseFromJobAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            var chunkComponentType = m_Manager.GetComponentTypeHandle<EcsTestData>(false);

            var changeValuesJobs = new UseComponentTypeHandle
            {
                ecsTestData = chunkComponentType,
            };

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates ComponentTypeHandle

            Assert.That(() => { changeValuesJobs.Run(); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<InvalidOperationException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "ComponentTypeHandle<Unity.Entities.Tests.EcsTestData> UseComponentTypeHandle.ecsTestData which has been invalidated by a structural change."));
        }

        struct UseDynamicComponentTypeHandle : IJob
        {
            public DynamicComponentTypeHandle ecsTestData;
            public void Execute()
            {
            }
        }

        [Test,DotsRuntimeFixme]
        public void DynamicComponentTypeHandle_UseAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            var chunkComponentType = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsTestData)));

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates DynamicComponentTypeHandle

            var chunk = m_Manager.GetChunk(entity);
            Assert.That(() => { chunk.GetDynamicComponentDataArrayReinterpret<int>(chunkComponentType, UnsafeUtility.SizeOf<int>()); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<ObjectDisposedException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains("Unity.Entities.DynamicComponentTypeHandle which has been invalidated by a structural change"));
        }

        [Test,DotsRuntimeFixme]
        public void DynamicComponentTypeHandle_UseFromJobAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            var chunkComponentType = m_Manager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(typeof(EcsTestData)));

            var changeValuesJobs = new UseDynamicComponentTypeHandle
            {
                ecsTestData = chunkComponentType,
            };

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates DynamicComponentTypeHandle

            Assert.That(() => { changeValuesJobs.Run(); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<InvalidOperationException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains("Unity.Entities.DynamicComponentTypeHandle UseDynamicComponentTypeHandle.ecsTestData which has been invalidated by a structural change."));
        }

        struct UseBufferTypeHandle : IJob
        {
            public Unity.Entities.BufferTypeHandle<EcsIntElement> ecsTestData;
            public void Execute()
            {
            }
        }

        [Test,DotsRuntimeFixme]
        public void BufferTypeHandle_UseAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsIntElement));
            var ecsTestData = m_Manager.GetBufferTypeHandle<EcsIntElement>(false);

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates BufferTypeHandle

            var chunk = m_Manager.GetChunk(entity);
            Assert.That(() => { chunk.GetBufferAccessor(ecsTestData); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<ObjectDisposedException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "BufferTypeHandle<Unity.Entities.Tests.EcsIntElement> which has been invalidated by a structural change."));
        }

        [Test,DotsRuntimeFixme]
        public void BufferTypeHandle_UseFromJobAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsIntElement));
            var ecsTestData = m_Manager.GetBufferTypeHandle<EcsIntElement>(false);

            var changeValuesJobs = new UseBufferTypeHandle
            {
                ecsTestData = ecsTestData,
            };

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates BufferTypeHandle

            Assert.That(() => { changeValuesJobs.Run(); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<InvalidOperationException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "BufferTypeHandle<Unity.Entities.Tests.EcsIntElement> UseBufferTypeHandle.ecsTestData which has been invalidated by a structural change."));
        }

        struct UseSharedComponentTypeHandle : IJob
        {
            public Unity.Entities.SharedComponentTypeHandle<EcsTestSharedComp> ecsTestData;
            public void Execute()
            {
            }
        }

        [Test,DotsRuntimeFixme]
        public void SharedComponentTypeHandle_UseAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.AddSharedComponentData(entity, new EcsTestSharedComp(17));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            var ecsTestData = m_Manager.GetSharedComponentTypeHandle<EcsTestSharedComp>();

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates SharedComponentTypeHandle

            // No main-thread code currently references SharedComponentTypeHandle.m_Safety, so we have to manually verify that it's been invalidated
            Assert.That(() => { AtomicSafetyHandle.CheckReadAndThrow(ecsTestData.m_Safety); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<ObjectDisposedException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "SharedComponentTypeHandle<Unity.Entities.Tests.EcsTestSharedComp> which has been invalidated by a structural change."));
        }

        [Test,DotsRuntimeFixme]
        public void SharedComponentTypeHandle_UseFromJobAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.AddSharedComponentData(entity, new EcsTestSharedComp(17));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            var ecsTestData = m_Manager.GetSharedComponentTypeHandle<EcsTestSharedComp>();

            var changeValuesJobs = new UseSharedComponentTypeHandle
            {
                ecsTestData = ecsTestData,
            };

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates SharedComponentTypeHandle

            Assert.That(() => { changeValuesJobs.Run(); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<InvalidOperationException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "SharedComponentTypeHandle<Unity.Entities.Tests.EcsTestSharedComp> UseSharedComponentTypeHandle.ecsTestData which has been invalidated by a structural change."));
        }

        struct UseEntityTypeHandle : IJob
        {
            public EntityTypeHandle ecsTestData;
            public void Execute()
            {
            }
        }

        [Test,DotsRuntimeFixme]
        public void EntityTypeHandle_UseAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            var chunkEntityType = m_Manager.GetEntityTypeHandle();

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates EntityTypeHandle

            var chunk = m_Manager.GetChunk(entity);
            Assert.That(() => { chunk.GetNativeArray(chunkEntityType); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<ObjectDisposedException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "Unity.Entities.EntityTypeHandle which has been invalidated by a structural change."));
        }

        [Test,DotsRuntimeFixme]
        public void EntityTypeHandle_UseFromJobAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            var chunkEntityType = m_Manager.GetEntityTypeHandle();

            var changeValuesJobs = new UseEntityTypeHandle
            {
                ecsTestData = chunkEntityType,
            };

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates EntityTypeHandle

            Assert.That(() => { changeValuesJobs.Run(); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<InvalidOperationException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "Unity.Entities.EntityTypeHandle UseEntityTypeHandle.ecsTestData which has been invalidated by a structural change."));
        }

#endif
    }
}
