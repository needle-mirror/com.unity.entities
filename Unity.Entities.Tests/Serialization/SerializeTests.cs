using NUnit.Framework;
using Unity.Collections;
using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Serialization;
using Unity.Jobs;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.Profiling;
using BinaryReader = Unity.Entities.Serialization.BinaryReader;
using BinaryWriter = Unity.Entities.Serialization.BinaryWriter;
using Random = Unity.Mathematics.Random;
#if !UNITY_PORTABLE_TEST_RUNNER
using System.IO;
#endif

namespace Unity.Entities.Tests
{
    public unsafe struct TestBinaryReader : BinaryReader
    {
        NativeList<byte> content;
        int position;
        public TestBinaryReader(TestBinaryWriter writer)
        {
            position = 0;
            content = writer.content;
            writer.content = new NativeList<byte>();
        }

        public void Dispose()
        {
            content.Dispose();
        }

        public void ReadBytes(void* data, int bytes)
        {
            UnsafeUtility.MemCpy(data, (byte*)content.GetUnsafePtr() + position, bytes);
            position += bytes;
        }

        public long Position
        {
            get => position;
            set => position = (int)value;
        }
    }

    public unsafe class TestBinaryWriter : BinaryWriter
    {
        public NativeList<byte> content;

        public TestBinaryWriter(Allocator allocator)
        {
            content = new NativeList<byte>(allocator);
        }

        public void Dispose()
        {
            content.Dispose();
        }

        public void WriteBytes(void* data, int bytes)
        {
            int length = (int)Position;
            content.Resize(length + bytes, NativeArrayOptions.UninitializedMemory);
            UnsafeUtility.MemCpy((byte*)content.GetUnsafePtr() + length, data, bytes);
            Position += bytes;
        }

        public long Position { get; set; }
    }

    struct DeserializeJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public TestBinaryReader Reader { get; set; }

        public void Execute()
        {
            SerializeUtility.DeserializeWorld(Transaction, Reader);
        }
    }

#if !UNITY_PORTABLE_TEST_RUNNER
    internal class YAMLSerializationHelpers
    {
        /// <summary>
        /// Compare two YAML file for equality, ignore CRLF mismatch
        /// </summary>
        /// <param name="fileA">Stream of the first file to compare</param>
        /// <param name="fileB">Stream of the second file to compare</param>
        /// <returns>true if both file are equal, false otherwise</returns>
        /// <remarks>
        /// This method start reading from both streams from their CURRENT position
        /// </remarks>
        public static bool EqualYAMLFiles(Stream fileA, Stream fileB)
        {
            using (var readerA = new StreamReader(fileA))
            using (var readerB = new StreamReader(fileB))
            {
                string lineA;
                while ((lineA = readerA.ReadLine()) != null)
                {
                    var lineB = readerB.ReadLine();
                    if (string.Compare(lineA, lineB, StringComparison.Ordinal) != 0)
                    {
                        return false;
                    }
                }
                return readerB.ReadLine() == null;
            }
        }
    }
#endif

#if UNITY_EDITOR
    public static class Hash128Helpers
    {
        private static uint _counter = 1;
        public static Hash128 New()
        {
            return new Hash128(123, 456, 789, _counter++);
        }
    }

    class DotsSerializationTest
    {
        public struct BlobTestA
        {
            public int A;
            public int B;
            public BlobArray<int> Values;

            public static BlobAssetReference<BlobTestA> Build(int a, int b, int v1, int v2, int v3)
            {
                var builder = new BlobBuilder(Allocator.Temp);
                ref var r = ref builder.ConstructRoot<BlobTestA>();
                r.A = a;
                r.B = b;
                var array = builder.Allocate(ref r.Values, 3);
                array[0] = v1;
                array[1] = v2;
                array[2] = v3;

                return builder.CreateBlobAssetReference<BlobTestA>(Allocator.Persistent);
            }

            public static BlobAssetReference<BlobTestA> Build(int a, int b, int v1, int v2, int v3, int v4, int v5)
            {
                var builder = new BlobBuilder(Allocator.Temp);
                ref var r = ref builder.ConstructRoot<BlobTestA>();
                r.A = a;
                r.B = b;
                var array = builder.Allocate(ref r.Values, 5);
                array[0] = v1;
                array[1] = v2;
                array[2] = v3;
                array[3] = v4;
                array[4] = v5;

                return builder.CreateBlobAssetReference<BlobTestA>(Allocator.Persistent);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NodeHeaderA : IComponentData
        {
            public DotsSerialization.NodeHeader Header;
            public int A;
            public int B;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NodeHeaderB : IComponentData
        {
            public DotsSerialization.NodeHeader Header;
            public float A;
            public float B;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NodeHeaderC : IComponentData
        {
            public DotsSerialization.NodeHeader Header;
            public uint4 A;
            public uint2 B;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NodeHeaderD : IComponentData
        {
            public DotsSerialization.NodeHeader Header;
            public uint2 A;
            public uint2 B;
        }

        ulong TypeHash<T>() => TypeManager.GetTypeInfo<T>().StableTypeHash;

        [Test]
        public void CreateWriteReadNodeStructureWithEnumeration()
        {
            var pfn = FileUtil.GetUniqueTempPathInProject();

            try
            {
                CreateSimpleFile(pfn);

                using (var reader = new StreamBinaryReader(pfn))
                using (var dsf = DotsSerialization.CreateReader(reader))
                {
                    Assert.That(dsf.RootNode.ChildrenCount, Is.EqualTo(2));

                    var curChild = default(DotsSerializationReader.NodeHandle);
                    while (dsf.RootNode.MoveToNextChild(ref curChild))
                    {
                        if (curChild.NodeTypeHash == TypeHash<NodeHeaderA>())
                        {
                            Assert.That(curChild.AsNodeHeader.Id, Is.EqualTo(NodeId_A));
                            Assert.That(curChild.NodeDotNetType, Is.EqualTo(typeof(NodeHeaderA)));

                            ref var nodeA = ref curChild.As<NodeHeaderA>();
                            Assert.That(nodeA.A, Is.EqualTo(1234));
                            Assert.That(nodeA.B, Is.EqualTo(5678));
                            Assert.That(nodeA.Header.NodeTypeHash, Is.EqualTo(TypeHash<NodeHeaderA>()));
                            Assert.That(nodeA.Header.Size, Is.EqualTo(UnsafeUtility.SizeOf<NodeHeaderA>()));
                            Assert.That(nodeA.Header.ChildrenCount, Is.EqualTo(3));
                        } else if (curChild.NodeTypeHash == TypeHash<NodeHeaderB>()) {
                            Assert.That(curChild.AsNodeHeader.Id, Is.EqualTo(NodeId_B));
                            Assert.That(curChild.NodeDotNetType, Is.EqualTo(typeof(NodeHeaderB)));

                            ref var nodeB = ref curChild.As<NodeHeaderB>();
                            Assert.That(nodeB.A, Is.EqualTo(1.1234f));
                            Assert.That(nodeB.B, Is.EqualTo(1.5678f));
                            Assert.That(nodeB.Header.NodeTypeHash, Is.EqualTo(TypeHash<NodeHeaderB>()));
                            Assert.That(nodeB.Header.Size, Is.EqualTo(UnsafeUtility.SizeOf<NodeHeaderB>()));
                            Assert.That(nodeB.Header.ChildrenCount, Is.EqualTo(2));

                            var counter = 0;
                            var nestedChild = default(DotsSerializationReader.NodeHandle);
                            while (curChild.MoveToNextChild( ref nestedChild))
                            {
                                Assert.That(nestedChild.NodeTypeHash, Is.EqualTo(TypeHash<NodeHeaderA>()));
                                Assert.That(nestedChild.NodeDotNetType, Is.EqualTo(typeof(NodeHeaderA)));

                                ref var childNode = ref nestedChild.As<NodeHeaderA>();

                                Assert.That(childNode.Header.NodeTypeHash, Is.EqualTo(TypeHash<NodeHeaderA>()));
                                Assert.That(childNode.Header.Size, Is.EqualTo(UnsafeUtility.SizeOf<NodeHeaderA>()));
                                if (counter == 0)
                                {
                                    Assert.That(childNode.Header.Id, Is.EqualTo(NodeId_BA1));
                                    Assert.That(childNode.A, Is.EqualTo(1));
                                    Assert.That(childNode.B, Is.EqualTo(2));
                                    Assert.That(childNode.Header.ChildrenCount, Is.EqualTo(0));
                                }
                                else
                                {
                                    Assert.That(childNode.Header.Id, Is.EqualTo(NodeId_BA2));
                                    Assert.That(childNode.A, Is.EqualTo(3));
                                    Assert.That(childNode.B, Is.EqualTo(4));
                                    Assert.That(childNode.Header.ChildrenCount, Is.EqualTo(1));
                                }

                                ++counter;
                            }
                        }
                    }
                }
            }
            finally
            {
                File.Delete(pfn);
            }
        }

        [Test]
        public void CreateWriteReadFromFind()
        {
            var pfn = FileUtil.GetUniqueTempPathInProject();

            try
            {
                CreateSimpleFile(pfn);

                using (var reader = new StreamBinaryReader(pfn))
                using (var dsf = DotsSerialization.CreateReader(reader))
                {
                    // Find Node A from type
                    var nha = dsf.RootNode.FindNode<NodeHeaderA>();
                    Assert.That(nha.IsValid, Is.True);
                    Assert.That(nha.NodeDotNetType, Is.EqualTo(typeof(NodeHeaderA)));

                    ref var nodeA = ref nha.As<NodeHeaderA>();
                    Assert.That(nodeA.A, Is.EqualTo(1234));
                    Assert.That(nodeA.B, Is.EqualTo(5678));

                    // Find Node AC1
                    var nhc = nha.FindNode(NodeId_AC1);
                    Assert.That(nhc.IsValid, Is.True);
                    ref var nodeC1 = ref nhc.As<NodeHeaderC>();
                    Assert.That(nodeC1.Header.Id, Is.EqualTo(NodeId_AC1));

                    // Find Node AC2
                    nhc = nha.FindNode(NodeId_AC2);
                    Assert.That(nhc.IsValid, Is.True);
                    ref var nodeC2 = ref nhc.As<NodeHeaderC>();
                    Assert.That(nodeC2.Header.Id, Is.EqualTo(NodeId_AC2));

                    // Find Node AC3
                    nhc = nha.FindNode(NodeId_AC3);
                    Assert.That(nhc.IsValid, Is.True);
                    ref var nodeC3 = ref nhc.As<NodeHeaderC>();
                    Assert.That(nodeC3.Header.Id, Is.EqualTo(NodeId_AC3));

                    // Attempt to find BA2CD but can't because there's not instance inside of A
                    var nhd = nha.FindNode(NodeId_BA2CD);
                    Assert.That(nhd.IsValid, Is.False);

                    // Attempt to find BA2CD but can't because of nested level too short
                    nhd = dsf.RootNode.FindNode(NodeId_BA2CD, 3);
                    Assert.That(nhd.IsValid, Is.False);

                    nhd = dsf.RootNode.FindNode(NodeId_BA2CD, 4);
                    Assert.That(nhd.IsValid, Is.True);
                }
            }
            finally
            {
                File.Delete(pfn);
            }
        }

        [Test]
        public void CreateWriteReadNodeStructureWithMetadata()
        {
            var pfn = FileUtil.GetUniqueTempPathInProject();

            try
            {
                using (var writer = new StreamBinaryWriter(pfn))
                using (var dsf1 = DotsSerialization.CreateWriter(writer, Hash128Helpers.New(), "pipo"))
                {
                    using (var a = dsf1.CreateNode<NodeHeaderA>(NodeId_A))
                    {
                        a.NodeHeader.A = 1234;
                        a.NodeHeader.B = 5678;

                        a.SetMetadata(BlobTestA.Build(1, 2, 1, 2, 3));

                        var id = NodeId_AC1;
                        for (int i = 0; i < 3; i++)
                        {
                            id.Value.z = (uint)(i + 1);
                            using (var ba2c = dsf1.CreateNode<NodeHeaderC>(id))
                            {
                                ba2c.NodeHeader.A = new uint4((uint)(1+i*10), 2, 3, 4);
                                ba2c.NodeHeader.B = new uint2((uint)(2+i*10), 2);
                                ba2c.SetMetadata(BlobTestA.Build(i, 2, i, i, i));
                            }
                        }
                    }
                }

                using (var reader = new StreamBinaryReader(pfn))
                using (var dsf = DotsSerialization.CreateReader(reader))
                {
                    // Find Node A from type
                    var nha = dsf.RootNode.FindNode<NodeHeaderA>();
                    {
                        Assert.That(nha.IsValid, Is.True);
                        Assert.That(nha.NodeDotNetType, Is.EqualTo(typeof(NodeHeaderA)));

                        Assert.That(nha.HasMetadata, Is.True);
                        ref var metadata = ref nha.GetMetadata<BlobTestA>().Value;
                        Assert.That(metadata.A, Is.EqualTo(1));
                        Assert.That(metadata.B, Is.EqualTo(2));
                        Assert.That(metadata.Values[0], Is.EqualTo(1));
                        Assert.That(metadata.Values[1], Is.EqualTo(2));
                        Assert.That(metadata.Values[2], Is.EqualTo(3));

                        ref var nodeA = ref nha.As<NodeHeaderA>();
                        Assert.That(nodeA.A, Is.EqualTo(1234));
                        Assert.That(nodeA.B, Is.EqualTo(5678));
                    }

                    // Find Node AC1
                    var id = NodeId_AC1;

                    for (int i = 0; i < 3; i++)
                    {
                        id.Value.z = (uint)(i + 1);

                        var nhc = nha.FindNode(id);
                        Assert.That(nhc.IsValid, Is.True);
                        ref var nodeC1 = ref nhc.As<NodeHeaderC>();
                        Assert.That(nodeC1.Header.Id, Is.EqualTo(id));

                        Assert.That(nha.HasMetadata, Is.True);
                        ref var metadata = ref nhc.GetMetadata<BlobTestA>().Value;
                        Assert.That(metadata.A, Is.EqualTo(i));
                        Assert.That(metadata.B, Is.EqualTo(2));
                        Assert.That(metadata.Values[0], Is.EqualTo(i));
                        Assert.That(metadata.Values[1], Is.EqualTo(i));
                        Assert.That(metadata.Values[2], Is.EqualTo(i));
                    }
                }
            }
            finally
            {
                File.Delete(pfn);
            }
        }

        [Test]
        public unsafe void CreateWriteReadNodeStructureWithData()
        {
            var pfn = FileUtil.GetUniqueTempPathInProject();

            const int nodeADataSize = 1024*1024;
            try
            {
                using (var writer = new StreamBinaryWriter(pfn))
                using (var dsf1 = DotsSerialization.CreateWriter(writer, Hash128Helpers.New(), "pipo"))
                {
                    using (var a = dsf1.CreateNode<NodeHeaderA>(NodeId_A))
                    {
                        a.NodeHeader.A = 1234;
                        a.NodeHeader.B = 5678;

                        var data = new NativeArray<int>(nodeADataSize, Allocator.Temp);
                        for (int i = 0; i < nodeADataSize; i++)
                        {
                            data[i] = i;
                        }
                        a.WriteData(data.GetUnsafePtr(), nodeADataSize * sizeof(int));

                        var id = NodeId_AC1;
                        for (int i = 0; i < 3; i++)
                        {
                            id.Value.z = (uint)(i + 1);
                            using (var ba2c = dsf1.CreateNode<NodeHeaderC>(id))
                            {
                                ba2c.NodeHeader.A = new uint4((uint)(1+i*10), 2, 3, 4);
                                ba2c.NodeHeader.B = new uint2((uint)(2+i*10), 2);

                                // Issue two writes of the same data
                                ba2c.WriteData(data.GetUnsafePtr(), 4*4);
                                ba2c.WriteData(data.GetUnsafePtr(), 4*4);
                            }
                        }

                        data.Dispose();
                    }
                }

                using (var reader = new StreamBinaryReader(pfn))
                using (var dsf = DotsSerialization.CreateReader(reader))
                {
                    // Find Node A from type
                    var nha = dsf.RootNode.FindNode<NodeHeaderA>();
                    {
                        Assert.That(nha.IsValid, Is.True);
                        Assert.That(nha.NodeDotNetType, Is.EqualTo(typeof(NodeHeaderA)));

                        // Check data
                        var dataSize = nha.DataSize;
                        Assert.That(dataSize, Is.EqualTo(nodeADataSize * sizeof(int)));

                        var data = new NativeArray<int>((int)dataSize/4, Allocator.Temp);
                        nha.ReadData((byte*)data.GetUnsafePtr());

                        for (int i = 0; i < data.Length; i++)
                        {
                            Assert.That(data[i], Is.EqualTo(i));
                        }

                        data.Dispose();

                        // Check node
                        ref var nodeA = ref nha.As<NodeHeaderA>();
                        Assert.That(nodeA.A, Is.EqualTo(1234));
                        Assert.That(nodeA.B, Is.EqualTo(5678));
                    }

                    // Find Node AC1
                    var id = NodeId_AC1;

                    for (int i = 0; i < 3; i++)
                    {
                        var nhc = nha.FindNode(id);
                        Assert.That(nhc.IsValid, Is.True);
                        ref var nodeC1 = ref nhc.As<NodeHeaderC>();
                        Assert.That(nodeC1.Header.Id, Is.EqualTo(id));

                        // Check data
                        var dataSize = nhc.DataSize;
                        Assert.That(dataSize, Is.EqualTo(8*4));

                        var data = new NativeArray<int>(8, Allocator.Temp);
                        nhc.ReadData((byte*)data.GetUnsafePtr());

                        for (int j = 0; j < 4; j++)
                        {
                            Assert.That(data[j], Is.EqualTo(j));
                            Assert.That(data[j+4], Is.EqualTo(j));
                        }

                        data.Dispose();

                        id.Value.z = (uint)(i + 1);
                    }
                }
            }
            finally
            {
                File.Delete(pfn);
            }
        }


        [Test]
        public unsafe void CreateWriteReadNodeStructureWithDataInterleaved()
        {
            var pfn = FileUtil.GetUniqueTempPathInProject();

            const int nodeADataSize = 1024*1024;
            try
            {
                using (var writer = new StreamBinaryWriter(pfn))
                using (var dsf1 = DotsSerialization.CreateWriter(writer, Hash128Helpers.New(), "pipo"))
                {
                    using (var a = dsf1.CreateNode<NodeHeaderA>(NodeId_A))
                    {
                        a.NodeHeader.A = 1234;
                        a.NodeHeader.B = 5678;

                        var data = new NativeArray<int>(nodeADataSize, Allocator.Temp);
                        for (int i = 0; i < nodeADataSize; i++)
                        {
                            data[i] = i;
                        }
                        a.WriteData(data.GetUnsafePtr(), nodeADataSize*4);

                        var id = NodeId_AC1;
                        for (int i = 0; i < 3; i++)
                        {
                            id.Value.z = (uint)(i + 1);
                            using (var ba2c = dsf1.CreateNode<NodeHeaderC>(id))
                            {
                                ba2c.NodeHeader.A = new uint4((uint)(1+i*10), 2, 3, 4);
                                ba2c.NodeHeader.B = new uint2((uint)(2+i*10), 2);

                                // Issue two writes of the same data
                                ba2c.WriteData(data.GetUnsafePtr(), 4*4);
                                ba2c.WriteData(data.GetUnsafePtr(), 4*4);
                            }
                        }

                        // Writing before and after should cause an exception
                        Assert.Throws<InvalidOperationException>(() => a.WriteData(data.GetUnsafePtr(), nodeADataSize * 4));

                        data.Dispose();
                    }
                }
            }
            finally
            {
                File.Delete(pfn);
            }
        }

        [Test]
        public void CreateFileWithDeferredNode()
        {
            var pfn = FileUtil.GetUniqueTempPathInProject();

            try
            {
                // |- NodeA
                //    |- NodeAC1 (Deferred)
                //    |- NodeAC2
                using (var writer = new StreamBinaryWriter(pfn))
                using (var dsf = DotsSerialization.CreateWriter(writer, Hash128Helpers.New(), "pipo"))
                {
                    using (var a = dsf.CreateNode<NodeHeaderA>(NodeId_A))
                    {
                        // Here the dispose is NOT used to pop-up from the hierarchy tree, but to actually write the String Table to RawData
                        // The StringTable is designed to be a leaf node.
                        using (var st = dsf.CreateStringTableNode(NodeId_AC1))
                        {
                            st.WriteString("Pipo");
                            st.WriteString("Bimbo");

                            a.NodeHeader.A = 1234;
                            a.NodeHeader.B = 5678;

                            // Will be children of A
                            using (var ba2c = dsf.CreateNode<NodeHeaderC>(NodeId_AC2))
                            {
                                ba2c.NodeHeader.A = new uint4(1, 2, 3, 4);
                                ba2c.NodeHeader.B = new uint2((uint)st.WriteString("ba2cst1"), (uint)st.WriteString("ba2cst2"));
                            }
                        }
                    }
                }

                using (var reader = new StreamBinaryReader(pfn))
                using (var dsf = DotsSerialization.CreateReader(reader))
                {
                    // Find Node AC1
                    var nhac1 = dsf.RootNode.FindNode(NodeId_AC1);
                    Assert.That(nhac1.IsValid, Is.True);

                    // GetString can only be called during the lifetime of st, once the object is disposed the StringTable buffer will be freed
                    using (var st = dsf.OpenStringTableNode(nhac1))
                    {
                        var nhac2 = dsf.RootNode.FindNode(NodeId_AC2);
                        ref var nodeAC2 = ref nhac2.As<NodeHeaderC>();

                        var st1 = st.GetString((int)nodeAC2.B.x);
                        Assert.That(st1, Is.EqualTo("ba2cst1"));

                        var st2 = st.GetString((int)nodeAC2.B.y);
                        Assert.That(st2, Is.EqualTo("ba2cst2"));
                    }
                }
            }
            finally
            {
                File.Delete(pfn);
            }
        }

        private static readonly Hash128 NodeId_A     = new Hash128(0, 0, 0, 1);
        private static readonly Hash128 NodeId_AC1   = new Hash128(0, 0, 1, 1);
        private static readonly Hash128 NodeId_AC2   = new Hash128(0, 0, 2, 1);
        private static readonly Hash128 NodeId_AC3   = new Hash128(0, 0, 3, 1);
        private static readonly Hash128 NodeId_B     = new Hash128(0, 0, 0, 2);
        private static readonly Hash128 NodeId_BA1   = new Hash128(0, 0, 1, 2);
        private static readonly Hash128 NodeId_BA2   = new Hash128(0, 0, 2, 2);
        private static readonly Hash128 NodeId_BA2C  = new Hash128(0, 1, 2, 1);
        private static readonly Hash128 NodeId_BA2CD = new Hash128(1, 1, 2, 1);

        private static void CreateSimpleFile(string pfn)
        {
            // |- NodeA
            //    |- NodeAC1
            //    |- NodeAC2
            //    |- NodeAC3
            // |- NodeB
            //    |- NodeBA1
            //    |- NodeBA2
            //       |- NodeBA2C
            //         |- NodeBA2CD
            using (var writer = new StreamBinaryWriter(pfn))
            using (var dsf = DotsSerialization.CreateWriter(writer, Hash128Helpers.New(), "pipo"))
            {
                using (var a = dsf.CreateNode<NodeHeaderA>(NodeId_A))
                {
                    a.NodeHeader.A = 1234;
                    a.NodeHeader.B = 5678;

                    var id = NodeId_AC1;
                    for (int i = 0; i < 3; i++)
                    {
                        id.Value.z = (uint)(i + 1);
                        using (var ba2c = dsf.CreateNode<NodeHeaderC>(id))
                        {
                            ba2c.NodeHeader.A = new uint4((uint)(1+i*10), 2, 3, 4);
                            ba2c.NodeHeader.B = new uint2((uint)(2+i*10), 2);
                        }
                    }
                }

                using (var b = dsf.CreateNode<NodeHeaderB>(NodeId_B))
                {
                    b.NodeHeader.A = 1.1234f;
                    b.NodeHeader.B = 1.5678f;

                    using (var ba1 = dsf.CreateNode<NodeHeaderA>(NodeId_BA1))
                    {
                        ba1.NodeHeader.A = 1;
                        ba1.NodeHeader.B = 2;
                    }

                    using (var ba2 = dsf.CreateNode<NodeHeaderA>(NodeId_BA2))
                    {
                        ba2.NodeHeader.A = 3;
                        ba2.NodeHeader.B = 4;

                        using (var ba2c = dsf.CreateNode<NodeHeaderC>(NodeId_BA2C))
                        {
                            ba2c.NodeHeader.A = new uint4(1, 2, 3, 4);
                            ba2c.NodeHeader.B = new uint2(10, 20);

                            using (var ba2cd = dsf.CreateNode<NodeHeaderD>(NodeId_BA2CD))
                            {
                                ba2cd.NodeHeader.A = new uint2(1000, 2000);
                                ba2cd.NodeHeader.B = new uint2(2000, 4000);
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public unsafe void PagedAllocation_AllocationTest()
        {
            var pa = new PagedAllocation(Allocator.Persistent, 1024);
            try
            {
                // Check initial state
                Assert.That(pa.IsDisposed, Is.EqualTo(false));
                Assert.That(pa.Allocator, Is.EqualTo(Allocator.Persistent));
                Assert.That(pa.CurrentGlobalOffset, Is.EqualTo(0));
                Assert.That(pa.PageSize, Is.EqualTo(1024));
                Assert.That(pa.Pages.Length, Is.EqualTo(1));

                // Reserve in first page
                var b = pa.Reserve(10);
                Assert.That((IntPtr)b, Is.EqualTo((IntPtr)pa.Pages[0].Buffer));
                Assert.That(10, Is.EqualTo(pa.Pages[0].FreeOffset));
                Assert.That(10, Is.EqualTo(pa.CurrentGlobalOffset));

                // Can't fit in first page, has to be in a second one
                b = pa.Reserve(1020);
                Assert.That(2, Is.EqualTo(pa.Pages.Length));
                Assert.That((IntPtr)b, Is.EqualTo((IntPtr)pa.Pages[1].Buffer));
                Assert.That(10, Is.EqualTo(pa.Pages[0].FreeOffset));
                Assert.That(1020, Is.EqualTo(pa.Pages[1].FreeOffset));
                Assert.That(1030, Is.EqualTo(pa.CurrentGlobalOffset));

                // Fit in second page
                b = pa.Reserve(4);
                Assert.That(2, Is.EqualTo(pa.Pages.Length));
                Assert.That((IntPtr)b, Is.EqualTo((IntPtr)(pa.Pages[1].Buffer + 1020)));
                Assert.That(10, Is.EqualTo(pa.Pages[0].FreeOffset));
                Assert.That(1024, Is.EqualTo(pa.Pages[1].FreeOffset));
                Assert.That(1034, Is.EqualTo(pa.CurrentGlobalOffset));

                // Fit in third page
                b = pa.Reserve(20);
                Assert.That(3, Is.EqualTo(pa.Pages.Length));
                Assert.That((IntPtr)b, Is.EqualTo((IntPtr)pa.Pages[2].Buffer));
                Assert.That(10, Is.EqualTo(pa.Pages[0].FreeOffset));
                Assert.That(1024, Is.EqualTo(pa.Pages[1].FreeOffset));
                Assert.That(20, Is.EqualTo(pa.Pages[2].FreeOffset));
                Assert.That(1054, Is.EqualTo(pa.CurrentGlobalOffset));

                // Dedicated fourth because reserved size is bigger than page's one
                b = pa.Reserve(2000);
                Assert.That(4, Is.EqualTo(pa.Pages.Length));
                Assert.That((IntPtr)b, Is.EqualTo((IntPtr)pa.Pages[3].Buffer));
                Assert.That(10, Is.EqualTo(pa.Pages[0].FreeOffset));
                Assert.That(1024, Is.EqualTo(pa.Pages[1].FreeOffset));
                Assert.That(20, Is.EqualTo(pa.Pages[2].FreeOffset));
                Assert.That(2000, Is.EqualTo(pa.Pages[3].FreeOffset));
                Assert.That(3054, Is.EqualTo(pa.CurrentGlobalOffset));

                // Fifth, normal alloc
                b = pa.Reserve(100);
                Assert.That(5, Is.EqualTo(pa.Pages.Length));
                Assert.That((IntPtr)b, Is.EqualTo((IntPtr)pa.Pages[4].Buffer));
                Assert.That(10, Is.EqualTo(pa.Pages[0].FreeOffset));
                Assert.That(1024, Is.EqualTo(pa.Pages[1].FreeOffset));
                Assert.That(20, Is.EqualTo(pa.Pages[2].FreeOffset));
                Assert.That(2000, Is.EqualTo(pa.Pages[3].FreeOffset));
                Assert.That(100, Is.EqualTo(pa.Pages[4].FreeOffset));
                Assert.That(3154, Is.EqualTo(pa.CurrentGlobalOffset));

                // Fifth, normal alloc
                b = pa.Reserve(200);
                Assert.That(5, Is.EqualTo(pa.Pages.Length));
                Assert.That((IntPtr)b, Is.EqualTo((IntPtr)(pa.Pages[4].Buffer + 100)));
                Assert.That(10, Is.EqualTo(pa.Pages[0].FreeOffset));
                Assert.That(1024, Is.EqualTo(pa.Pages[1].FreeOffset));
                Assert.That(20, Is.EqualTo(pa.Pages[2].FreeOffset));
                Assert.That(2000, Is.EqualTo(pa.Pages[3].FreeOffset));
                Assert.That(300, Is.EqualTo(pa.Pages[4].FreeOffset));
                Assert.That(3354, Is.EqualTo(pa.CurrentGlobalOffset));
            }
            finally
            {
                // Dispose and check cleaned state
                pa.Dispose();
                Assert.That(pa.Pages.IsCreated, Is.EqualTo(false));
                Assert.That(pa.Pages.Length, Is.EqualTo(0));
                Assert.That(pa.IsDisposed, Is.EqualTo(true));
            }
        }
    }
#endif

    class SerializeTests : ECSTestsFixture
    {
        public struct TestComponentData1 : IComponentData
        {
            public int value;
            public Entity referencedEntity;
        }

        public struct TestComponentData2 : IComponentData
        {
            public int value;
            public Entity referencedEntity;
        }

        [InternalBufferCapacity(16)]
        public struct TestBufferElement : IBufferElementData
        {
            public Entity entity;
            public int value;
        }

        public struct EcsTestSharedCompBlobAssetRef : ISharedComponentData
        {
            public BlobAssetReference<int> value;
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        public class EcsTestManagedDataBlobAssetRef : IComponentData
        {
            public BlobAssetReference<int> value;
        }
#endif

        [Test]
        public void SerializeIntoExistingWorldThrows()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));

            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);

            var reader = new TestBinaryReader(writer);

            Assert.Throws<ArgumentException>(() =>
                SerializeUtility.DeserializeWorld(m_Manager.BeginExclusiveEntityTransaction(), reader)
            );
        }

#if UNITY_EDITOR
        [Test]
        public void TestBigEntityCountDeserialization()
        {
            var _previousWorld = World.DefaultGameObjectInjectionWorld;

            var _bigEntitiesFilePFN = FileUtil.GetUniqueTempPathInProject();

            var entityCount = 4_000_000;
            var world = World.DefaultGameObjectInjectionWorld = new World("Big Entity Count World");
            var manager1 = world.EntityManager;

            var targetArchetype = manager1.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3), typeof(EcsTestData4), typeof(EcsTestData5));
            var entities = manager1.CreateEntity(targetArchetype, entityCount, World.UpdateAllocator.ToAllocator);

            for (int i = 0; i < entityCount; i++)
            {
                manager1.SetComponentData(entities[i], new EcsTestData(i));
                manager1.SetComponentData(entities[i], new EcsTestData2(i));
                manager1.SetComponentData(entities[i], new EcsTestData3(i));
                manager1.SetComponentData(entities[i], new EcsTestData4(i));
                manager1.SetComponentData(entities[i], new EcsTestData5(i));
            }

            long _bigEntitiesFileSize;
            using (var writer = new StreamBinaryWriter(_bigEntitiesFilePFN))
            {
                SerializeUtility.SerializeWorld(manager1, writer);
                _bigEntitiesFileSize = new FileInfo(_bigEntitiesFilePFN).Length;
            }

            world.Dispose();

            // NOTE: uncomment to enable profiling
            // Profiler.logFile = @"D:\LoadScene";
            // Profiler.enableAllocationCallstacks = false;
            // Profiler.enableBinaryLog = true;
            // Profiler.maxUsedMemory = 256 * 1024 * 1024;
            // Profiler.enabled = true;

            var sw = new Stopwatch();
            TimeSpan _bigEntitiesFileReadTime = default;
            for (int i = 0; i < 3; i++)
            {
                sw.Start();

                Profiler.BeginSample("Read From Disk");

                File.ReadAllBytes(_bigEntitiesFilePFN);

                Profiler.EndSample();

                sw.Stop();
                _bigEntitiesFileReadTime = sw.Elapsed;


                using (var reader = new StreamBinaryReader(_bigEntitiesFilePFN))
                using (var deserializedWorld = new World("Big Entity Count World Load"))
                {
                    var manager = deserializedWorld.EntityManager;

                    sw = new Stopwatch();
                    sw.Start();

                    Profiler.BeginSample("DOTS File Deserialization");

                    SerializeUtility.DeserializeWorld(manager.BeginExclusiveEntityTransaction(), reader);

                    Profiler.EndSample();

                    sw.Stop();
                    manager.EndExclusiveEntityTransaction();
                }
            }

            // Profiler.enableBinaryLog = false;
            // Profiler.enabled = false;
            // Profiler.logFile = "";

            var t = sw.Elapsed;

            var r = _bigEntitiesFileReadTime.TotalMilliseconds / t.TotalMilliseconds;
            var sizemb = (double)_bigEntitiesFileSize / (1024 * 1024);
            Debug.Log($"File Read Time {_bigEntitiesFileReadTime.TotalMilliseconds:N2}ms, EntityScene Load Time {t.TotalMilliseconds:N2}ms\r\nFile Size {sizemb:N2}MB, Ratio {r:N2}");

            File.Delete(_bigEntitiesFilePFN);

            World.DefaultGameObjectInjectionWorld = _previousWorld;
        }
#endif

        [Test]
        public unsafe void SerializeEntities()
        {
            var dummyEntity = CreateEntityWithDefaultData(0); //To ensure entity indices are offset
            var e1 = CreateEntityWithDefaultData(1);
            var e2 = CreateEntityWithDefaultData(2);
            var e3 = CreateEntityWithDefaultData(3);
            m_Manager.AddComponentData(e1, new TestComponentData1 { value = 10, referencedEntity = e2 });
            m_Manager.AddComponentData(e2, new TestComponentData2 { value = 20, referencedEntity = e1 });
            m_Manager.AddComponentData(e3, new TestComponentData1 { value = 30, referencedEntity = Entity.Null });
            m_Manager.AddComponentData(e3, new TestComponentData2 { value = 40, referencedEntity = Entity.Null });
            m_Manager.AddBuffer<EcsIntElement>(e1);
            m_Manager.RemoveComponent<EcsTestData2>(e3);
            m_Manager.AddBuffer<EcsIntElement>(e3);

            m_Manager.GetBuffer<EcsIntElement>(e1).CopyFrom(new EcsIntElement[] { 1, 2, 3 }); // no overflow
            m_Manager.GetBuffer<EcsIntElement>(e3).CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }); // overflow into heap

            var e4 = m_Manager.CreateEntity();
            m_Manager.AddBuffer<EcsComplexEntityRefElement>(e4);
            var ebuf = m_Manager.GetBuffer<EcsComplexEntityRefElement>(e4);
            ebuf.Add(new EcsComplexEntityRefElement { Entity = e1, Dummy = 1 });
            ebuf.Add(new EcsComplexEntityRefElement { Entity = e2, Dummy = 2 });
            ebuf.Add(new EcsComplexEntityRefElement { Entity = e3, Dummy = 3 });

            m_Manager.DestroyEntity(dummyEntity);
            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);

            SerializeUtility.SerializeWorld(m_Manager, writer);
            var reader = new TestBinaryReader(writer);

            var deserializedWorld = new World("SerializeEntities Test World 3");
            var entityManager = deserializedWorld.EntityManager;

            SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
            entityManager.EndExclusiveEntityTransaction();

            try
            {
                var allEntities = entityManager.GetAllEntities(Allocator.Temp);
                var count = allEntities.Length;
                allEntities.Dispose();

                Assert.AreEqual(4, count);

                var group1 = entityManager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2),
                    typeof(TestComponentData1));
                var group2 = entityManager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2),
                    typeof(TestComponentData2));
                var group3 = entityManager.CreateEntityQuery(typeof(EcsTestData),
                    typeof(TestComponentData1), typeof(TestComponentData2));
                var group4 = entityManager.CreateEntityQuery(typeof(EcsComplexEntityRefElement));

                Assert.AreEqual(1, group1.CalculateEntityCount());
                Assert.AreEqual(1, group2.CalculateEntityCount());
                Assert.AreEqual(1, group3.CalculateEntityCount());
                Assert.AreEqual(1, group4.CalculateEntityCount());

                var everythingGroup = entityManager.CreateEntityQuery(Array.Empty<ComponentType>());
                var chunks = everythingGroup.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(4, chunks.Length);
                everythingGroup.Dispose();

                var entityType = entityManager.GetEntityTypeHandle();
                Assert.AreEqual(1, chunks[0].GetNativeArray(entityType).Length);
                Assert.AreEqual(1, chunks[1].GetNativeArray(entityType).Length);
                Assert.AreEqual(1, chunks[2].GetNativeArray(entityType).Length);
                Assert.AreEqual(1, chunks[3].GetNativeArray(entityType).Length);

                var entities1 = group1.ToEntityArray(World.UpdateAllocator.ToAllocator);
                var entities2 = group2.ToEntityArray(World.UpdateAllocator.ToAllocator);
                var entities3 = group3.ToEntityArray(World.UpdateAllocator.ToAllocator);
                var entities4 = group4.ToEntityArray(World.UpdateAllocator.ToAllocator);

                var new_e1 = entities1[0];
                var new_e2 = entities2[0];
                var new_e3 = entities3[0];
                var new_e4 = entities4[0];

                Assert.AreEqual(1, entityManager.GetComponentData<EcsTestData>(new_e1).value);
                Assert.AreEqual(-1, entityManager.GetComponentData<EcsTestData2>(new_e1).value0);
                Assert.AreEqual(-1, entityManager.GetComponentData<EcsTestData2>(new_e1).value1);
                Assert.AreEqual(10, entityManager.GetComponentData<TestComponentData1>(new_e1).value);

                Assert.AreEqual(2, entityManager.GetComponentData<EcsTestData>(new_e2).value);
                Assert.AreEqual(-2, entityManager.GetComponentData<EcsTestData2>(new_e2).value0);
                Assert.AreEqual(-2, entityManager.GetComponentData<EcsTestData2>(new_e2).value1);
                Assert.AreEqual(20, entityManager.GetComponentData<TestComponentData2>(new_e2).value);

                Assert.AreEqual(3, entityManager.GetComponentData<EcsTestData>(new_e3).value);
                Assert.AreEqual(30, entityManager.GetComponentData<TestComponentData1>(new_e3).value);
                Assert.AreEqual(40, entityManager.GetComponentData<TestComponentData2>(new_e3).value);

                Assert.IsTrue(entityManager.Exists(entityManager.GetComponentData<TestComponentData1>(new_e1).referencedEntity));
                Assert.IsTrue(entityManager.Exists(entityManager.GetComponentData<TestComponentData2>(new_e2).referencedEntity));
                Assert.AreEqual(new_e2 , entityManager.GetComponentData<TestComponentData1>(new_e1).referencedEntity);
                Assert.AreEqual(new_e1 , entityManager.GetComponentData<TestComponentData2>(new_e2).referencedEntity);

                var buf1 = entityManager.GetBuffer<EcsIntElement>(new_e1);
                Assert.AreEqual(3, buf1.Length);
                Assert.AreNotEqual((UIntPtr)m_Manager.GetBuffer<EcsIntElement>(e1).GetUnsafeReadOnlyPtr(), (UIntPtr)buf1.GetUnsafeReadOnlyPtr());

                for (int i = 0; i < 3; ++i)
                {
                    Assert.AreEqual(i + 1, buf1[i].Value);
                }

                var buf3 = entityManager.GetBuffer<EcsIntElement>(new_e3);
                Assert.AreEqual(10, buf3.Length);
                Assert.AreNotEqual((UIntPtr)m_Manager.GetBuffer<EcsIntElement>(e3).GetUnsafeReadOnlyPtr(), (UIntPtr)buf3.GetUnsafeReadOnlyPtr());

                for (int i = 0; i < 10; ++i)
                {
                    Assert.AreEqual(i + 1, buf3[i].Value);
                }

                var buf4 = entityManager.GetBuffer<EcsComplexEntityRefElement>(new_e4);
                Assert.AreEqual(3, buf4.Length);

                Assert.AreEqual(1, buf4[0].Dummy);
                Assert.AreEqual(new_e1, buf4[0].Entity);

                Assert.AreEqual(2, buf4[1].Dummy);
                Assert.AreEqual(new_e2, buf4[1].Entity);

                Assert.AreEqual(3, buf4[2].Dummy);
                Assert.AreEqual(new_e3, buf4[2].Entity);

                entityManager.Debug.CheckInternalConsistency();
                m_Manager.Debug.CheckInternalConsistency();
            }
            finally
            {
                deserializedWorld.Dispose();
            }
        }

/*

// There is no simple way to deserialize with an unknown type, so this test needs to be manually executed, twice:
//  - Once to save a file, comment everything between the LOAD sections and uncomment the SAVE section.
//  - Once to load the file, reverse the commented load and save sections.
// It's not pretty, but

// [LOAD
        public struct TestUnknownComponent : IComponentData
        {
            public int Value;
        }
// LOAD]

        [Test]
        public void DeserializeWithUnknownComponentType()
        {
            const string tempFileName = "DesWithUnknownFile.tmp";
            var pfn = $"{Path.GetTempPath()}{tempFileName}";

// [LOAD
            try
            {
                using (var reader = new StreamBinaryReader(pfn))
                {
                    var deserializedWorld = new World("Test World");
                    var entityManager = deserializedWorld.EntityManager;

                    Assert.Throws<ArgumentException>(() =>
                    {
                        SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
                        entityManager.EndExclusiveEntityTransaction();
                    });
                }
            }
            finally
            {
                File.Delete(pfn);
            }
// LOAD]
// [SAVE
            var e1 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e1, new TestUnknownComponent { Value = 7 });

            using (var writer = new StreamBinaryWriter(pfn))
            {
                SerializeUtility.SerializeWorld(m_Manager, writer);
            }
// SAVE]
        }
*/

        //测试

        public struct 测试 : IComponentData
        {
            public int value;
        }

        [Test]
        public void SerializeEntitiesSupportsNonASCIIComponentTypeNames()
        {
            var e1 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e1, new 测试 { value = 7 });

            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);
            var reader = new TestBinaryReader(writer);

            var deserializedWorld = new World("SerializeEntitiesSupportsNonASCIIComponentTypeNames Test World");
            var entityManager = deserializedWorld.EntityManager;

            SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
            entityManager.EndExclusiveEntityTransaction();

            try
            {
                var allEntities = entityManager.GetAllEntities(Allocator.Temp);
                var count = allEntities.Length;
                allEntities.Dispose();

                Assert.AreEqual(1, count);

                var group1 = entityManager.CreateEntityQuery(typeof(测试));

                Assert.AreEqual(1, group1.CalculateEntityCount());

                var entities = group1.ToEntityArray(World.UpdateAllocator.ToAllocator);
                var new_e1 = entities[0];

                Assert.AreEqual(7, entityManager.GetComponentData<测试>(new_e1).value);
            }
            finally
            {
                deserializedWorld.Dispose();
            }
        }

        [Test]
        public unsafe void SerializeEntitiesRemapsEntitiesInBuffers()
        {
            var dummyEntity = CreateEntityWithDefaultData(0); //To ensure entity indices are offset

            var e1 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e1, new EcsTestData(1));
            var e2 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e2, new EcsTestData2(2));

            m_Manager.AddBuffer<TestBufferElement>(e1);
            var buffer1 = m_Manager.GetBuffer<TestBufferElement>(e1);
            for (int i = 0; i < 1024; ++i)
                buffer1.Add(new TestBufferElement {entity = e2, value = 2});

            m_Manager.AddBuffer<TestBufferElement>(e2);
            var buffer2 = m_Manager.GetBuffer<TestBufferElement>(e2);
            for (int i = 0; i < 8; ++i)
                buffer2.Add(new TestBufferElement {entity = e1, value = 1});

            m_Manager.DestroyEntity(dummyEntity);
            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);

            SerializeUtility.SerializeWorld(m_Manager, writer);
            var reader = new TestBinaryReader(writer);

            var deserializedWorld = new World("SerializeEntities Test World 3");
            var entityManager = deserializedWorld.EntityManager;

            SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
            entityManager.EndExclusiveEntityTransaction();

            try
            {
                var group1 = entityManager.CreateEntityQuery(typeof(EcsTestData), typeof(TestBufferElement));
                var group2 = entityManager.CreateEntityQuery(typeof(EcsTestData2), typeof(TestBufferElement));

                Assert.AreEqual(1, group1.CalculateEntityCount());
                Assert.AreEqual(1, group2.CalculateEntityCount());

                var entities1 = group1.ToEntityArray(World.UpdateAllocator.ToAllocator);
                var entities2 = group2.ToEntityArray(World.UpdateAllocator.ToAllocator);

                var new_e1 = entities1[0];
                var new_e2 = entities2[0];

                var newBuffer1 = entityManager.GetBuffer<TestBufferElement>(new_e1);
                Assert.AreEqual(1024, newBuffer1.Length);
                for (int i = 0; i < 1024; ++i)
                {
                    Assert.AreEqual(new_e2, newBuffer1[i].entity);
                    Assert.AreEqual(2, newBuffer1[i].value);
                }

                var newBuffer2 = entityManager.GetBuffer<TestBufferElement>(new_e2);
                Assert.AreEqual(8, newBuffer2.Length);
                for (int i = 0; i < 8; ++i)
                {
                    Assert.AreEqual(new_e1, newBuffer2[i].entity);
                    Assert.AreEqual(1, newBuffer2[i].value);
                }
            }
            finally
            {
                deserializedWorld.Dispose();
            }
        }

        [Test]
        public unsafe void SerializeEntitiesWorksWithChunkComponents()
        {
            var dummyEntity = CreateEntityWithDefaultData(0); //To ensure entity indices are offset

            var e1 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e1, new EcsTestData(1));
            m_Manager.AddChunkComponentData<EcsTestData3>(e1);
            m_Manager.SetChunkComponentData(m_Manager.GetChunk(e1), new EcsTestData3(42));
            var e2 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e2, new EcsTestData2(2));
            m_Manager.AddChunkComponentData<EcsTestData3>(e2);
            m_Manager.SetChunkComponentData(m_Manager.GetChunk(e2), new EcsTestData3(57));

            m_Manager.DestroyEntity(dummyEntity);
            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);

            SerializeUtility.SerializeWorld(m_Manager, writer);
            var reader = new TestBinaryReader(writer);

            var deserializedWorld = new World("SerializeEntities Test World 3");
            var entityManager = deserializedWorld.EntityManager;

            SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
            entityManager.EndExclusiveEntityTransaction();

            try
            {
                var group1 = entityManager.CreateEntityQuery(typeof(EcsTestData));
                var group2 = entityManager.CreateEntityQuery(typeof(EcsTestData2));

                Assert.AreEqual(1, group1.CalculateEntityCount());
                Assert.AreEqual(1, group2.CalculateEntityCount());

                var entities1 = group1.ToEntityArray(World.UpdateAllocator.ToAllocator);
                var entities2 = group2.ToEntityArray(World.UpdateAllocator.ToAllocator);

                var new_e1 = entities1[0];
                var new_e2 = entities2[0];

                Assert.AreEqual(1, entityManager.GetComponentData<EcsTestData>(new_e1).value);
                Assert.AreEqual(42, entityManager.GetChunkComponentData<EcsTestData3>(new_e1).value0);

                Assert.AreEqual(2, entityManager.GetComponentData<EcsTestData2>(new_e2).value0);
                Assert.AreEqual(57, entityManager.GetChunkComponentData<EcsTestData3>(new_e2).value0);
            }
            finally
            {
                deserializedWorld.Dispose();
            }
        }

        [Test]
        public void SerializeDoesntRemapOriginalHeapBuffers()
        {
            var dummyEntity = CreateEntityWithDefaultData(0); //To ensure entity indices are offset

            var e1 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e1, new EcsTestData(1));
            var e2 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e2, new EcsTestData2(2));

            m_Manager.AddBuffer<TestBufferElement>(e1);
            var buffer1 = m_Manager.GetBuffer<TestBufferElement>(e1);
            for (int i = 0; i < 1024; ++i)
                buffer1.Add(new TestBufferElement {entity = e2, value = 2});

            m_Manager.AddBuffer<TestBufferElement>(e2);
            var buffer2 = m_Manager.GetBuffer<TestBufferElement>(e2);
            for (int i = 0; i < 8; ++i)
                buffer2.Add(new TestBufferElement {entity = e1, value = 1});

            m_Manager.DestroyEntity(dummyEntity);
            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);

            SerializeUtility.SerializeWorld(m_Manager, writer);

            buffer1 = m_Manager.GetBuffer<TestBufferElement>(e1);
            for (int i = 0; i < buffer1.Length; ++i)
            {
                Assert.AreEqual(e2, buffer1[i].entity);
                Assert.AreEqual(2, buffer1[i].value);
            }

            buffer2 = m_Manager.GetBuffer<TestBufferElement>(e2);
            for (int i = 0; i < buffer2.Length; ++i)
            {
                Assert.AreEqual(e1, buffer2[i].entity);
                Assert.AreEqual(1, buffer2[i].value);
            }
        }

        struct ExternalSharedComponentValue
        {
            public object obj;
            public int hashcode;
            public int typeIndex;
        }

        unsafe ExternalSharedComponentValue[] ExtractSharedComponentValues(int[] indices, EntityManager manager)
        {
            var values = new ExternalSharedComponentValue[indices.Length];
            ManagedComponentStore mcs = manager.GetCheckedEntityDataAccess()->ManagedComponentStore;
            for (int i = 0; i < indices.Length; ++i)
            {
                object value = mcs.GetSharedComponentDataNonDefaultBoxed(indices[i]);
                int typeIndex = TypeManager.GetTypeIndex(value.GetType());
                int hash = TypeManager.GetHashCode(value, typeIndex);
                values[i] = new ExternalSharedComponentValue {obj = value, hashcode = hash, typeIndex = typeIndex};
            }
            return values;
        }

        unsafe void InsertSharedComponentValues(ExternalSharedComponentValue[] values, EntityManager manager)
        {
            ManagedComponentStore mcs = manager.GetCheckedEntityDataAccess()->ManagedComponentStore;
            for (int i = 0; i < values.Length; ++i)
            {
                ExternalSharedComponentValue value = values[i];
                int index = mcs.InsertSharedComponentAssumeNonDefault(value.typeIndex,
                    value.hashcode, value.obj);
                Assert.AreEqual(i + 1, index);
            }
        }

        [Ignore("This test is unstable and will leak occasionally on CI causing PRs to fail. Disabled until fixed - DOTS-4206")]
        [Test]
        [DotsRuntimeFixme] // Unity.Properties support required
        public unsafe void SerializeEntitiesWorksWithBlobAssetReferences()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestData));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestSharedComp2), typeof(EcsTestData2));
            var dummyEntity = CreateEntityWithDefaultData(0); //To ensure entity indices are offset

            var builder = new BlobBuilder(Allocator.Temp);
            ref var blobArray = ref builder.ConstructRoot<BlobArray<float>>();
            var array = builder.Allocate(ref blobArray, 5);
            array[0] = 1.7f;
            array[1] = 2.6f;
            array[2] = 3.5f;
            array[3] = 4.4f;
            array[4] = 5.3f;

            var arrayComponent = new EcsTestDataBlobAssetArray { array = builder.CreateBlobAssetReference<BlobArray<float>>(Allocator.Temp) };
            builder.Dispose();

            const int entityCount = 1000;
            var entities = new NativeArray<Entity>(entityCount, Allocator.Temp);

            m_Manager.CreateEntity(archetype1, entities);
            for (int i = 0; i < entityCount; ++i)
            {
                m_Manager.AddComponentData(entities[i], arrayComponent);
                m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp(i % 4));
            }

            var intComponents = new NativeArray<EcsTestDataBlobAssetRef>(entityCount / 5, Allocator.Temp);
            for (int i = 0; i < intComponents.Length; ++i)
                intComponents[i] = new EcsTestDataBlobAssetRef { value = BlobAssetReference<int>.Create(i) };


            m_Manager.CreateEntity(archetype2, entities);
            for (int i = 0; i < entityCount; ++i)
            {
                var intComponent = intComponents[i % intComponents.Length];
                m_Manager.AddComponentData(entities[i], intComponent);
                m_Manager.SetComponentData(entities[i], new EcsTestData2(intComponent.value.Value));
                m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp2(i % 3));

                m_Manager.AddBuffer<EcsTestDataBlobAssetElement>(entities[i]);
                var buffer = m_Manager.GetBuffer<EcsTestDataBlobAssetElement>(entities[i]);
                int numBlobs = i % 100;
                buffer.EnsureCapacity(numBlobs);
                for (int j = 0; j < numBlobs; ++j)
                {
                    buffer.Add(new EcsTestDataBlobAssetElement() { blobElement = BlobAssetReference<int>.Create(j) });
                }

                m_Manager.AddBuffer<EcsTestDataBlobAssetElement2>(entities[i]);
                var buffer2 = m_Manager.GetBuffer<EcsTestDataBlobAssetElement2>(entities[i]);
                buffer2.EnsureCapacity(numBlobs);
                for (int j = 0; j < numBlobs; ++j)
                {
                    buffer2.Add(new EcsTestDataBlobAssetElement2()
                    {
                        blobElement = BlobAssetReference<int>.Create(j + 1),
                        blobElement2 = BlobAssetReference<int>.Create(j + 2)
                    });
                }
            }

            m_Manager.DestroyEntity(dummyEntity);

            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);

            SerializeUtility.SerializeWorld(m_Manager, writer);

            m_Manager.DestroyEntity(m_Manager.UniversalQuery);

            arrayComponent.array.Dispose();
            for (int i = 0; i < intComponents.Length; ++i)
                intComponents[i].value.Dispose();

            var reader = new TestBinaryReader(writer);

            var deserializedWorld = new World("SerializeEntities Test World 3");
            var entityManager = deserializedWorld.EntityManager;

            var job = new DeserializeJob {Transaction = entityManager.BeginExclusiveEntityTransaction(), Reader = reader};
            job.Schedule().Complete();
            entityManager.EndExclusiveEntityTransaction();

            entityManager.Debug.CheckInternalConsistency();

            var access = entityManager.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var mcs = access->ManagedComponentStore;

            Assert.IsTrue(mcs.AllSharedComponentReferencesAreFromChunks(ecs));

            try
            {
                var group1 = entityManager.CreateEntityQuery(typeof(EcsTestDataBlobAssetArray));
                var group2 = entityManager.CreateEntityQuery(typeof(EcsTestDataBlobAssetRef));
                var group3 = entityManager.CreateEntityQuery(typeof(EcsTestDataBlobAssetElement));
                var group4 = entityManager.CreateEntityQuery(typeof(EcsTestDataBlobAssetElement2));

                var entities1 = group1.ToEntityArray(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(entityCount, entities1.Length);
                var new_e1 = entities1[0];
                arrayComponent = entityManager.GetComponentData<EcsTestDataBlobAssetArray>(new_e1);
                var a = arrayComponent.array;
                Assert.AreEqual(1.7f, a.Value[0]);
                Assert.AreEqual(2.6f, a.Value[1]);
                Assert.AreEqual(3.5f, a.Value[2]);
                Assert.AreEqual(4.4f, a.Value[3]);
                Assert.AreEqual(5.3f, a.Value[4]);

                var entities2 = group2.ToEntityArray(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(entityCount, entities2.Length);
                for (int i = 0; i < entityCount; ++i)
                {
                    var val = entityManager.GetComponentData<EcsTestData2>(entities2[i]).value0;
                    Assert.AreEqual(val, entityManager.GetComponentData<EcsTestDataBlobAssetRef>(entities2[i]).value.Value);
                }

                var entities3 = group3.ToEntityArray(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(entityCount, entities3.Length);

                // We can't rely on the entity order matching how we filled the buffers so we instead ensure
                // that we see buffers as many buffers as there are entities and we see buffers with 1 to 'entityCount'
                // elements in them with ascending values from 0 to bufferLength-1
                NativeParallelHashMap<int, int> bufferMap = new NativeParallelHashMap<int, int>(entityCount, Allocator.Temp);
                for (int i = 0; i < entityCount; ++i)
                {
                    var buffer = entityManager.GetBuffer<EcsTestDataBlobAssetElement>(entities3[i]);

                    for (int j = 0; j < buffer.Length; ++j)
                    {
                        Assert.AreEqual(j, buffer[j].blobElement.Value);
                    }
                    if (!bufferMap.TryGetValue(buffer.Length, out var count))
                    {
                        bufferMap.TryAdd(buffer.Length, 1);
                    }
                    else
                    {
                        bufferMap[buffer.Length] = count + 1;
                    }
                }
                for (int i = 0; i < entityCount; ++i)
                {
                    Assert.IsTrue(bufferMap[i % 100] == 10);
                }
                bufferMap.Dispose();


                var entities4 = group4.ToEntityArray(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(entityCount, entities4.Length);

                NativeParallelHashMap<int, int> bufferMap2 = new NativeParallelHashMap<int, int>(entityCount, Allocator.Temp);
                for (int i = 0; i < entityCount; ++i)
                {
                    var buffer = entityManager.GetBuffer<EcsTestDataBlobAssetElement2>(entities4[i]);

                    for (int j = 0; j < buffer.Length; ++j)
                    {
                        Assert.AreEqual(j + 1, buffer[j].blobElement.Value);
                        Assert.AreEqual(j + 2, buffer[j].blobElement2.Value);
                    }
                    if (!bufferMap2.TryGetValue(buffer.Length, out var count))
                    {
                        bufferMap2.TryAdd(buffer.Length, 1);
                    }
                    else
                    {
                        bufferMap2[buffer.Length] = count + 1;
                    }
                }
                for (int i = 0; i < entityCount; ++i)
                {
                    Assert.IsTrue(bufferMap2[i % 100] == 10);
                }
                bufferMap2.Dispose();
            }
            finally
            {
                deserializedWorld.Dispose();
            }

#if !NET_DOTS // If memory has been unmapped this can throw exceptions other than InvalidOperation
            float f = 1.0f;
            Assert.Throws<InvalidOperationException>(() => f = arrayComponent.array.Value[0]);
#endif
        }

        // A test for DOTS Runtime to validate blob assets.
        [Ignore("This test is unstable and will leak occasionally on CI causing PRs to fail. Disabled until fixed - DOTS-4206")]
        [Test]
        public unsafe void SerializeEntitiesWorksWithBlobAssetReferencesNoSharedComponents()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData2));
            var dummyEntity = CreateEntityWithDefaultData(0); //To ensure entity indices are offset

            var builder = new BlobBuilder(Allocator.Temp);
            ref var blobArray = ref builder.ConstructRoot<BlobArray<float>>();
            var array = builder.Allocate(ref blobArray, 5);
            array[0] = 1.7f;
            array[1] = 2.6f;
            array[2] = 3.5f;
            array[3] = 4.4f;
            array[4] = 5.3f;

            var arrayComponent = new EcsTestDataBlobAssetArray { array = builder.CreateBlobAssetReference<BlobArray<float>>(Allocator.Temp) };
            builder.Dispose();

            const int entityCount = 1000;
            var entities = new NativeArray<Entity>(entityCount, Allocator.Temp);

            m_Manager.CreateEntity(archetype1, entities);
            for (int i = 0; i < entityCount; ++i)
            {
                m_Manager.AddComponentData(entities[i], arrayComponent);
            }

            var intComponents = new NativeArray<EcsTestDataBlobAssetRef>(entityCount / 5, Allocator.Temp);
            for (int i = 0; i < intComponents.Length; ++i)
                intComponents[i] = new EcsTestDataBlobAssetRef { value = BlobAssetReference<int>.Create(i) };


            m_Manager.CreateEntity(archetype2, entities);
            for (int i = 0; i < entityCount; ++i)
            {
                var intComponent = intComponents[i % intComponents.Length];
                m_Manager.AddComponentData(entities[i], intComponent);
                m_Manager.SetComponentData(entities[i], new EcsTestData2(intComponent.value.Value));

                m_Manager.AddBuffer<EcsTestDataBlobAssetElement>(entities[i]);
                var buffer = m_Manager.GetBuffer<EcsTestDataBlobAssetElement>(entities[i]);
                int numBlobs = i % 100;
                buffer.EnsureCapacity(numBlobs);
                for (int j = 0; j < numBlobs; ++j)
                {
                    buffer.Add(new EcsTestDataBlobAssetElement() { blobElement = BlobAssetReference<int>.Create(j) });
                }

                m_Manager.AddBuffer<EcsTestDataBlobAssetElement2>(entities[i]);
                var buffer2 = m_Manager.GetBuffer<EcsTestDataBlobAssetElement2>(entities[i]);
                buffer2.EnsureCapacity(numBlobs);
                for (int j = 0; j < numBlobs; ++j)
                {
                    buffer2.Add(new EcsTestDataBlobAssetElement2()
                    {
                        blobElement = BlobAssetReference<int>.Create(j + 1),
                        blobElement2 = BlobAssetReference<int>.Create(j + 2)
                    });
                }
            }

            m_Manager.DestroyEntity(dummyEntity);

            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);

            SerializeUtility.SerializeWorld(m_Manager, writer);

            m_Manager.DestroyEntity(m_Manager.UniversalQuery);

            arrayComponent.array.Dispose();
            for (int i = 0; i < intComponents.Length; ++i)
                intComponents[i].value.Dispose();

            var reader = new TestBinaryReader(writer);

            var deserializedWorld = new World("SerializeEntities Test World 3");
            var entityManager = deserializedWorld.EntityManager;

            var job = new DeserializeJob { Transaction = entityManager.BeginExclusiveEntityTransaction(), Reader = reader };
            job.Schedule().Complete();
            entityManager.EndExclusiveEntityTransaction();

            entityManager.Debug.CheckInternalConsistency();

            var access = entityManager.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var mcs = access->ManagedComponentStore;

            Assert.IsTrue(mcs.AllSharedComponentReferencesAreFromChunks(ecs));

            try
            {
                var group1 = entityManager.CreateEntityQuery(typeof(EcsTestDataBlobAssetArray));
                var group2 = entityManager.CreateEntityQuery(typeof(EcsTestDataBlobAssetRef));
                var group3 = entityManager.CreateEntityQuery(typeof(EcsTestDataBlobAssetElement));
                var group4 = entityManager.CreateEntityQuery(typeof(EcsTestDataBlobAssetElement2));

                var entities1 = group1.ToEntityArray(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(entityCount, entities1.Length);
                var new_e1 = entities1[0];
                arrayComponent = entityManager.GetComponentData<EcsTestDataBlobAssetArray>(new_e1);
                var a = arrayComponent.array;
                Assert.AreEqual(1.7f, a.Value[0]);
                Assert.AreEqual(2.6f, a.Value[1]);
                Assert.AreEqual(3.5f, a.Value[2]);
                Assert.AreEqual(4.4f, a.Value[3]);
                Assert.AreEqual(5.3f, a.Value[4]);

                var entities2 = group2.ToEntityArray(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(entityCount, entities2.Length);
                for (int i = 0; i < entityCount; ++i)
                {
                    var val = entityManager.GetComponentData<EcsTestData2>(entities2[i]).value0;
                    Assert.AreEqual(val, entityManager.GetComponentData<EcsTestDataBlobAssetRef>(entities2[i]).value.Value);
                }

                var entities3 = group3.ToEntityArray(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(entityCount, entities3.Length);

                // We can't rely on the entity order matching how we filled the buffers so we instead ensure
                // that we see buffers as many buffers as there are entities and we see buffers with 1 to 'entityCount'
                // elements in them with ascending values from 0 to bufferLength-1
                NativeParallelHashMap<int, int> bufferMap = new NativeParallelHashMap<int, int>(entityCount, Allocator.Temp);
                for (int i = 0; i < entityCount; ++i)
                {
                    var buffer = entityManager.GetBuffer<EcsTestDataBlobAssetElement>(entities3[i]);

                    for (int j = 0; j < buffer.Length; ++j)
                    {
                        Assert.AreEqual(j, buffer[j].blobElement.Value);
                    }
                    if (!bufferMap.TryGetValue(buffer.Length, out var count))
                    {
                        bufferMap.TryAdd(buffer.Length, 1);
                    }
                    else
                    {
                        bufferMap[buffer.Length] = count + 1;
                    }
                }
                for (int i = 0; i < entityCount; ++i)
                {
                    Assert.IsTrue(bufferMap[i % 100] == 10);
                }
                bufferMap.Dispose();


                var entities4 = group4.ToEntityArray(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(entityCount, entities4.Length);

                NativeParallelHashMap<int, int> bufferMap2 = new NativeParallelHashMap<int, int>(entityCount, Allocator.Temp);
                for (int i = 0; i < entityCount; ++i)
                {
                    var buffer = entityManager.GetBuffer<EcsTestDataBlobAssetElement2>(entities4[i]);

                    for (int j = 0; j < buffer.Length; ++j)
                    {
                        Assert.AreEqual(j + 1, buffer[j].blobElement.Value);
                        Assert.AreEqual(j + 2, buffer[j].blobElement2.Value);
                    }
                    if (!bufferMap2.TryGetValue(buffer.Length, out var count))
                    {
                        bufferMap2.TryAdd(buffer.Length, 1);
                    }
                    else
                    {
                        bufferMap2[buffer.Length] = count + 1;
                    }
                }
                for (int i = 0; i < entityCount; ++i)
                {
                    Assert.IsTrue(bufferMap2[i % 100] == 10);
                }
                bufferMap2.Dispose();
            }
            finally
            {
                deserializedWorld.Dispose();
            }

#if !UNITY_DOTSRUNTIME // If memory has been unmapped this can throw exceptions other than InvalidOperation
            float f = 1.0f;
            Assert.Throws<InvalidOperationException>(() => f = arrayComponent.array.Value[0]);
#endif
        }

#pragma warning disable 169
        public unsafe struct ComponentWithPointer : IComponentData
        {
            int m_Pad;
            byte* m_Data;
        }

#if !UNITY_DOTSRUNTIME // We don't have reflection to validate if a type is serializable in NET_DOTS
        [Test]
        public void SerializeComponentWithPointerField()
        {
            var e1 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e1, new ComponentWithPointer());

            using (var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator))
            {
                Assert.Throws<ArgumentException>(() =>
                    SerializeUtility.SerializeWorld(m_Manager, writer)
                );
            }
        }

        public struct ComponentWithIntPtr : IComponentData
        {
            int m_Pad;
            IntPtr m_Data;
        }

        [Test]
        public void SerializeComponentWithIntPtrField()
        {
            var e1 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e1, new ComponentWithIntPtr());

            using (var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator))
            {
                Assert.Throws<ArgumentException>(() =>
                    SerializeUtility.SerializeWorld(m_Manager, writer)
                );
            }
        }

        public struct ValidTypeForSerialization
        {
            public int m_Int;
            public byte m_Byte;
        }

        public unsafe struct TypeWithPointer
        {
            int m_Pad;
            byte* m_Data;
        }

        public struct TypeWithNestedPointer
        {
            int m_Pad;
            ValidTypeForSerialization m_ValidStruct;
            TypeWithPointer m_Data;
            int m_Pad1;
        }

        public struct ComponentWithNestedPointer : IComponentData
        {
            int m_Pad;
            TypeWithNestedPointer m_PointerField;
            int m_Pad1;
        }

        [Test]
        public void SerializeComponentWithNestedPointerField()
        {
            var e1 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e1, new ComponentWithNestedPointer());

            using (var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator))
            {
                Assert.Throws<ArgumentException>(() =>
                    SerializeUtility.SerializeWorld(m_Manager, writer)
                );
            }
        }

        public struct TypeWithNestedWhiteListType
        {
            int m_Pad;
            ChunkHeader m_Header; // whitelisted pointer type
            int m_Pad1;
        }

        public struct ComponentWithNestedPointerAndNestedWhiteListType : IComponentData
        {
            TypeWithNestedPointer m_PointerField;
            TypeWithNestedWhiteListType m_NestedWhitelistField;
        }
#pragma warning restore 169

        [Test]
        public void EnsureSerializationWhitelistingDoesNotTrumpValidation()
        {
            var e1 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e1, new ComponentWithNestedPointerAndNestedWhiteListType());

            using (var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator))
            {
                Assert.Throws<ArgumentException>(() =>
                    SerializeUtility.SerializeWorld(m_Manager, writer)
                );
            }
        }

#endif

        [Test]
        public void DeserializedChunksAreConsideredChangedOnlyOnce()
        {
            TestBinaryReader CreateSerializedData()
            {
                var world = new World("DeserializedChunksAreConsideredChangedOnlyOnce World");
                var manager = world.EntityManager;
                var entity = manager.CreateEntity();
                manager.AddComponentData(entity, new EcsTestData(42));

                // owned by caller via reader
                var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);
                SerializeUtility.SerializeWorld(manager, writer, out var objRefs);
                world.Dispose();
                return new TestBinaryReader(writer);
            }

            var reader = CreateSerializedData();

            var deserializedWorld = new World("DeserializedChunksAreConsideredChangedOnlyOnce World 2");
            var deserializedManager = deserializedWorld.EntityManager;
            var system = deserializedWorld.GetOrCreateSystem<TestEcsChangeSystem>();

            Assert.AreEqual(0, system.NumChanged);

            SerializeUtility.DeserializeWorld(deserializedManager.BeginExclusiveEntityTransaction(), reader);
            deserializedManager.EndExclusiveEntityTransaction();
            reader.Dispose();

            system.Update();
            Assert.AreEqual(1, system.NumChanged);

            system.Update();
            Assert.AreEqual(0, system.NumChanged);

            deserializedWorld.Dispose();
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS && !UNITY_DOTSRUNTIME
        public class ManagedComponent : IComponentData
        {
            public string String;
            public Dictionary<string, int> Map;
        }

        public class ManagedComponent2 : IComponentData
        {
            public List<string> List;
        }

        [Test]
        [DotsRuntimeFixme] // DOTS Runtime Managed Component Serialization
        public void SerializeEntities_HandlesNullManagedComponents()
        {
            var e = m_Manager.CreateEntity(typeof(ManagedComponent));
            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);
            using (var reader = new TestBinaryReader(writer))
            using (var deserializedWorld = new World("SerializeEntities_HandlesNullManagedComponents Test World"))
            {
                var entityManager = deserializedWorld.EntityManager;

                SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
                entityManager.EndExclusiveEntityTransaction();

                Assert.IsNull(entityManager.CreateEntityQuery(typeof(ManagedComponent)).GetSingleton<ManagedComponent>());
            }
        }

        [Test]
        [DotsRuntimeFixme] // DOTS Runtime Managed Component Serialization
        public void SerializeEntities_RemapsEntitiesInManagedComponents()
        {
            int numberOfEntitiesPerManager = 10000;

            var targetArchetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var targetEntities = new NativeArray<Entity>(numberOfEntitiesPerManager, Allocator.Temp);
            m_Manager.CreateEntity(targetArchetype, targetEntities);
            for (int i = 0; i != targetEntities.Length; i++)
                m_Manager.SetComponentData(targetEntities[i], new EcsTestData(i));

            var sourceArchetype = m_Manager.CreateArchetype(typeof(EcsTestManagedDataEntity));
            var sourceEntities = new NativeArray<Entity>(numberOfEntitiesPerManager, Allocator.Temp);
            m_Manager.CreateEntity(sourceArchetype, sourceEntities);
            for (int i = 0; i != sourceEntities.Length; i++)
            {
                int index = i & ~1;
                m_Manager.SetComponentData(sourceEntities[i],  new EcsTestManagedDataEntity("foo", targetEntities[index], index));
            }

            // Destroy ever 2nd target entity to ensure something changes when entity ids are compacted during serialization
            for (int i = 0; i != targetEntities.Length / 2; i++)
                m_Manager.DestroyEntity(targetEntities[i * 2 + 1]);

            var deserializedWorld = new World("SerializeEntities_HandlesNullManagedComponents Test World");

            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);
            using (var reader = new TestBinaryReader(writer))
            {
                SerializeUtility.DeserializeWorld(deserializedWorld.EntityManager.BeginExclusiveEntityTransaction(), reader);
                deserializedWorld.EntityManager.EndExclusiveEntityTransaction();
            }

            var entityManager = deserializedWorld.EntityManager;

            m_Manager.Debug.CheckInternalConsistency();
            entityManager.Debug.CheckInternalConsistency();


            var group = entityManager.CreateEntityQuery(typeof(EcsTestData));
            Assert.AreEqual(numberOfEntitiesPerManager / 2, group.CalculateEntityCount());

            var managedGroup = entityManager.CreateEntityQuery(typeof(EcsTestManagedDataEntity));
            Assert.AreEqual(numberOfEntitiesPerManager, managedGroup.CalculateEntityCount());


            var managedTestDataArray = managedGroup.ToComponentDataArray<EcsTestManagedDataEntity>();
            for (int i = 0; i != managedTestDataArray.Length; i++)
            {
                Assert.AreEqual(managedTestDataArray[i].value2, entityManager.GetComponentData<EcsTestData>(managedTestDataArray[i].value1).value);
            }

            targetEntities.Dispose();
            sourceEntities.Dispose();
            deserializedWorld.Dispose();
        }

        [Test]
        [DotsRuntimeFixme] // DOTS Runtime Managed Component Serialization
        public void SerializeEntities_ManagedComponents()
        {
            int expectedEntityCount = 1000;
            for (int i = 0; i < expectedEntityCount; ++i)
            {
                var e1 = m_Manager.CreateEntity();

                var expectedManagedComponent = new ManagedComponent { String = i.ToString(), Map = new Dictionary<string, int>() };
                expectedManagedComponent.Map.Add("positive", i);
                expectedManagedComponent.Map.Add("negative", -i);

                var expectedManagedComponent2 = new ManagedComponent2() { List = new List<string>() };
                expectedManagedComponent2.List.Add("one");
                expectedManagedComponent2.List.Add("two");
                expectedManagedComponent2.List.Add("three");
                expectedManagedComponent2.List.Add("four");

                m_Manager.AddComponentData(e1, expectedManagedComponent);
                m_Manager.AddComponentData(e1, expectedManagedComponent2);
            }

            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);
            var reader = new TestBinaryReader(writer);

            var deserializedWorld = new World("SerializeEntities_ManagedComponents Test World");
            var entityManager = deserializedWorld.EntityManager;

            SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
            entityManager.EndExclusiveEntityTransaction();

            try
            {
                var allEntities = entityManager.GetAllEntities(Allocator.Temp);
                var actualEntityCount = allEntities.Length;
                allEntities.Dispose();

                Assert.AreEqual(expectedEntityCount, actualEntityCount);

                var group1 = entityManager.CreateEntityQuery(typeof(ManagedComponent));

                Assert.AreEqual(actualEntityCount, group1.CalculateEntityCount());

                using (var entities = group1.ToEntityArray(World.UpdateAllocator.ToAllocator))
                {
                    for (int i = 0; i < entities.Length; ++i)
                    {
                        var e = entities[i];

                        var actualManagedComponent = entityManager.GetComponentData<ManagedComponent>(e);
                        Assert.AreEqual(i.ToString(), actualManagedComponent.String);
                        Assert.AreEqual(2, actualManagedComponent.Map.Count);
                        Assert.AreEqual(i, actualManagedComponent.Map["positive"]);
                        Assert.AreEqual(-i, actualManagedComponent.Map["negative"]);

                        var actualManagedComponent2 = entityManager.GetComponentData<ManagedComponent2>(e);
                        Assert.AreEqual(4, actualManagedComponent2.List.Count);
                        Assert.AreEqual("one", actualManagedComponent2.List[0]);
                        Assert.AreEqual("two", actualManagedComponent2.List[1]);
                        Assert.AreEqual("three", actualManagedComponent2.List[2]);
                        Assert.AreEqual("four", actualManagedComponent2.List[3]);
                    }
                }
            }
            finally
            {
                deserializedWorld.Dispose();
                reader.Dispose();
            }
        }

        public interface InterfaceType {}

        public class InterfaceImplType : InterfaceType
        {
            public string String;
            public InterfaceImplType()
            {
                String = null;
            }

            public InterfaceImplType(int i)
            {
                String = i.ToString();
            }
        }

        public class InterfaceImplType2 : InterfaceType
        {
            public int Int;
            public InterfaceType NestedInterfaceType;
            public InterfaceImplType2()
            {
                NestedInterfaceType = null;
            }

            public InterfaceImplType2(int i)
            {
                Int = i;
                NestedInterfaceType = new InterfaceImplType(i);
            }
        }

        public abstract class AbstractType {}
        public class AbstractImplType : AbstractType
        {
            public string String;

            public AbstractImplType()
            {
                String = null;
            }

            public AbstractImplType(int i)
            {
                String = i.ToString();
            }
        }

        public class NestedAbstractImplType : AbstractImplType
        {
            public int Int;

            public NestedAbstractImplType()
                : base()
            {
                Int = 0;
            }

            public NestedAbstractImplType(int i) : base(i)
            {
                Int = i;
            }
        }

        public class BaseType
        {
            public int Int;

            public BaseType()
            {
                Int = 0;
            }

            public BaseType(int i)
            {
                Int = i;
            }
        }

        public class ChildType : BaseType
        {
            public string String;

            public ChildType()
                : base()
            {
                String = null;
            }

            public ChildType(int i)
                : base(i)
            {
                String = (i + 1).ToString();
            }
        }

        public class GrandChildType : ChildType
        {
            public object BoxedByte;

            public GrandChildType()
                : base()
            {
                BoxedByte = (byte)0;
            }

            public GrandChildType(int i)
                : base(i)
            {
                BoxedByte = (byte)(i + 2);
            }
        }

        public class MyClass
        {
            public const int kIterations = 16;
            public int mId;
            public uint4 mInt4;
            public int[] mArray;
            public BaseType mBaseType;
            public InterfaceType mInterfaceType;
            public AbstractType mAbstractType;
            public List<InterfaceType> mInterfaceList;
            public List<AbstractType> mAbstractClassList;
            public List<BaseType> mBaseTypeList;

            public MyClass()
            {
                mId = 0;
                mInt4 = new uint4();
                mArray = null;
            }

            public MyClass(int v)
            {
                mId = v;
                mInt4 = new uint4(v + 1);
                mArray = new int[kIterations];
                for (int i = 0; i < kIterations; ++i)
                    mArray[i] = v + i;

                mInterfaceType = new InterfaceImplType(v);
                mAbstractType = new AbstractImplType(v);
                mBaseType = new ChildType(v);

                mInterfaceList = new List<InterfaceType>();
                for (int i = 0; i < kIterations; ++i)
                {
                    if ((i & 1) == 0)
                        mInterfaceList.Add(new InterfaceImplType(v + i));
                    else
                        mInterfaceList.Add(new InterfaceImplType2(v + i));
                }

                mAbstractClassList = new List<AbstractType>();
                for (int i = 0; i < kIterations; ++i)
                {
                    if ((i & 1) == 0)
                        mAbstractClassList.Add(new AbstractImplType(v + i));
                    else
                        mAbstractClassList.Add(new NestedAbstractImplType(v + i));
                }

                mBaseTypeList = new List<BaseType>();
                for (int i = 0; i < kIterations; ++i)
                {
                    if (i % 3 == 0)
                        mBaseTypeList.Add(new BaseType(v + i));
                    else if (i % 3 == 1)
                        mBaseTypeList.Add(new ChildType(v + i));
                    else if (i % 3 == 2)
                        mBaseTypeList.Add(new GrandChildType(v + i));
                }
            }

            public int GetId() { return mId; }
        }

        public class ManagedComponentCustomClass : IComponentData
        {
            public MyClass mClass;

            public ManagedComponentCustomClass()
            {
                mClass = null;
            }

            public ManagedComponentCustomClass(MyClass c)
            {
                mClass = c;
            }
        }

        [Test]
        [DotsRuntimeFixme] // DOTS Runtime Managed Component Serialization
        public void SerializeEntitiesManagedComponentWithCustomClass_ManagedComponents()
        {
            int expectedEntityCount = 100;
            for (int i = 0; i < expectedEntityCount; ++i)
            {
                var e1 = m_Manager.CreateEntity();

                var expectedManagedComponent = new ManagedComponentCustomClass(new MyClass(i));
                m_Manager.AddComponentData(e1, expectedManagedComponent);
            }

            // disposed via reader
            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);
            var reader = new TestBinaryReader(writer);

            var deserializedWorld = new World("SerializeEntitiesManagedComponentWithCustomClass_ManagedComponents Test World");
            var entityManager = deserializedWorld.EntityManager;

            SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
            entityManager.EndExclusiveEntityTransaction();

            try
            {
                var allEntities = entityManager.GetAllEntities(Allocator.Temp);
                var actualEntityCount = allEntities.Length;
                allEntities.Dispose();

                Assert.AreEqual(expectedEntityCount, actualEntityCount);

                var group1 = entityManager.CreateEntityQuery(typeof(ManagedComponentCustomClass));

                Assert.AreEqual(actualEntityCount, group1.CalculateEntityCount());

                using (var entities = group1.ToEntityArray(World.UpdateAllocator.ToAllocator))
                {
                    NativeParallelHashMap<int, int> idSet = new NativeParallelHashMap<int, int>(entities.Length, Allocator.Temp);
                    for (int i = 0; i < entities.Length; ++i)
                    {
                        var e = entities[i];

                        var actualManagedComponent = entityManager.GetComponentData<ManagedComponentCustomClass>(e);
                        var myClass = actualManagedComponent.mClass;
                        Assert.NotNull(actualManagedComponent);
                        Assert.NotNull(myClass);
                        int id = actualManagedComponent.mClass.GetId();
                        Assert.IsTrue(idSet.TryAdd(id, id));
                        Assert.IsTrue(myClass.mInt4.Equals(new uint4(id + 1)));
                        Assert.IsNotNull(myClass.mArray);
                        Assert.IsTrue(myClass.mArray.Length == MyClass.kIterations);
                        for (int j = 0; j < MyClass.kIterations; ++j)
                        {
                            Assert.IsTrue(myClass.mArray[j] == id + j);
                        }

                        // Polymorphic types
                        Assert.IsNotNull(myClass.mInterfaceType);
                        var concreteInterfaceType = (InterfaceImplType)myClass.mInterfaceType;
                        Assert.IsTrue(concreteInterfaceType.String == id.ToString());

                        Assert.IsNotNull(myClass.mAbstractType);
                        var concreteAbstractType = (AbstractImplType)myClass.mAbstractType;
                        Assert.IsTrue(concreteAbstractType.String == id.ToString());

                        Assert.IsNotNull(myClass.mBaseType);
                        var concreteBaseType = (ChildType)myClass.mBaseType;
                        Assert.IsTrue(concreteBaseType.Int == id);
                        Assert.IsTrue(concreteBaseType.String == (id + 1).ToString());

                        Assert.IsNotNull(myClass.mInterfaceList);
                        Assert.IsTrue(myClass.mInterfaceList.Count == MyClass.kIterations);
                        for (int j = 0; j < MyClass.kIterations; ++j)
                        {
                            var interfaceType = myClass.mInterfaceList[j];
                            if ((j & 1) == 0)
                            {
                                var concreteType = (InterfaceImplType)interfaceType;
                                Assert.AreEqual((id + j).ToString(), concreteType.String);
                            }
                            else
                            {
                                var concreteType = (InterfaceImplType2)interfaceType;
                                Assert.IsTrue(concreteType.Int == (id + j));
                                var nestedConcreteType = (InterfaceImplType)concreteType.NestedInterfaceType;
                                Assert.IsTrue(nestedConcreteType.String == (id + j).ToString());
                            }
                        }

                        Assert.IsNotNull(myClass.mAbstractClassList);
                        Assert.IsTrue(myClass.mAbstractClassList.Count == MyClass.kIterations);
                        for (int j = 0; j < MyClass.kIterations; ++j)
                        {
                            var abstractType = myClass.mAbstractClassList[j];
                            if ((j & 1) == 0)
                            {
                                var concreteType = (AbstractImplType)abstractType;
                                Assert.IsTrue(concreteType.String == (id + j).ToString());
                            }
                            else
                            {
                                var concreteType = (NestedAbstractImplType)abstractType;
                                Assert.IsTrue(concreteType.String == (id + j).ToString());
                                Assert.IsTrue(concreteType.Int == (id + j));
                            }
                        }

                        Assert.IsNotNull(myClass.mBaseTypeList);
                        Assert.IsTrue(myClass.mBaseTypeList.Count == MyClass.kIterations);
                        for (int j = 0; j < MyClass.kIterations; ++j)
                        {
                            var baseType = myClass.mBaseTypeList[j];
                            if ((j % 3) == 0)
                            {
                                var concreteType = (BaseType)baseType;
                                Assert.AreEqual((id + j), concreteType.Int);
                            }
                            else if ((j % 3) == 1)
                            {
                                var concreteType = (ChildType)baseType;
                                Assert.AreEqual((id + j), concreteType.Int);
                                Assert.AreEqual((id + j + 1).ToString(), concreteType.String);
                            }
                            else if ((j % 3) == 2)
                            {
                                var concreteType = (GrandChildType)baseType;
                                Assert.AreEqual((id + j), concreteType.Int);
                                Assert.AreEqual((id + j + 1).ToString(), concreteType.String);
                                Assert.AreEqual((id + j + 2), (byte)concreteType.BoxedByte);
                            }
                        }
                    }
                    idSet.Dispose();
                }
            }
            finally
            {
                deserializedWorld.Dispose();
            }
        }

#if UNITY_EDITOR
        [Test]
        public void WorldYamlSerializationTest()
        {
            var dummyEntity = CreateEntityWithDefaultData(0); //To ensure entity indices are offset
            var e1 = CreateEntityWithDefaultData(1);
            var e2 = CreateEntityWithDefaultData(2);
            var e3 = CreateEntityWithDefaultData(3);
            m_Manager.AddComponentData(e1, new TestComponentData1 { value = 10, referencedEntity = e2 });
            m_Manager.AddComponentData(e2, new TestComponentData2 { value = 20, referencedEntity = e1 });
            m_Manager.AddComponentData(e3, new TestComponentData1 { value = 30, referencedEntity = Entity.Null });
            m_Manager.AddComponentData(e3, new TestComponentData2 { value = 40, referencedEntity = Entity.Null });
            m_Manager.AddBuffer<EcsIntElement>(e1);
            m_Manager.RemoveComponent<EcsTestData2>(e3);
            m_Manager.AddBuffer<EcsIntElement>(e3);

            m_Manager.GetBuffer<EcsIntElement>(e1).CopyFrom(new EcsIntElement[] { 1, 2, 3 }); // no overflow
            m_Manager.GetBuffer<EcsIntElement>(e3).CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }); // overflow into heap

            var e4 = m_Manager.CreateEntity();
            m_Manager.AddBuffer<EcsComplexEntityRefElement>(e4);
            var ebuf = m_Manager.GetBuffer<EcsComplexEntityRefElement>(e4);
            ebuf.Add(new EcsComplexEntityRefElement { Entity = e1, Dummy = 1 });
            ebuf.Add(new EcsComplexEntityRefElement { Entity = e2, Dummy = 2 });
            ebuf.Add(new EcsComplexEntityRefElement { Entity = e3, Dummy = 3 });

            m_Manager.DestroyEntity(dummyEntity);

            var refFilePathName = @"Packages\com.unity.entities\Unity.Entities.Tests\Serialization\WorldTest.yaml";

            // To generate the file we'll test against
            using (var sw = new StreamWriter(refFilePathName))
            {
                sw.NewLine = "\n";
                SerializeUtility.SerializeWorldIntoYAML(m_Manager, sw, false);
            }

            using (var memStream = new MemoryStream())
            {
                byte[] testContentBuffer;

                // Save the World to a memory buffer via a a Stream Writer
                using (var sw = new StreamWriter(memStream))
                {
                    sw.NewLine = "\n";
                    SerializeUtility.SerializeWorldIntoYAML(m_Manager, sw, false);
                    sw.Flush();
                    memStream.Seek(0, SeekOrigin.Begin);
                    testContentBuffer = memStream.ToArray();
                }

                // Load both reference content and the test one into strings and compare
                using (var sr = File.OpenRead(refFilePathName))
                using (var testMemoryStream = new MemoryStream(testContentBuffer))
                {
                    Assert.IsTrue(YAMLSerializationHelpers.EqualYAMLFiles(sr, testMemoryStream));
                }
            }
        }

        [Test]
        public void WorldYamlSerialization_UsingStreamWriterWithCRLF_ThrowsArgumentException()
        {
            using (var memStream = new MemoryStream())
            using (var streamWriter = new StreamWriter(memStream))
            {
                streamWriter.NewLine = "\r\n";
                Assert.Throws<ArgumentException>(() =>
                {
                    SerializeUtility.SerializeWorldIntoYAML(m_Manager, streamWriter, false);
                });
            }
        }

#endif // !UNITY_DOTSRUNTIME
#endif // !UNITY_DISABLE_MANAGED_COMPONENTS

        [Test]
        [DotsRuntimeFixme] // Unity.Properties support required
        public void SerializeEntities_WithBlobAssetReferencesInSharedComponents()
        {
            Entity a = m_Manager.CreateEntity();
            m_Manager.AddSharedComponentData(a, new EcsTestSharedCompBlobAssetRef
            {
                value = BlobAssetReference<int>.Create(123)
            });

            Entity b = m_Manager.CreateEntity();
            m_Manager.AddSharedComponentData(b, new EcsTestSharedCompBlobAssetRef
            {
                value = BlobAssetReference<int>.Create(123)
            });

            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);

            m_Manager.DestroyEntity(m_Manager.UniversalQuery);

            var deserializedWorld = new World("Deserialized World");
            var entityManager = deserializedWorld.EntityManager;

            using (var reader = new TestBinaryReader(writer))
            {
                SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
                entityManager.EndExclusiveEntityTransaction();
            }

            entityManager.Debug.CheckInternalConsistency();

            using (var query = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadWrite<EcsTestSharedCompBlobAssetRef>()}
            }))
            {
                Assert.AreEqual(2, query.CalculateChunkCount());
            }
        }

        [Test]
        [DotsRuntimeFixme] // Unity.Properties support required
        public void SerializeEntities_WithEntityReferencesInSharedComponents_ThrowsException()
        {
            {
                var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>());
                var entities = m_Manager.CreateEntity(archetype, 20, Allocator.Temp);

                Entity a = m_Manager.CreateEntity();
                m_Manager.SetComponentData(entities[12], new EcsTestData(12345));
                m_Manager.AddSharedComponentData(a, new EcsTestSharedCompEntity(entities[12]));

                Entity b = m_Manager.CreateEntity();
                m_Manager.SetComponentData(entities[17], new EcsTestData(23456));
                m_Manager.AddSharedComponentData(b, new EcsTestSharedCompEntity(entities[17]));

                entities[12] = Entity.Null;
                entities[17] = Entity.Null;
                m_Manager.DestroyEntity(entities);
            }

            using (var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator))
            {
                Assert.Throws<ArgumentException>(() => SerializeUtility.SerializeWorld(m_Manager, writer));
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        [DotsRuntimeFixme] // Unity.Properties support required
        public void SerializeEntities_WithBlobAssetReferencesInManagedComponents()
        {
            {
                Entity a = m_Manager.CreateEntity();
                m_Manager.AddComponentData(a, new EcsTestManagedDataBlobAssetRef
                {
                    value = BlobAssetReference<int>.Create(123)
                });

                Entity b = m_Manager.CreateEntity();
                m_Manager.AddComponentData(b, new EcsTestManagedDataBlobAssetRef
                {
                    value = BlobAssetReference<int>.Create(234)
                });
            }

            var writer = new TestBinaryWriter(m_Manager.World.UpdateAllocator.ToAllocator);
            SerializeUtility.SerializeWorld(m_Manager, writer);

            m_Manager.DestroyEntity(m_Manager.UniversalQuery);

            var deserializedWorld = new World("Deserialized World");
            var entityManager = deserializedWorld.EntityManager;

            using (var reader = new TestBinaryReader(writer))
            {
                SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
                entityManager.EndExclusiveEntityTransaction();
            }

            entityManager.Debug.CheckInternalConsistency();

            using (var query = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadWrite<EcsTestManagedDataBlobAssetRef>()}
            }))
            {
                var entities = query.ToEntityArray(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(2, entities.Length);

                var a = entityManager.GetComponentData<EcsTestManagedDataBlobAssetRef>(entities[0]).value.Value;
                var b = entityManager.GetComponentData<EcsTestManagedDataBlobAssetRef>(entities[1]).value.Value;

                Assert.AreEqual(123 + 234, a + b);
            }
        }
#endif // !UNITY_DISABLE_MANAGED_COMPONENTS
    }
}
