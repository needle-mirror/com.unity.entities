using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [TestFixture]
    public class TransformTests : ECSTestsFixture
    {
        [Test]
        [Ignore("Likely bug in TransformSystem")]
        public void LocalSpaceToGlobalSpace()
        {
            var parent = m_Manager.CreateEntity(typeof(Position), typeof(LocalPosition), typeof(LocalRotation), typeof(Rotation), typeof(TransformParent));
            var child = m_Manager.CreateEntity(typeof(Position), typeof(LocalPosition), typeof(LocalRotation), typeof(Rotation), typeof(TransformParent));
            
            m_Manager.SetComponentData(parent, new LocalPosition(new float3(1, 2, 3)));
            m_Manager.SetComponentData(parent, new LocalRotation(quaternion.identity));

            m_Manager.SetComponentData(child, new TransformParent { Value = parent });

            m_Manager.SetComponentData(child, new LocalPosition(new float3(4, 5, 6)));
            m_Manager.SetComponentData(child, new LocalRotation(quaternion.identity));

            World.GetOrCreateManager<TransformSystem>().Update();
            
            //@TODO: check all component values...
            Assert.AreEqual(new float3(5, 7, 9), m_Manager.GetComponentData<Position>(child).Value);
        }
    }
}
