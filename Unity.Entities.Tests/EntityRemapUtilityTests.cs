using NUnit.Framework;
using Unity.Collections;

namespace Unity.Entities.Tests
{
    public class EntityRemapUtilityTests : ECSTestsCommonBase
    {
        NativeArray<EntityRemapUtility.EntityRemapInfo> m_Remapping;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            m_Remapping = new NativeArray<EntityRemapUtility.EntityRemapInfo>(100, Allocator.Persistent);
        }

        [TearDown]
        public override void TearDown()
        {
            m_Remapping.Dispose();
            base.TearDown();
        }

        [Test]
        public void RemapEntityMapsSourceToTarget()
        {
            var a = new Entity { Index = 1, Version = 2 };
            var b = new Entity { Index = 3, Version = 5 };
            EntityRemapUtility.AddEntityRemapping(ref m_Remapping, a, b);

            Assert.AreEqual(b, EntityRemapUtility.RemapEntity(ref m_Remapping, a));
        }

        [Test]
        public void RemapEntityMapsNonExistentSourceToNull()
        {
            var a = new Entity { Index = 1, Version = 2 };
            var b = new Entity { Index = 3, Version = 5 };
            var oldA = new Entity { Index = 1, Version = 1 };
            EntityRemapUtility.AddEntityRemapping(ref m_Remapping, a, b);

            Assert.AreEqual(Entity.Null, EntityRemapUtility.RemapEntity(ref m_Remapping, oldA));
        }

        [Test]
        public void RemapEntityMapsNullSourceToNull()
        {
            Assert.AreEqual(Entity.Null, EntityRemapUtility.RemapEntity(ref m_Remapping, Entity.Null));
        }

        struct EmptyStruct : IComponentData
        {
        }

        static TypeManager.EntityOffsetInfo[] GetEntityOffsets(System.Type type)
        {
            unsafe
            {
                var info = TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(type));
                if (info.EntityOffsetCount > 0)
                {
                    TypeManager.EntityOffsetInfo[] ei = new TypeManager.EntityOffsetInfo[info.EntityOffsetCount];
                    for (var i = 0; i < info.EntityOffsetCount; ++i)
                        ei[i] = TypeManager.GetEntityOffsets(info)[i];
                    return ei;
                }
                return null;
            }
        }

        [Test]
        public void CalculateEntityOffsetsReturnsNullIfNoEntities()
        {
            var offsets = GetEntityOffsets(typeof(EmptyStruct));
            Assert.IsNull(offsets);
        }

        [Test]
        public void CalculateEntityOffsetsReturns0IfEntity()
        {
            var offsets = GetEntityOffsets(typeof(Entity));
            Assert.AreEqual(1, offsets.Length);
            Assert.AreEqual(0, offsets[0].Offset);
        }

        struct TwoEntityStruct : IComponentData
        {
            // The offsets of these fields are accessed through reflection
            #pragma warning disable CS0169  // field never used warning.
            Entity a;
            int b;
            Entity c;
            float d;
            #pragma warning restore CS0169
        }

        [Test]
        public void CalculateEntityOffsetsReturnsOffsetsOfEntities()
        {
            var offsets = GetEntityOffsets(typeof(TwoEntityStruct));
            Assert.AreEqual(2, offsets.Length);
            Assert.AreEqual(0, offsets[0].Offset);
            Assert.AreEqual(12, offsets[1].Offset);
        }

        struct EmbeddedEntityStruct : IComponentData
        {
            // The offsets of these fields are accessed through reflection
            #pragma warning disable CS0169  // field never used warning.
            int a;
            TwoEntityStruct b;
            #pragma warning restore CS0169
        }

        [Test]
        public void CalculateEntityOffsetsReturnsOffsetsOfEmbeddedEntities()
        {
            var offsets = GetEntityOffsets(typeof(EmbeddedEntityStruct));
            Assert.AreEqual(2, offsets.Length);
            Assert.AreEqual(4, offsets[0].Offset);
            Assert.AreEqual(16, offsets[1].Offset);
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        // Test uses class component types
        [Test]
        public void HasReferencesManaged_Basic()
        {
            //shallow types with no recursion

            //primitive
            EntityRemapUtility.HasReferencesManaged(typeof(string),out var entRef, out var blobRef, out var unityObjectRef);

            Assert.IsFalse(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsFalse(unityObjectRef);

            EntityRemapUtility.HasReferencesManaged(typeof(System.Int32),out entRef, out blobRef, out unityObjectRef);

            Assert.IsFalse(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsFalse(unityObjectRef);

            //blob only
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.TypeOverridesBlob),out entRef, out blobRef, out unityObjectRef);

            Assert.IsFalse(entRef);
            Assert.IsTrue(blobRef);
            Assert.IsFalse(unityObjectRef);

            //entity only
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.TypeOverridesEntity),out entRef, out blobRef, out unityObjectRef);

            Assert.IsTrue(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsFalse(unityObjectRef);

            //blob and entity and unityobjref - Will early out because all three are known to be false, so actually returns false
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.TypeOverridesBlobEntityUnityObject),out entRef, out blobRef, out unityObjectRef);

            Assert.IsFalse(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsFalse(unityObjectRef);

            // entity ref in class with managed strings
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.TestEntityInClassWithManagedFields),out entRef, out blobRef, out unityObjectRef);

            Assert.IsTrue(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsFalse(unityObjectRef);

            // blob asset ref in class with managed strings
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.TestBlobRefInClassWithManagedFields),out entRef, out blobRef, out unityObjectRef);

            Assert.IsFalse(entRef);
            Assert.IsTrue(blobRef);
            Assert.IsFalse(unityObjectRef);
        }

        public sealed class RecursionA3: IComponentData
        {
            public RecursionA2 a2;
            public RecursionB2 b2;
            public RecursionC2 c2;
        }
        public sealed class RecursionA2: IComponentData
        {
            public Recursion1LayerBlob blob1;
            public Recursion1LayerEntity entity1;
            public Recursion1LayerUnityObject objectRef;
        }

        public sealed class RecursionB2: IComponentData
        {
            public Recursion1LayerBlob blob1;
            public Recursion1LayerEntity entity1;
            public Recursion1LayerUnityObject objectRef;
        }

        public sealed class RecursionC2: IComponentData
        {
            public Recursion1LayerBlob blob1;
            public Recursion1LayerEntity entity1;
            public Recursion1LayerUnityObject objectRef;
        }

        public sealed class Recursion1LayerEntity: IComponentData
        {
            TypeManagerTests.TypeOverridesEntity entity;
        }

        public sealed class Recursion1LayerBlob: IComponentData
        {
            TypeManagerTests.TypeOverridesBlob blob;
        }

        public sealed class Recursion1LayerUnityObject : IComponentData
        {
            TypeManagerTests.TypeOverridesUnityObjectRef unityObjectRef;
        }

        [Test]
        public void HasReferencesManaged_Recursion()
        {
            EntityRemapUtility.HasReferencesManaged(typeof(RecursionA2),out var entRef, out var blobRef, out var unityObjectRef);

            Assert.IsTrue(entRef);
            Assert.IsTrue(blobRef);
            Assert.IsTrue(unityObjectRef);

            EntityRemapUtility.HasReferencesManaged(typeof(RecursionA3),out  entRef, out  blobRef, out unityObjectRef);

            Assert.IsTrue(entRef);
            Assert.IsTrue(blobRef);
            Assert.IsTrue(unityObjectRef);

            EntityRemapUtility.HasReferencesManaged(typeof(EcsTestManagedDataEntity),out  entRef, out  blobRef, out unityObjectRef);

            Assert.IsTrue(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsFalse(unityObjectRef);

            // Has nested entity, force override true for entity
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.ForceReferenceSearchEntity),out entRef, out blobRef, out unityObjectRef);

            Assert.IsTrue(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsFalse(unityObjectRef);

            // Has nested blob, force override true for blob
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.ForceReferenceSearchBlob),out entRef, out blobRef, out unityObjectRef);

            Assert.IsFalse(entRef);
            Assert.IsTrue(blobRef);
            Assert.IsFalse(unityObjectRef);

            // Has nested unityobjref, force override true for unityobjref
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.ForceReferenceSearchUnityObjectRef),out entRef, out blobRef, out unityObjectRef);

            Assert.IsFalse(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsTrue(unityObjectRef);

            // Has no nested entity, force override true for entity
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.ForceReferenceSearchNoEntity),out entRef, out blobRef, out unityObjectRef);

            Assert.IsTrue(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsFalse(unityObjectRef);

            // Has no nested blob, force override true for blob
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.ForceReferenceSearchNoBlob),out entRef, out blobRef, out unityObjectRef);

            Assert.IsFalse(entRef);
            Assert.IsTrue(blobRef);
            Assert.IsFalse(unityObjectRef);

            // Has no nested unityobjref, force override true for unityobjref
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.ForceReferenceSearchNoUnityObjectRef),out entRef, out blobRef, out unityObjectRef);

            Assert.IsFalse(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsTrue(unityObjectRef);

            // Has no nested refs, force override true for all ref types
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.ForceReferenceSearchNoRefs),out entRef, out blobRef, out unityObjectRef);

            Assert.IsTrue(entRef);
            Assert.IsTrue(blobRef);
            Assert.IsTrue(unityObjectRef);

            // Has references, but are beyond the maxDepth, should mark as not having any references
            EntityRemapUtility.HasReferencesManaged(typeof(TypeManagerTests.EntityBlobAndUnityObjectRef),out entRef, out blobRef, out unityObjectRef, null, 0);

            Assert.IsFalse(entRef);
            Assert.IsFalse(blobRef);
            Assert.IsFalse(unityObjectRef);
        }
#endif // !UNITY_DISABLE_MANAGED_COMPONENTS

    }
}
