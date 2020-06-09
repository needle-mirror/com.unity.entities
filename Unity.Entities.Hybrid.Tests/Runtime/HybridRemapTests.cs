using System;
using NUnit.Framework;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public class HybridRemapTests : ECSTestsFixture
    {
        class TestHybridComponent : MonoBehaviour
        {
        }

        [Test]
        public unsafe void HybridComponents_WillThrow_IfRemapped()
        {
            var gameObject = new GameObject("TestHybridComponent", typeof(TestHybridComponent));

            var managedObjectRemap = new ManagedObjectRemap();
            Assert.Throws<ArgumentException>(() =>
            {
                var local = (object) gameObject;
                managedObjectRemap.RemapEntityReferences(ref local, null);
            });
        }

        [Test]
        public unsafe void HybridComponents_WillThrow_IfRemappedPrefab()
        {
            var gameObject = new GameObject("TestHybridComponent", typeof(TestHybridComponent));

            var managedObjectRemap = new ManagedObjectRemap();
            Assert.Throws<ArgumentException>(() =>
            {
                var local = (object) gameObject;
                managedObjectRemap.RemapEntityReferencesForPrefab(ref local, null, null, 0);
            });
        }
    }
}
