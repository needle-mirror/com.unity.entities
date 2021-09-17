#if !UNITY_DOTSRUNTIME && ENABLE_UNITY_COLLECTIONS_CHECKS

using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Entities.Tests
{
    public unsafe class ScratchpadAllocatorTests : ECSTestsCommonBase
    {
        World m_world;
        Scratchpad m_scratchpad;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            m_world = new World("ScratchpadAllocatorTests");
            var system = m_world.GetOrCreateSystem<WorldUpdateAllocatorResetSystem>();
            var group = m_world.GetOrCreateSystem<InitializationSystemGroup>();
            group.AddSystemToUpdateList(system.Handle);
            group.SortSystems();
            m_scratchpad = new Scratchpad(JobsUtility.MaxJobThreadCount);
        }

        [TearDown]
        public override void TearDown()
        {
            m_scratchpad.Dispose();
            m_world.Dispose();
            base.TearDown();
        }

        struct RewindInvalidatesNativeArrayJob : IJobParallelFor
        {
            internal ScratchpadAllocator m_temp;
            public void Execute(int index)
            {
                var array = m_temp.AllocateNativeArray<float>(100);
                m_temp.Rewind();
                array[0] = index; // should throw an exception - isn't safe
            }
        }

        [Test]
        public void RewindInvalidatesNativeArrayInJob()
        {
            m_scratchpad.Rewind();
            RewindInvalidatesNativeArrayJob job = new RewindInvalidatesNativeArrayJob {
                m_temp = new ScratchpadAllocator(m_scratchpad),
            };
            LogAssert.Expect(LogType.Exception, new Regex("ObjectDisposedException"));
                // temporary check to be replaced with the one below following an editor version promotion that includes
                // a NativeArray and NativeSlice atomic safety handle check revert
            // LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException")); - temporarily commenting out updated assert checks to ensure editor version promotion succeeds
            job.Schedule(1, 1).Complete();
        }

        struct RewindInvalidatesNativeListJob : IJobParallelFor
        {
            internal ScratchpadAllocator m_temp;
            public void Execute(int index)
            {
                var list = m_temp.AllocateNativeList<float>(100);
                m_temp.Rewind();
                list.Add(0); // should throw an exception - isn't safe
            }
        }

        [Test]
        public void RewindInvalidatesNativeListInJob()
        {
            m_scratchpad.Rewind();
            RewindInvalidatesNativeListJob job = new RewindInvalidatesNativeListJob {
                m_temp = new ScratchpadAllocator(m_scratchpad),
            };
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            job.Schedule(1, 1).Complete();
        }

        struct ScratchpadNativeListCanResizeJob : IJobParallelFor
        {
            internal ScratchpadAllocator m_temp;
            public void Execute(int index)
            {
                m_temp.Rewind();
                var list = new NativeList<float>(20, (Allocator)m_temp.Handle.Value);
                list.Capacity = 200;
                list.Capacity = 2000;
                list.Add(0); // should be okay.
            }
        }

        [Test]
        public void ScratchpadNativeListCanResizeInJob()
        {
            m_scratchpad.Rewind();
            ScratchpadNativeListCanResizeJob job = new ScratchpadNativeListCanResizeJob {
                m_temp = new ScratchpadAllocator(m_scratchpad),
            };
            job.Schedule(1, 1).Complete();
        }

        struct AccumulatorJob : IJobParallelFor
        {
            internal ScratchpadAllocator m_temp;
            internal NativeArray<float> m_results;
            public void Execute(int index)
            {
                m_temp.Rewind();
                var array = m_temp.AllocateNativeArray<float>(100);
                for(var i = 0; i < 100; ++i)
                    array[i] = index;
                float total = 0;
                for(var i = 0; i < 100; ++i)
                    total += array[i];
                m_results[index] = total;
            }
        }

        [Test]
        public void CanCheaplyAllocateTemporaryMemoryInJob()
        {
            m_scratchpad.Rewind();
            using (var results = new NativeArray<float>(100, Allocator.Persistent))
            {
                AccumulatorJob job = new AccumulatorJob
                {
                    m_temp = new ScratchpadAllocator(m_scratchpad),
                    m_results = results,
                };
                job.Schedule(100, 1).Complete();
                for (var i = 0; i < 100; ++i)
                    Assert.AreEqual(100 * i, results[i]);
            }
        }

        struct RewindJob : IJobParallelFor
        {
            internal ScratchpadAllocator m_temp;
            internal NativeArray<float> m_results;
            public void Execute(int index)
            {
                var array = m_temp.AllocateNativeArray<float>(8000); // 32000 bytes
                m_temp.Rewind();
                array = m_temp.AllocateNativeArray<float>(8000); // another 32000 bytes!
                for(var i = 0; i < 8000; ++i)
                    array[i] = index;
                float total = 0;
                for(var i = 0; i < 8000; ++i)
                    total += array[i];
                m_results[index] = total;
                m_temp.Rewind();
            }
        }

        [Test]
        public void CanRewindTemporaryMemoryInJob()
        {
            m_scratchpad.Rewind();
            using (var results = new NativeArray<float>(100, Allocator.Persistent))
            {
                RewindJob job = new RewindJob
                {
                    m_temp = new ScratchpadAllocator(m_scratchpad),
                    m_results = results,
                };
                job.Schedule(100, 1).Complete();
                for (var i = 0; i < 100; ++i)
                    Assert.AreEqual(8000 * i, results[i]);
            }
        }
    }
}

#endif
