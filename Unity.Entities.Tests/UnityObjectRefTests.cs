#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Entities.Tests
{
    [Serializable]
    public class UnityObjectRefTests : ECSTestsCommonBase
    {
        private string TempAssetDir;
        private string TempAssetPath;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            var guid = AssetDatabase.CreateFolder("Assets", nameof(UnityObjectRefTests));
            TempAssetDir = AssetDatabase.GUIDToAssetPath(guid);
            TempAssetPath = $"{TempAssetDir}/TempTextAsset.asset";
            var textAsset = new TextAsset("Foo");
            AssetDatabase.CreateAsset(textAsset, TempAssetPath);
            AssetDatabase.Refresh();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            AssetDatabase.DeleteAsset(TempAssetDir);
        }

        struct StructWithUnityObjectRef : IComponentData
        {
            public UnityObjectRef<UnityEngine.Object> UnityObjectRef;
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        class ClassWithUnityObjectRef : IComponentData
        {
            public UnityObjectRef<UnityEngine.Object> UnityObjectRef;
        }

        struct SharedComponentManagedWithUnityObjectRef : ISharedComponentData, IEquatable<SharedComponentManagedWithUnityObjectRef>
        {
            public UnityObjectRef<UnityEngine.Object> UnityObjectRef;
            public UnityEngine.Object DummyManagedField;

            public bool Equals(SharedComponentManagedWithUnityObjectRef other)
            {
                return UnityObjectRef.Equals(other.UnityObjectRef) && Equals(DummyManagedField, other.DummyManagedField);
            }

            public override bool Equals(object obj)
            {
                return obj is SharedComponentManagedWithUnityObjectRef other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(UnityObjectRef, DummyManagedField);
            }
        }
#endif // !UNITY_DISABLE_MANAGED_COMPONENTS

        struct SharedComponentWithUnityObjectRef : ISharedComponentData
        {
            public UnityObjectRef<UnityEngine.Object> UnityObjectRef;
        }

        [TypeManager.TypeOverrides(hasNoBlobReferences:true, hasNoEntityReferences:true, hasNoUnityObjectReferences:true)]
        struct StructWithUnityObjectRefOverride : IComponentData
        {
            public UnityObjectRef<UnityEngine.Object> UnityObjectRef;
        }

#if (UNITY_2022_3 && UNITY_2022_3_43F1_OR_NEWER) || (UNITY_6000 && UNITY_6000_0_16F1_OR_NEWER)
        [UnityTest]
        public IEnumerator AssetGC_StructWithUnityObjectRefOverride_AssetReleased()
        {
            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(TempAssetPath);
            var instanceID = textAsset.GetInstanceID();

            using var world = new World("TestWorld");
            var entity = world.EntityManager.CreateEntity(new ComponentType(typeof(StructWithUnityObjectRefOverride)));
            world.EntityManager.SetComponentData(entity, new StructWithUnityObjectRefOverride{UnityObjectRef = textAsset});

            Assert.IsTrue(AssetDatabase.IsMainAssetAtPathLoaded(TempAssetPath));

            textAsset = null;

            yield return Resources.UnloadUnusedAssets();

            Assert.IsFalse(AssetDatabase.IsMainAssetAtPathLoaded(TempAssetPath));
        }

        [UnityTest]
        public IEnumerator AssetGC_StructComponent_AssetNotReleased()
        {
            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(TempAssetPath);
            var instanceID = textAsset.GetInstanceID();

            using var world = new World("TestWorld");
            var entity = world.EntityManager.CreateEntity(new ComponentType(typeof(StructWithUnityObjectRef)));
            world.EntityManager.SetComponentData(entity, new StructWithUnityObjectRef{UnityObjectRef = textAsset});

            Assert.IsTrue(AssetDatabase.IsMainAssetAtPathLoaded(TempAssetPath));

            textAsset = null;

            yield return Resources.UnloadUnusedAssets();

            Assert.IsTrue(AssetDatabase.IsMainAssetAtPathLoaded(TempAssetPath));
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [UnityTest]
        public IEnumerator AssetGC_ClassComponent_AssetNotReleased()
        {
            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(TempAssetPath);
            var instanceID = textAsset.GetInstanceID();

            using var world = new World("TestWorld");
            var entity = world.EntityManager.CreateEntity(new ComponentType(typeof(ClassWithUnityObjectRef)));
            world.EntityManager.SetComponentData(entity, new ClassWithUnityObjectRef{UnityObjectRef = textAsset});

            Assert.IsTrue(AssetDatabase.IsMainAssetAtPathLoaded(TempAssetPath));

            textAsset = null;

            yield return Resources.UnloadUnusedAssets();

            Assert.IsTrue(AssetDatabase.IsMainAssetAtPathLoaded(TempAssetPath));
        }

        [UnityTest]
        public IEnumerator AssetGC_SharedComponentManaged_AssetNotReleased()
        {
            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(TempAssetPath);
            var instanceID = textAsset.GetInstanceID();

            using var world = new World("TestWorld");
            var entity = world.EntityManager.CreateEntity(new ComponentType(typeof(SharedComponentManagedWithUnityObjectRef)));
            world.EntityManager.SetSharedComponentManaged(entity, new SharedComponentManagedWithUnityObjectRef{UnityObjectRef = textAsset});

            Assert.IsTrue(AssetDatabase.IsMainAssetAtPathLoaded(TempAssetPath));

            textAsset = null;

            yield return Resources.UnloadUnusedAssets();

            Assert.IsTrue(AssetDatabase.IsMainAssetAtPathLoaded(TempAssetPath));
        }
#endif

        [UnityTest]
        public IEnumerator AssetGC_SharedComponent_AssetNotReleased()
        {
            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(TempAssetPath);
            var instanceID = textAsset.GetInstanceID();

            using var world = new World("TestWorld");
            var entity = world.EntityManager.CreateEntity(new ComponentType(typeof(SharedComponentWithUnityObjectRef)));
            world.EntityManager.SetSharedComponent(entity, new SharedComponentWithUnityObjectRef{UnityObjectRef = textAsset});

            Assert.IsTrue(AssetDatabase.IsMainAssetAtPathLoaded(TempAssetPath));

            textAsset = null;

            yield return Resources.UnloadUnusedAssets();

            Assert.IsTrue(AssetDatabase.IsMainAssetAtPathLoaded(TempAssetPath));
        }
#endif // (UNITY_2022_3 && UNITY_2022_3_43F1_OR_NEWER) || (UNITY_6000 && UNITY_6000_0_16F1_OR_NEWER)
    }
}
#endif
