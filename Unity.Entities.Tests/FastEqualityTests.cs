#if !UNITY_DOTSRUNTIME // No UnsafeUtility.GetFieldOffset, so FastEquality doesn't implement CreateTypeInfo() even with Tiny BCL
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System;
using System.Collections.Generic;
using Unity.Burst;

namespace Unity.Entities.Tests
{
    [BurstCompile]
    public class FastEqualityTests
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Simple
        {
            int a;
            int b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SimpleEmbedded
        {
            float4 a;
            int b;
        }

        [StructLayout(LayoutKind.Sequential)]

        struct BytePadding
        {
            byte a;
            byte b;
            float c;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct AlignSplit
        {
            float3 a;
            double b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct EndPadding
        {
            double a;
            float b;

            public EndPadding(double a, float b)
            {
                this.a = a;
                this.b = b;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        unsafe struct FloatPointer
        {
            float* a;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ClassInStruct : IEquatable<ClassInStruct>
        {
            string blah;

            public bool Equals(ClassInStruct other)
            {
                return blah == other.blah;
            }

            public override int GetHashCode()
            {
                if (blah != null)
                    return blah.GetHashCode();
                else
                    return 0;
            }
        }

        [StructLayout((LayoutKind.Sequential))]
        unsafe struct FixedArrayStruct
        {
            public fixed int array[3];
        }

        enum Nephew
        {
            Huey,
            Dewey,
            Louie
        }

        [StructLayout((LayoutKind.Sequential))]
        struct EnumStruct
        {
            public Nephew nephew;
        }

        [Test]
        public void ClassLayout()
        {
            var ti = FastEquality.CreateTypeInfo(typeof(ClassInStruct));
            Assert.IsNotNull(ti.GetHashFn);
            Assert.IsNotNull(ti.EqualFn);
        }

        [Test]
        public void EqualsInt4()
        {
            var typeInfo = FastEquality.CreateTypeInfo(typeof(int4));

            Assert.IsTrue(FastEquality.Equals(new int4(1, 2, 3, 4), new int4(1, 2, 3, 4), typeInfo));
            Assert.IsFalse(FastEquality.Equals(new int4(1, 2, 3, 4), new int4(1, 2, 3, 5), typeInfo));
            Assert.IsFalse(FastEquality.Equals(new int4(1, 2, 3, 4), new int4(0, 2, 3, 4), typeInfo));
            Assert.IsFalse(FastEquality.Equals(new int4(1, 2, 3, 4), new int4(5, 6, 7, 8), typeInfo));
        }

        [Test]
        public void EqualsPadding()
        {
            var typeInfo = FastEquality.CreateTypeInfo(typeof(EndPadding));

            Assert.IsTrue(FastEquality.Equals(new EndPadding(1, 2), new EndPadding(1, 2), typeInfo));
            Assert.IsFalse(FastEquality.Equals(new EndPadding(1, 2), new EndPadding(1, 3), typeInfo));
            Assert.IsFalse(FastEquality.Equals(new EndPadding(1, 2), new EndPadding(4, 2), typeInfo));
        }

        [Test]
        public unsafe void EqualsFixedArray()
        {
            var typeInfo = FastEquality.CreateTypeInfo(typeof(FixedArrayStruct));

            var a = new FixedArrayStruct();
            a.array[0] = 123;
            a.array[1] = 234;
            a.array[2] = 345;

            var b = a;

            Assert.IsTrue(FastEquality.Equals(a, b, typeInfo));

            b.array[1] = 456;

            Assert.IsFalse(FastEquality.Equals(a, b, typeInfo));
        }

        [Test]
        public void EqualsEnum()
        {
            var typeInfo = FastEquality.CreateTypeInfo(typeof(EnumStruct));

            var a = new EnumStruct { nephew = Nephew.Huey };
            var b = new EnumStruct { nephew = Nephew.Dewey };

            Assert.IsTrue(FastEquality.Equals(a, a, typeInfo));
            Assert.IsFalse(FastEquality.Equals(a, b, typeInfo));
        }

        [DisableAutoTypeRegistration]
        struct TypeWithoutHashCodeOverride : ISharedComponentData, IEquatable<TypeWithoutHashCodeOverride>
        {
#pragma warning disable 649
            public string Foo;
#pragma warning restore 649

            public bool Equals(TypeWithoutHashCodeOverride other)
            {
                return Foo == other.Foo;
            }
        }

        [Test]
        public void ForgettingGetHashCodeIsAnError()
        {
            var ex = Assert.Throws<ArgumentException>(() => FastEquality.CreateTypeInfo(typeof(TypeWithoutHashCodeOverride)));
            Assert.IsTrue(ex.Message.Contains("GetHashCode"));
        }

        struct DoubleEquals : ISharedComponentData, IEquatable<DoubleEquals>
        {
            public bool Equals(DoubleEquals other)
            {
                return true;
            }

            public override bool Equals(object obj)
            {
                return false;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }

        [Test]
        public void CorrectEqualsIsUsed()
        {
            var typeInfo = FastEquality.CreateTypeInfo(typeof(DoubleEquals));
            var a = new DoubleEquals {};
            var b = new DoubleEquals {};
            bool iseq = FastEquality.Equals<DoubleEquals>(a, b, typeInfo);

            Assert.IsTrue(iseq);
        }

        private unsafe delegate bool _dlg_CallsEquals(ref Entity a, ref Entity b);
        private unsafe delegate int _dlg_CallsGetHashCode(ref Entity a);
        private static readonly FunctionPointer<_dlg_CallsEquals> BurstedTestFuncCallingEquals;
        private static readonly FunctionPointer<_dlg_CallsGetHashCode> BurstedTestFuncCallingGetHashCode;

        static unsafe FastEqualityTests()
        {
            BurstedTestFuncCallingEquals = BurstCompiler.CompileFunctionPointer<_dlg_CallsEquals>(_mono_to_burst_CallEquals);
            BurstedTestFuncCallingGetHashCode = BurstCompiler.CompileFunctionPointer<_dlg_CallsGetHashCode>(_mono_to_burst_CallGetHashCode);
        }

        [BurstCompile(CompileSynchronously = true)]
        private static unsafe bool _mono_to_burst_CallEquals(ref Entity a, ref Entity b)
        {
            return FastEquality.Equals(a, b);
        }

        [BurstCompile(CompileSynchronously = true)] 
        private static unsafe int _mono_to_burst_CallGetHashCode(ref Entity a)
        {
            return FastEquality.GetHashCode(a);
        }

        [Test]
        public void FastEqualityWorksFromBurst()
        {
            TypeManager.Initialize();

            Entity a1 = new Entity() {Index = 1, Version = 2};
            Entity a2 = a1;
            Entity b = new Entity() {Index = 2, Version = 1};
            var monoA1EqualsA2 = FastEquality.Equals(a1, a2);
            var monoA1EqualsB = FastEquality.Equals(a1, b);
            var burstA1EqualsA2 = BurstedTestFuncCallingEquals.Invoke(ref a1, ref a2);
            var burstA1EqualsB = BurstedTestFuncCallingEquals.Invoke(ref a1, ref b);

            Assert.IsTrue(monoA1EqualsA2);
            Assert.IsTrue(burstA1EqualsA2);
            Assert.IsFalse(monoA1EqualsB);
            Assert.IsFalse(burstA1EqualsB);

            var monoA1GetHashCode = FastEquality.GetHashCode(a1);
            var monoA2GetHashCode = FastEquality.GetHashCode(a2);
            var monoBGetHashCode = FastEquality.GetHashCode(b);
            var burstA1GetHashCode = BurstedTestFuncCallingGetHashCode.Invoke(ref a1);
            var burstA2GetHashCode = BurstedTestFuncCallingGetHashCode.Invoke(ref a2);
            var burstBGetHashCode = BurstedTestFuncCallingGetHashCode.Invoke(ref b);

            Assert.AreEqual(monoA1GetHashCode, burstA1GetHashCode);
            Assert.AreEqual(monoA2GetHashCode, burstA2GetHashCode);
            Assert.AreEqual(monoBGetHashCode, burstBGetHashCode);
            Assert.AreEqual(monoA1GetHashCode, monoA2GetHashCode);
            Assert.AreNotEqual(monoA1GetHashCode, monoBGetHashCode);
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        class ComponentWithUnityObjectArray : IComponentData
        {
            public UnityEngine.Texture2D Texture;
            public UnityEngine.Object[] Objects;
            public List<string> Strings;
        }

        class ComponentOverridesGetHashCode : IComponentData
        {
            public bool GetHashCodeWasCalled = false;

            public override int GetHashCode()
            {
                GetHashCodeWasCalled = true;
                return base.GetHashCode();
            }
        }

        class ComponentImplementsIEquatable : IComponentData, IEquatable<ComponentImplementsIEquatable>
        {
            public bool EqualsWasCalled = false;
            public bool Equals(ComponentImplementsIEquatable other)
            {
                bool result = other.EqualsWasCalled == EqualsWasCalled;
                EqualsWasCalled = true;
                return result;
            }
        }

        [Test]
        public void ManagedComponentEquals()
        {
            {
                var typeInfo = FastEquality.CreateTypeInfo(typeof(EcsTestManagedComponent));
                var obj1 = new EcsTestManagedComponent() { value = "SomeString" };
                var obj12 = new EcsTestManagedComponent() { value = "SomeString" };
                var obj2 = new EcsTestManagedComponent() { value = "SomeOtherString" };
                Assert.IsTrue(FastEquality.ManagedEquals(obj1, obj1, typeInfo));
                Assert.IsTrue(FastEquality.ManagedEquals(obj1, obj12, typeInfo));
                Assert.IsFalse(FastEquality.ManagedEquals(obj1, obj2, typeInfo));
            }

            {
                var typeInfo = FastEquality.CreateTypeInfo(typeof(ComponentWithUnityObjectArray));
                var tex1 = new UnityEngine.Texture2D(512, 512);
                var tex2 = new UnityEngine.Texture2D(512, 512);
                var tex3 = new UnityEngine.Texture2D(512, 512);
                var stringList1  = new List<string>() { "yo", "hi", "hej", "hello" };
                var stringList12 = new List<string>() { "yo", "hi", "hej", "hello" };
                var stringList2  = new List<string>() { "yo", "hi", "hey", "hello" };

                var obj1  = new ComponentWithUnityObjectArray() { Strings = stringList1,  Texture = tex1, Objects = new[] { tex2, tex3 } };
                var obj12 = new ComponentWithUnityObjectArray() { Strings = stringList1,  Texture = tex1, Objects = new[] { tex2, tex3 } };
                var obj13 = new ComponentWithUnityObjectArray() { Strings = stringList12, Texture = tex1, Objects = new[] { tex2, tex3 } };
                var obj2  = new ComponentWithUnityObjectArray() { Strings = stringList1,  Texture = tex1, Objects = new[] { tex2, tex2 } };
                var obj3  = new ComponentWithUnityObjectArray() { Strings = stringList1,  Texture = tex1, Objects = new[] { tex1, null } };
                var obj4  = new ComponentWithUnityObjectArray() { Strings = stringList1,  Texture = tex1, Objects = new[] { tex1 }};
                var obj5  = new ComponentWithUnityObjectArray() { Strings = stringList2,  Texture = tex1, Objects = new[] { tex2, tex3 } };
                var obj6  = new ComponentWithUnityObjectArray() { Strings = stringList2,  Texture = tex2, Objects = new[] { tex3, tex1 } };
                Assert.IsTrue(FastEquality.ManagedEquals(obj1, obj1, typeInfo));
                Assert.IsTrue(FastEquality.ManagedEquals(obj1, obj12, typeInfo));
                Assert.IsTrue(FastEquality.ManagedEquals(obj1, obj13, typeInfo));
                Assert.IsFalse(FastEquality.ManagedEquals(obj1, obj2, typeInfo));
                Assert.IsFalse(FastEquality.ManagedEquals(obj1, obj3, typeInfo));
                Assert.IsFalse(FastEquality.ManagedEquals(obj1, obj4, typeInfo));
                Assert.IsFalse(FastEquality.ManagedEquals(obj1, obj5, typeInfo));
                Assert.IsFalse(FastEquality.ManagedEquals(obj1, obj6, typeInfo));
            }

            {
                var typeInfo = FastEquality.CreateTypeInfo(typeof(ComponentImplementsIEquatable));
                var obj = new ComponentImplementsIEquatable();
                Assert.IsTrue(FastEquality.ManagedEquals(obj, obj, typeInfo));
                Assert.IsTrue(obj.EqualsWasCalled);
            }
        }

        [Test]
        public void ManagedComponentGetHashCode()
        {
            {
                var typeInfo = FastEquality.CreateTypeInfo(typeof(EcsTestManagedComponent));
                var obj1 = new EcsTestManagedComponent() { value = "SomeString" };
                var obj12 = new EcsTestManagedComponent() { value = "SomeString" };
                var obj2 = new EcsTestManagedComponent() { value = "SomeOtherString" };
                Assert.AreEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj1, typeInfo));
                Assert.AreEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj12, typeInfo));
                Assert.AreNotEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj2, typeInfo));
            }

            {
                var typeInfo = FastEquality.CreateTypeInfo(typeof(ComponentWithUnityObjectArray));
                var tex1 = new UnityEngine.Texture2D(512, 512);
                var tex2 = new UnityEngine.Texture2D(512, 512);
                var tex3 = new UnityEngine.Texture2D(512, 512);
                var stringList1  = new List<string>() { "yo", "hi", "hej", "hello" };
                var stringList12 = new List<string>() { "yo", "hi", "hej", "hello" };
                var stringList2  = new List<string>() { "yo", "hi", "hey", "hello" };

                var obj1  = new ComponentWithUnityObjectArray() { Strings = stringList1,  Texture = tex1, Objects = new[] { tex2, tex3 } };
                var obj12 = new ComponentWithUnityObjectArray() { Strings = stringList1,  Texture = tex1, Objects = new[] { tex2, tex3 } };
                var obj13 = new ComponentWithUnityObjectArray() { Strings = stringList12, Texture = tex1, Objects = new[] { tex2, tex3 } };
                var obj2  = new ComponentWithUnityObjectArray() { Strings = stringList1,  Texture = tex1, Objects = new[] { tex2, tex2 } };
                var obj3  = new ComponentWithUnityObjectArray() { Strings = stringList1,  Texture = tex1, Objects = new[] { tex1, null } };
                var obj4  = new ComponentWithUnityObjectArray() { Strings = stringList1,  Texture = tex1, Objects = new[] { tex1 } };
                var obj5  = new ComponentWithUnityObjectArray() { Strings = stringList2,  Texture = tex1, Objects = new[] { tex2, tex3 } };
                var obj6  = new ComponentWithUnityObjectArray() { Strings = stringList2,  Texture = tex2, Objects = new[] { tex3, tex1 } };
                Assert.AreEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj1, typeInfo));
                Assert.AreEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj12, typeInfo));
                Assert.AreEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj13, typeInfo));
                Assert.AreNotEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj2, typeInfo));
                Assert.AreNotEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj3, typeInfo));
                Assert.AreNotEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj4, typeInfo));
                Assert.AreNotEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj5, typeInfo));
                Assert.AreNotEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj6, typeInfo));
            }

            {
                var typeInfo = FastEquality.CreateTypeInfo(typeof(ComponentOverridesGetHashCode));
                var obj = new ComponentOverridesGetHashCode();
                Assert.AreEqual(FastEquality.ManagedGetHashCode(obj, typeInfo), FastEquality.ManagedGetHashCode(obj, typeInfo));
                Assert.IsTrue(obj.GetHashCodeWasCalled);
            }
        }

#endif
    }
}
#endif
