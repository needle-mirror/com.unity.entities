using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using UnityEngine.TestTools;
using System.Diagnostics;
using Unity.Jobs.LowLevel.Unsafe;

public class BurstSafetyTests
{
        static string SafetyChecksMenu = "Jobs > Burst > Safety Checks";
        [SetUp]
        public virtual void Setup()
        {
            Assert.IsTrue(BurstCompiler.Options.EnableBurstSafetyChecks, $"Burst safety tests must have Burst safety checks enabled! To enable, go to {SafetyChecksMenu}");
        }

    [BurstCompile(CompileSynchronously = true)]
    struct ThrowExceptionJob : IJobParallelFor
    {
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void Throw()
        {
            throw new System.ArgumentException("Blah");
        }

        public void Execute(int index)
        {
            Throw();
        }
    }

    [Test]
    public void ThrowExceptionParallelForStress()
    {
        for (int i = 0; i < JobsUtility.JobWorkerCount + 1; i++)
            LogAssert.Expect(LogType.Exception, new Regex("ArgumentException: Blah"));

        var jobData = new ThrowExceptionJob();
        jobData.Schedule(100, 1).Complete();
    }

    [BurstCompile(CompileSynchronously = true)]
    struct WriteToReadOnlyArrayJob : IJob
    {
        [ReadOnly]
        public NativeArray<int> test;
        public void Execute()
        {
            test[0] = 5;
        }
    }

    [Test]
    public void WriteToReadOnlyArray()
    {
        LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));

        var jobData = new WriteToReadOnlyArrayJob();
        jobData.test = new NativeArray<int>(1, Allocator.Persistent);

        jobData.Run();

        jobData.test.Dispose();
    }

    [BurstCompile(CompileSynchronously = true)]
    struct ParallelForIndexChecksJob : IJobParallelFor
    {
        public NativeArray<int> test;

        public void Execute(int index)
        {
            test[0] = 5;
        }
    }

    [Test]
    public void ParallelForMinMaxChecks()
    {
        for (int i = 0; i < JobsUtility.JobWorkerCount + 1; i++)
            LogAssert.Expect(LogType.Exception, new Regex("IndexOutOfRangeException"));

        var jobData = new ParallelForIndexChecksJob();
        jobData.test = new NativeArray<int>(2, Allocator.Persistent);

        jobData.Schedule(100, 1).Complete();

        jobData.test.Dispose();
    }

    [BurstCompile(CompileSynchronously = true)]
    struct AccessNullNativeArrayJob : IJobParallelFor
    {
        public void Execute(int index)
        {
            var array = new NativeArray<float>();
            array[0] = 5;
        }
    }

    [Test]
    [Ignore("Crashes Unity - Important")]
    public void AccessNullNativeArray()
    {
        LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException"));

        new AccessNullNativeArrayJob().Schedule(100, 1).Complete();
    }

    [BurstCompile(CompileSynchronously = true)]
    unsafe struct AccessNullUnsafePtrJob : IJob
    {
#pragma warning disable 649
        [NativeDisableUnsafePtrRestriction] float* myArray;
#pragma warning restore 649

        public void Execute()
        {
            myArray[0] = 5;
        }
    }

#if !UNITY_DOTSRUNTIME
    [Test]
    [Ignore("Crashes Unity - No user is supposed to write code like this, so not very important")]
    public void AccessNullUnsafePtr()
    {
        LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException"));

        new AccessNullUnsafePtrJob().Run();
    }

#endif
}
