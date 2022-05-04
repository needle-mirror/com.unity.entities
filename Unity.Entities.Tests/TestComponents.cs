using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Entities;
using Unity.Entities.Tests;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

[assembly: RegisterGenericComponentType(typeof(EcsTestGeneric<int>))]
[assembly: RegisterGenericComponentType(typeof(EcsTestGeneric<float>))]
[assembly: RegisterGenericComponentType(typeof(EcsTestGenericTag<int>))]
[assembly: RegisterGenericComponentType(typeof(EcsTestGenericTag<float>))]

namespace Unity.Entities.Tests
{
    // In case we need a generic way to access the first int of the EcsTestData* structures
    interface IGetValue
    {
        int GetValue();
    }
    public struct EcsTestData : IComponentData, IGetValue
    {
        public int value;

        public EcsTestData(int inValue)
        {
            value = inValue;
        }

        public override string ToString()
        {
            return value.ToString();
        }

        public int GetValue() => value;
    }

    public struct EcsTestData2 : IComponentData, IGetValue
    {
        public int value0;
        public int value1;

        public EcsTestData2(int inValue)
        {
            value1 = value0 = inValue;
        }

        public int GetValue() => value0;
    }

    public struct EcsTestData3 : IComponentData, IGetValue
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestData3(int inValue)
        {
            value2 = value1 = value0 = inValue;
        }

        public int GetValue() => value0;
    }

    public struct EcsTestData4 : IComponentData, IGetValue
    {
        public int value0;
        public int value1;
        public int value2;
        public int value3;

        public EcsTestData4(int inValue)
        {
            value3 = value2 = value1 = value0 = inValue;
        }

        public int GetValue() => value0;
    }

    public struct EcsTestData5 : IComponentData, IGetValue
    {
        public int value0;
        public int value1;
        public int value2;
        public int value3;
        public int value4;

        public EcsTestData5(int inValue)
        {
            value4 = value3 = value2 = value1 = value0 = inValue;
        }

        public int GetValue() => value0;
    }

    public struct EcsTestDataEnableable : IComponentData, IGetValue, IEnableableComponent
    {
        public int value;

        public EcsTestDataEnableable(int inValue)
        {
            value = inValue;
        }

        public override string ToString()
        {
            return value.ToString();
        }

        public int GetValue() => value;
    }

    public struct EcsTestDataEnableable2 : IComponentData, IGetValue, IEnableableComponent
    {
        public int value0;
        public int value1;

        public EcsTestDataEnableable2(int inValue)
        {
            value1 = value0 = inValue;
        }

        public int GetValue() => value0;
    }

    public struct EcsTestDataEnableable3 : IComponentData, IGetValue, IEnableableComponent
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestDataEnableable3(int inValue)
        {
            value2 = value1 = value0 = inValue;
        }

        public int GetValue() => value0;
    }

    public struct EcsTestDataEnableable4 : IComponentData, IGetValue, IEnableableComponent
    {
        public int value0;
        public int value1;
        public int value2;
        public int value3;

        public EcsTestDataEnableable4(int inValue)
        {
            value3 = value2 = value1 = value0 = inValue;
        }

        public int GetValue() => value0;
    }

    public struct EcsTestDataEnableable5 : IComponentData, IGetValue, IEnableableComponent
    {
        public int value0;
        public int value1;
        public int value2;
        public int value3;
        public int value4;

        public EcsTestDataEnableable5(int inValue)
        {
            value4 = value3 = value2 = value1 = value0 = inValue;
        }

        public int GetValue() => value0;
    }

    public struct EcsTestNonComponent
    {
        public int Value;
    }

    public struct EcsTestFloatData : IComponentData
    {
        public float Value;
    }

    public struct EcsTestFloatData2 : IComponentData
    {
        public float Value0;
        public float Value1;
    }

    public struct EcsTestFloatData3 : IComponentData
    {
        public float Value0;
        public float Value1;
        public float Value2;
    }

    public struct EcsTestSharedComp : ISharedComponentData
    {
        public int value;

        public EcsTestSharedComp(int inValue)
        {
            value = inValue;
        }
    }

    public struct EcsTestSharedComp2 : ISharedComponentData
    {
        public int value0;
        public int value1;

        public EcsTestSharedComp2(int inValue)
        {
            value0 = value1 = inValue;
        }
    }

    public struct EcsTestSharedComp3 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp3(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    // need many shared types for testing that we don't exceed kMaxNumSharedComponentCount
    public struct EcsTestSharedComp4 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp4(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp5 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp5(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp6 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp6(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp7 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp7(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp8 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp8(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp9 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp9(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp10 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp10(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp11 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp11(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp12 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp12(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp13 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp13(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp14 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp14(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp15 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp15(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp16 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp16(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    public struct EcsTestSharedComp17 : ISharedComponentData
    {
        public int value0;
        public int value1;
        public int value2;

        public EcsTestSharedComp17(int inValue)
        {
            value0 = value1 = value2 = inValue;
        }
    }

    [MaximumChunkCapacity(475)]
    struct EcsTestSharedCompWithMaxChunkCapacity : ISharedComponentData
    {
        public int Value;

        public EcsTestSharedCompWithMaxChunkCapacity(int value)
        {
            Value = value;
        }
    }

    public unsafe struct EcsTestSharedCompWithRefCount : ISharedComponentData, IRefCounted
    {
        readonly int* RefCount;

        public EcsTestSharedCompWithRefCount(int* refCount)
        {
            Assert.IsTrue(refCount != null);
            this.RefCount = refCount;
        }

        public void Retain()
        {
            Assert.IsTrue(RefCount != null);
            Interlocked.Increment(ref *RefCount);
        }

        public void Release()
        {
            Assert.IsTrue(RefCount != null);
            Interlocked.Decrement(ref *RefCount);
        }
    }


    public struct EcsTestDataEntity : IComponentData
    {
        public int value0;
        public Entity value1;

        public EcsTestDataEntity(int inValue0, Entity inValue1)
        {
            value0 = inValue0;
            value1 = inValue1;
        }
    }

    public struct EcsTestDataEntity2 : IComponentData
    {
        public int value0;
        public Entity value1;
        public Entity value2;
    }

    public struct EcsTestDataBlobAssetRef : IComponentData
    {
        public BlobAssetReference<int> value;
    }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
    public class EcsTestDataBlobAssetRefClass : IComponentData
    {
        public BlobAssetReference<int> value;
        public BlobAssetReference<int> value2;
    }
#endif

    public struct EcsTestDataBlobAssetRefShared : ISharedComponentData
    {
        public BlobAssetReference<int> value;
        public BlobAssetReference<int> value2;
    }

    public struct EcsTestDataBlobAssetRef2 : IComponentData
    {
        public BlobAssetReference<int> value;
        public BlobAssetReference<int> value2;
    }

    public struct EcsTestDataBlobAssetArray : IComponentData
    {
        public BlobAssetReference<BlobArray<float>> array;
    }

    public struct EcsTestDataBlobAssetElement : IBufferElementData
    {
        public BlobAssetReference<int> blobElement;
    }

    public struct EcsTestDataBlobAssetElement2 : IBufferElementData
    {
        public BlobAssetReference<int> blobElement;
        byte pad;
        public BlobAssetReference<int> blobElement2;
    }

    public struct EcsTestSharedCompEntity : ISharedComponentData
    {
        public Entity value;

        public EcsTestSharedCompEntity(Entity inValue)
        {
            value = inValue;
        }
    }

    public struct EcsState1 : ISystemStateComponentData
    {
        public int Value;

        public EcsState1(int value)
        {
            Value = value;
        }
    }

    public struct EcsStateShared1 : ISystemStateSharedComponentData
    {
        public int Value;

        public EcsStateShared1(int value)
        {
            Value = value;
        }
    }

    public struct EcsStateTag1 : ISystemStateComponentData
    {
    }

    [InternalBufferCapacity(8)]
    public struct EcsIntElement : IBufferElementData
    {
        public static implicit operator int(EcsIntElement e)
        {
            return e.Value;
        }

        public static implicit operator EcsIntElement(int e)
        {
            return new EcsIntElement {Value = e};
        }

        public int Value;
    }

    [InternalBufferCapacity(8)]
    public struct EcsIntElement2 : IBufferElementData
    {
        public int Value0;
        public int Value1;
    }

    [InternalBufferCapacity(8)]
    public struct EcsIntElement3 : IBufferElementData
    {
        public int Value0;
        public int Value1;
        public int Value2;
    }

    [InternalBufferCapacity(8)]
    public struct EcsIntElement4 : IBufferElementData
    {
        public int Value0;
        public int Value1;
        public int Value2;
        public int Value3;
    }

    [InternalBufferCapacity(8)]
    public struct EcsIntElementEnableable : IBufferElementData, IEnableableComponent
    {
        public static implicit operator int(EcsIntElementEnableable e)
        {
            return e.Value;
        }

        public static implicit operator EcsIntElementEnableable(int e)
        {
            return new EcsIntElementEnableable {Value = e};
        }

        public int Value;
    }

    [InternalBufferCapacity(8)]
    public struct EcsIntElementEnableable2 : IBufferElementData, IEnableableComponent
    {
        public int Value0;
        public int Value1;
    }

    [InternalBufferCapacity(8)]
    public struct EcsIntElementEnableable3 : IBufferElementData, IEnableableComponent
    {
        public int Value0;
        public int Value1;
        public int Value2;
    }

    [InternalBufferCapacity(8)]
    public struct EcsIntElementEnableable4 : IBufferElementData, IEnableableComponent
    {
        public int Value0;
        public int Value1;
        public int Value2;
        public int Value3;
    }

    [InternalBufferCapacity(8)]
    public struct EcsIntStateElement : ISystemStateBufferElementData
    {
        public static implicit operator int(EcsIntStateElement e)
        {
            return e.Value;
        }

        public static implicit operator EcsIntStateElement(int e)
        {
            return new EcsIntStateElement {Value = e};
        }

        public int Value;
    }

    [InternalBufferCapacity(4)]
    public struct EcsComplexEntityRefElement : IBufferElementData
    {
        public int Dummy;
        public Entity Entity;
    }

    public struct EcsTestTag : IComponentData
    {
    }

    public struct EcsTestTagEnableable : IComponentData, IEnableableComponent
    {
    }

    public struct EcsTestSharedTag : ISharedComponentData
    {
    }

    public struct EcsTestComponentWithBool : IComponentData, IEquatable<EcsTestComponentWithBool>
    {
        public bool value;

        public override int GetHashCode()
        {
            return value ? 0x11001100 : 0x22112211;
        }

        public bool Equals(EcsTestComponentWithBool other)
        {
            return other.value == value;
        }
    }

    public struct EcsStringSharedComponent : ISharedComponentData, IEquatable<EcsStringSharedComponent>
    {
        public string Value;

        public bool Equals(EcsStringSharedComponent other)
        {
            return Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }

    public struct EcsTestGeneric<T> : IComponentData
        where T : struct
    {
        public T value;
    }

    public struct EcsTestGenericTag<T> : IComponentData
        where T : struct
    {
    }

#if !UNITY_DISABLE_MANAGED_COMPONENTS

    sealed public class ClassWithString
    {
        public string String;
    }
    sealed public class ClassWithClassFields
    {
        public ClassWithString ClassWithString;
    }

    public class EcsTestManagedDataEntity : IComponentData
    {
        public string value0;
        public Entity value1;
        public int value2;
        public ClassWithClassFields nullField;

        public EcsTestManagedDataEntity()
        {
        }

        public EcsTestManagedDataEntity(string inValue0, Entity inValue1, int inValue2 = 0)
        {
            value0 = inValue0;
            value1 = inValue1;
            value2 = inValue2;
            nullField = null;
        }
    }

#if !NET_DOTS
// https://unity3d.atlassian.net/browse/DOTSR-1432

    public class EcsTestManagedDataEntityCollection : IComponentData
    {
        public List<string> value0;
        public List<Entity> value1;
        public List<ClassWithClassFields> nullField;

        public EcsTestManagedDataEntityCollection()
        {
        }

        public EcsTestManagedDataEntityCollection(string[] inValue0, Entity[] inValue1)
        {
            value0 = new List<string>(inValue0);
            value1 = new List<Entity>(inValue1);
            nullField = null;
        }
    }
#endif

    public class EcsTestManagedComponent : IComponentData
    {
        public string value;
        public ClassWithClassFields nullField;

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public bool Equals(EcsTestManagedComponent other)
        {
            return value == other.value;
        }
    }

    public class EcsTestManagedComponent2 : EcsTestManagedComponent
    {
        public string value2;
    }

    public class EcsTestManagedComponent3 : EcsTestManagedComponent2
    {
        public string value3;
    }

    public class EcsTestManagedComponent4 : EcsTestManagedComponent3
    {
        public string value4;
    }

    public unsafe class EcsTestManagedCompWithRefCount : IComponentData, ICloneable, IDisposable
    {
        readonly int* RefCount;

        public EcsTestManagedCompWithRefCount()
        {
            RefCount = null;
        }

        public EcsTestManagedCompWithRefCount(int* refCount)
        {
            Assert.IsTrue(refCount != null);
            this.RefCount = refCount;
        }

        public object Clone()
        {
            Assert.IsTrue(RefCount != null);
            Interlocked.Increment(ref *RefCount);
            return this;
        }

        public void Dispose()
        {
            Assert.IsTrue(RefCount != null);
            Interlocked.Decrement(ref *RefCount);
        }
    }

#endif

    public partial struct EcsTestUpdateOneComponentJob : IJobEntity
    {
        public void Execute(ref EcsTestData ecsTestData)
        {
            ecsTestData.value++;
        }
    }

    public partial struct EcsTestUpdateTwoComponentsJob : IJobEntity
    {
        public void Execute(ref EcsTestData ecsTestData, ref EcsTestData2 ecsTestData2)
        {
            ecsTestData.value++;
            ecsTestData2.value0++;
        }
    }

    public partial struct EcsTestUpdateThreeComponentsJob : IJobEntity
    {
        public void Execute(ref EcsTestData ecsTestData, ref EcsTestData2 ecsTestData2, ref EcsTestData3 ecsTestData3)
        {
            ecsTestData.value++;
            ecsTestData2.value0++;
            ecsTestData3.value0++;
        }
    }

    public partial struct EcsTestUpdateOneComponentWithValuesFromOtherComponentsJob : IJobEntity
    {
        public void Execute(ref EcsTestData ecsTestData, in EcsTestData2 ecsTestData2, in EcsTestData3 ecsTestData3)
        {
            ecsTestData.value = ecsTestData2.value0 * ecsTestData3.value0 +
                                ecsTestData2.value1 * ecsTestData3.value1 +
                                ecsTestData3.value2;
        }
    }

    [BurstCompile(FloatPrecision.Standard, FloatMode.Default, CompileSynchronously = true)]
    public partial struct EcsTestSetComponentValueTo10 : IJobEntity
    {
        public void Execute(ref EcsTestData ecsTestData)
        {
            ecsTestData = new EcsTestData {value = 10};
        }
    }

    public partial struct EcsTestSetFirstComponentValueTo10 : IJobEntity
    {
        public void Execute(ref EcsTestData ecsTestData, ref EcsTestData2 ecsTestData2)
        {
            ecsTestData = new EcsTestData {value = 10};
        }
    }

    public partial struct EcsTestSetFirstComponentValueTo10_WithSharedComponent : IJobEntity
    {
        public void Execute(ref EcsTestData ecsTestData, ref EcsTestData2 ecsTestData2)
        {
            ecsTestData = new EcsTestData {value = 10};
        }
    }

    [NoAlias]
    [BurstCompile(FloatPrecision.Standard, FloatMode.Default, CompileSynchronously = true)]
    struct EcsTestSetComponentValueTo10_BaseLine : IJobEntityBatch
    {
        public ComponentTypeHandle<EcsTestData> EcsTestDataRW;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var data = batchInChunk.GetNativeArray(EcsTestDataRW);
            for (int i = 0; i < batchInChunk.Count; i++)
            {
                data[i] = new EcsTestData {value = 10};
            }
        }
    }
}
