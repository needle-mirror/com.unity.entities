using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Entities.Tests;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

#pragma warning disable 649

[assembly: RegisterGenericComponentType(typeof(TypeManagerTests.GenericComponent<int>))]
[assembly: RegisterGenericComponentType(typeof(TypeManagerTests.GenericComponent<short>))]
[assembly: RegisterGenericComponentType(typeof(TypeManagerTests.GenericComponent<Entity>))]
[assembly: RegisterGenericComponentType(typeof(TypeManagerTests.GenericComponent<BlobAssetReference<float>>))]
[assembly: RegisterGenericComponentType(typeof(TypeManagerTests.GenericComponent<NativeArray<int>>))]



namespace Unity.Entities.Tests
{
    partial class TypeManagerTests : ECSTestsFixture
    {
        internal unsafe struct MyGenericPointerStruct<T> where T : unmanaged
        {
#pragma warning disable 8500
            T* ptr;
#pragma warning restore 8500
        }

        internal struct MyIEquatableTupleMember : IEquatable<MyIEquatableTupleMember>
        {
            int x;

            public bool Equals(MyIEquatableTupleMember other)
            {
                throw new NotImplementedException();
            }
        }

        /*
         * this exercises the type traversal logic in the typemanager ILPP; it catches a bug
         * that only triggered when one of the members of the tuple implemented IEquatable<T>
         */
#if !UNITY_DISABLE_MANAGED_COMPONENTS
        internal class TestClassTypeWithArrayOfTuples: IComponentData
        {
            internal (int x, MyIEquatableTupleMember y)[] MyArray;
        }

        internal class ClassWithCircularSelfReference
        {
            internal ClassWithCircularSelfReference[] array;
            int otherfield;
        }

        //this catches a bug that would cause the ilpp to stack overflow trying to
        //incorrectly traverse the layout of managed ISCD for no reason
        internal struct TestStructISCDWithCircularClassReference : ISharedComponentData, IEquatable<TestStructISCDWithCircularClassReference>
        {
            ClassWithCircularSelfReference field;

            public bool Equals(TestStructISCDWithCircularClassReference other)
            {
                throw new NotImplementedException();
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

        }
#endif

        public struct TestTypeWithFunkyIEQOverride : ISharedComponentData, IEquatable<TestTypeWithFunkyIEQOverride>
        {
            public int x;
            bool System.IEquatable<TestTypeWithFunkyIEQOverride>.Equals(Unity.Entities.Tests.TypeManagerTests.TestTypeWithFunkyIEQOverride other)
            {
                return x == other.x;
            }
            public override int GetHashCode()
            {
                return x;
            }
        }

        public struct ZeroSizedTestTypeWithStatic : IComponentData
        {
            public static readonly int test = 1;
        }

        public struct TestTypeWithGCHandle : IComponentData
        {
            public GCHandle handle;
        }

        public struct TestTypeWithGuid : IComponentData
        {
            public Guid guid;
        }
        internal struct TestType1 : IComponentData
        {
            int empty;
        }
        struct TestTypeWithEntity : IComponentData
        {
            Entity Entity;
        }
        struct TestTypeWithBlobRef : IComponentData
        {
            BlobAssetReference<float> Blobb;
        }
        struct TestTypeWithNativeContainer : IComponentData
        {
            NativeArray<int> array;
        }

        struct TestType2 : IComponentData
        {
            int empty;
        }

        struct TestTypeWithBool : IComponentData, IEquatable<TestTypeWithBool>
        {
            bool empty;

            public bool Equals(TestTypeWithBool other)
            {
                return other.empty == empty;
            }

            public override int GetHashCode()
            {
                return empty.GetHashCode();
            }
        }

        internal class AttributeWithStringArgumentAttribute : Attribute
        {
            internal AttributeWithStringArgumentAttribute(string argument) { }
        }



        [AttributeWithStringArgument(null)]
        partial class SystemWithAttributeWithNullString : SystemBase
        {
            protected override void OnCreate()
            {
                throw new NotImplementedException();
            }
            protected override void OnUpdate()
            {
                throw new NotImplementedException();
            }
            protected override void OnDestroy()
            {
                throw new NotImplementedException();
            }
        }


#nullable enable

        // this will make sure the typemanager tolerates ICD & MB's coexisting.
        class CombinedMBAndICD : MonoBehaviour, IComponentData
        {

        }

        [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation, WorldSystemFilterFlags.Default)]
        [UpdateAfter(typeof(TestComponentSystem))]
        partial class NullableSystem : SystemBase
        {
#pragma warning disable 0067
            //without a field like this, roslyn doesn't emit the nullable and nullablecontext attributes that
            //exercise the fixed logic in typemanager. so, don't delete it or its usage!
            private static event Action? Quitting;

            private static void OnQuit()
            {
                Quitting?.Invoke();
            }
#pragma warning restore 0067
            internal void noop() { }
            protected override void OnCreate()
            {
                throw new NotImplementedException();
            }
            protected override void OnUpdate()
            {
                throw new NotImplementedException();
            }
            protected override void OnDestroy()
            {
                throw new NotImplementedException();
            }
        }

        class ThingUsingNullableSystem
        {
            static NullableSystem?[]? field;
            private static void Method()
            {
                if (field != null)
                {
                    foreach (var s in field)
                    {
                        s?.noop();
                    }
                }
            }
        }
#nullable disable

        struct TestTypeWithChar : IComponentData, IEquatable<TestTypeWithChar>
        {
            char empty;

            public bool Equals(TestTypeWithChar other)
            {
                return empty == other.empty;
            }

            public override int GetHashCode()
            {
                return empty.GetHashCode();
            }
        }

        public struct GenericComponent<T> : IComponentData
        {
            T value;
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        public class TestTypeBaseClass : IComponentData, IEquatable<TestTypeBaseClass>
        {
            int empty;
            public bool Equals(TestTypeBaseClass other)
            {
                return empty == other.empty;
            }

            public override int GetHashCode()
            {
                return empty.GetHashCode();
            }
        }

        public class TestTypeSubClass1 : TestTypeBaseClass, IEquatable<TestTypeSubClass1>
        {
            int empty;
            public bool Equals(TestTypeSubClass1 other)
            {
                return empty == other.empty;
            }

            public override int GetHashCode()
            {
                return empty.GetHashCode();
            }

        }

        public class TestTypeSubClass2 : TestTypeBaseClass, IEquatable<TestTypeSubClass2>
        {
            int empty;
            public bool Equals(TestTypeSubClass2 other)
            {
                return empty == other.empty;
            }

            public override int GetHashCode()
            {
                return empty.GetHashCode();
            }

        }

        public class TestTypeSubSubClass1 : TestTypeSubClass1, IEquatable<TestTypeSubSubClass1>
        {
            int empty;
            public bool Equals(TestTypeSubSubClass1 other)
            {
                return empty == other.empty;
            }

            public override int GetHashCode()
            {
                return empty.GetHashCode();
            }

        }

        public class TestTypeSubSubClass2 : TestTypeSubClass2, IEquatable<TestTypeSubSubClass2>
        {
            int empty;
            public bool Equals(TestTypeSubSubClass2 other)
            {
                return empty == other.empty;
            }

            public override int GetHashCode()
            {
                return empty.GetHashCode();
            }

        }

        public class TestTypeSingleClass : IComponentData, IEquatable<TestTypeSingleClass>
        {
            int empty;

            public bool Equals(TestTypeSingleClass other)
            {
                return empty == other.empty;
            }

            public override int GetHashCode()
            {
                return empty.GetHashCode();
            }
        }

        public static unsafe void AssertWeakAssetRefOffsetIs(in TypeManager.TypeInfo ti, int expectedOffset)
        {
            var y = TypeManager.GetWeakAssetRefOffsets(ti);
            Assert.AreEqual(1, ti.WeakAssetRefOffsetCount);
            Assert.AreEqual(8, y[0].Offset);
        }

        [Test]
        public void ZeroSizedTypeWithStatic_IsZeroSized()
        {
            Assert.IsTrue(TypeManager.IsZeroSized(TypeManager.GetTypeIndex<ZeroSizedTestTypeWithStatic>()));
        }

        [Test]
        public void TypeWithGCHandle_HasCorrectSize()
        {
            Assert.AreEqual(Marshal.SizeOf<TestTypeWithGCHandle>(), TypeManager.GetTypeInfo<TestTypeWithGCHandle>().TypeSize);
        }

        [Test]
        public void TypeWithGuid_HasCorrectSize()
        {
            Assert.AreEqual(Marshal.SizeOf<TestTypeWithGuid>(), TypeManager.GetTypeInfo<TestTypeWithGuid>().TypeSize);
        }

        [Test]
        public void TestDescendents_DescendantsOfDescendants()
        {
            // TestTypeBaseClass
            //     TestTypeSubClass1 : TestTypeBaseClass
            //         TestTypeSubSubClass1 : TestTypeSubClass1
            //
            //     TestTypeSubClass2 : TestTypeBaseClass
            //         TestTypeSubSubClass2 : TestTypeSubClass2


            var baseTypeIndex = TypeManager.GetTypeIndex<TestTypeBaseClass>();

            var subClass1TypeIndex = TypeManager.GetTypeIndex<TestTypeSubClass1>();
            var subClass2TypeIndex = TypeManager.GetTypeIndex<TestTypeSubClass2>();

            var subSubClass1TypeIndex = TypeManager.GetTypeIndex<TestTypeSubSubClass1>();
            var subSubClass2TypeIndex = TypeManager.GetTypeIndex<TestTypeSubSubClass2>();

            Assert.IsTrue(TypeManager.HasDescendants(baseTypeIndex));
            Assert.IsTrue(TypeManager.HasDescendants(subClass1TypeIndex));
            Assert.IsTrue(TypeManager.HasDescendants(subClass2TypeIndex));

            Assert.IsFalse(TypeManager.HasDescendants(subSubClass1TypeIndex));
            Assert.IsFalse(TypeManager.HasDescendants(subSubClass2TypeIndex));

            Assert.AreEqual(4, TypeManager.GetDescendantCount(baseTypeIndex));

            Assert.AreEqual(1, TypeManager.GetDescendantCount(subClass1TypeIndex));
            Assert.AreEqual(1, TypeManager.GetDescendantCount(subClass2TypeIndex));

            Assert.AreEqual(0, TypeManager.GetDescendantCount(subSubClass1TypeIndex));
            Assert.AreEqual(0, TypeManager.GetDescendantCount(subSubClass2TypeIndex));

            // Check every type in this tree if types is a descendant of base type
            Assert.IsTrue(TypeManager.IsDescendantOf(baseTypeIndex, baseTypeIndex));
            Assert.IsTrue(TypeManager.IsDescendantOf(subClass1TypeIndex, baseTypeIndex));
            Assert.IsTrue(TypeManager.IsDescendantOf(subClass2TypeIndex, baseTypeIndex));
            Assert.IsTrue(TypeManager.IsDescendantOf(subSubClass1TypeIndex, baseTypeIndex));
            Assert.IsTrue(TypeManager.IsDescendantOf(subSubClass2TypeIndex, baseTypeIndex));

            // Test that the Sub Sub types are descendants of their immediate parent types
            Assert.IsTrue(TypeManager.IsDescendantOf(subSubClass1TypeIndex, subClass1TypeIndex));
            Assert.IsTrue(TypeManager.IsDescendantOf(subSubClass2TypeIndex, subClass2TypeIndex));
            Assert.IsFalse(TypeManager.IsDescendantOf(subSubClass1TypeIndex, subClass2TypeIndex));
            Assert.IsFalse(TypeManager.IsDescendantOf(subSubClass2TypeIndex, subClass1TypeIndex));
        }

        [Test]
        public void TestDescendants_StructType_NoDescendants()
        {
            var structTypeIndex = TypeManager.GetTypeIndex<TestType1>();
            Assert.IsFalse(TypeManager.HasDescendants(structTypeIndex));
            Assert.AreEqual(0, TypeManager.GetDescendantCount(structTypeIndex));
        }

        [Test]
        public void TestDescendants_IsDescendantOfSelf()
        {
            var baseTypeIndex = TypeManager.GetTypeIndex<TestTypeBaseClass>();
            Assert.IsTrue(TypeManager.IsDescendantOf(baseTypeIndex, baseTypeIndex));
        }

        [Test]
        public void TestDescendants_SingleClassType_NoDescendants()
        {
            var singleClassTypeIndex = TypeManager.GetTypeIndex<TestTypeSingleClass>();
            Assert.IsFalse(TypeManager.HasDescendants(singleClassTypeIndex));
            Assert.AreEqual(0, TypeManager.GetDescendantCount(singleClassTypeIndex));
        }
#endif

        [Test]
        public void CreateArchetypes()
        {
            var archetype1 = m_Manager.CreateArchetype(ComponentType.ReadWrite<TestType1>(), ComponentType.ReadWrite<TestType2>());
            var archetype1Same = m_Manager.CreateArchetype(ComponentType.ReadWrite<TestType1>(), ComponentType.ReadWrite<TestType2>());
            Assert.AreEqual(archetype1, archetype1Same);

            var archetype2 = m_Manager.CreateArchetype(ComponentType.ReadWrite<TestType1>());
            var archetype2Same = m_Manager.CreateArchetype(ComponentType.ReadWrite<TestType1>());
            Assert.AreEqual(archetype2Same, archetype2Same);

            Assert.AreNotEqual(archetype1, archetype2);
        }

        [Test]
        public void TestPrimitiveButNotBlittableTypesAllowed()
        {
            Assert.AreEqual(1, TypeManager.GetTypeInfo<TestTypeWithBool>().SizeInChunk);
            Assert.AreEqual(2, TypeManager.GetTypeInfo<TestTypeWithChar>().SizeInChunk);
        }

        // We need to decide whether this should actually be allowed; for now, add a test to make sure
        // we don't break things more than they already are.


        [Test]
        public void TestGenericComponents()
        {
            var index1 = TypeManager.GetTypeIndex<GenericComponent<int>>();
            var index2 = TypeManager.GetTypeIndex<GenericComponent<short>>();

            Assert.AreNotEqual(index1, index2);
        }

        [Test]
        public void TestEntityRef()
        {
            Assert.IsTrue(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestTypeWithEntity>()));
            Assert.IsTrue(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<GenericComponent<Entity>>()));
            Assert.IsFalse(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<GenericComponent<short>>()));
            Assert.IsFalse(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestType1>()));
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires component type safety checks")]
        public void TestNativeContainer()
        {
            Assert.IsTrue(TypeManager.GetTypeIndex<TestTypeWithNativeContainer>().HasNativeContainer);
            Assert.IsTrue(TypeManager.GetTypeIndex<GenericComponent<NativeArray<int>>>().HasNativeContainer);
            Assert.IsFalse(TypeManager.GetTypeIndex<GenericComponent<short>>().HasNativeContainer);
            Assert.IsFalse(TypeManager.GetTypeIndex<TestType1>().HasNativeContainer);
        }

        [Test]
        public void TestBlobRef()
        {
            Assert.IsTrue(TypeManager.GetTypeInfo<TestTypeWithBlobRef>().HasBlobAssetRefs);
            Assert.IsTrue(TypeManager.GetTypeInfo<GenericComponent<BlobAssetReference<float>>>().HasBlobAssetRefs);
            Assert.IsFalse(TypeManager.GetTypeInfo<GenericComponent<short>>().HasBlobAssetRefs);
            Assert.IsFalse(TypeManager.GetTypeInfo<TestType1>().HasBlobAssetRefs);
        }

        [Test]
        public void TestGenericComponentsThrowsOnUnregisteredGeneric()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                TypeManager.GetTypeIndex<GenericComponent<long>>();
            });
        }

        [Test]
        public unsafe void TestConstructComponentFromBuffer()
        {
            EcsTestData td;
            td.value = 42;

            object o = TypeManager.ConstructComponentFromBuffer(TypeManager.GetTypeIndex<EcsTestData>(), &td);
            EcsTestData tr = (EcsTestData) o;
            Assert.AreEqual(td.value, tr.value);
        }

        [InternalBufferCapacity(99)]
        public struct IntElement : IBufferElementData
        {
            public int Value;
        }

        [Test]
        public void BufferTypeClassificationWorks()
        {
            var t = TypeManager.GetTypeInfo<IntElement>();
            Assert.AreEqual(TypeManager.TypeCategory.BufferData, t.Category);
            Assert.AreEqual(99, t.BufferCapacity);
            Assert.AreEqual(UnsafeUtility.SizeOf<BufferHeader>() + 99 * sizeof(int), t.SizeInChunk);
        }

        [Test]
        public void TestTypeManager()
        {
            var entityType = ComponentType.ReadWrite<Entity>();
            var testDataType = ComponentType.ReadWrite<EcsTestData>();

            Assert.AreEqual(entityType, ComponentType.ReadWrite<Entity>());
            Assert.AreEqual(entityType, new ComponentType(typeof(Entity)));
            Assert.AreEqual(testDataType, ComponentType.ReadWrite<EcsTestData>());
            Assert.AreEqual(testDataType, new ComponentType(typeof(EcsTestData)));
            Assert.AreNotEqual(ComponentType.ReadWrite<Entity>(), ComponentType.ReadWrite<EcsTestData>());

            Assert.AreEqual(ComponentType.AccessMode.ReadOnly, ComponentType.ReadOnly<EcsTestData>().AccessModeType);
            Assert.AreEqual(ComponentType.AccessMode.ReadOnly, ComponentType.ReadOnly(typeof(EcsTestData)).AccessModeType);

            Assert.AreEqual(typeof(Entity), entityType.GetManagedType());
        }

        [Test]
        public void TestAlignUp_Align0ToPow2()
        {
            Assert.AreEqual(0, CollectionHelper.Align(0, 1));
            Assert.AreEqual(0, CollectionHelper.Align(0, 2));
            Assert.AreEqual(0, CollectionHelper.Align(0, 4));
            Assert.AreEqual(0, CollectionHelper.Align(0, 8));
            Assert.AreEqual(0, CollectionHelper.Align(0, 16));
            Assert.AreEqual(0, CollectionHelper.Align(0, 32));
            Assert.AreEqual(0, CollectionHelper.Align(0, 64));
            Assert.AreEqual(0, CollectionHelper.Align(0, 128));
        }

        [Test]
        public void TestAlignUp_AlignMultipleOfAlignment()
        {
            Assert.AreEqual(2, CollectionHelper.Align(2, 1));
            Assert.AreEqual(4, CollectionHelper.Align(4, 2));
            Assert.AreEqual(8, CollectionHelper.Align(8, 4));
            Assert.AreEqual(16, CollectionHelper.Align(16, 8));
            Assert.AreEqual(32, CollectionHelper.Align(32, 16));
            Assert.AreEqual(64, CollectionHelper.Align(64, 32));
            Assert.AreEqual(128, CollectionHelper.Align(128, 64));
            Assert.AreEqual(256, CollectionHelper.Align(256, 128));
        }

        [Test]
        public void TestAlignUp_Align1ToPow2()
        {
            Assert.AreEqual(1, CollectionHelper.Align(1, 1));
            Assert.AreEqual(2, CollectionHelper.Align(1, 2));
            Assert.AreEqual(4, CollectionHelper.Align(1, 4));
            Assert.AreEqual(8, CollectionHelper.Align(1, 8));
            Assert.AreEqual(16, CollectionHelper.Align(1, 16));
            Assert.AreEqual(32, CollectionHelper.Align(1, 32));
            Assert.AreEqual(64, CollectionHelper.Align(1, 64));
            Assert.AreEqual(128, CollectionHelper.Align(1, 128));
        }

        [Test]
        public void TestAlignUp_Align3ToPow2()
        {
            Assert.AreEqual(3, CollectionHelper.Align(3, 1));
            Assert.AreEqual(4, CollectionHelper.Align(3, 2));
            Assert.AreEqual(4, CollectionHelper.Align(3, 4));
            Assert.AreEqual(8, CollectionHelper.Align(3, 8));
            Assert.AreEqual(16, CollectionHelper.Align(3, 16));
            Assert.AreEqual(32, CollectionHelper.Align(3, 32));
            Assert.AreEqual(64, CollectionHelper.Align(3, 64));
            Assert.AreEqual(128, CollectionHelper.Align(3, 128));
        }

        [Test]
        public void TestAlignUp_Align15ToPow2()
        {
            Assert.AreEqual(15, CollectionHelper.Align(15, 1));
            Assert.AreEqual(16, CollectionHelper.Align(15, 2));
            Assert.AreEqual(16, CollectionHelper.Align(15, 4));
            Assert.AreEqual(16, CollectionHelper.Align(15, 8));
            Assert.AreEqual(16, CollectionHelper.Align(15, 16));
            Assert.AreEqual(32, CollectionHelper.Align(15, 32));
            Assert.AreEqual(64, CollectionHelper.Align(15, 64));
            Assert.AreEqual(128, CollectionHelper.Align(15, 128));
        }

        [Test]
        public void TestAlignUp_Align63ToPow2()
        {
            Assert.AreEqual(63, CollectionHelper.Align(63, 1));
            Assert.AreEqual(64, CollectionHelper.Align(63, 2));
            Assert.AreEqual(64, CollectionHelper.Align(63, 4));
            Assert.AreEqual(64, CollectionHelper.Align(63, 8));
            Assert.AreEqual(64, CollectionHelper.Align(63, 16));
            Assert.AreEqual(64, CollectionHelper.Align(63, 32));
            Assert.AreEqual(64, CollectionHelper.Align(63, 64));
            Assert.AreEqual(128, CollectionHelper.Align(63, 128));
        }

        [Test]
        public void TestAlignUp_ZeroAlignment()
        {
            for (int value = 0; value < 512; ++value)
            {
                Assert.AreEqual(value, CollectionHelper.Align(value, 0));
            }
        }

        [Test]
        public void TestAlignUlong_Align0ToPow2()
        {
            Assert.AreEqual(0ul, CollectionHelper.Align(0ul, 1ul));
            Assert.AreEqual(0ul, CollectionHelper.Align(0ul, 2ul));
            Assert.AreEqual(0ul, CollectionHelper.Align(0ul, 4ul));
            Assert.AreEqual(0ul, CollectionHelper.Align(0ul, 8ul));
            Assert.AreEqual(0ul, CollectionHelper.Align(0ul, 16ul));
            Assert.AreEqual(0ul, CollectionHelper.Align(0ul, 32ul));
            Assert.AreEqual(0ul, CollectionHelper.Align(0ul, 64ul));
            Assert.AreEqual(0ul, CollectionHelper.Align(0ul, 128ul));
        }

        [Test]
        public void TestAlignUlong_AlignMultipleOfAlignment()
        {
            Assert.AreEqual(2ul, CollectionHelper.Align(2ul, 1ul));
            Assert.AreEqual(4ul, CollectionHelper.Align(4ul, 2ul));
            Assert.AreEqual(8ul, CollectionHelper.Align(8ul, 4ul));
            Assert.AreEqual(16ul, CollectionHelper.Align(16ul, 8ul));
            Assert.AreEqual(32ul, CollectionHelper.Align(32ul, 16ul));
            Assert.AreEqual(64ul, CollectionHelper.Align(64ul, 32ul));
            Assert.AreEqual(128ul, CollectionHelper.Align(128ul, 64ul));
            Assert.AreEqual(256ul, CollectionHelper.Align(256ul, 128ul));
        }

        [Test]
        public void TestAlignUlong_Align1ToPow2()
        {
            Assert.AreEqual(1ul, CollectionHelper.Align(1ul, 1ul));
            Assert.AreEqual(2ul, CollectionHelper.Align(1ul, 2ul));
            Assert.AreEqual(4ul, CollectionHelper.Align(1ul, 4ul));
            Assert.AreEqual(8ul, CollectionHelper.Align(1ul, 8ul));
            Assert.AreEqual(16ul, CollectionHelper.Align(1ul, 16ul));
            Assert.AreEqual(32ul, CollectionHelper.Align(1ul, 32ul));
            Assert.AreEqual(64ul, CollectionHelper.Align(1ul, 64ul));
            Assert.AreEqual(128ul, CollectionHelper.Align(1ul, 128ul));
        }

        [Test]
        public void TestAlignUlong_Align3ToPow2()
        {
            Assert.AreEqual(3ul, CollectionHelper.Align(3ul, 1ul));
            Assert.AreEqual(4ul, CollectionHelper.Align(3ul, 2ul));
            Assert.AreEqual(4ul, CollectionHelper.Align(3ul, 4ul));
            Assert.AreEqual(8ul, CollectionHelper.Align(3ul, 8ul));
            Assert.AreEqual(16ul, CollectionHelper.Align(3ul, 16ul));
            Assert.AreEqual(32ul, CollectionHelper.Align(3ul, 32ul));
            Assert.AreEqual(64ul, CollectionHelper.Align(3ul, 64ul));
            Assert.AreEqual(128ul, CollectionHelper.Align(3ul, 128ul));
        }

        [Test]
        public void TestAlignUlong_Align15ToPow2()
        {
            Assert.AreEqual(15ul, CollectionHelper.Align(15ul, 1ul));
            Assert.AreEqual(16ul, CollectionHelper.Align(15ul, 2ul));
            Assert.AreEqual(16ul, CollectionHelper.Align(15ul, 4ul));
            Assert.AreEqual(16ul, CollectionHelper.Align(15ul, 8ul));
            Assert.AreEqual(16ul, CollectionHelper.Align(15ul, 16ul));
            Assert.AreEqual(32ul, CollectionHelper.Align(15ul, 32ul));
            Assert.AreEqual(64ul, CollectionHelper.Align(15ul, 64ul));
            Assert.AreEqual(128ul, CollectionHelper.Align(15ul, 128ul));
        }

        [Test]
        public void TestAlignUlong_Align63ToPow2()
        {
            Assert.AreEqual(63ul, CollectionHelper.Align(63ul, 1ul));
            Assert.AreEqual(64ul, CollectionHelper.Align(63ul, 2ul));
            Assert.AreEqual(64ul, CollectionHelper.Align(63ul, 4ul));
            Assert.AreEqual(64ul, CollectionHelper.Align(63ul, 8ul));
            Assert.AreEqual(64ul, CollectionHelper.Align(63ul, 16ul));
            Assert.AreEqual(64ul, CollectionHelper.Align(63ul, 32ul));
            Assert.AreEqual(64ul, CollectionHelper.Align(63ul, 64ul));
            Assert.AreEqual(128ul, CollectionHelper.Align(63ul, 128ul));
        }

        [Test]
        public void TestAlignUlong_ZeroAlignment()
        {
            for (ulong value = 0; value < 512ul; ++value)
            {
                Assert.AreEqual(value, CollectionHelper.Align(value, 0ul));
            }
        }

        [Test]
        public unsafe void TestAlignPointer_ToPow2()
        {
            ulong ulongOnStack = 0;
            byte* bytePtrOnStack = (byte*)&ulongOnStack;
            nuint expectedAlignedAddr = (nuint)(&ulongOnStack + 1);

            Assert.AreEqual((nuint)(&ulongOnStack), (nuint)CollectionHelper.AlignPointer(bytePtrOnStack+0, sizeof(ulong)));
            Assert.AreEqual(expectedAlignedAddr, (nuint)CollectionHelper.AlignPointer(bytePtrOnStack+1, sizeof(ulong)));
            Assert.AreEqual(expectedAlignedAddr, (nuint)CollectionHelper.AlignPointer(bytePtrOnStack+2, sizeof(ulong)));
            Assert.AreEqual(expectedAlignedAddr, (nuint)CollectionHelper.AlignPointer(bytePtrOnStack+3, sizeof(ulong)));
            Assert.AreEqual(expectedAlignedAddr, (nuint)CollectionHelper.AlignPointer(bytePtrOnStack+4, sizeof(ulong)));
            Assert.AreEqual(expectedAlignedAddr, (nuint)CollectionHelper.AlignPointer(bytePtrOnStack+5, sizeof(ulong)));
            Assert.AreEqual(expectedAlignedAddr, (nuint)CollectionHelper.AlignPointer(bytePtrOnStack+6, sizeof(ulong)));
            Assert.AreEqual(expectedAlignedAddr, (nuint)CollectionHelper.AlignPointer(bytePtrOnStack+7, sizeof(ulong)));
            Assert.AreEqual(expectedAlignedAddr, (nuint)CollectionHelper.AlignPointer(bytePtrOnStack+8, sizeof(ulong)));
        }



        [UpdateBefore(typeof(PresentationSystemGroup))]
        [UpdateAfter(typeof(InitializationSystemGroup))]
        partial class TestComponentSystem : SystemBase
        {
            protected override void OnUpdate()
            {
            }
        }

        partial class TestComponentSystemGroup : ComponentSystemGroup
        {
            protected override void OnUpdate()
            {
            }
        }

        [Test]
        public void TestGetSystems()
        {
            // Remove disabled systems since in Entities.Tests we have systems that are intentionally broken and will throw exceptions
            var allSystemTypes = TypeManager.GetSystemTypeIndices(WorldSystemFilterFlags.All & ~WorldSystemFilterFlags.Disabled);
            bool foundTestSystem = false;
            for(int i = 0; i < allSystemTypes.Length;++i)
            {
                var sys = allSystemTypes[i];
                var systemTypeIndex = TypeManager.GetSystemTypeIndex<PresentationSystemGroup>();// A group we know will always exist

                if(sys == systemTypeIndex)
                {
                    foundTestSystem = true;
                    break;
                }
            }
            Assert.IsTrue(foundTestSystem);
        }

        [Test]
        [Conditional("DEBUG")]
        public void TestGetSystemName()
        {
            Assert.AreEqual("Unity.Entities.Tests.TypeManagerTests+TestComponentSystem", TypeManager.GetSystemName(typeof(TestComponentSystem)));
        }


        // Warnings will be thrown about redundant attributes since all systems are disabled in this assembly
        // [DisableAutoCreation]
        partial class DisabledSystem : SystemBase
        {
            protected override void OnUpdate()
            {
            }
        }

        partial class ChildOfDisabledSystem : SystemBase
        {
            protected override void OnUpdate()
            {
            }
        }

        [Test]
        public void TestGetSystemAttributes()
        {
            var updateBeforeAttributes = TypeManager.GetSystemAttributes(typeof(TestComponentSystem), typeof(UpdateBeforeAttribute));
            Assert.AreEqual(1, updateBeforeAttributes.Length);
            Assert.AreEqual(typeof(PresentationSystemGroup), ((UpdateBeforeAttribute)updateBeforeAttributes[0]).SystemType);

            var updateAfterAttributes = TypeManager.GetSystemAttributes(typeof(TestComponentSystem), typeof(UpdateAfterAttribute));
            Assert.AreEqual(1, updateAfterAttributes.Length);
            Assert.AreEqual(typeof(InitializationSystemGroup), ((UpdateAfterAttribute)updateAfterAttributes[0]).SystemType);

            var disableAttributes = TypeManager.GetSystemAttributes(typeof(DisabledSystem), typeof(DisableAutoCreationAttribute));
            Assert.AreEqual(1, disableAttributes.Length);

            // Annoyingly we cannot test this without adding a new dependent assembly to this test assembly. This is because all systems are disabled (rightfully so)
            // for this test assembly via [assembly: DisableAutoCreation] so we cannot check that a child system defined in this assembly is _not_ disabled
            //var inheritedDisableAttributes = TypeManager.GetSystemAttributes(typeof(ChildOfDisabledSystem), typeof(DisableAutoCreationAttribute));
            //Assert.AreEqual(0, inheritedDisableAttributes.Length); // we should not inherit DisableAutoCreation attributes
        }

        [Test]
        public void TestIsComponentSystemGroup()
        {
            Assert.IsTrue(!TypeManager.IsSystemAGroup(typeof(TestComponentSystem)));
            Assert.IsTrue(TypeManager.IsSystemAGroup(typeof(TestComponentSystemGroup)));
        }

        [WorldSystemFilter(WorldSystemFilterFlags.Default)]
        partial class DefaultFilteredSystem : SystemBase{ protected override void OnUpdate() { } }
        [WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
        partial class ProcessAfterLoadFilteredSystem : SystemBase { protected override void OnUpdate() { } }

        int ValidateFilterFlags(WorldSystemFilterFlags expectedFilterFlags, WorldSystemFilterFlags requiredFlags = 0)
        {
            Assert.IsTrue(expectedFilterFlags != WorldSystemFilterFlags.All);
            if ((expectedFilterFlags & WorldSystemFilterFlags.Default) != 0)
            {
                expectedFilterFlags &= ~WorldSystemFilterFlags.Default;
                expectedFilterFlags |= WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.Presentation;
            }

            var filteredTypes = TypeManager.GetSystemTypeIndices(expectedFilterFlags, requiredFlags);
            requiredFlags &= ~WorldSystemFilterFlags.Default;
            foreach(var t in filteredTypes)
            {
                var actualFilterFlags = TypeManager.GetSystemFilterFlags(t);

                Assert.IsTrue((expectedFilterFlags & actualFilterFlags) != 0, $"Flags for system {t} do not match");
                Assert.GreaterOrEqual((int)actualFilterFlags, (int)requiredFlags, $"Actual Flags for system {t} should be greater than or equal to required flags");
            }

            return filteredTypes.Length;
        }

        [Test]
        public void GetSystemsWorldSystemFilterFlags()
        {
            var allTypesCount = TypeManager.GetSystemTypeIndices(WorldSystemFilterFlags.All & ~WorldSystemFilterFlags.Disabled).Length;

            var numDefaultSystems = ValidateFilterFlags(WorldSystemFilterFlags.Default, 0);
            var numProcessAfterLoadSystems = ValidateFilterFlags(WorldSystemFilterFlags.ProcessAfterLoad, WorldSystemFilterFlags.ProcessAfterLoad);
            var numCombinedSystems = ValidateFilterFlags(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.ProcessAfterLoad);
            Assert.AreEqual(numCombinedSystems, numDefaultSystems + numProcessAfterLoadSystems);
            Assert.IsTrue(numCombinedSystems <= allTypesCount);
        }

        [WorldSystemFilter(WorldSystemFilterFlags.Default, WorldSystemFilterFlags.LocalSimulation)]
        partial class Test_FirstLevelGroup : ComponentSystemGroup { }
        [WorldSystemFilter(WorldSystemFilterFlags.Default, WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Presentation)]
        [UpdateInGroup(typeof(Test_FirstLevelGroup))]
        partial class Test_SecondLevelGroup : ComponentSystemGroup { }
        [UpdateInGroup(typeof(Test_FirstLevelGroup))]
        partial class Test_FirstLevelSystem : SystemBase { protected override void OnUpdate() { } }
        [UpdateInGroup(typeof(Test_SecondLevelGroup))]
        partial class Test_SecondLevelSystem : SystemBase { protected override void OnUpdate() { } }
        [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.ClientSimulation)]
        [UpdateInGroup(typeof(Test_SecondLevelGroup))]
        partial class Test_SecondLevelExtendedSystem : SystemBase { protected override void OnUpdate() { } }
        [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
        [UpdateInGroup(typeof(Test_SecondLevelGroup))]
        partial class Test_SecondLevelExplicitSystem : SystemBase { protected override void OnUpdate() { } }

        [Test]
        public void GetDefaultWorldSystemFilterFlagsFromGroup()
        {
            Assert.AreEqual(WorldSystemFilterFlags.LocalSimulation, TypeManager.GetSystemFilterFlags(typeof(Test_FirstLevelSystem)));
            Assert.AreEqual(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.Presentation, TypeManager.GetSystemFilterFlags(typeof(Test_SecondLevelSystem)));
            Assert.AreEqual(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.Presentation | WorldSystemFilterFlags.ClientSimulation, TypeManager.GetSystemFilterFlags(typeof(Test_SecondLevelExtendedSystem)));
            Assert.AreEqual(WorldSystemFilterFlags.ClientSimulation, TypeManager.GetSystemFilterFlags(typeof(Test_SecondLevelExplicitSystem)));
        }

        [CreateAfter(typeof(Test_CreateOrder_B))]
        [WorldSystemFilter(WorldSystemFilterFlags.Editor)]
        partial class Test_CreateOrder_C : SystemBase { protected override void OnUpdate() { } }
        [WorldSystemFilter(WorldSystemFilterFlags.Editor)]
        partial class Test_CreateOrder_B : SystemBase { protected override void OnUpdate() { } }
        [CreateBefore(typeof(Test_CreateOrder_B))]
        [WorldSystemFilter(WorldSystemFilterFlags.Editor)]
        partial class Test_CreateOrder_A : SystemBase { protected override void OnUpdate() { } }

        [Test]
        [Ignore("Fix Filter Flags to allow disabled systems to be found outside of All queries - DOTS-5966")]
        public void GetSystemsRespectsCreateBeforeCreateAfter()
        {
            // All systems in the test assembly are disabled by default. If we fetch disabled systems we will trip on intentionally
            // broken systems, so we instead stuff our disabled systems into the Editor world filter which will exclude the broken systems
            var allTypes = TypeManager.GetSystemTypeIndices(WorldSystemFilterFlags.All, WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.Disabled);

            var indexOfA = allTypes.IndexOf(TypeManager.GetSystemTypeIndex<Test_CreateOrder_A>());
            var indexOfB = allTypes.IndexOf(TypeManager.GetSystemTypeIndex<Test_CreateOrder_B>());
            var indexOfC = allTypes.IndexOf(TypeManager.GetSystemTypeIndex<Test_CreateOrder_C>());

            Assert.Less(indexOfA, indexOfB);
            Assert.Less(indexOfB, indexOfC);
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences: false, hasNoBlobReferences: true, hasNoUnityObjectReferences: true)]
        public struct TypeOverridesNoBlobNoUnityObjectUnmanaged : IComponentData
        {
            public Entity entity;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences: true, hasNoBlobReferences: false, hasNoUnityObjectReferences: true)]
        public struct TypeOverridesNoEntityNoUnityObjectUnmanaged : IComponentData
        {
            public BlobAssetReference<int> blob;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences: true, hasNoBlobReferences: true, hasNoUnityObjectReferences: false)]
        public struct TypeOverridesNoEntityNoBlobUnmanaged : IComponentData
        {
            public UnityObjectRef<UnityEngine.Object> obj;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences: false, hasNoBlobReferences: false, hasNoUnityObjectReferences: false)]
        public struct TypeOverridesNoBlobNoEntityNoUnityObjectUnmanaged : IComponentData
        {
            public float data;
        }

        [Test]
        public void TypeOverrideWorks_Unmanaged_ValidTypesDoNotThrow()
        {
            var typeOverridesNoBlobNoUnityObjectInfo = TypeManager.GetTypeInfo<TypeOverridesNoBlobNoUnityObjectUnmanaged>();
            Assert.IsTrue(TypeManager.HasEntityReferences(typeOverridesNoBlobNoUnityObjectInfo.TypeIndex));
            Assert.IsFalse(typeOverridesNoBlobNoUnityObjectInfo.HasUnityObjectRefs);
            Assert.IsFalse(typeOverridesNoBlobNoUnityObjectInfo.HasBlobAssetRefs);

            var typeOverridesNoEntityNoUnityObjectInfo = TypeManager.GetTypeInfo<TypeOverridesNoEntityNoUnityObjectUnmanaged>();
            Assert.IsTrue(typeOverridesNoEntityNoUnityObjectInfo.HasBlobAssetRefs);
            Assert.IsFalse(typeOverridesNoEntityNoUnityObjectInfo.HasUnityObjectRefs);
            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesNoEntityNoUnityObjectInfo.TypeIndex));

            var typeOverridesNoEntityNoBlobInfo = TypeManager.GetTypeInfo<TypeOverridesNoEntityNoBlobUnmanaged>();
            Assert.IsTrue(typeOverridesNoEntityNoBlobInfo.HasUnityObjectRefs);
            Assert.IsFalse(typeOverridesNoEntityNoBlobInfo.HasBlobAssetRefs);
            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesNoEntityNoBlobInfo.TypeIndex));

            var typeOverridesNoBlobNoEntityNoUnityObjectInfo = TypeManager.GetTypeInfo<TypeOverridesNoBlobNoEntityNoUnityObjectUnmanaged>();
            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesNoBlobNoEntityNoUnityObjectInfo.TypeIndex));
            Assert.IsFalse(typeOverridesNoBlobNoEntityNoUnityObjectInfo.HasBlobAssetRefs);
            Assert.IsFalse(typeOverridesNoBlobNoEntityNoUnityObjectInfo.HasUnityObjectRefs);
        }

        [DisableAutoTypeRegistration]
        struct NonRegisteredComponentType : IComponentData
        {
            public int Foo;
        }

        [Test]
        [TestRequiresCollectionChecks("Requires a check in TypeManager.BuildComponentType which is guarded by ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public void BuildComponentType_ThrowsArgumentException_IfCalledAfterTypeManagerInitializationIsComplete()
        {
            Assert.Throws<InvalidOperationException>(
                () => TypeManager.BuildComponentType(typeof(NonRegisteredComponentType), new TypeManager.BuildComponentCache())
            );
        }

        [Test]
        public void BuildComponentType_DoesNotThrow_IfCalledAfterTypeManagerInitializationIsCompleteAndStaticsAreExplicitlyReset()
        {
            TypeManager.ShutdownSharedStatics();
            TypeManager.BuildComponentType(typeof(NonRegisteredComponentType), new TypeManager.BuildComponentCache());
            TypeManager.InitializeSharedStatics();
        }

        [DisableAutoTypeRegistration]
        struct NonBlittableComponentData : IComponentData
        {
            string empty;
        }

        [DisableAutoTypeRegistration]
        struct NonBlittableComponentData2 : IComponentData
        {
            IComponentData empty;
        }

        interface InterfaceComponentData : IComponentData
        {
        }

        [DisableAutoTypeRegistration]
        struct NonBlittableBuffer : IBufferElementData
        {
            string empty;
        }

        //uncomment this and make sure an ILPP throws an exception
        /*
        class ClassBuffer : IBufferElementData
        {
        }*/

        interface InterfaceBuffer : IBufferElementData
        {
        }

        //uncomment this and make sure an ILPP throws an exception
        /*class ClassShared : ISharedComponentData
        {
        }*/

        interface InterfaceShared : ISharedComponentData
        {
        }

        [DisableAutoTypeRegistration]
        struct Cleanup : ICleanupComponentData, IEnableableComponent
        {

        }

        [DisableAutoTypeRegistration]
        struct Shared : ISharedComponentData, IEnableableComponent
        {
            public int mVal;
        }

        [DisableAutoTypeRegistration]
        struct EmptyBufferComponent : IBufferElementData
        {

        }

        [DisableAutoTypeRegistration]
        struct EmptySharedComponent : ISharedComponentData
        {

        }

        [TestCase(typeof(InterfaceComponentData), @"\binterface\b", TestName = "Interface implementing IComponentData")]
        [TestCase(typeof(NonBlittableComponentData), @"\bblittable\b", TestName = "Non-blittable component data (string)")]
        [TestCase(typeof(NonBlittableComponentData2), @"\bblittable\b", TestName = "Non-blittable component data (interface)")]

        [TestCase(typeof(InterfaceBuffer), @"\binterface\b", TestName = "Interface implementing IBufferElementData")]
        [TestCase(typeof(NonBlittableBuffer), @"\bblittable\b", TestName = "Non-blittable IBufferElementData")]

        [TestCase(typeof(InterfaceShared), @"\binterface\b", TestName = "Interface implementing ISharedComponentData")]

        [TestCase(typeof(Cleanup), @"\bdisabled\b", TestName = "Implements both ICleanupComponentData and IEnableableComponent")]
        [TestCase(typeof(Shared), @"\bdisabled\b", TestName = "Implements both ISharedComponentData and IEnableableComponent")]

        [TestCase(typeof(float), @"\b(not .*|in)valid\b", TestName = "Not valid component type")]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires component type safety checks")]
        public void BuildComponentType_ThrowsArgumentException_WithExpectedFailures(Type type, string keywordPattern)
        {
            TypeManager.ShutdownSharedStatics();
            Assert.That(
                () => TypeManager.BuildComponentType(type, new TypeManager.BuildComponentCache()),
                Throws.ArgumentException.With.Message.Matches(keywordPattern)
            );
            TypeManager.InitializeSharedStatics();
        }

        [TestCase(typeof(EmptyBufferComponent), TestName = "IBufferElementData types can be empty")]
        [TestCase(typeof(EmptySharedComponent), TestName = "ISharedComponentData types can be empty")]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires component type safety checks")]
        public void BuildComponentType_DoesNotThrow(Type type)
        {
            // This was briefly a fatal error in TypeManager initialization, but we decided to relax it;
            // empty components are pointless and wasteful, but not actively harmful.
            TypeManager.ShutdownSharedStatics();
            Assert.DoesNotThrow(() => TypeManager.BuildComponentType(type, new TypeManager.BuildComponentCache()));
            TypeManager.InitializeSharedStatics();
        }

        struct UnmanagedSharedComponent : ISharedComponentData
        {
            int a;
        }

        struct ManagedSharedComponent : ISharedComponentData, IEquatable<ManagedSharedComponent>
        {
            private string a;

            public bool Equals(ManagedSharedComponent other)
            {
                return a == other.a;
            }

            public override bool Equals(object obj)
            {
                return obj is ManagedSharedComponent other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (a != null ? a.GetHashCode() : 0);
            }
        }

        [Test]
        public void SharedComponent_ManagedFlagCorrectlySet()
        {
            Assert.IsTrue(TypeManager.IsManagedType(TypeManager.GetTypeIndex<ManagedSharedComponent>()));
            Assert.IsTrue(TypeManager.IsSharedComponentType(TypeManager.GetTypeIndex<ManagedSharedComponent>()));
            Assert.IsTrue(TypeManager.IsManagedSharedComponent(TypeManager.GetTypeIndex<ManagedSharedComponent>()));


            Assert.IsFalse(TypeManager.IsManagedType(TypeManager.GetTypeIndex<UnmanagedSharedComponent>()));
            Assert.IsTrue(TypeManager.IsSharedComponentType(TypeManager.GetTypeIndex<UnmanagedSharedComponent>()));
            Assert.IsFalse(TypeManager.IsManagedSharedComponent(TypeManager.GetTypeIndex<UnmanagedSharedComponent>()));
        }

        struct OptionalEnableableComponent : IComponentData, IEnableableComponent
        {

        }

        [Test]
        public void CleanupFlag_IsCorrectlySet()
        {
            Assert.IsTrue(TypeManager.IsCleanupComponent(TypeManager.GetTypeIndex<EcsCleanup1>()));
        }

        [Test]
        public void BufferFlag_IsCorrectlySet()
        {
            Assert.IsTrue(TypeManager.IsBuffer(TypeManager.GetTypeIndex<EcsTestEnableableBuffer1>()));
        }

        [Test]
        public void IEquatableFlag_IsCorrectlySet()
        {
            Assert.IsTrue(TypeManager.IsIEquatable(TypeManager.GetTypeIndex<TestTypeWithChar>()));
        }


        [Test]
        public void OptionalComponent_IsEnableableFlagCorrectlySet()
        {
            Assert.IsTrue(TypeManager.IsEnableable(TypeManager.GetTypeIndex<OptionalEnableableComponent>()));
        }

        [TestCase(typeof(UnityEngine.Transform))]
        [TestCase(typeof(TypeManagerTests))]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires component type safety checks")]
        public void BuildComponentType_WithClass_WhenUnityEngineObjectTypeIsNull_ThrowsArgumentException(Type type)
        {
            TypeManager.ShutdownSharedStatics();
            var componentType = TypeManager.UnityEngineObjectType;
            TypeManager.UnityEngineObjectType = null;
            try
            {
                Assert.That(
                    () => TypeManager.BuildComponentType(type, new TypeManager.BuildComponentCache()),
                    Throws.ArgumentException.With.Message.Matches($"\\bregister\\b.*\\b{nameof(TypeManager.UnityEngineObjectType)}\\b")
                );
            }
            finally
            {
                TypeManager.UnityEngineObjectType = componentType;
                TypeManager.InitializeSharedStatics();
            }
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires component type safety checks")]
        public void BuildComponentType_WithNonComponent_WhenUnityEngineObjectTypeIsCorrect_ThrowsArgumentException()
        {
            TypeManager.ShutdownSharedStatics();
            var componentType = TypeManager.UnityEngineObjectType;
            TypeManager.UnityEngineObjectType = typeof(UnityEngine.Component);
            try
            {
                var type = typeof(TypeManagerTests);
                Assert.That(
                    () => TypeManager.BuildComponentType(type, new TypeManager.BuildComponentCache()),
                    Throws.ArgumentException.With.Message.Matches($"\\bmust inherit {typeof(UnityEngine.Component)}\\b")
                );
            }
            finally
            {
                TypeManager.UnityEngineObjectType = componentType;
                TypeManager.InitializeSharedStatics();
            }
        }

        [Test]
        public void BuildComponentType_WithComponent_WhenUnityEngineObjectTypeIsCorrect_DoesNotThrowException()
        {
            TypeManager.ShutdownSharedStatics();
            var componentType = TypeManager.UnityEngineObjectType;
            TypeManager.UnityEngineObjectType = typeof(UnityEngine.Component);
            try
            {
                TypeManager.BuildComponentType(typeof(UnityEngine.Transform), new TypeManager.BuildComponentCache());
            }
            finally
            {
                TypeManager.UnityEngineObjectType = componentType;
                TypeManager.InitializeSharedStatics();
            }
        }

        [TestCase(null)]
        [TestCase(typeof(TestType1))]
        [TestCase(typeof(InterfaceShared))]
        [TestCase(typeof(UnityEngine.Transform))]
        public void RegisterUnityEngineObjectType_WithWrongType_ThrowsArgumentException(Type type)
        {
            Assert.Throws<ArgumentException>(() => TypeManager.RegisterUnityEngineObjectType(type));
        }

        [Test]
        public void IsAssemblyReferencingEntities()
        {
            Assert.IsFalse(TypeManager.IsAssemblyReferencingEntities(typeof(UnityEngine.GameObject).Assembly));
            Assert.IsFalse(TypeManager.IsAssemblyReferencingEntities(typeof(System.Collections.Generic.List<>).Assembly));
            Assert.IsFalse(TypeManager.IsAssemblyReferencingEntities(typeof(Collections.NativeList<>).Assembly));

            Assert.IsTrue(TypeManager.IsAssemblyReferencingEntities(typeof(IComponentData).Assembly));
            Assert.IsTrue(TypeManager.IsAssemblyReferencingEntities(typeof(EcsTestData).Assembly));
        }

        [Test]
        public void IsAssemblyReferencingEntitiesOrUnityEngine()
        {
            TypeManager.IsAssemblyReferencingEntitiesOrUnityEngine(typeof(UnityEngine.GameObject).Assembly, out var gameObjectAsmRefersToEntities, out var gameObjectAsmRefersToUnityEngine);
            Assert.IsFalse(gameObjectAsmRefersToEntities);
            Assert.IsTrue(gameObjectAsmRefersToUnityEngine);

            TypeManager.IsAssemblyReferencingEntitiesOrUnityEngine(typeof(System.Collections.Generic.List<>).Assembly, out var listAsmRefersToEntities, out var listAsmRefersToUnityEngine);
            Assert.IsFalse(listAsmRefersToEntities);
            Assert.IsFalse(listAsmRefersToUnityEngine);

            TypeManager.IsAssemblyReferencingEntitiesOrUnityEngine(typeof(Collections.NativeList<>).Assembly, out var nativeListAsmRefersToEntities, out var nativeListAsmRefersToUnityEngine);
            Assert.IsFalse(nativeListAsmRefersToEntities);
            Assert.IsTrue(nativeListAsmRefersToUnityEngine);

            TypeManager.IsAssemblyReferencingEntitiesOrUnityEngine(typeof(IComponentData).Assembly, out var icomponentAsmRefersToEntities, out var icomponentAsmRefersToUnityEngine);
            Assert.IsTrue(icomponentAsmRefersToEntities);
            Assert.IsTrue(icomponentAsmRefersToUnityEngine);

            TypeManager.IsAssemblyReferencingEntitiesOrUnityEngine(typeof(EcsTestData).Assembly, out var ecsTestDataAsmRefersToEntities, out var ecsTestDataAsmRefersToUnityEngine);
            Assert.IsTrue(ecsTestDataAsmRefersToEntities);
            Assert.IsTrue(ecsTestDataAsmRefersToUnityEngine);
        }

        [DisableAutoTypeRegistration]
        public struct UnregisteredComponent : IComponentData
        {
            public int Int;
        }

        public struct ComponentWithPointerAndThenEntity_ForTestingOffsets : IComponentData
        {
            public IntPtr myptr;
            public Entity e;
        }

        [Test]
        public unsafe void TypeInfo_EntityReferenceOffsets_AreSortedAndCorrect()
        {
            var typeInfo = TypeManager.GetTypeInfo<EcsTestDataEntity2>();
            var typeInfo2 = TypeManager.GetTypeInfo<ComponentWithPointerAndThenEntity_ForTestingOffsets>();
            Assert.IsTrue(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<EcsTestDataEntity2>()));
            Assert.IsTrue(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<ComponentWithPointerAndThenEntity_ForTestingOffsets>()));
            Assert.AreEqual(2, typeInfo.EntityOffsetCount);
            Assert.AreEqual(1, typeInfo2.EntityOffsetCount);
            var offsets = TypeManager.GetEntityOffsets(typeInfo);
            var offsets2 = TypeManager.GetEntityOffsets(typeInfo2);
            int offsetA = offsets[0].Offset;
            int offsetB = offsets[1].Offset;
            int offset2a = offsets2[0].Offset;
            Assert.Less(offsetA, offsetB, "Entity offsets are assumed to be sorted.");
            Assert.AreEqual(UnsafeUtility.GetFieldOffset(typeof(EcsTestDataEntity2).GetField(nameof(EcsTestDataEntity2.value1))), offsetA);
            Assert.AreEqual(UnsafeUtility.GetFieldOffset(typeof(EcsTestDataEntity2).GetField(nameof(EcsTestDataEntity2.value2))), offsetB);
            Assert.AreEqual(UnsafeUtility.GetFieldOffset(typeof(ComponentWithPointerAndThenEntity_ForTestingOffsets).GetField(nameof(ComponentWithPointerAndThenEntity_ForTestingOffsets.e))), offset2a);

        }

        [Test]
        public unsafe void TypeInfo_BlobAssetReferenceOffsets_AreSortedAndCorrect()
        {
            var typeInfo = TypeManager.GetTypeInfo<EcsTestDataBlobAssetRef2>();
            Assert.AreEqual(2, typeInfo.BlobAssetRefOffsetCount);
            var offsets = TypeManager.GetBlobAssetRefOffsets(typeInfo);
            int offsetA = offsets[0].Offset;
            int offsetB = offsets[1].Offset;
            Assert.Less(offsetA, offsetB, "BlobAssetOffsets offsets are assumed to be sorted.");
            Assert.AreEqual(UnsafeUtility.GetFieldOffset(typeof(EcsTestDataBlobAssetRef2).GetField(nameof(EcsTestDataBlobAssetRef2.value))), offsetA);
            Assert.AreEqual(UnsafeUtility.GetFieldOffset(typeof(EcsTestDataBlobAssetRef2).GetField(nameof(EcsTestDataBlobAssetRef2.value2))), offsetB);
        }

        [Test]
        public unsafe void TypeInfo_WeakAssetReferenceOffsets_AreSortedAndCorrect()
        {
            var typeInfo = TypeManager.GetTypeInfo<EcsTestDataWeakAssetRef2>();
            Assert.AreEqual(2, typeInfo.WeakAssetRefOffsetCount);
            var offsets = TypeManager.GetWeakAssetRefOffsets(typeInfo);
            int offsetA = offsets[0].Offset;
            int offsetB = offsets[1].Offset;
            Assert.Less(offsetA, offsetB, "WeakAssetOffsets offsets are assumed to be sorted.");
            Assert.AreEqual(UnsafeUtility.GetFieldOffset(typeof(EcsTestDataWeakAssetRef2).GetField(nameof(EcsTestDataWeakAssetRef2.value))), offsetA);
            Assert.AreEqual(UnsafeUtility.GetFieldOffset(typeof(EcsTestDataWeakAssetRef2).GetField(nameof(EcsTestDataWeakAssetRef2.value2))), offsetB);
        }

        [Test]
        public unsafe void TypeInfo_UnityObjectReferenceOffsets_AreSortedAndCorrect()
        {
            var typeInfo = TypeManager.GetTypeInfo<EcsTestDataUnityObjectRef2>();
            Assert.AreEqual(2, typeInfo.UnityObjectRefOffsetCount);
            var offsets = TypeManager.GetUnityObjectRefOffsets(typeInfo);
            int offsetA = offsets[0].Offset;
            int offsetB = offsets[1].Offset;
            Assert.Less(offsetA, offsetB, "UnityObjectRef Offsets offsets are assumed to be sorted.");
            Assert.AreEqual(UnsafeUtility.GetFieldOffset(typeof(EcsTestDataUnityObjectRef2).GetField(nameof(EcsTestDataUnityObjectRef2.value))), offsetA);
            Assert.AreEqual(UnsafeUtility.GetFieldOffset(typeof(EcsTestDataUnityObjectRef2).GetField(nameof(EcsTestDataUnityObjectRef2.value2))), offsetB);
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences: true, hasNoBlobReferences: true)]
        public struct TypeOverridesBlobEntityUnmanaged : IComponentData
        {
            public Entity entity;
            public BlobAssetReference<int> blob;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences: true, hasNoBlobReferences: false)]
        public struct TypeOverridesEntityUnmanaged : IComponentData
        {
            public Entity entity;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences: false, hasNoBlobReferences: true)]
        public struct TypeOverridesBlobUnmanaged : IComponentData
        {
            public BlobAssetReference<int> blob;
        }

        [Test]
        public void TypeOverrideWorks_Unmanaged_InvalidTypesThrow()
        {
            var typeOverridesNoBlobNoUnityObjectInfo = TypeManager.GetTypeInfo<TypeOverridesBlobUnmanaged>();
            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesNoBlobNoUnityObjectInfo.TypeIndex));
            Assert.IsFalse(typeOverridesNoBlobNoUnityObjectInfo.HasUnityObjectRefs);
            Assert.IsFalse(typeOverridesNoBlobNoUnityObjectInfo.HasBlobAssetRefs);

            var typeOverridesNoEntityNoUnityObjectInfo = TypeManager.GetTypeInfo<TypeOverridesEntityUnmanaged>();
            Assert.IsFalse(typeOverridesNoEntityNoUnityObjectInfo.HasBlobAssetRefs);
            Assert.IsFalse(typeOverridesNoEntityNoUnityObjectInfo.HasUnityObjectRefs);
            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesNoEntityNoUnityObjectInfo.TypeIndex));

            var typeOverridesNoEntityNoBlobInfo = TypeManager.GetTypeInfo<TypeOverridesBlobEntityUnmanaged>();
            Assert.IsFalse(typeOverridesNoEntityNoBlobInfo.HasUnityObjectRefs);
            Assert.IsFalse(typeOverridesNoEntityNoBlobInfo.HasBlobAssetRefs);
            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesNoEntityNoBlobInfo.TypeIndex));

            var typeOverridesNoBlobNoEntityNoUnityObjectInfo = TypeManager.GetTypeInfo<TypeOverridesNoBlobNoEntityNoUnityObjectUnmanaged>();
            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesNoBlobNoEntityNoUnityObjectInfo.TypeIndex));
            Assert.IsFalse(typeOverridesNoBlobNoEntityNoUnityObjectInfo.HasBlobAssetRefs);
            Assert.IsFalse(typeOverridesNoBlobNoEntityNoUnityObjectInfo.HasUnityObjectRefs);
        }

        [DisableAutoTypeRegistration]
        struct NativeContainerComponent : IComponentData
        {
            NativeArray<int> data;
        }
        [DisableAutoTypeRegistration]
        struct NestedNativeContainerComponent : IComponentData
        {
            NativeArray<NativeArray<int>> data;
        }

        [Test]
        public void TestNativeContainersWorks()
        {
            TypeManager.ShutdownSharedStatics();
            Assert.DoesNotThrow(
                () => TypeManager.BuildComponentType(typeof(NativeContainerComponent), new TypeManager.BuildComponentCache()));
            TypeManager.InitializeSharedStatics();
        }

        [Test]
        public void TestNestedNativeContainersDoesNotThrow()
        {
            TypeManager.ShutdownSharedStatics();
            Assert.DoesNotThrow(() =>
            {
                Assert.IsTrue(TypeManager.BuildComponentType(typeof(NestedNativeContainerComponent), new TypeManager.BuildComponentCache()).TypeIndex.HasNativeContainer);
            });
            TypeManager.InitializeSharedStatics();
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        interface IBaseInterface
        {
        }
        class BaseClass
        {
        }

        sealed class SealedNothing
        {
            private float value;
        }

        sealed class ClassWithBlobAndEntity : BaseClass
        {
            BlobAssetReference<float> blob;
            Entity entity;
        }

        class TestNoRefManaged : IComponentData
        {
            float3 blah;
            SealedNothing nothing;
            float[] floatArray;
            SealedNothing[] nothingArray;
        }

        class TestEmbedInterface : IComponentData
        {
            private IBaseInterface baseInterface;
        }

        class TestTestEmbedBaseClass : IComponentData
        {
            private BaseClass baseClass;
        }

        class TestHasBlobRefAndEntityRef : IComponentData
        {
            ClassWithBlobAndEntity reference;
        }
        class TestEntityArray : IComponentData
        {
            Entity[] entityArray;
        }
        class TestBlobArray : IComponentData
        {
            BlobAssetReference<float>[] entityArray;
        }
        class TestEntityInClass : IComponentData
        {
            Entity entityRef;
        }
        public class TestEntityInClassWithManagedFields : IComponentData
        {
            public Entity EntityRef;
            public string Str1;
            public string Str2;
        }
        class TestNativeContainerInClass : IComponentData
        {
            NativeArray<int> array;
        }
        public class TestNativeContainerInClassWithManagedFields : IComponentData
        {
            public NativeArray<int> array;
            public string Str1;
            public string Str2;
        }

        public class TestBlobRefInClassWithManagedFields : IComponentData
        {
            public BlobAssetReference<float> BlobRef;
            public string Str1;
            public string Str2;
        }

#if UNITY_2022_3_11F1_OR_NEWER
        class CircularReferenceB : IComponentData
        {
            CircularReferenceA m_A1;
            CircularReferenceA m_A2;
            CircularReferenceB m_B;
            CircularReferenceA m_A3;
        }

        class CircularReferenceA : IComponentData
        {
            CircularReferenceB m_B1;
            CircularReferenceB m_B2;
            CircularReferenceA m_A;
            CircularReferenceB m_B3;
        }

        internal class MyAttributeTakingArray : Attribute
        {
            internal MyAttributeTakingArray(int[] arg) { }

        }

        /*
         * These systems exist to make sure that if you put a weird
         * attribute on your system, the ILPP / codegen situation will not
         * blow up.
         */
        [MyAttributeTakingArray(new[] { 1, 2, 3 })]
        partial struct ISystemWithFunkyAttribute: ISystem
        {

        }

        [MyAttributeTakingArray(new[] { 1, 2, 3 })]
        partial class SystemBaseWithFunkyAttribute :SystemBase
        {
            public SystemBaseWithFunkyAttribute() {

                var attarr = new Attribute[2];
                var y = new int[3];
                y[0] = 5; y[1] = 6; y[2] = 7;
                var x = new MyAttributeTakingArray(y);

                attarr[0] = x;
                attarr[1] = x;

            }

            protected override void OnUpdate()
            {
                throw new NotImplementedException();
            }
        }


        [Test]
        public void TestTypeHashOfUnregisteredType()
        {
            Assert.DoesNotThrow(() => TypeHash.CalculateStableTypeHash(typeof(int)));
        }

        [Test]
        public void TestTypeHashComponentWithCircularReference()
        {
            var cache = new Dictionary<Type, ulong>();
            var hashA = TypeHash.CalculateStableTypeHash(typeof(CircularReferenceA), cache);
            var hashB = TypeHash.CalculateStableTypeHash(typeof(CircularReferenceB), cache);

            // Clearing the cache is to simulate rebuilding the hash from a player build however to
            // confirm our hashes are stable, hash the types in reverse order and ensure the hashes match
            cache.Clear();

            Assert.AreEqual(hashB, TypeHash.CalculateStableTypeHash(typeof(CircularReferenceB), cache));
            Assert.AreEqual(hashA, TypeHash.CalculateStableTypeHash(typeof(CircularReferenceA), cache));
        }

        [Test]
        public void TestCalculateStableTypeHash_MatchesTypeManagerVersion()
        {
            Assert.AreEqual(
                TypeHash.CalculateStableTypeHash(typeof(CircularReferenceA), new Dictionary<Type, ulong>()),
                TypeManager.GetTypeInfo(TypeManager.GetTypeIndex<CircularReferenceA>()).StableTypeHash);
        }
#endif

        [DisableAutoTypeRegistration]
        [TypeManager.TypeOverrides(hasNoEntityReferences:true, hasNoBlobReferences:true, hasNoUnityObjectReferences:true)]
        public sealed class TypeOverridesBlobEntityUnityObject: IComponentData
        {
            public Entity entity;
            public BlobAssetReference<int> blob;
            public UnityObjectRef<UnityEngine.Object> objectRef;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences:true, hasNoBlobReferences:false, hasNoUnityObjectReferences:false)]
        public sealed class TypeOverridesEntity : IComponentData
        {
            public Entity entity;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences:false, hasNoBlobReferences:true, hasNoUnityObjectReferences:false)]
        public sealed class TypeOverridesBlob: IComponentData
        {
            public BlobAssetReference<int> blob;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences:false, hasNoBlobReferences:false, hasNoUnityObjectReferences:true)]
        public sealed class TypeOverridesUnityObjectRef: IComponentData
        {
            public UnityObjectRef<UnityEngine.Object> UnityObjectRef;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences:false, hasNoBlobReferences:true, hasNoUnityObjectReferences:false)]
        public sealed class TypeOverridesNoBlob: IComponentData
        {
            public Entity entity;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences:true, hasNoBlobReferences:false, hasNoUnityObjectReferences:false)]
        public sealed class TypeOverridesNoEntity : IComponentData
        {
            public BlobAssetReference<int> blob;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences:false, hasNoBlobReferences:false, hasNoUnityObjectReferences:true)]
        public sealed class TypeOverridesNoUnityObjectRefs : IComponentData
        {
            public BlobAssetReference<int> blob;
        }

        [TypeManager.TypeOverrides(hasNoEntityReferences:true, hasNoBlobReferences:true, hasNoUnityObjectReferences:true)]
        public sealed class TypeOverridesReferencesEntity: IComponentData
        {
            public string data;
        }

        struct TestTypeWithUnityObjRf : IComponentData
        {
            UnityObjectRef<UnityEngine.Object> UnityObjectRef;
        }

        [DisableAutoTypeRegistration]
        public sealed class EntityBlobAndUnityObjectRef : IComponentData
        {
            TestTypeWithUnityObjRf UnityObjectRef;
            TestTypeWithBlobRef blob;
            TestTypeWithEntity entity;
        }

        [Test]
        public void TestHasEntityReferencedManaged_Managed()
        {
            Assert.IsFalse(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestNoRefManaged>()));
            Assert.IsFalse(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestBlobArray>()));
            Assert.IsFalse(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestBlobRefInClassWithManagedFields>()));
            Assert.IsFalse(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestEmbedInterface>()));
            Assert.IsFalse(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestTestEmbedBaseClass>()));

            Assert.IsTrue(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestHasBlobRefAndEntityRef>()));
            Assert.IsTrue(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestEntityInClass>()));
            Assert.IsTrue(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestEntityInClassWithManagedFields>()));
            Assert.IsTrue(TypeManager.HasEntityReferences(TypeManager.GetTypeIndex<TestEntityArray>()));
        }

        [Test]
        public void TestHasBlobReferencesManaged()
        {
            Assert.IsFalse(TypeManager.GetTypeInfo<TestNoRefManaged>().HasBlobAssetRefs);
            Assert.IsFalse(TypeManager.GetTypeInfo<TestEntityInClass>().HasBlobAssetRefs);
            Assert.IsFalse(TypeManager.GetTypeInfo<TestEntityInClassWithManagedFields>().HasBlobAssetRefs);
            Assert.IsFalse(TypeManager.GetTypeInfo<TestEntityArray>().HasBlobAssetRefs);
            Assert.IsFalse(TypeManager.GetTypeInfo<TestEmbedInterface>().HasBlobAssetRefs);
            Assert.IsFalse(TypeManager.GetTypeInfo<TestTestEmbedBaseClass>().HasBlobAssetRefs);

            Assert.IsTrue(TypeManager.GetTypeInfo<TestHasBlobRefAndEntityRef>().HasBlobAssetRefs);
            Assert.IsTrue(TypeManager.GetTypeInfo<TestHasBlobRefAndEntityRef>().HasBlobAssetRefs);
            Assert.IsTrue(TypeManager.GetTypeInfo<TestBlobRefInClassWithManagedFields>().HasBlobAssetRefs);
        }
#endif


#if !UNITY_DISABLE_MANAGED_COMPONENTS

#pragma warning disable 649
        class TestScriptableObjectWithFields : UnityEngine.ScriptableObject
        {
            public int Value;
        }

        class ComponentWithScriptableObjectReference : IComponentData
        {
            public TestScriptableObjectWithFields Value;
        }
#pragma warning restore 649
        [Test]
        public void TypeManagerGetHashCode_WithNullScriptableObject_DoesNotThrow()
        {
            var component = new ComponentWithScriptableObjectReference();
            Assert.DoesNotThrow(() =>
            {
                TypeManager.GetHashCode(component, TypeManager.GetTypeIndex<ComponentWithScriptableObjectReference>());
            });
        }

        [DisableAutoTypeRegistration]
        class ArrayNativeContainerComponent : IComponentData
        {
            NativeArray<int>[] data;
        }
        [DisableAutoTypeRegistration]
        class NestedArrayNativeContainerComponent : IComponentData
        {
            NativeArray<NativeArray<int>>[] data;
        }

        [Test]
        public void TestArrayNativeContainersWorks()
        {
            TypeManager.ShutdownSharedStatics();
            Assert.DoesNotThrow(
                () => TypeManager.BuildComponentType(typeof(ArrayNativeContainerComponent), new TypeManager.BuildComponentCache()));
            TypeManager.InitializeSharedStatics();
        }

        [Test]
        public void TestNestedArrayNativeContainersDoesNotThrow()
        {
            TypeManager.ShutdownSharedStatics();
            Assert.DoesNotThrow(() =>
            {
                Assert.IsTrue(TypeManager.BuildComponentType(typeof(NestedArrayNativeContainerComponent), new TypeManager.BuildComponentCache()).TypeIndex.HasNativeContainer);
            });
            TypeManager.InitializeSharedStatics();
        }

        [Test]
        public void TypeOverrideWorks_Managed_InvalidTypesDoNotThrow()
        {
            var typeOverridesBlobInfo = TypeManager.GetTypeInfo<TypeOverridesBlob>();
            // Shouldn't have
            Assert.IsFalse(typeOverridesBlobInfo.HasBlobAssetRefs);
            Assert.IsTrue(typeOverridesBlobInfo.BlobAssetRefOffsetCount == 0);

            var typeOverridesEntityInfo = TypeManager.GetTypeInfo<TypeOverridesEntity>();
            // Shouldn't have
            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesEntityInfo.TypeIndex));
            Assert.IsTrue(typeOverridesEntityInfo.EntityOffsetCount == 0);

            var typeOverridesUnityObjectRefInfo = TypeManager.GetTypeInfo<TypeOverridesUnityObjectRef>();
            // Shouldn't have
            Assert.IsFalse(typeOverridesUnityObjectRefInfo.HasBlobAssetRefs);
            Assert.IsTrue(typeOverridesUnityObjectRefInfo.BlobAssetRefOffsetCount == 0);

            Assert.IsFalse(typeOverridesUnityObjectRefInfo.TypeIndex.HasEntityReferences);
            Assert.IsTrue(typeOverridesUnityObjectRefInfo.EntityOffsetCount == 0);

            Assert.IsFalse(typeOverridesUnityObjectRefInfo.HasUnityObjectRefs);
            Assert.IsTrue(typeOverridesUnityObjectRefInfo.UnityObjectRefOffsetCount == 0);
        }

        [Test]
        public void TypeOverrideWorks_Managed_ValidTypesDoNotThrow()
        {
            var typeOverridesNoBlobInfo = TypeManager.GetTypeInfo<TypeOverridesNoBlob>();
            // Shouldn't have
            Assert.IsFalse(typeOverridesNoBlobInfo.HasBlobAssetRefs);
            Assert.IsTrue(typeOverridesNoBlobInfo.BlobAssetRefOffsetCount == 0);
            // Should have
            Assert.IsTrue(TypeManager.HasEntityReferences(typeOverridesNoBlobInfo.TypeIndex));

            var typeOverridesNoEntityInfo = TypeManager.GetTypeInfo<TypeOverridesNoEntity>();
            // Shouldn't have
            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesNoEntityInfo.TypeIndex));
            Assert.IsTrue(typeOverridesNoEntityInfo.EntityOffsetCount == 0);
            // Should have
            Assert.IsTrue(typeOverridesNoEntityInfo.HasBlobAssetRefs);

            var typeOverridesNoUnityObjRefsInfo = TypeManager.GetTypeInfo<TypeOverridesNoUnityObjectRefs>();
            // Shouldn't have
            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesNoUnityObjRefsInfo.TypeIndex));
            Assert.IsTrue(typeOverridesNoUnityObjRefsInfo.EntityOffsetCount == 0);
            // Should have
            Assert.IsTrue(typeOverridesNoUnityObjRefsInfo.HasBlobAssetRefs);

            var typeOverridesNoBlobNoEntityInfo = TypeManager.GetTypeInfo<TypeOverridesReferencesEntity>();
            // Shouldn't have
            Assert.IsFalse(typeOverridesNoBlobNoEntityInfo.HasBlobAssetRefs);
            Assert.IsTrue(typeOverridesNoBlobNoEntityInfo.BlobAssetRefOffsetCount == 0);

            Assert.IsFalse(TypeManager.HasEntityReferences(typeOverridesNoBlobNoEntityInfo.TypeIndex));
            Assert.IsTrue(typeOverridesNoBlobNoEntityInfo.EntityOffsetCount == 0);

            Assert.IsFalse(typeOverridesNoBlobNoEntityInfo.HasUnityObjectRefs);
            Assert.IsTrue(typeOverridesNoBlobNoEntityInfo.UnityObjectRefOffsetCount == 0);
        }

        [TypeManager.ForceReference(hasEntityReferences:false, hasBlobReferences:true, hasUnityObjectReferences:false)]
        public sealed class ForceReferenceSearchBlob: IComponentData
        {
            public BlobAssetReference<int> blob;
        }

        [TypeManager.ForceReference(hasEntityReferences:true, hasBlobReferences:false, hasUnityObjectReferences:false)]
        public sealed class ForceReferenceSearchEntity : IComponentData
        {
            public Entity entity;
        }

        [TypeManager.ForceReference(hasEntityReferences:false, hasBlobReferences:false, hasUnityObjectReferences:true)]
        public sealed class ForceReferenceSearchUnityObjectRef: IComponentData
        {
            public UnityObjectRef<UnityEngine.Object> UnityObjectRef;
        }

        [TypeManager.ForceReference(hasEntityReferences:false, hasBlobReferences:false, hasUnityObjectReferences:true)]
        public sealed class ForceReferenceSearchNoUnityObjectRef : IComponentData
        {
            public string data;
        }

        [TypeManager.ForceReference(hasEntityReferences:false, hasBlobReferences:true, hasUnityObjectReferences:false)]
        public sealed class ForceReferenceSearchNoBlob : IComponentData
        {
            public string data;
        }

        [TypeManager.ForceReference(hasEntityReferences:true, hasBlobReferences:false, hasUnityObjectReferences:false)]
        public sealed class ForceReferenceSearchNoEntity : IComponentData
        {
            public string data;
        }

        [TypeManager.ForceReference(hasEntityReferences:true, hasBlobReferences:true, hasUnityObjectReferences:true)]
        public sealed class ForceReferenceSearchAllRefs
            : IComponentData
        {
            public Entity entity;
            public BlobAssetReference<int> blob;
            public UnityObjectRef<UnityEngine.Object> UnityObjectRef;
        }

        [TypeManager.ForceReference(hasEntityReferences:true, hasBlobReferences:true, hasUnityObjectReferences:true)]
        public sealed class ForceReferenceSearchNoRefs
            : IComponentData
        {
            public string data;
        }

        [TypeManager.ForceReference(hasEntityReferences:false, hasBlobReferences:false, hasUnityObjectReferences:true)]
        public sealed class ForceReferenceSearchAllRefsNotAllOverridden
            : IComponentData
        {
            public Entity entity;
            public BlobAssetReference<int> blob;
            public UnityObjectRef<UnityEngine.Object> UnityObjectRef;
        }


        [Test]
        public void ForceReferenceSearchWorks_Managed()
        {
            // Assert you have it: you are telling the truth
            var frsBlobInfo = TypeManager.GetTypeInfo<ForceReferenceSearchBlob>();
            Assert.IsTrue(frsBlobInfo.HasBlobAssetRefs);

            var frsEntityInfo = TypeManager.GetTypeInfo<ForceReferenceSearchEntity>();
            Assert.IsTrue(TypeManager.HasEntityReferences(frsEntityInfo.TypeIndex));

            var frsUnityObjectRefInfo = TypeManager.GetTypeInfo<ForceReferenceSearchUnityObjectRef>();
            Assert.IsTrue(frsUnityObjectRefInfo.HasUnityObjectRefs);

            var frsAllRefsInfo = TypeManager.GetTypeInfo<ForceReferenceSearchAllRefs>();
            Assert.IsTrue(frsAllRefsInfo.HasBlobAssetRefs);
            Assert.IsTrue(TypeManager.HasEntityReferences(frsAllRefsInfo.TypeIndex));
            Assert.IsTrue(frsAllRefsInfo.HasUnityObjectRefs);

            // Assert you have it: you are lying
            var frsNoBlobInfo = TypeManager.GetTypeInfo<ForceReferenceSearchNoBlob>();
            Assert.IsTrue(frsNoBlobInfo.HasBlobAssetRefs); //

            var frsNoEntityInfo = TypeManager.GetTypeInfo<ForceReferenceSearchNoEntity>();
            Assert.IsTrue(TypeManager.HasEntityReferences(frsNoEntityInfo.TypeIndex));

            var frsNoUnityObjectRefInfo = TypeManager.GetTypeInfo<ForceReferenceSearchNoUnityObjectRef>();
            Assert.IsTrue(frsNoUnityObjectRefInfo.HasUnityObjectRefs);

            var frsNoAllRefsInfo = TypeManager.GetTypeInfo<ForceReferenceSearchNoRefs>();
            Assert.IsTrue(frsNoAllRefsInfo.HasBlobAssetRefs);
            Assert.IsTrue(TypeManager.HasEntityReferences(frsNoAllRefsInfo.TypeIndex));
            Assert.IsTrue(frsNoAllRefsInfo.HasUnityObjectRefs);

            // Assert you have one: still find the others before depth
            var frsAllRefsInfoNotAllOverridden = TypeManager.GetTypeInfo<ForceReferenceSearchAllRefsNotAllOverridden>();
            Assert.IsTrue(frsAllRefsInfoNotAllOverridden.HasBlobAssetRefs);
            Assert.IsTrue(TypeManager.HasEntityReferences(frsAllRefsInfoNotAllOverridden.TypeIndex));
            Assert.IsTrue(frsAllRefsInfoNotAllOverridden.HasUnityObjectRefs);
        }


        class CircularReferenceViaArray
        {
            public CircularReferenceViaArray[] m_Value;
        }

        class CircularReferenceViaArrayWithNestedNativeContainer
        {
            public CircularReferenceViaArrayWithNestedNativeContainer[] m_Value;
            public NativeArray<NativeArray<int>> data;
        }

        [DisableAutoTypeRegistration]
        class ComponentWithValidCircularReference : IComponentData
        {
            public CircularReferenceViaArray[] m_NestedTypeCircularReference;
        }

        [DisableAutoTypeRegistration]
        class ComponentWithValidCircularReferenceAndNestedNativeContainer : IComponentData
        {
            public CircularReferenceViaArrayWithNestedNativeContainer[] m_NestedTypeCircularReference;
        }

        [Test]
        [TestRequiresDotsDebugOrCollectionChecks("Test requires component type safety checks")]
        public void TestNestedNativeContainerCachingHandlesTypeWithValidCircularReference()
        {
            TypeManager.ShutdownSharedStatics();
            Assert.DoesNotThrow(
                () => TypeManager.BuildComponentType(typeof(ComponentWithValidCircularReference), new TypeManager.BuildComponentCache()));
            TypeManager.InitializeSharedStatics();
        }

        [Test]
        public void TestNestedNativeContainerCachingHandlesTypeWithValidCircularReference_StillDetectsNestedNativeContainers()
        {
            TypeManager.ShutdownSharedStatics();
            Assert.DoesNotThrow(() =>
            {
                Assert.IsTrue(TypeManager.BuildComponentType(typeof(ComponentWithValidCircularReferenceAndNestedNativeContainer), new TypeManager.BuildComponentCache()).TypeIndex.HasNativeContainer);
            });
            TypeManager.InitializeSharedStatics();
        }
#endif
    }
}
