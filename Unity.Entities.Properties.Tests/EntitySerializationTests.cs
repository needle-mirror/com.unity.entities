using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Properties.Serialization;
using UnityEngine;
using Unity.Properties;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace Unity.Entities.Properties.Tests
{
    [TestFixture]
    public sealed class EntitySerializationTests
    {
        private World 			m_PreviousWorld;
        private World 			m_World;
        private EntityManager   m_Manager;

        [SetUp]
        public void Setup()
        {
            m_PreviousWorld = World.Active;
            m_World = World.Active = new World ("Test World");
            m_Manager = m_World.GetOrCreateManager<EntityManager> ();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Manager != null)
            {
                m_World.Dispose();
                m_World = null;

                World.Active = m_PreviousWorld;
                m_PreviousWorld = null;
                m_Manager = null;
            }
        }

        /// <summary>
        /// Writes an entity to json
        /// </summary>
        [Test]
        public void SimpleFlat()
        {
            var entity = m_Manager.CreateEntity(typeof(TestComponent), typeof(TestComponent2));

            var testComponent = m_Manager.GetComponentData<TestComponent>(entity);
            testComponent.x = 123f;
            m_Manager.SetComponentData(entity, testComponent);

            var container = new EntityContainer(m_Manager, entity);
            var json = JsonSerializer.Serialize(ref container);
            Debug.Log(json);
        }

        [Test]
        public void SimpleNested()
        {
            var entity = m_Manager.CreateEntity(typeof(NestedComponent));

            var nestedComponent = m_Manager.GetComponentData<NestedComponent>(entity);
            nestedComponent.test.x = 123f;
            m_Manager.SetComponentData(entity, nestedComponent);

            var container = new EntityContainer(m_Manager, entity);
            var json = JsonSerializer.Serialize(ref container);
            Debug.Log(json);
        }
        
        [Test]
        public void MathOverrides()
        {
            var entity = m_Manager.CreateEntity(typeof(MathComponent));

            var math = m_Manager.GetComponentData<MathComponent>(entity);
            math.v2 = new float2(1f, 2f);
            math.v3 = new float3(1f, 2f, 3f);
            math.v4 = new float4(1f, 2f, 3f, 4f);
            m_Manager.SetComponentData(entity, math);

            var container = new EntityContainer(m_Manager, entity);
            var json = JsonSerializer.Serialize(ref container);
            Debug.Log(json);
        }
        
        [Test]
        public void BlittableTest()
        {
            var entity = m_Manager.CreateEntity(typeof(BlitComponent));

            var comp = m_Manager.GetComponentData<BlitComponent>(entity);
            comp.blit.x = 123f;
            comp.blit.y = 456.789;
            comp.blit.z = -12;
            comp.flt = 0.01f;

            var container = new EntityContainer(m_Manager, entity);
            var json = JsonSerializer.Serialize(ref container);
            Debug.Log(json);
        }
    }
}
