using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.PerformanceTesting;
using Assert = Unity.Assertions.Assert;

namespace Unity.Entities.PerformanceTests
{
    [Category("Performance")]
    public class CopyFromPerformanceTests : EntityDifferTestFixture
    {
        [Test, Performance]
        public void CopyEntitiesToOtherWorld([Values] bool collectDstEntities)
        {
            var srcEntities = CollectionHelper.CreateNativeArray<Entity>(5000, SrcWorld.UpdateAllocator.ToAllocator);
            var dstEntities = CollectionHelper.CreateNativeArray<Entity>(srcEntities.Length, DstWorld.UpdateAllocator.ToAllocator);

            var archetype = SrcEntityManager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>());

            for (int ei = 0, ec = srcEntities.Length; ei < ec; ei++)
            {
                srcEntities[ei] = SrcEntityManager.CreateEntity(archetype);
                SrcEntityManager.CreateEntity(archetype, 99);
            }

            Measure.Method(() =>
                {
                    if (collectDstEntities)
                    {
                        DstEntityManager.CopyEntitiesFrom(SrcEntityManager, srcEntities, dstEntities);
                    }
                    else
                    {
                        DstEntityManager.CopyEntitiesFrom(SrcEntityManager, srcEntities);
                    }
                })
                .CleanUp(() =>
                {
                    Assert.AreEqual(srcEntities.Length * 100, SrcEntityManager.Debug.EntityCount);
                    Assert.AreEqual(srcEntities.Length, DstEntityManager.Debug.EntityCount);
                    DstEntityManager.DestroyEntity(DstEntityManager.UniversalQuery);
                })
                .MeasurementCount(10)
                .IterationsPerMeasurement(1)
                .WarmupCount(2)
                .Run();
        }
    }
}
