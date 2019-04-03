using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
    public class WorldTests
    {
        World m_PreviousWorld;

        [SetUp]
        public virtual void Setup()
        {
            m_PreviousWorld = World.Active;
        }

        [TearDown]
        public virtual void TearDown()
        {
            World.Active = m_PreviousWorld;
        }

        
        [Test]
        [StandaloneFixme]
        public void ActiveWorldResets()
        {
            int count = World.AllWorlds.Count();
            var worldA = new World("WorldA");
            var worldB = new World("WorldB");

            World.Active = worldB; 
            
            Assert.AreEqual(worldB, World.Active);
            Assert.AreEqual(count + 2, World.AllWorlds.Count());
            Assert.AreEqual(worldA, World.AllWorlds[World.AllWorlds.Count()-2]);
            Assert.AreEqual(worldB, World.AllWorlds[World.AllWorlds.Count()-1]);
            
            worldB.Dispose();
            
            Assert.IsFalse(worldB.IsCreated);
            Assert.IsTrue(worldA.IsCreated);
            Assert.AreEqual(null, World.Active);
            
            worldA.Dispose();
            
            Assert.AreEqual(count, World.AllWorlds.Count());
        }

        [DisableAutoCreation]
        class TestManager : ComponentSystem
        {
            protected override void OnUpdate() {}
        }

        [Test]
        [StandaloneFixme]
        public void WorldVersionIsConsistent()
        {
            var world = new World("WorldX");

            Assert.AreEqual(0, world.Version);

            var version = world.Version;
            world.GetOrCreateManager<TestManager>();
            Assert.AreNotEqual(version, world.Version);

            version = world.Version;
            var manager = world.GetOrCreateManager<TestManager>();
            Assert.AreEqual(version, world.Version);

            version = world.Version;
            world.DestroyManager(manager);
            Assert.AreNotEqual(version, world.Version);
            
            world.Dispose();
        }
        
        [Test]
        [StandaloneFixme]
        public void UsingDisposedWorldThrows()
        {
            var world = new World("WorldX");
            world.Dispose();

            Assert.Throws<ArgumentException>(() => world.GetExistingManager<TestManager>());
        }
        
        [DisableAutoCreation]
        class AddWorldDuringConstructorThrowsSystem : ComponentSystem
        {
            public AddWorldDuringConstructorThrowsSystem()
            {
                Assert.AreEqual(null, World);
                World.Active.AddManager(this);
            }

            protected override void OnUpdate() { }
        }
        [Test]
        [StandaloneFixme]
        public void AddWorldDuringConstructorThrows ()
        {
            var world = new World("WorldX");
            World.Active = world;
            // Adding a manager during construction is not allowed
            Assert.Throws<TargetInvocationException>(() => world.CreateManager<AddWorldDuringConstructorThrowsSystem>());
            // The manager will not be added to the list of managers if throws
            Assert.AreEqual(0, world.BehaviourManagers.Count());
            
            world.Dispose();
        }
        
        
        [DisableAutoCreation]
        class SystemThrowingInOnCreateManagerIsRemovedSystem : ComponentSystem
        {
            protected override void OnCreateManager()
            {
                throw new AssertionException("");
            }

            protected override void OnUpdate() { }
        }
        [Test]
        [StandaloneFixme]
        public void SystemThrowingInOnCreateManagerIsRemoved()
        {
            var world = new World("WorldX");
            world.GetOrCreateManager<EntityManager>();
            Assert.AreEqual(1, world.BehaviourManagers.Count());

            Assert.Throws<AssertionException>(() => world.GetOrCreateManager<SystemThrowingInOnCreateManagerIsRemovedSystem>());

            // throwing during OnCreateManager does not add the manager to the behaviour manager list
            Assert.AreEqual(1, world.BehaviourManagers.Count());
            
            world.Dispose();
        }

        [DisableAutoCreation]
        class SystemIsAccessibleDuringOnCreateManagerSystem : ComponentSystem
        {
            protected override void OnCreateManager()
            {
                Assert.AreEqual(this, World.GetOrCreateManager<SystemIsAccessibleDuringOnCreateManagerSystem>());
            }
            
            protected override void OnUpdate() { }
        }
        [Test]
        [StandaloneFixme]
        public void SystemIsAccessibleDuringOnCreateManager ()
        {
            var world = new World("WorldX");
            world.GetOrCreateManager<EntityManager>();
            Assert.AreEqual(1, world.BehaviourManagers.Count());
            world.CreateManager<SystemIsAccessibleDuringOnCreateManagerSystem>();
            Assert.AreEqual(2, world.BehaviourManagers.Count());
            
            world.Dispose();
        }
        
        //@TODO: Test for adding a manager from one world to another. 
    }
}
