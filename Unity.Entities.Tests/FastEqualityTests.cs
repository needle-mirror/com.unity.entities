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
    public unsafe class FastEqualityTests
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Simple
        {
            public int a;
            public int b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SimpleEmbedded
        {
            public float4 a;
            public int b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BytePadding
        {
            public byte a;
            public byte b;
            public float c;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct AlignSplit
        {
            public float3 a;
            public double b;
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
            public float* a;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SubStruct1
        {
            public Nephew a;
            public int b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SubStruct2
        {
            public Nephew a;
            public int b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SubStruct3
        {
            public int a;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SubStruct4
        {
            public int a;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SubStruct5 { }

        [StructLayout(LayoutKind.Sequential)]
        struct Nested1
        {
            public SubStruct1 a;
            public SubStruct2 b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Nested2
        {
            public SubStruct3 a;
            public SubStruct4 b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Nested3
        {
            public SubStruct1 a;
            public SubStruct3 b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Nested4
        {
            public SubStruct3 a;
            public SubStruct2 b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Nested5
        {
            public SubStruct1 a;
            public SubStruct1 b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Nested6
        {
            public SubStruct3 a;
            public SubStruct3 b;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Nested7
        {
            public int a;
            public SubStruct5 b;
            public int c;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Nested8
        {
            public Nephew a;
            public SubStruct5 b;
            public int c;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Nested9
        {
            public int a;
            public Simple b;
            public int c;
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

        class ParentAbstractGetHashCode : IEquatable<ParentAbstractGetHashCode>
        {
            public bool Equals(ParentAbstractGetHashCode other)
            {
                return false;
            }

            virtual public int GetHashCode(int someoverload)
            {
                return 0;
            }
        }

        class ChildComponentMultipleHashCodes : ParentAbstractGetHashCode
        {
            public override int GetHashCode()
            {
                int hash = 17;

                return hash;
            }
        }

        [Test]
        public unsafe void TestFindCorrectEqualityMethods()
        {
            using var ti = FastEquality.CreateTypeInfo(typeof(ChildComponentMultipleHashCodes));
            // Don't take the parent IEquatable methods
            Assert.IsTrue(ti.EqualsDelegateIndex == FastEquality.TypeInfo.Null.EqualsDelegateIndex);
            // Ensure we can find our hashcode without error
            Assert.IsTrue(ti.GetHashCodeDelegateIndex != FastEquality.TypeInfo.Null.GetHashCodeDelegateIndex);
        }

        [Test]
        public unsafe void ClassLayout()
        {
            using var ti = FastEquality.CreateTypeInfo(typeof(ClassInStruct));
            Assert.IsTrue(ti.EqualsDelegateIndex != FastEquality.TypeInfo.Null.EqualsDelegateIndex);
            Assert.IsTrue(ti.GetHashCodeDelegateIndex != FastEquality.TypeInfo.Null.GetHashCodeDelegateIndex);
        }

        [Test]
        public void EqualsInt4()
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(int4));

            var a  = new int4(1, 2, 3, 4);
            var aa = new int4(1, 2, 3, 4);
            var b = new int4(1, 2, 3, 5);
            var c = new int4(0, 2, 3, 4);
            var d = new int4(5, 6, 7, 8);
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref d), in typeInfo));
        }

        [Test]
        public void EqualsSimple()
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(Simple));
            var a = new Simple(){a = 1, b = 2};
            var aa = new Simple(){a = 1, b = 2};
            var b = new Simple(){a = 1, b = 3};
            var c = new Simple(){a = 4, b = 2};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsSimpleEmbedded()
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(SimpleEmbedded));
            var a = new SimpleEmbedded(){a = new float4(1), b = 2};
            var aa = new SimpleEmbedded(){a = new float4(1), b = 2};
            var b = new SimpleEmbedded(){a = new float4(1), b = 3};
            var c = new SimpleEmbedded(){a = new float4(4), b = 2};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsBytePadding()
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(BytePadding));
            var a = new BytePadding(){a = 1, b = 2};
            var aa = new BytePadding(){a = 1, b = 2};
            var b = new BytePadding(){a = 1, b = 3};
            var c = new BytePadding(){a = 4, b = 2};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsAlignSplit()
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(AlignSplit));
            var a = new AlignSplit(){a = new float3(1), b = 2};
            var aa = new AlignSplit(){a = new float3(1), b = 2};
            var b = new AlignSplit(){a = new float3(1), b = 3};
            var c = new AlignSplit(){a = new float3(4), b = 2};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsFloatPointer()
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(FloatPointer));
            var a = new FloatPointer(){a = (float*) 1};
            var aa = new FloatPointer(){a = (float*) 1};
            var b = new FloatPointer(){a = (float*) 3};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
        }

        [Test]
        public void EqualsPadding()
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(EndPadding));
            var a = new EndPadding(1, 2);
            var b = new EndPadding(1, 2);
            var c = new EndPadding(1, 3);
            var d = new EndPadding(4, 2);
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref d), in typeInfo));
        }

        [Test]
        public void EqualsNested1() // Offsets are properly propagated
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(Nested1));
            var a = new Nested1(){a = new SubStruct1(){a = Nephew.Dewey, b = 1}, b = new SubStruct2(){a = Nephew.Huey, b = 2}};
            var aa = new Nested1(){a = new SubStruct1(){a = Nephew.Dewey, b = 1}, b = new SubStruct2(){a = Nephew.Huey, b = 2}};
            var b = new Nested1(){a = new SubStruct1(){a = Nephew.Dewey, b = 1}, b = new SubStruct2(){a = Nephew.Huey, b = 3}};
            var c = new Nested1(){a = new SubStruct1(){a = Nephew.Dewey, b = 1}, b = new SubStruct2(){a = Nephew.Louie, b = 2}};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsNested2() // Offsets are properly propagated
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(Nested2));
            var a = new Nested2(){a = new SubStruct3(){a = 2}, b = new SubStruct4(){a = 2}};
            var aa = new Nested2(){a = new SubStruct3(){a = 2}, b = new SubStruct4(){a = 2}};
            var b = new Nested2(){a = new SubStruct3(){a = 3}, b = new SubStruct4(){a = 3}};
            var c = new Nested2(){a = new SubStruct3(){a = 3}, b = new SubStruct4(){a = 2}};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsNested3()  // Offsets are properly propagated
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(Nested3));
            var a = new Nested3(){a = new SubStruct1(){a = Nephew.Dewey, b = 1}, b = new SubStruct3(){a = 2}};
            var aa = new Nested3(){a = new SubStruct1(){a = Nephew.Dewey, b = 1}, b = new SubStruct3(){a = 2}};
            var b = new Nested3(){a = new SubStruct1(){a = Nephew.Dewey, b = 1}, b = new SubStruct3(){a = 3}};
            var c = new Nested3(){a = new SubStruct1(){a = Nephew.Dewey, b = 3}, b = new SubStruct3(){a = 2}};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsNested4() // Offsets are properly propagated
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(Nested4));
            var a = new Nested4(){a = new SubStruct3(){a = 2}, b = new SubStruct2(){a = Nephew.Huey, b = 2}};
            var aa = new Nested4(){a = new SubStruct3(){a = 2}, b = new SubStruct2(){a = Nephew.Huey, b = 2}};
            var b = new Nested4(){a = new SubStruct3(){a = 2}, b = new SubStruct2(){a = Nephew.Huey, b = 3}};
            var c = new Nested4(){a = new SubStruct3(){a = 2}, b = new SubStruct2(){a = Nephew.Louie, b = 2}};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsNested5() // Should test that cached versions also go right
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(Nested5));
            var a = new Nested5(){a = new SubStruct1(){a = Nephew.Dewey, b = 1}, b = new SubStruct1(){a = Nephew.Huey, b = 2}};
            var aa = new Nested5(){a = new SubStruct1(){a = Nephew.Dewey, b = 1}, b = new SubStruct1(){a = Nephew.Huey, b = 2}};
            var b = new Nested5(){a = new SubStruct1(){a = Nephew.Dewey, b = 1}, b = new SubStruct1(){a = Nephew.Huey, b = 3}};
            var c = new Nested5(){a = new SubStruct1(){a = Nephew.Dewey, b = 3}, b = new SubStruct1(){a = Nephew.Louie, b = 2}};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsNested6() // Should test that cached versions also go right
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(Nested6));
            var a = new Nested6(){a = new SubStruct3(){a = 1}, b = new SubStruct3(){a = 2}};
            var aa = new Nested6(){a = new SubStruct3(){a = 1}, b = new SubStruct3(){a = 2}};
            var b = new Nested6(){a = new SubStruct3(){a = 1}, b = new SubStruct3(){a = 3}};
            var c = new Nested6(){a = new SubStruct3(){a = 3}, b = new SubStruct3(){a = 2}};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsNested7() // Offsets for empty structs are correct
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(Nested7));
            var a = new Nested7(){a = 1, b = new SubStruct5(), c = 2};
            var aa = new Nested7(){a =1, b = new SubStruct5(), c = 2};
            var b = new Nested7(){a = 1, b = new SubStruct5(), c = 4};
            var c = new Nested7(){a = 3, b = new SubStruct5(), c = 2};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsNested8() // Offsets for empty structs are correct
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(Nested8));
            var a = new Nested8(){a = Nephew.Dewey, b = new SubStruct5(), c = 2};
            var aa = new Nested8(){a = Nephew.Dewey, b = new SubStruct5(), c = 2};
            var b = new Nested8(){a = Nephew.Dewey, b = new SubStruct5(), c = 4};
            var c = new Nested8(){a = Nephew.Louie, b = new SubStruct5(), c = 2};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public void EqualsNested9() // All Copy commands are merged
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(Nested9));
            var a = new Nested9(){a = 1, b = new Simple(), c = 2};
            var aa = new Nested9(){a = 1, b = new Simple(), c = 2};
            var b = new Nested9(){a = 1, b = new Simple(), c = 4};
            var c = new Nested9(){a = 3, b = new Simple(), c = 2};
            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref c), in typeInfo));
        }

        [Test]
        public unsafe void EqualsFixedArray()
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(FixedArrayStruct));

            var a = new FixedArrayStruct();
            var b = new FixedArrayStruct();
            a.array[0] = b.array[0] = 123;
            a.array[1] = b.array[1] = 234;
            a.array[2] = b.array[2] = 345;

            Assert.IsTrue(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));

            b.array[1] = 456;

            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo));
        }

        [Test]
        public void EqualsEnum()
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(EnumStruct));

            var a = new EnumStruct { nephew = Nephew.Huey };
            var aa = new EnumStruct { nephew = Nephew.Huey };
            var b = new EnumStruct { nephew = Nephew.Dewey };

            Assert.IsTrue (FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b),  in typeInfo));
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
            public int Value;

            public bool Equals(DoubleEquals other)
            {
                return Value == other.Value;
            }

            public override bool Equals(object obj)
            {
                if(obj is DoubleEquals)
                    return Value == ((DoubleEquals)obj).Value;
                return false;
            }

            public override int GetHashCode()
            {
                return Value;
            }
        }

        [Test]
        public void CorrectEqualsIsUsed()
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(DoubleEquals));
            var a = new DoubleEquals {};
            var b = new DoubleEquals {};
            bool iseq = FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo);

            Assert.IsTrue(iseq);
        }

        [BurstCompile(CompileSynchronously = true)]
        private static unsafe bool EqualsBurst(ref Entity a, ref Entity b, ref FastEquality.TypeInfo typeInfo)
        {
            return FastEquality.Equals(UnsafeUtility.AddressOf(ref a), UnsafeUtility.AddressOf(ref b), in typeInfo);
        }

        [BurstCompile(CompileSynchronously = true)]
        private static unsafe int GetHashCodeBurst(ref Entity a, ref FastEquality.TypeInfo typeInfo)
        {
            return FastEquality.GetHashCode(UnsafeUtility.AddressOf(ref a), in typeInfo);
        }

        [Test]
        public void FastEqualityWorksFromBurst()
        {
            TypeManager.Initialize();
            var typeInfo = FastEquality.CreateTypeInfo(typeof(Entity));

            Entity a1 = new Entity() {Index = 1, Version = 2};
            Entity a2 = a1;
            Entity b = new Entity() {Index = 2, Version = 1};
            var monoA1EqualsA2 = FastEquality.Equals(UnsafeUtility.AddressOf(ref a1), UnsafeUtility.AddressOf(ref a2), in typeInfo);
            var monoA1EqualsB = FastEquality.Equals(UnsafeUtility.AddressOf(ref a1), UnsafeUtility.AddressOf(ref b), in typeInfo);
            var burstA1EqualsA2 = EqualsBurst(ref a1, ref a2, ref typeInfo);
            var burstA1EqualsB = EqualsBurst(ref a1, ref b, ref typeInfo);

            Assert.IsTrue(monoA1EqualsA2);
            Assert.IsTrue(burstA1EqualsA2);
            Assert.IsFalse(monoA1EqualsB);
            Assert.IsFalse(burstA1EqualsB);

            var monoA1GetHashCode = FastEquality.GetHashCode(UnsafeUtility.AddressOf(ref a1), in typeInfo);
            var monoA2GetHashCode = FastEquality.GetHashCode(UnsafeUtility.AddressOf(ref a2), in typeInfo);
            var monoBGetHashCode = FastEquality.GetHashCode(UnsafeUtility.AddressOf(ref b), in typeInfo);
            var burstA1GetHashCode = GetHashCodeBurst(ref a1, ref typeInfo);
            var burstA2GetHashCode = GetHashCodeBurst(ref a2, ref typeInfo);
            var burstBGetHashCode = GetHashCodeBurst(ref b, ref typeInfo);

            Assert.AreEqual(monoA1GetHashCode, burstA1GetHashCode);
            Assert.AreEqual(monoA2GetHashCode, burstA2GetHashCode);
            Assert.AreEqual(monoBGetHashCode, burstBGetHashCode);
            Assert.AreEqual(monoA1GetHashCode, monoA2GetHashCode);
            Assert.AreNotEqual(monoA1GetHashCode, monoBGetHashCode);
            typeInfo.Dispose();
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
                using var typeInfo = FastEquality.CreateTypeInfo(typeof(EcsTestManagedComponent));
                var obj1 = new EcsTestManagedComponent() { value = "SomeString" };
                var obj12 = new EcsTestManagedComponent() { value = "SomeString" };
                var obj2 = new EcsTestManagedComponent() { value = "SomeOtherString" };
                Assert.IsTrue(FastEquality.ManagedEquals(obj1, obj1, typeInfo));
                Assert.IsTrue(FastEquality.ManagedEquals(obj1, obj12, typeInfo));
                Assert.IsFalse(FastEquality.ManagedEquals(obj1, obj2, typeInfo));
            }

            {
                using var typeInfo = FastEquality.CreateTypeInfo(typeof(ComponentWithUnityObjectArray));
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
                using var typeInfo = FastEquality.CreateTypeInfo(typeof(ComponentImplementsIEquatable));
                var obj = new ComponentImplementsIEquatable();
                Assert.IsTrue(FastEquality.ManagedEquals(obj, obj, typeInfo));
                Assert.IsTrue(obj.EqualsWasCalled);
            }
        }

        [Test]
        public void ManagedComponentGetHashCode()
        {
            {
                using var typeInfo = FastEquality.CreateTypeInfo(typeof(EcsTestManagedComponent));
                var obj1 = new EcsTestManagedComponent() { value = "SomeString" };
                var obj12 = new EcsTestManagedComponent() { value = "SomeString" };
                var obj2 = new EcsTestManagedComponent() { value = "SomeOtherString" };
                Assert.AreEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj1, typeInfo));
                Assert.AreEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj12, typeInfo));
                Assert.AreNotEqual(FastEquality.ManagedGetHashCode(obj1, typeInfo), FastEquality.ManagedGetHashCode(obj2, typeInfo));
            }

            {
                using var typeInfo = FastEquality.CreateTypeInfo(typeof(ComponentWithUnityObjectArray));
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
                using var typeInfo = FastEquality.CreateTypeInfo(typeof(ComponentOverridesGetHashCode));
                var obj = new ComponentOverridesGetHashCode();
                Assert.AreEqual(FastEquality.ManagedGetHashCode(obj, typeInfo), FastEquality.ManagedGetHashCode(obj, typeInfo));
                Assert.IsTrue(obj.GetHashCodeWasCalled);
            }
        }


        public struct GenericComponent<T> : IComponentData
            where T : unmanaged
        {
            public byte mByte;
            public T mT;
            public int mInt;
        }

        static unsafe void ValidateEqualsForGeneric<T>(T val) where T : unmanaged
        {
            using var typeInfo = FastEquality.CreateTypeInfo(typeof(GenericComponent<T>));
            var aa = default(GenericComponent<T>);
            var ab = default(GenericComponent<T>);
            var ba = default(GenericComponent<T>);
            var bb = default(GenericComponent<T>);
            UnsafeUtility.MemSet(&aa, 0xDD, UnsafeUtility.SizeOf<GenericComponent<T>>());
            UnsafeUtility.MemSet(&ba, 0xDD, UnsafeUtility.SizeOf<GenericComponent<T>>());

            UnsafeUtility.MemSet(&ab, 0x77, UnsafeUtility.SizeOf<GenericComponent<T>>());
            UnsafeUtility.MemSet(&bb, 0x77, UnsafeUtility.SizeOf<GenericComponent<T>>());

            aa.mByte = ab.mByte = 0x11;
            aa.mT = ab.mT = val;
            aa.mInt = ab.mInt = 0x22222222;

            ba.mByte = bb.mByte = 0x22;
            ba.mT = bb.mT = val;
            ba.mInt = bb.mInt = 0x22222222;

            Assert.IsTrue(FastEquality.Equals(UnsafeUtility.AddressOf(ref aa), UnsafeUtility.AddressOf(ref aa), in typeInfo));
            Assert.IsTrue(FastEquality.Equals(UnsafeUtility.AddressOf(ref aa), UnsafeUtility.AddressOf(ref ab), in typeInfo));

            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref aa), UnsafeUtility.AddressOf(ref ba), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref aa), UnsafeUtility.AddressOf(ref bb), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref ab), UnsafeUtility.AddressOf(ref ba), in typeInfo));
            Assert.IsFalse(FastEquality.Equals(UnsafeUtility.AddressOf(ref ab), UnsafeUtility.AddressOf(ref bb), in typeInfo));
        }

        [Test]
        public unsafe void EqualsGenericComponentPadding()
        {
            ValidateEqualsForGeneric<byte>(0xAA);
            ValidateEqualsForGeneric<ushort>(0xAAAA);
            ValidateEqualsForGeneric<uint>(0xAAAAAAAA);
            ValidateEqualsForGeneric<ulong>(0xAAAAAAAAAAAAAAAA);
        }
#endif
    }
}
