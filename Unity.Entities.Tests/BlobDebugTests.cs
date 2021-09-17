#if !NET_DOTS
using UnityEngine;
using NUnit.Framework;
using System;
using System.Collections.ObjectModel;
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
using Unity.Entities.DebugProxies;
using Unity.Entities.Serialization;
using Unity.Entities.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;

[TestFixture]
public class BlobDebugTests
{
    struct BlobWithString
    {
        public int Value;
        public BlobString String;
    }

    struct BlobWithNestedStruct
    {
        public float Value;
        public BlobWithString Struct;
    }

    struct BlobWithPtr
    {
        public int Value;
        public BlobString String;
        public BlobPtr<BlobString> PtrToString;
    }

    struct BlobWithArray
    {
        public int Value;
        public BlobArray<int> Array;
    }

    struct BlobWithStringNestedInArray
    {
        public int Value;
        public BlobArray<BlobString> ArrayOfStrings;
    }

    [Test]
    public void BlobAssetReferenceDebugProxy_CanDisplayInvalidValues()
    {
        BlobAssetReference<int> bar = default;
        var proxy = new BlobAssetReferenceProxy<int>(bar);
        Assert.AreEqual(null, proxy.Value);
    }

    [Test]
    public void BlobAssetReferenceDebugProxy_WorksWithPrimitiveValue()
    {
        var builder = new BlobBuilder(Allocator.Temp);
        {
            ref var v = ref builder.ConstructRoot<int>();
            v = 42;
        }
        BlobAssetReference<int> bar = builder.CreateBlobAssetReference<int>(Allocator.Temp);
        var proxy = new BlobAssetReferenceProxy<int>(bar);
        Assert.AreEqual(42, proxy.Value);
    }

    [Test]
    public void BlobAssetReferenceDebugProxy_WorksWithBlobString()
    {
        var builder = new BlobBuilder(Allocator.Temp);
        {
            ref var v = ref builder.ConstructRoot<BlobWithString>();
            v.Value = 42;
            builder.AllocateString(ref v.String, "Test");
        }

        var bar = builder.CreateBlobAssetReference<BlobWithString>(Allocator.Temp);
        var proxy = new BlobAssetReferenceProxy<BlobWithString>(bar);
        Assert.AreEqual(typeof(BlobStruct<BlobWithString>), proxy.Value.GetType());
        var bs = (BlobStruct<BlobWithString>) proxy.Value;
        Assert.AreEqual(2, bs.Members.Length);
        Assert.AreEqual(nameof(BlobWithString.Value), bs.Members[0].Key);
        Assert.AreEqual(bar.Value.Value, bs.Members[0].Value);
        Assert.AreEqual(nameof(BlobWithString.String), bs.Members[1].Key);
        Assert.AreEqual(bar.Value.String.ToString(), bs.Members[1].Value);
    }

    [Test]
    public void BlobAssetReferenceDebugProxy_WorksWithNestedStruct()
    {
        var builder = new BlobBuilder(Allocator.Temp);
        {
            ref var v = ref builder.ConstructRoot<BlobWithNestedStruct>();
            v.Value = 42;
            v.Struct.Value = 21;
            builder.AllocateString(ref v.Struct.String, "Test");
        }

        var bar = builder.CreateBlobAssetReference<BlobWithNestedStruct>(Allocator.Temp);
        var proxy = new BlobAssetReferenceProxy<BlobWithNestedStruct>(bar);
        Assert.AreEqual(typeof(BlobStruct<BlobWithNestedStruct>), proxy.Value.GetType());
        var bs = (BlobStruct<BlobWithNestedStruct>) proxy.Value;
        Assert.AreEqual(2, bs.Members.Length);
        Assert.AreEqual(nameof(BlobWithNestedStruct.Value), bs.Members[0].Key);
        Assert.AreEqual(bar.Value.Value, bs.Members[0].Value);
        Assert.AreEqual(nameof(BlobWithNestedStruct.Struct), bs.Members[1].Key);
        Assert.AreEqual(typeof(BlobStruct<BlobWithString>), bs.Members[1].Value.GetType());
        var s = (BlobStruct<BlobWithString>)bs.Members[1].Value;
        Assert.AreEqual(nameof(BlobWithString.Value), s.Members[0].Key);
        Assert.AreEqual(bar.Value.Struct.Value, s.Members[0].Value);
        Assert.AreEqual(nameof(BlobWithString.String), s.Members[1].Key);
        Assert.AreEqual(bar.Value.Struct.String.ToString(), s.Members[1].Value);
    }

    [Test]
    public void BlobAssetReferenceDebugProxy_WorksWithBlobArray()
    {
        var builder = new BlobBuilder(Allocator.Temp);
        {
            ref var v = ref builder.ConstructRoot<BlobWithArray>();
            v.Value = 42;
            var array = builder.Allocate(ref v.Array, 5);
            for (int i = 0; i < 5; i++)
                array[i] = i;
        }
        var bar = builder.CreateBlobAssetReference<BlobWithArray>(Allocator.Temp);
        var proxy = new BlobAssetReferenceProxy<BlobWithArray>(bar);
        Assert.AreEqual(typeof(BlobStruct<BlobWithArray>), proxy.Value.GetType());
        var bs = (BlobStruct<BlobWithArray>) proxy.Value;
        Assert.AreEqual(2, bs.Members.Length);
        Assert.AreEqual(nameof(BlobWithArray.Value), bs.Members[0].Key);
        Assert.AreEqual(bar.Value.Value, bs.Members[0].Value);
        Assert.AreEqual(nameof(BlobWithArray.Array), bs.Members[1].Key);
        Assert.AreEqual(typeof(BlobArrayDebug<int>), bs.Members[1].Value.GetType());
        var arr = (BlobArrayDebug<int>) bs.Members[1].Value;
        Assert.AreEqual(5, arr.Length);
        CollectionAssert.AreEqual(new int[5]{0, 1,2,3,4}, arr.Entries);
    }

    [Test]
    public void BlobAssetReferenceDebugProxy_WorksWithBlobPtr()
    {
        var builder = new BlobBuilder(Allocator.Temp);
        {
            ref var v = ref builder.ConstructRoot<BlobWithPtr>();
            v.Value = 42;
            builder.AllocateString(ref v.String, "Test");
            builder.SetPointer(ref v.PtrToString, ref v.String);
        }
        var bar = builder.CreateBlobAssetReference<BlobWithPtr>(Allocator.Temp);
        var proxy = new BlobAssetReferenceProxy<BlobWithPtr>(bar);
        Assert.AreEqual(typeof(BlobStruct<BlobWithPtr>), proxy.Value.GetType());
        var bs = (BlobStruct<BlobWithPtr>) proxy.Value;
        Assert.AreEqual(3, bs.Members.Length);
        Assert.AreEqual(nameof(BlobWithPtr.Value), bs.Members[0].Key);
        Assert.AreEqual(bar.Value.Value, bs.Members[0].Value);
        Assert.AreEqual(nameof(BlobWithPtr.String), bs.Members[1].Key);
        Assert.AreEqual(bar.Value.String.ToString(), bs.Members[1].Value);
        Assert.AreEqual(nameof(BlobWithPtr.PtrToString), bs.Members[2].Key);
        Assert.AreEqual(bar.Value.PtrToString.Value.ToString(), ((BlobPtrDebug<BlobString>)bs.Members[2].Value).Value);
    }

    [Test]
    public void BlobAssetReferenceDebugProxy_WorksWithStringNestedInArray()
    {
        var builder = new BlobBuilder(Allocator.Temp);
        {
            ref var v = ref builder.ConstructRoot<BlobWithStringNestedInArray>();
            v.Value = 42;
            var arrayBuilder = builder.Allocate(ref v.ArrayOfStrings, 3);
            builder.AllocateString(ref arrayBuilder[0], "One");
            builder.AllocateString(ref arrayBuilder[1], "Two");
            builder.AllocateString(ref arrayBuilder[2], "Three");
        }
        var bar = builder.CreateBlobAssetReference<BlobWithStringNestedInArray>(Allocator.Temp);
        var proxy = new BlobAssetReferenceProxy<BlobWithStringNestedInArray>(bar);
        Assert.AreEqual(typeof(BlobStruct<BlobWithStringNestedInArray>), proxy.Value.GetType());
        var bs = (BlobStruct<BlobWithStringNestedInArray>) proxy.Value;
        Assert.AreEqual(2, bs.Members.Length);
        Assert.AreEqual(nameof(BlobWithStringNestedInArray.Value), bs.Members[0].Key);
        Assert.AreEqual(bar.Value.Value, bs.Members[0].Value);
        Assert.AreEqual(nameof(BlobWithStringNestedInArray.ArrayOfStrings), bs.Members[1].Key);
        Assert.AreEqual(typeof(BlobArrayDebug<BlobString>), bs.Members[1].Value.GetType());
        var arr = (BlobArrayDebug<BlobString>)bs.Members[1].Value;
        CollectionAssert.AreEqual(new []{"One", "Two", "Three"}, arr.Entries);
    }
}
#endif
