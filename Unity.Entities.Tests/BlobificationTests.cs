using UnityEngine;
using NUnit.Framework;
using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using Unity.Entities.Tests;
using Unity.Collections.LowLevel.Unsafe;
using Assert = NUnit.Framework.Assert;
using Unity.Mathematics;
using System.IO;
using Unity.IO.LowLevel.Unsafe;
#if UNITY_DOTSRUNTIME
using Unity.Tiny;
using Unity.Tiny.IO;
#endif

public class BlobTests : ECSTestsFixture
{
    //@TODO: Test Prevent NativeArray and other containers inside of Blob data
    //@TODO: Test Prevent BlobPtr, BlobArray onto job struct
    //@TODO: Various tests trying to break the Allocator. eg. mix multiple BlobAllocator in the same BlobRoot...

    struct MyData
    {
        public BlobArray<float> floatArray;
        public BlobPtr<float> nullPtr;
        public BlobPtr<float3> oneVector3;
        public float embeddedFloat;
        public BlobArray<BlobArray<int>> nestedArray;
        public BlobString str;
        public BlobString emptyStr;
    }

    static unsafe BlobBuilder ConstructBlobBuilder()
    {
        var builder = new BlobBuilder(Allocator.Temp);

        ref var root = ref builder.ConstructRoot<MyData>();

        var floatArray = builder.Allocate(ref root.floatArray, 3);
        ref float3 oneVector3 = ref builder.Allocate(ref root.oneVector3);
        var nestedArrays = builder.Allocate(ref root.nestedArray, 2);

        var nestedArray0 = builder.Allocate(ref nestedArrays[0], 1);
        var nestedArray1 = builder.Allocate(ref nestedArrays[1], 2);

        builder.AllocateString(ref root.str, "Blah");
        builder.AllocateString(ref root.emptyStr, "");

        nestedArray0[0] = 0;
        nestedArray1[0] = 1;
        nestedArray1[1] = 2;

        floatArray[0] = 0;
        floatArray[1] = 1;
        floatArray[2] = 2;

        root.embeddedFloat = 4;
        oneVector3 = new float3(3, 3, 3);

        return builder;
    }

    static unsafe BlobAssetReference<MyData> ConstructBlobData()
    {
        var builder = ConstructBlobBuilder();
        var blobAsset = builder.CreateBlobAssetReference<MyData>(Allocator.Persistent);
        builder.Dispose();

        return blobAsset;
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    static void ValidateBlobData(ref MyData root)
    {
        // not using Assert.AreEqual here because the asserts have to execute in burst jobs

        if (3 != root.floatArray.Length)
            throw new AssertionException("ValidateBlobData didn't match");
        if (0 != root.floatArray[0])
            throw new AssertionException("ValidateBlobData didn't match");
        if (1 != root.floatArray[1])
            throw new AssertionException("ValidateBlobData didn't match");
        if (2 != root.floatArray[2])
            throw new AssertionException("ValidateBlobData didn't match");
        if (!new float3(3, 3, 3).Equals(root.oneVector3.Value))
            throw new AssertionException("ValidateBlobData didn't match");

        if (4 != root.embeddedFloat)
            throw new AssertionException("ValidateBlobData didn't match");

        if (1 != root.nestedArray[0].Length)
            throw new AssertionException("ValidateBlobData didn't match");
        if (2 != root.nestedArray[1].Length)
            throw new AssertionException("ValidateBlobData didn't match");
        if (0 != root.nestedArray[0][0])
            throw new AssertionException("ValidateBlobData didn't match");
        if (1 != root.nestedArray[1][0])
            throw new AssertionException("ValidateBlobData didn't match");
        if (2 != root.nestedArray[1][1])
            throw new AssertionException("ValidateBlobData didn't match");
        ValidateBlobString(ref root);
    }

    [BurstDiscard]
    static void ValidateBlobString(ref MyData root)
    {
        var str = root.str.ToString();
        if ("Blah" != str)
            throw new AssertionException("ValidateBlobData didn't match");
        var emptyStr = root.emptyStr.ToString();
        if ("" != emptyStr)
            throw new AssertionException("ValidateBlobData didn't match");
    }

    static void ValidateBlobDataBurst(ref MyData root)
    {
        Assert.AreEqual(3, root.floatArray.Length);
        Assert.AreEqual(0, root.floatArray[0]);
        Assert.AreEqual(1, root.floatArray[1]);
        Assert.AreEqual(2, root.floatArray[2]);
        Assert.AreEqual(new float3(3, 3, 3), root.oneVector3.Value);
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

        blob.Dispose();
    }

    [Test]
    public unsafe void BlobAccessAfterReleaseThrows()
    {
        var blob = ConstructBlobData();
        var blobCopy = blob;
        blob.Dispose();

        Assert.Throws<InvalidOperationException>(() => { blobCopy.GetUnsafePtr(); });
        Assert.IsTrue(blob.GetUnsafePtr() == null);

        Assert.Throws<InvalidOperationException>(() => { var p = blobCopy.Value.embeddedFloat; });
        Assert.Throws<InvalidOperationException>(() => { var p = blobCopy.Value.embeddedFloat; });

        Assert.Throws<InvalidOperationException>(() => { blobCopy.Dispose(); });
        Assert.Throws<InvalidOperationException>(() => { blob.Dispose(); });
    }

    struct ComponentWithBlobData : IComponentData
    {
        public bool DidSucceed;
        public BlobAssetReference<MyData> blobAsset;
    }

    // Cannot be compiled by Burst because of the call to `builder.AllocateString` in `ConstructBlobBuilder`.
    //[BurstCompile(CompileSynchronously = true)]
    struct ConstructAccessAndDisposeBlobData : IJob
    {
        public void Execute()
        {
            var blobData = ConstructBlobData();
            ValidateBlobData(ref blobData.Value);
            blobData.Dispose();
        }
    }

    [Test]
    public void BurstedConstructionAndAccess()
    {
        new ConstructAccessAndDisposeBlobData().Schedule().Complete();
    }

#if !UNITY_DOTSRUNTIME  // IJobForEach is deprecated
#pragma warning disable 618
    [BurstCompile(CompileSynchronously = true)]
    struct AccessAndDisposeBlobDataBurst : IJobForEach<ComponentWithBlobData>
    {
        public void Execute(ref ComponentWithBlobData data)
        {
            ValidateBlobData(ref data.blobAsset.Value);
            data.blobAsset.Dispose();
            data.DidSucceed = true;
        }
    }
#pragma warning restore 618


    [Test]
    public  void ReadAndDestroyBlobDataFromBurstJob()
    {
        var entities = CreateUniqueBlob();

        new AccessAndDisposeBlobDataBurst().Schedule(EmptySystem).Complete();

        foreach (var e in entities)
        {
            Assert.IsTrue(m_Manager.GetComponentData<ComponentWithBlobData>(e).DidSucceed);
            Assert.IsFalse(m_Manager.GetComponentData<ComponentWithBlobData>(e).blobAsset.IsCreated);
        }
    }

#pragma warning disable 618
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

            component.DidSucceed = true;
        }
    }
#pragma warning restore 618


    [Test]
    public unsafe void ParallelBlobAccessFromEntityJob()
    {
        var blob = CreateSharedBlob();

        var jobData = new ValidateBlobInComponentJob();

        var jobHandle = jobData.Schedule(EmptySystem);

        ValidateBlobData(ref blob.Value);

        jobHandle.Complete();

        blob.Dispose();
    }

    [Test]
    public void DestroyedBlobAccessFromEntityJobThrows()
    {
        var blob = CreateSharedBlob();

        blob.Dispose();

        var jobData = new ValidateBlobInComponentJob();
        jobData.ExpectException = true;
        jobData.Schedule(EmptySystem).Complete();
    }
#endif

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

        blob1.Dispose();
        blob2.Dispose();
    }

    [Test]
    public void SourceBlobArrayThrowsOnIndex()
    {
        var builder = new BlobBuilder(Allocator.Temp);

        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            //can't access ref variable if it's created outside of the lambda
            ref var root = ref builder.ConstructRoot<MyData>();
            builder.Allocate(ref root.floatArray, 3);

            // Throw on access expected here
            root.floatArray[0] = 7;
        });

        builder.Dispose();
    }

    [Test]
    public void BlobArrayToArrayCopiesResults()
    {
        var blob = ConstructBlobData();
        ref MyData root = ref blob.Value;

        var floatArray = root.floatArray.ToArray();
        Assert.AreEqual(new float[] { 0, 1, 2 }, floatArray);

        blob.Dispose();
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

            // Throw on access expected here
            root.oneVector3.Value = float3.zero;
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

    static unsafe void AssertAlignment(void* p, int alignment)
    {
        ulong mask = (ulong)alignment - 1;
        Assert.IsTrue(((ulong)(IntPtr)p & mask) == 0);
    }

    [Test]
    public unsafe void BasicAlignmentWorks()
    {
        var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<BlobArray<AlignmentTest>>();
        Assert.AreEqual(4, UnsafeUtility.AlignOf<int>());

        const int count = 100;
        var topLevelArray = builder.Allocate(ref root, count);
        for (int x = 0; x < count; ++x)
        {
            builder.Allocate(ref topLevelArray[x].shortPointer);
            builder.Allocate(ref topLevelArray[x].intPointer);
            builder.Allocate(ref topLevelArray[x].bytePointer);
            builder.Allocate(ref topLevelArray[x].intArray, x + 1);
        }

        var blob = builder.CreateBlobAssetReference<BlobArray<AlignmentTest>>(Allocator.Temp);
        builder.Dispose();

        for (int x = 0; x < count; ++x)
        {
            AssertAlignment(blob.Value[x].shortPointer.GetUnsafePtr(), 2);
            AssertAlignment(blob.Value[x].intPointer.GetUnsafePtr(), 4);
            AssertAlignment(blob.Value[x].intArray.GetUnsafePtr(), 4);
        }

        blob.Dispose();
    }

    [Test]
    public unsafe void CreatedBlobsAre16ByteAligned()
    {
        var blobAssetReference = BlobAssetReference<int>.Create(42);
        AssertAlignment(blobAssetReference.GetUnsafePtr(), 16);
    }

    [Test]
    public void BlobBuilderArrayThrowsOnOutOfBoundsIndex()
    {
        using (var builder = new BlobBuilder(Allocator.Temp, 128))
        {
            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                ref var root = ref builder.ConstructRoot<BlobArray<int>>();
                var array = builder.Allocate(ref root, 100);
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
        var array = builder.Allocate(ref root, count);
        for (int i = 0; i < count; i++)
            array[i] = i;

        var blob = builder.CreateBlobAssetReference<BlobArray<int>>(Allocator.Temp);
        builder.Dispose();

        for (int i = 0; i < count; i++)
            Assert.AreEqual(i, blob.Value[i]);

        blob.Dispose();
    }

    [Test]
    public void CreatingLargeBlobAssetWorks()
    {
        var builder = new BlobBuilder(Allocator.Temp, 512);
        ref var root = ref builder.ConstructRoot<BlobArray<BlobArray<BlobArray<BlobPtr<int>>>>>();

        const int topLevelCount = 100;

        int expectedValue = 42;
        var level0 = builder.Allocate(ref root, topLevelCount);
        for (int x = 0; x < topLevelCount; x++)
        {
            var level1 = builder.Allocate(ref level0[x], x + 1);
            for (int y = 0; y < x + 1; y++)
            {
                var level2 = builder.Allocate(ref level1[y], y + 1);
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

        blob.Dispose();
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

        var array = builder.Allocate(ref root.intArray, 100);
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

        blob.Dispose();
    }

    BlobAssetReference<MyData> CreateSharedBlob()
    {
        var blob = ConstructBlobData();

        for (int i = 0; i != 32; i++)
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentData(entity, new ComponentWithBlobData() {blobAsset = blob});
        }
        return blob;
    }

    NativeArray<Entity> CreateUniqueBlob()
    {
        var entities = new NativeArray<Entity>(32, Allocator.Temp);
        for (int i = 0; i != entities.Length; i++)
        {
            entities[i] = m_Manager.CreateEntity();
            m_Manager.AddComponentData(entities[i], new ComponentWithBlobData() {blobAsset = ConstructBlobData()});
        }

        return entities;
    }

#if !UNITY_DOTSRUNTIME
    [Test]
    public void BlobAssetReferenceTryRead()
    {
        const int kVersion = 42;
        const int kIncorrectVersion = 13;
        Assert.AreNotEqual(kVersion, kIncorrectVersion);

        string path = "BlobAssetReferenceIOTestData.blob";
        if (File.Exists(path))
            File.Delete(path);

        try
        {
            bool result = false;
            var blobBuilder = ConstructBlobBuilder();
            BlobAssetReference<MyData>.Write(blobBuilder, path, kVersion);

            result = BlobAssetReference<MyData>.TryRead(path, kIncorrectVersion, out var incorrectBlobResult);
            Assert.IsTrue(!result);

            result = BlobAssetReference<MyData>.TryRead(path, kVersion, out var correctBlobResult);
            Assert.IsTrue(result);
            ValidateBlobData(ref correctBlobResult.Value);
            correctBlobResult.Dispose();
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
#else
    [Test]
    public unsafe void BlobAssetReferenceTryRead()
    {
        const int kVersion = 42;
        const int kIncorrectVersion = 13;
        Assert.AreNotEqual(kVersion, kIncorrectVersion);

        string path = "BlobAssetReferenceIOTestData.blob";
        var startTime = Time.realtimeSinceStartup;
        var op = IOService.RequestAsyncRead(path);
        while (op.GetStatus() <= AsyncOp.Status.InProgress)
        {
            if ((Time.realtimeSinceStartup - startTime) > 5.0 /*seconds*/)
                break;
        }
        Assert.IsTrue(op.GetStatus() == AsyncOp.Status.Success);

        bool result = false;
        op.GetData(out var data, out var dataSize);
        result = BlobAssetReference<MyData>.TryRead(data, kIncorrectVersion, out var incorrectBlobResult);
        Assert.IsTrue(!result);

        result = BlobAssetReference<MyData>.TryRead(data, kVersion, out var correctBlobResult);
        Assert.IsTrue(result);
        ValidateBlobData(ref correctBlobResult.Value);

        correctBlobResult.Dispose();
        op.Dispose();
    }
#endif

    public struct Node
    {
        BlobPtr<byte> _parent;
        BlobArray<byte> _children;
        public int id;

        // Core CLR can't handle cyclic references.
        // https://github.com/dotnet/runtime/issues/5479
        // Thus we do this workaround for blob ptr
        unsafe public ref BlobPtr<Node> parent
        {
            get
            {
                return ref UnsafeUtility.AsRef<BlobPtr<Node>>(UnsafeUtility.AddressOf(ref _parent));
            }
        }

        unsafe public ref BlobArray<Node> children
        {
           get
            {
                return ref UnsafeUtility.AsRef<BlobArray<Node>>(UnsafeUtility.AddressOf(ref _children));
            }
        }
    }

    [Test]
    public void BlobSetPointer()
    {
        BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        {
            ref var rootNode = ref builder.ConstructRoot<Node>();

            rootNode.id = 0;

            var children = builder.Allocate(ref rootNode.children, 2);

            builder.SetPointer(ref children[0].parent, ref rootNode);
            children[0].id = 1;
            builder.SetPointer(ref children[1].parent, ref rootNode);
            children[1].id = 2;

            var grandChildren = builder.Allocate(ref children[1].children, 1);

            builder.SetPointer(ref grandChildren[0].parent, ref children[1]);
            grandChildren[0].id = 3;
        }

        var blob = builder.CreateBlobAssetReference<Node>(Allocator.Temp);

        builder.Dispose();

        {
            ref var rootNode = ref blob.Value;

            ref var child0 = ref rootNode.children[0];
            ref var child1 = ref rootNode.children[1];

            ref var grandChild = ref child1.children[0];

            Assert.AreEqual(0, rootNode.id);
            Assert.AreEqual(1, child0.id);
            Assert.AreEqual(2, child1.id);
            Assert.AreEqual(3, grandChild.id);

            Assert.IsFalse(rootNode.parent.IsValid);
            Assert.AreEqual(0, child0.parent.Value.id);
            Assert.AreEqual(0, child1.parent.Value.id);
            Assert.AreEqual(2, grandChild.parent.Value.id);
        }

        blob.Dispose();
    }

    [Test]
    public void BlobSetPointerException()
    {
        Assert.Throws<ArgumentException>(() => {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref var rootNode = ref builder.ConstructRoot<Node>();
            var child = new Node();
            builder.SetPointer(ref rootNode.parent, ref child);
            });
    }
}
