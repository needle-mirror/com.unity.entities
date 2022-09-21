using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Conversion;
using Unity.Entities.Tests;
using Unity.Jobs;
using UnityEngine;

namespace Unity.Entities.Hybrid.Tests.Baking
{
    struct UnityObjectRefTestJob : IJob
    {
        [ReadOnly]
        public UnityObjectRefTestComponent unityObjRefComponent;
        [ReadOnly]
        public UnityObjectRefTestComponent unityObjRefComponent2;

        // The code actually running on the job
        public void Execute()
        {
            Assert.AreEqual(unityObjRefComponent, unityObjRefComponent2);
            Assert.AreEqual(unityObjRefComponent.Texture, unityObjRefComponent2.Texture);
        }
    }

    struct UnityObjectRefTestComponent : IComponentData
    {
        public UnityObjectRef<Texture2D> Texture;
    }

    public class UnityObjectRefTests : ECSTestsCommonBase
    {
        [SetUp]
        public override void Setup()
        {
            base.Setup();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
        }

        [Test]
        public void UnityObjectRef_Access()
        {
            Texture2D texture = Texture2D.whiteTexture;
            UnityObjectRefTestComponent unityObjRefComponent = new UnityObjectRefTestComponent() {Texture = Texture2D.whiteTexture};
            UnityObjectRefTestComponent unityObjRefComponent2 = new UnityObjectRefTestComponent() {Texture = Texture2D.whiteTexture};

            Assert.AreEqual(unityObjRefComponent, unityObjRefComponent2);
            Assert.AreNotEqual(unityObjRefComponent.Texture, texture);
            Assert.AreEqual(unityObjRefComponent.Texture, unityObjRefComponent2.Texture);
            Assert.AreEqual(unityObjRefComponent.Texture.Value, texture);

            var job = new UnityObjectRefTestJob()
            {
                unityObjRefComponent = unityObjRefComponent,
                unityObjRefComponent2 = unityObjRefComponent2
            };

            job.Run();

            var jobHandle = job.Schedule();
            jobHandle.Complete();
        }
    }
}
