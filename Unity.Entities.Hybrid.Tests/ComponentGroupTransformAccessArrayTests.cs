using NUnit.Framework;
using UnityEngine;
using UnityEngine.Jobs;
#pragma warning disable 649

namespace Unity.Entities.Tests
{
    class ComponentGroupTransformAccessArrayTests : ECSTestsFixture
	{
#pragma warning disable 618
	    TransformAccessArrayInjectionHook m_TransformAccessArrayInjectionHook = new TransformAccessArrayInjectionHook();
	    ComponentArrayInjectionHook m_ComponentArrayInjectionHook = new ComponentArrayInjectionHook();
#pragma warning restore 618
	    [OneTimeSetUp]
	    public void Init()
	    {
	        InjectionHookSupport.RegisterHook(m_ComponentArrayInjectionHook);
	        InjectionHookSupport.RegisterHook(m_TransformAccessArrayInjectionHook);
	    }

	    [OneTimeTearDown]
	    public void Cleanup()
	    {
	        InjectionHookSupport.RegisterHook(m_TransformAccessArrayInjectionHook);
	        InjectionHookSupport.UnregisterHook(m_ComponentArrayInjectionHook);
	    }

        public ComponentGroupTransformAccessArrayTests()
        {
            Assert.IsTrue(Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled, "JobDebugger must be enabled for these tests");
        }

	    public struct TransformAccessArrayTestTag : IComponentData
	    {
	    }
	    [DisallowMultipleComponent]
        [UnityEngine.AddComponentMenu("Hidden/DontUse")]
	    public class TransformAccessArrayTestTagProxy : ComponentDataProxy<TransformAccessArrayTestTag> { }

	    [Test]
		public void EmptyTransformAccessArrayWorks()
	    {
	        var group = EmptySystem.GetComponentGroup(typeof(Transform), typeof(TransformAccessArrayTestTag));
	        var ta = group.GetTransformAccessArray();
			Assert.AreEqual(0, ta.length);
	    }
	    [Test]
	    public void SingleItemTransformAccessArrayWorks()
	    {
	        var go = new GameObject();
	        go.AddComponent<TransformAccessArrayTestTagProxy>();
	        var group = EmptySystem.GetComponentGroup(typeof(Transform), typeof(TransformAccessArrayTestTag));
	        var ta = group.GetTransformAccessArray();
	        Assert.AreEqual(1, ta.length);

	        Object.DestroyImmediate(go);
	    }
	    [Test]
	    public void AddAndGetNewTransformAccessArrayUpdatesContent()
	    {
	        var go = new GameObject();
	        go.AddComponent<TransformAccessArrayTestTagProxy>();
	        var group = EmptySystem.GetComponentGroup(typeof(Transform), typeof(TransformAccessArrayTestTag));
	        var ta = group.GetTransformAccessArray();
	        Assert.AreEqual(1, ta.length);

	        var go2 = new GameObject();
	        go2.AddComponent<TransformAccessArrayTestTagProxy>();
	        ta = group.GetTransformAccessArray();
	        Assert.AreEqual(2, ta.length);

	        Object.DestroyImmediate(go);
	        Object.DestroyImmediate(go2);
	    }
	    [Test]
	    // The atomic safety handle of TransformAccessArrays are not invalidated when injection changes, the array represents the transforms when you got it
	    public void AddAndUseOldTransformAccessArrayDoesNotUpdateContent()
	    {
	        var go = new GameObject();
	        go.AddComponent<TransformAccessArrayTestTagProxy>();
	        var group = EmptySystem.GetComponentGroup(typeof(Transform), typeof(TransformAccessArrayTestTag));
	        var ta = group.GetTransformAccessArray();
	        Assert.AreEqual(1, ta.length);

	        var go2 = new GameObject();
	        go2.AddComponent<TransformAccessArrayTestTagProxy>();
	        Assert.AreEqual(1, ta.length);

	        Object.DestroyImmediate(go);
	        Object.DestroyImmediate(go2);
	    }
	    [Test]
	    public void DestroyAndGetNewTransformAccessArrayUpdatesContent()
	    {
	        var go = new GameObject();
	        go.AddComponent<TransformAccessArrayTestTagProxy>();
	        var go2 = new GameObject();
	        go2.AddComponent<TransformAccessArrayTestTagProxy>();

	        var group = EmptySystem.GetComponentGroup(typeof(Transform), typeof(TransformAccessArrayTestTag));
	        var ta = group.GetTransformAccessArray();
	        Assert.AreEqual(2, ta.length);

	        Object.DestroyImmediate(go);

	        ta = group.GetTransformAccessArray();
	        Assert.AreEqual(1, ta.length);

	        Object.DestroyImmediate(go2);
	    }
	    [Test]
	    // The atomic safety handle of TransformAccessArrays are not invalidated when injection changes, the array represents the transforms when you got it
	    public void DestroyAndUseOldTransformAccessArrayDoesNotUpdateContent()
	    {
	        var go = new GameObject();
	        go.AddComponent<TransformAccessArrayTestTagProxy>();
	        var go2 = new GameObject();
	        go2.AddComponent<TransformAccessArrayTestTagProxy>();

	        var group = EmptySystem.GetComponentGroup(typeof(Transform), typeof(TransformAccessArrayTestTag));
	        var ta = group.GetTransformAccessArray();
	        Assert.AreEqual(2, ta.length);

	        Object.DestroyImmediate(go);

	        Assert.AreEqual(2, ta.length);

	        Object.DestroyImmediate(go2);
	    }

	    [DisableAutoCreation]
	    public class GameObjectArrayWithTransformAccessSystem : ComponentSystem
        {
            public ComponentGroup group;
	        protected override void OnUpdate()
	        {
	        }

	        public new void UpdateInjectedComponentGroups()
	        {
	            base.UpdateInjectedComponentGroups();
	        }

            protected override void OnCreateManager()
            {
                group = GetComponentGroup(typeof(Transform));
            }
        }

        #pragma warning disable 618
	    [Test]
	    public void GameObjectArrayWorksWithTransformAccessArray()
	    {
	        var hook = new GameObjectArrayInjectionHook();
	        InjectionHookSupport.RegisterHook(hook);

	        var go = new GameObject("test");
	        GameObjectEntity.AddToEntityManager(m_Manager, go);

	        var manager = World.GetOrCreateManager<GameObjectArrayWithTransformAccessSystem>();

	        manager.UpdateInjectedComponentGroups();

	        Assert.AreEqual(1, manager.group.CalculateLength());
	        Assert.AreEqual(go, manager.group.GetGameObjectArray()[0]);
	        Assert.AreEqual(go, manager.group.GetTransformAccessArray()[0].gameObject);

	        Object.DestroyImmediate (go);

	        InjectionHookSupport.UnregisterHook(hook);

	        TearDown();
	    }
        #pragma warning restore 618

	    [DisableAutoCreation]
	    public class TransformWithTransformAccessSystem : ComponentSystem
	    {
            public ComponentGroup group;

            protected override void OnUpdate()
	        {
	        }

	        public new void UpdateInjectedComponentGroups()
	        {
	            base.UpdateInjectedComponentGroups();
	        }

            protected override void OnCreateManager()
            {
                group = GetComponentGroup(typeof(Transform));
            }
        }

        #pragma warning disable 618
	    [Test]
	    public void TransformArrayWorksWithTransformAccessArray()
	    {
	        var go = new GameObject("test");
	        GameObjectEntity.AddToEntityManager(m_Manager, go);

	        var manager = World.GetOrCreateManager<TransformWithTransformAccessSystem>();

	        manager.UpdateInjectedComponentGroups();

	        Assert.AreEqual(1, manager.group.CalculateLength());
	        Assert.AreEqual(manager.group.GetComponentArray<Transform>()[0].gameObject, manager.group.GetTransformAccessArray()[0].gameObject);

	        Object.DestroyImmediate (go);
	        TearDown();
	    }
        #pragma warning restore 618
    }
}
