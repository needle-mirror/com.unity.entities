using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.Entities.Tests
{
    [DisableAutoCreation]

#if UNITY_CSHARP_TINY
    public class EmptySystem : ComponentSystem
    {
        protected override void OnUpdate()
        {

        }
        public new ComponentGroup GetComponentGroup(params EntityArchetypeQuery[] queries)
        {
            return base.GetComponentGroup(queries);
        }

        public new ComponentGroup GetComponentGroup(params ComponentType[] componentTypes)
        {
            return base.GetComponentGroup(componentTypes);
        }
        public new ComponentGroup GetComponentGroup(NativeArray<ComponentType> componentTypes)
        {
            return base.GetComponentGroup(componentTypes);
        }
        public BufferFromEntity<T> GetBufferFromEntity<T>(bool isReadOnly = false) where T : struct, IBufferElementData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetBufferFromEntity<T>(isReadOnly);
        }
    }
#else
    public class EmptySystem : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle dep) { return dep; }


        new public ComponentGroup GetComponentGroup(params EntityArchetypeQuery[] queries)
        {
            return base.GetComponentGroup(queries);
        }

        new public ComponentGroup GetComponentGroup(params ComponentType[] componentTypes)
        {
            return base.GetComponentGroup(componentTypes);
        }
        new public ComponentGroup GetComponentGroup(NativeArray<ComponentType> componentTypes)
        {
            return base.GetComponentGroup(componentTypes);
        }
#if !UNITY_ZEROPLAYER
        #pragma warning disable 618
        new public ComponentGroupArray<T> GetEntities<T>() where T : struct
        {
            return base.GetEntities<T>();
        }
        #pragma warning restore 618
#endif
    }
#endif
    public class ECSTestsFixture
    {
        protected World m_PreviousWorld;
        protected World World;
        protected EntityManager m_Manager;
        protected EntityManager.EntityManagerDebug m_ManagerDebug;

        protected int StressTestEntityCount = 1000;

        [SetUp]
        public virtual void Setup()
        {
            m_PreviousWorld = World.Active;
#if !UNITY_ZEROPLAYER
            World = World.Active = new World("Test World");
#else
            World = DefaultTinyWorldInitialization.Initialize("Test World");
#endif

            m_Manager = World.GetOrCreateManager<EntityManager>();
            m_ManagerDebug = new EntityManager.EntityManagerDebug(m_Manager);

#if !UNITY_ZEROPLAYER
#if !UNITY_2019_2_OR_NEWER
            // Not raising exceptions can easily bring unity down with massive logging when tests fail.
            // From Unity 2019.2 on this field is always implicitly true and therefore removed.

            UnityEngine.Assertions.Assert.raiseExceptions = true;
#endif  // #if !UNITY_2019_2_OR_NEWER
#endif  // #if !UNITY_ZEROPLAYER
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (m_Manager != null)
            {
                // Clean up systems before calling CheckInternalConsistency because we might have filters etc
                // holding on SharedComponentData making checks fail
                var system = World.GetExistingManager<ComponentSystemBase>();
                while (system != null)
                {
                    World.DestroyManager(system);
                    system = World.GetExistingManager<ComponentSystemBase>();
                }

                m_ManagerDebug.CheckInternalConsistency();

                World.Dispose();
                World = null;

                World.Active = m_PreviousWorld;
                m_PreviousWorld = null;
                m_Manager = null;
            }
        }

        public void AssertDoesNotExist(Entity entity)
        {
            Assert.IsFalse(m_Manager.HasComponent<EcsTestData>(entity));
            Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(entity));
            Assert.IsFalse(m_Manager.HasComponent<EcsTestData3>(entity));
            Assert.IsFalse(m_Manager.Exists(entity));
        }

        public void AssertComponentData(Entity entity, int index)
        {
            Assert.IsTrue(m_Manager.HasComponent<EcsTestData>(entity));
            Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(entity));
            Assert.IsFalse(m_Manager.HasComponent<EcsTestData3>(entity));
            Assert.IsTrue(m_Manager.Exists(entity));

            Assert.AreEqual(-index, m_Manager.GetComponentData<EcsTestData2>(entity).value0);
            Assert.AreEqual(-index, m_Manager.GetComponentData<EcsTestData2>(entity).value1);
            Assert.AreEqual(index, m_Manager.GetComponentData<EcsTestData>(entity).value);
        }

        public Entity CreateEntityWithDefaultData(int index)
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));

            // HasComponent & Exists setup correctly
            Assert.IsTrue(m_Manager.HasComponent<EcsTestData>(entity));
            Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(entity));
            Assert.IsFalse(m_Manager.HasComponent<EcsTestData3>(entity));
            Assert.IsTrue(m_Manager.Exists(entity));

            // Create must initialize values to zero
            Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestData2>(entity).value0);
            Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestData2>(entity).value1);
            Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestData>(entity).value);

            // Setup some non zero default values
            m_Manager.SetComponentData(entity, new EcsTestData2(-index));
            m_Manager.SetComponentData(entity, new EcsTestData(index));

            AssertComponentData(entity, index);

            return entity;
        }

        public EmptySystem EmptySystem
        {
            get
            {
                return World.Active.GetOrCreateManager<EmptySystem>();
            }
        }
    }
}
