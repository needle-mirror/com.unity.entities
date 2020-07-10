using System;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;

#pragma warning disable 0618

namespace Unity.Entities.Tests.ForEachCodegen
{
    [TestFixture]
    public class ForEachWithDeallocateOnJobCompletion : ECSTestsFixture
    {
        TestSystemType TestSystem;

        [SetUp]
        public void SetUp()
        {
            TestSystem = World.GetOrCreateSystem<TestSystemType>();
        }

        struct CanContainDisposedStruct
        {
            public SupportsDeallocateOnJobCompletion SupportsDeallocateOnJobCompletion;
        }

        class CanContainDisposedClass
        {
            public SupportsDeallocateOnJobCompletion SupportsDeallocateOnJobCompletion = new SupportsDeallocateOnJobCompletion();
        }

        [NativeContainerSupportsDeallocateOnJobCompletion]
        struct SupportsDeallocateOnJobCompletion : IDisposable
        {
            AtomicSafetyHandle Handle;

            public void Dispose()
            {
                AtomicSafetyHandle.CheckDeallocateAndThrow(Handle);
                AtomicSafetyHandle.Release(Handle);
            }

            public void CheckCanRead() => AtomicSafetyHandle.CheckReadAndThrow(Handle);

            public bool HasBeenDisposed()
            {
                try
                {
                    AtomicSafetyHandle.CheckDeallocateAndThrow(Handle);
                }
                catch
                {
                    return true;
                }
                return false;
            }

            public void Release()
            {
                if (!HasBeenDisposed())
                    AtomicSafetyHandle.Release(Handle);
            }

            public static SupportsDeallocateOnJobCompletion Create() => new SupportsDeallocateOnJobCompletion
            {
                Handle = AtomicSafetyHandle.Create()
            };
        }

        [Test]
        [ManagedExceptionInPortableTests]
        public void DeallocateOnJobCompletion_WithRun_WithMultipleChunks_DeallocatesAtEnd()
        {
            m_Manager.CreateEntity(typeof(EcsTestFloatData));
            var c = SupportsDeallocateOnJobCompletion.Create();
            try
            {
                Assert.DoesNotThrow(() => TestSystem.DeallocateOnJobCompletion_WithRun(c));
                Assert.IsTrue(c.HasBeenDisposed(), "Dispose has not been called");
            }
            finally
            {
                c.Release();
            }
        }

        [Test]
        [ManagedExceptionInPortableTests]
        public void DeallocateInsideStructOnJobCompletion_WithRun_DeallocatesAtEnd()
        {
            m_Manager.CreateEntity(typeof(EcsTestFloatData));
            var c = new CanContainDisposedStruct {SupportsDeallocateOnJobCompletion = SupportsDeallocateOnJobCompletion.Create()};
            try
            {
                Assert.DoesNotThrow(() => TestSystem.DeallocateInsideStructOnJobCompletion_WithRun(c));
                Assert.IsTrue(c.SupportsDeallocateOnJobCompletion.HasBeenDisposed(), "Dispose has not been called for contained struct");
            }
            finally
            {
                c.SupportsDeallocateOnJobCompletion.Release();
            }
        }

        [Test]
        [ManagedExceptionInPortableTests]
        public void DeallocateInsideClassOnJobCompletion_WithRun_DeallocatesAtEnd()
        {
            m_Manager.CreateEntity(typeof(EcsTestFloatData));
            var c = new CanContainDisposedClass {SupportsDeallocateOnJobCompletion = SupportsDeallocateOnJobCompletion.Create()};
            try
            {
                Assert.DoesNotThrow(() => TestSystem.DeallocateInsideClassOnJobCompletion_WithRun(c));
                Assert.IsTrue(c.SupportsDeallocateOnJobCompletion.HasBeenDisposed(), "Dispose has not been called for contained struct");
            }
            finally
            {
                c.SupportsDeallocateOnJobCompletion.Release();
            }
        }

        [Test]
        [ManagedExceptionInPortableTests]
        public void DeallocateOnJobCompletion_WithStructuralChanges_Deallocates()
        {
            m_Manager.CreateEntity(typeof(EcsTestFloatData));
            var c = SupportsDeallocateOnJobCompletion.Create();
            try
            {
                Assert.DoesNotThrow(() => TestSystem.DeallocateOnJobCompletion_WithStructuralChanges(c));
                Assert.IsTrue(c.HasBeenDisposed(), "Dispose has not been called");
            }
            finally
            {
                c.Release();
            }
        }

        class TestSystemType : SystemBase
        {
            protected override void OnUpdate()
            {
            }

            public void DeallocateOnJobCompletion_WithRun(SupportsDeallocateOnJobCompletion c)
            {
                Entities.WithDeallocateOnJobCompletion(c).ForEach((ref EcsTestFloatData _) => { c.CheckCanRead(); }).Run();
            }

            public void DeallocateInsideStructOnJobCompletion_WithRun(CanContainDisposedStruct c)
            {
                Entities.WithDeallocateOnJobCompletion(c).ForEach((ref EcsTestFloatData _) => { c.SupportsDeallocateOnJobCompletion.CheckCanRead(); }).Run();
            }

            public void DeallocateInsideClassOnJobCompletion_WithRun(CanContainDisposedClass c)
            {
                Entities.WithoutBurst().WithDeallocateOnJobCompletion(c).ForEach((ref EcsTestFloatData _) => { c.SupportsDeallocateOnJobCompletion.CheckCanRead(); }).Run();
            }

            public void DeallocateOnJobCompletion_WithStructuralChanges(SupportsDeallocateOnJobCompletion c)
            {
                Entities.WithStructuralChanges().WithDeallocateOnJobCompletion(c).ForEach((ref EcsTestFloatData _) => { c.CheckCanRead(); }).Run();
            }
        }
    }
}
