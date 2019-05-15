#if !UNITY_DOTSPLAYER
using UnityEngine;
using NUnit.Framework;
using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using Unity.Entities.Tests;
using Unity.Collections.LowLevel.Unsafe;

public class BlobTests : ECSTestsFixture
{
    //@TODO: Test Prevent NativeArray and other containers inside of Blob data
    //@TODO: Test Prevent BlobPtr, BlobArray onto job struct
    //@TODO: Various tests trying to break the Allocator. eg. mix multiple BlobAllocator in the same BlobRoot...

    struct MyData
    {
        public BlobArray<float> floatArray;
        public BlobPtr<float> nullPtr;
        public BlobPtr<Vector3> oneVector3;
        public float embeddedFloat;
        public BlobArray<BlobArray<int>> nestedArray;
    }

    static unsafe BlobAssetReference<MyData> ConstructBlobData()
    {
        var builder = new BlobBuilder(Allocator.Temp);

        ref var root = ref builder.ConstructRoot<MyData>();

        var floatArray = builder.Allocate(3, ref root.floatArray);
        ref Vector3 oneVector3 = ref builder.Allocate(ref root.oneVector3);
        var nestedArrays = builder.Allocate(2, ref root.nestedArray);

        var nestedArray0 = builder.Allocate(1, ref nestedArrays[0]);
        var nestedArray1 = builder.Allocate(2, ref nestedArrays[1]);

        nestedArray0[0] = 0;
        nestedArray1[0] = 1;
        nestedArray1[1] = 2;

        floatArray[0] = 0;
        floatArray[1] = 1;
        floatArray[2] = 2;

        root.embeddedFloat = 4;
        oneVector3 = new Vector3(3, 3, 3);

        var blobAsset = builder.CreateBlobAssetReference<MyData>(Allocator.Persistent);

        builder.Dispose();

        return blobAsset;
    }

    static void ValidateBlobData(ref MyData root)
    {
        Assert.AreEqual(3, root.floatArray.Length);
        Assert.AreEqual(0, root.floatArray[0]);
        Assert.AreEqual(1, root.floatArray[1]);
        Assert.AreEqual(2, root.floatArray[2]);
        Assert.AreEqual(new Vector3(3, 3, 3), root.oneVector3.Value);
        Assert.AreEqual(4, root.embeddedFloat);

        Assert.AreEqual(1, root.nestedArray[0].Length);
        Assert.AreEqual(2, root.nestedArray[1].Length);

        Assert.AreEqual(0, root.nestedArray[0][0]);
        Assert.AreEqual(1, root.nestedArray[1][0]);
        Assert.AreEqual(2, root.nestedArray[1][1]);
    }


    [Test]
    public unsafe void CreateBlobData()
    {
        var blob = ConstructBlobData();
        ValidateBlobData(ref blob.Value);

        blob.Release();
    }

    [Test]
    public unsafe void BlobAccessAfterReleaseThrows()
    {
        var blob = ConstructBlobData();
        var blobCopy = blob;
        blob.Release();
        Assert.Throws<InvalidOperationException>(() => { blobCopy.GetUnsafePtr(); });
    }

    struct ValidateBlobJob : IJob
    {
        public BlobAssetReference<MyData> blob;

        public unsafe void Execute()
        {
            ValidateBlobData(ref blob.Value);
        }
    }

    struct ComponentWithBlobData : IComponentData
    {
        public BlobAssetReference<MyData> blobAsset;
    }

    [Test]
    public void ReadBlobDataFromJob()
    {
        var blob = ConstructBlobData();

        var jobData = new ValidateBlobJob();
        jobData.blob = blob;

        jobData.Schedule().Complete();

        blob.Release();
    }


    struct ValidateBlobInComponentJob : IJobForEach<ComponentWithBlobData>
    {
        public bool ExpectException;

        public unsafe void Execute(ref ComponentWithBlobData component)
        {
            if (ExpectException)
            {
                var blobAsset = component.blobAsset;
                Assert.Throws<InvalidOperationException>(() => { blobAsset.GetUnsafePtr(); });
            }
            else
            {
                ValidateBlobData(ref component.blobAsset.Value);
            }
        }
    }

    [DisableAutoCreation]
    class DummySystem : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle inHandle)
        {
            return inHandle;
        }
    }

    [Test]
    public unsafe void ParallelBlobAccessFromEntityJob()
    {
        var blob = CreateBlobEntities();

        var jobData = new ValidateBlobInComponentJob();

        var system = World.Active.GetOrCreateSystem<DummySystem>();
        var jobHandle = jobData.Schedule(system);

        ValidateBlobData(ref blob.Value);

        jobHandle.Complete();

        blob.Release();
    }

    [Test]
    public void DestroyedBlobAccessFromEntityJobThrows()
    {
        var blob = CreateBlobEntities();

        blob.Release();

        var jobData = new ValidateBlobInComponentJob();
        jobData.ExpectException = true;
        var system = World.Active.GetOrCreateSystem<DummySystem>();
        var jobHandle = jobData.Schedule(system);

        jobHandle.Complete();
    }

    [Test]
    public void BlobAssetReferenceIsComparable()
    {
        var blob1 = ConstructBlobData();
        var blob2 = ConstructBlobData();
        var blobNull = new BlobAssetReference<MyData>();

        var temp1 = blob1;

        Assert.IsTrue(blob1 != blob2);
        Assert.IsTrue(blob1 != BlobAssetReference<MyData>.Null);
        Assert.IsTrue(blobNull == BlobAssetReference<MyData>.Null);
        Assert.IsTrue(blob1 == temp1);
        Assert.IsTrue(blob2 != temp1);

        blob1.Release();
        blob2.Release();
    }

    [Test]
    public void SourceBlobArrayThrowsOnIndex()
    {
        var builder = new BlobBuilder(Allocator.Temp);

        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            //can't access ref variable if it's created outside of the lambda
            ref var root = ref builder.ConstructRoot<MyData>();
            builder.Allocate(3, ref root.floatArray);
            root.floatArray[0] = 7;
        });

        builder.Dispose();
    }

    [Test]
    public void SourceBlobPtrThrowsOnDereference()
    {
        var builder = new BlobBuilder(Allocator.Temp);

        Assert.Throws<InvalidOperationException>(() =>
        {
            //can't access ref variable if it's created outside of the lambda
            ref var root = ref builder.ConstructRoot<MyData>();
            builder.Allocate(ref root.oneVector3);
            root.oneVector3.Value = Vector3.zero;
        });

        builder.Dispose();
    }

    struct AlignmentTest
    {
        public BlobPtr<short> shortPointer;
        public BlobPtr<int> intPointer;
        public BlobPtr<byte> bytePointer;
        public BlobArray<int> intArray;
    }

    [Test]
    public unsafe void BasicAlignmentWorks()
    {
        void AssertAlignment(void* p, int alignment)
        {
            ulong mask = (ulong) alignment - 1;
            Assert.IsTrue(((ulong) (IntPtr) p & mask) == 0);
        }

        var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<BlobArray<AlignmentTest>>();
        Assert.AreEqual(4, UnsafeUtility.AlignOf<int>());

        const int count = 100;
        var topLevelArray = builder.Allocate(count, ref root);
        for (int x = 0; x < count; ++x)
        {
            builder.Allocate(ref topLevelArray[x].shortPointer);
            builder.Allocate(ref topLevelArray[x].intPointer);
            builder.Allocate(ref topLevelArray[x].bytePointer);
            builder.Allocate(x + 1, ref topLevelArray[x].intArray);
        }

        var blob = builder.CreateBlobAssetReference<BlobArray<AlignmentTest>>(Allocator.Temp);
        builder.Dispose();

        for (int x = 0; x < count; ++x)
        {
            AssertAlignment(blob.Value[x].shortPointer.GetUnsafePtr(), 2);
            AssertAlignment(blob.Value[x].intPointer.GetUnsafePtr(), 4);
            AssertAlignment(blob.Value[x].intArray.GetUnsafePtr(), 4);
        }

        blob.Release();
    }

    [Test]
    public void BlobBuilderArrayThrowsOnOutOfBoundsIndex()
    {
        using (var builder = new BlobBuilder(Allocator.Temp, 128))
        {
            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                ref var root = ref builder.ConstructRoot<BlobArray<int>>();
                var array = builder.Allocate(100, ref root);
                array[100] = 7;
            });
        }
    }

    [Test]
    public void AllocationsLargerThanChunkSizeWorks()
    {
        var builder = new BlobBuilder(Allocator.Temp, 128);
        ref var root = ref builder.ConstructRoot<BlobArray<int>>();
        const int count = 100;
        var array = builder.Allocate(count, ref root);
        for (int i = 0; i < count; i++)
            array[i] = i;

        var blob = builder.CreateBlobAssetReference<BlobArray<int>>(Allocator.Temp);
        builder.Dispose();

        for (int i = 0; i < count; i++)
            Assert.AreEqual(i, blob.Value[i]);

        blob.Release();
    }

    [Test]
    public void CreatingLargeBlobAssetWorks()
    {
        var builder = new BlobBuilder(Allocator.Temp, 512);
        ref var root = ref builder.ConstructRoot<BlobArray<BlobArray<BlobArray<BlobPtr<int>>>>>();

        const int topLevelCount = 100;

        int expectedValue = 42;
        var level0 = builder.Allocate(topLevelCount, ref root);
        for (int x = 0; x < topLevelCount; x++)
        {
            var level1 = builder.Allocate(x + 1, ref level0[x]);
            for (int y = 0; y < x + 1; y++)
            {
                var level2 = builder.Allocate(y + 1, ref level1[y]);
                for (int z = 0; z < y + 1; z++)
                {
                    ref var i = ref builder.Allocate(ref level2[z]);
                    i = expectedValue++;
                }
            }
        }

        var blob = builder.CreateBlobAssetReference<BlobArray<BlobArray<BlobArray<BlobPtr<int>>>>>(Allocator.Temp);
        builder.Dispose();

        expectedValue = 42;
        for (int x = 0; x < topLevelCount; x++)
        {
            for (int y = 0; y < x + 1; y++)
            {
                for (int z = 0; z < y + 1; z++)
                {
                    int value = blob.Value[x][y][z].Value;

                    if (expectedValue != value)
                        Assert.AreEqual(expectedValue, value);
                    expectedValue++;
                }
            }
        }

        blob.Release();
    }


    public unsafe struct TestStruct256bytes
    {
        public BlobArray<int> intArray;
        public fixed int array[61];
        public BlobPtr<int> intPointer;
    }

    [Test]
    public void BlobAssetWithRootLargerThanChunkSizeWorks()
    {
        Assert.AreEqual(256, UnsafeUtility.SizeOf<TestStruct256bytes>());
        var builder = new BlobBuilder(Allocator.Temp, 128);
        ref var root = ref builder.ConstructRoot<TestStruct256bytes>();

        var array = builder.Allocate(100, ref root.intArray);
        for (int i = 0; i < array.Length; ++i)
        {
            array[i] = i;
        }

        builder.Allocate(ref root.intPointer);

        var blob = builder.CreateBlobAssetReference<TestStruct256bytes>(Allocator.Temp);
        builder.Dispose();

        for (int i = 0; i < blob.Value.intArray.Length; ++i)
        {
            if (i != blob.Value.intArray[i])
                Assert.AreEqual(i, blob.Value.intArray[i]);
        }

        blob.Release();
    }

    BlobAssetReference<MyData> CreateBlobEntities()
    {
        var blob = ConstructBlobData();

        for (int i = 0; i != 32; i++)
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentData(entity, new ComponentWithBlobData() {blobAsset = blob});
        }
        return blob;
    }
}
#endif
