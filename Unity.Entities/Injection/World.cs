using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace Unity.Entities
{
#if !UNITY_ZEROPLAYER
    public class World : IDisposable
    {
        static readonly List<World> allWorlds = new List<World>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        bool m_AllowGetManager = true;
#endif

        //@TODO: What about multiple managers of the same type...
        Dictionary<Type, ScriptBehaviourManager> m_BehaviourManagerLookup =
            new Dictionary<Type, ScriptBehaviourManager>();

        List<ScriptBehaviourManager> m_BehaviourManagers = new List<ScriptBehaviourManager>();
        static int ms_SystemIDAllocator = 0;

        public World(string name)
        {
            // Debug.LogError("Create World "+ name + " - " + GetHashCode());
            Name = name;
            allWorlds.Add(this);
        }

        public IEnumerable<ScriptBehaviourManager> BehaviourManagers =>
            new ReadOnlyCollection<ScriptBehaviourManager>(m_BehaviourManagers);

        public string Name { get; }

        public override string ToString()
        {
            return Name;
        }

        public int Version { get; private set; }

        public static World Active { get; set; }

        public EntityManager EntityManager 
        {
            get { return GetOrCreateManager<EntityManager>(); }
        }

        public static ReadOnlyCollection<World> AllWorlds => new ReadOnlyCollection<World>(allWorlds);

        public bool IsCreated => m_BehaviourManagers != null;

        public void Dispose()
        {
            if (!IsCreated)
                throw new ArgumentException("The World has already been Disposed.");
            // Debug.LogError("Dispose World "+ Name + " - " + GetHashCode());

            if (allWorlds.Contains(this))
                allWorlds.Remove(this);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_AllowGetManager = false;
#endif
            // Destruction should happen in reverse order to construction
            ScriptBehaviourManager em = null;
            for (int i = m_BehaviourManagers.Count - 1; i >= 0; --i)
            {
                var mgr = m_BehaviourManagers[i];
                if (mgr is EntityManager)
                {
                    em = mgr;
                    continue;
                }
                try
                {
                    mgr.DestroyInstance();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            // Destroy EntityManager last
            if (em != null)
            {
                try
                {
                    em.DestroyInstance();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (Active == this)
                Active = null;

            m_BehaviourManagers.Clear();
            m_BehaviourManagerLookup.Clear();

            m_BehaviourManagers = null;
            m_BehaviourManagerLookup = null;
        }

        public static void DisposeAllWorlds()
        {
            while (allWorlds.Count != 0)
                allWorlds[0].Dispose();
        }

        ScriptBehaviourManager CreateManagerInternal(Type type, object[] constructorArguments)
        {
            if (!typeof(ScriptBehaviourManager).IsAssignableFrom(type))
            {
                throw new ArgumentException($"Type {type} must be derived from ScriptBehaviourManager.");
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS

            if (constructorArguments != null && constructorArguments.Length != 0)
            {
                var constructors =
                    type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (constructors.Length == 1 && constructors[0].IsPrivate)
                    throw new MissingMethodException(
                        $"Constructing {type} failed because the constructor was private, it must be public.");
            }

            m_AllowGetManager = false;
#endif
            ScriptBehaviourManager manager;
            try
            {
                manager = Activator.CreateInstance(type, constructorArguments) as ScriptBehaviourManager;
            }
            catch (MissingMethodException)
            {
                Debug.LogError($"System/Manager {type} must be mentioned in a link.xml file, or annotated " +
                                "with a [Preserve] attribute to prevent its constructor from being stripped.  " +
                                "See https://docs.unity3d.com/Manual/ManagedCodeStripping.html for more information.");
                throw;
            }
            finally
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_AllowGetManager = true;
#endif
            }

            return AddManager(manager);
        }

        ScriptBehaviourManager GetExistingManagerInternal(Type type)
        {
            ScriptBehaviourManager manager;
            if (m_BehaviourManagerLookup.TryGetValue(type, out manager))
                return manager;

            return null;
        }

        ScriptBehaviourManager GetOrCreateManagerInternal(Type type)
        {
            var manager = GetExistingManagerInternal(type);

            return manager ?? CreateManagerInternal(type, null);
        }

        void AddTypeLookup(Type type, ScriptBehaviourManager manager)
        {
            while (type != typeof(ScriptBehaviourManager))
            {
                if (!m_BehaviourManagerLookup.ContainsKey(type))
                    m_BehaviourManagerLookup.Add(type, manager);

                type = type.BaseType;
            }
        }

        void RemoveManagerInternal(ScriptBehaviourManager manager)
        {
            if (!m_BehaviourManagers.Remove(manager))
                throw new ArgumentException($"manager does not exist in the world");
            ++Version;

            var type = manager.GetType();
            while (type != typeof(ScriptBehaviourManager))
            {
                if (m_BehaviourManagerLookup[type] == manager)
                {
                    m_BehaviourManagerLookup.Remove(type);

                    foreach (var otherManager in m_BehaviourManagers)
                        if (otherManager.GetType().IsSubclassOf(type))
                            AddTypeLookup(otherManager.GetType(), otherManager);
                }

                type = type.BaseType;
            }
        }

        void CheckGetOrCreateManager()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!IsCreated)
                throw new ArgumentException("The World has already been Disposed.");
            if (!m_AllowGetManager)
                throw new ArgumentException(
                    "During destruction and constructor of a system you are not allowed to get or create more systems.");
#endif
        }

        void CheckCreated()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!IsCreated)
                throw new ArgumentException("The World has already been Disposed.");
#endif
        }

        public ScriptBehaviourManager CreateManager(Type type, params object[] constructorArguments)
        {
            CheckGetOrCreateManager();

            return CreateManagerInternal(type, constructorArguments);
        }

        public T CreateManager<T>(params object[] constructorArguments) where T : ScriptBehaviourManager
        {
            CheckGetOrCreateManager();

            return (T) CreateManagerInternal(typeof(T), constructorArguments);
        }

        public T GetOrCreateManager<T>() where T : ScriptBehaviourManager
        {
            CheckGetOrCreateManager();

            return (T) GetOrCreateManagerInternal(typeof(T));
        }

        public ScriptBehaviourManager GetOrCreateManager(Type type)
        {
            CheckGetOrCreateManager();

            return GetOrCreateManagerInternal(type);
        }

        public T AddManager<T>(T manager) where T : ScriptBehaviourManager
        {
            CheckGetOrCreateManager();

            m_BehaviourManagers.Add(manager);
            AddTypeLookup(manager.GetType(), manager);

            try
            {
                manager.CreateInstance(this);
            }
            catch
            {
                RemoveManagerInternal(manager);
                throw;
            }
            ++Version;
            return manager;
        }

        public T GetExistingManager<T>() where T : ScriptBehaviourManager
        {
            CheckGetOrCreateManager();

            return (T) GetExistingManagerInternal(typeof(T));
        }

        public ScriptBehaviourManager GetExistingManager(Type type)
        {
            CheckGetOrCreateManager();

            return GetExistingManagerInternal(type);
        }

        public void DestroyManager(ScriptBehaviourManager manager)
        {
            CheckGetOrCreateManager();

            RemoveManagerInternal(manager);
            manager.DestroyInstance();
        }

        public bool QuitUpdate { get; set; }

        internal static int AllocateSystemID()
        {
            return ++ms_SystemIDAllocator;
        }
    }
#else
    public class World : IDisposable
    {
        static readonly List<World> allWorlds = new List<World>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        bool m_AllowGetManager = true;
#endif

        //@TODO: What about multiple managers of the same type...
        List<ScriptBehaviourManager> m_BehaviourManagers = new List<ScriptBehaviourManager>();

        int m_SystemIDAllocator = 0;

        public World(string name)
        {
            // Debug.LogError("Create World "+ name + " - " + GetHashCode());
            Name = name;
            allWorlds.Add(this);
        }

        // XXX fix me -- we need a readonly wrapper
        public ScriptBehaviourManager[] BehaviourManagers => m_BehaviourManagers.ToArray();

        public string Name { get; }

        public override string ToString()
        {
            return Name;
        }

        public int Version { get; private set; }

        public static World Active { get; set; }

        public static World[] AllWorlds => allWorlds.ToArray();

        public bool IsCreated => true;

        public void Dispose()
        {
            if (!IsCreated)
                throw new ArgumentException("World is already disposed");
            // Debug.LogError("Dispose World "+ Name + " - " + GetHashCode());

            if (allWorlds.Contains(this))
                allWorlds.Remove(this);

            // Destruction should happen in reverse order to construction
            ScriptBehaviourManager em = null;
            for (int i = m_BehaviourManagers.Count - 1; i >= 0; --i)
            {
                var mgr = m_BehaviourManagers[i];
                if (mgr is EntityManager)
                {
                    em = mgr;
                    continue;
                }
                try
                {
                    mgr.DestroyInstance();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            // Destroy EntityManager last
            if (em != null)
            {
                try
                {
                    em.DestroyInstance();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (Active == this)
                Active = null;

            m_BehaviourManagers.Clear();
            m_BehaviourManagers = null;
        }

        public static void DisposeAllWorlds()
        {
            while (allWorlds.Count != 0)
                allWorlds[0].Dispose();
        }

        private ScriptBehaviourManager CreateManagerInternal<T>() where T : new()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!m_AllowGetManager)
                throw new ArgumentException(
                    "During destruction of a system you are not allowed to create more systems.");

            m_AllowGetManager = true;
#endif
            ScriptBehaviourManager manager;
            try
            {
#if !UNITY_CSHARP_TINY
                manager = new T() as ScriptBehaviourManager;
#else
                manager = TypeManager.ConstructSystem(typeof(T));
#endif
            }
            catch
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_AllowGetManager = false;
#endif
                throw;
            }

            return AddManager(manager);
        }

        private ScriptBehaviourManager GetExistingManagerInternal<T>()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!IsCreated)
                throw new ArgumentException("During destruction ");
            if (!m_AllowGetManager)
                throw new ArgumentException(
                    "During destruction of a system you are not allowed to get or create more systems.");
#endif

            ScriptBehaviourManager manager;
            for (int i = 0; i < m_BehaviourManagers.Count; ++i) {
                var mgr = m_BehaviourManagers[i];
                if (mgr is T)
                    return mgr;
            }

            return null;
        }

        private ScriptBehaviourManager GetExistingManagerInternal(Type type)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!IsCreated)
                throw new ArgumentException("During destruction ");
            if (!m_AllowGetManager)
                throw new ArgumentException(
                    "During destruction of a system you are not allowed to get or create more systems.");
#endif

            ScriptBehaviourManager manager;
            for (int i = 0; i < m_BehaviourManagers.Count; ++i) {
                var mgr = m_BehaviourManagers[i];
                if (type.IsAssignableFrom(mgr.GetType()))
                    return mgr;
            }

            return null;
        }

        private ScriptBehaviourManager GetOrCreateManagerInternal<T>() where T : new()
        {
            var manager = GetExistingManagerInternal<T>();
            return manager ?? CreateManagerInternal<T>();
        }

        private void RemoveManagerInternal(ScriptBehaviourManager manager)
        {
            if (!m_BehaviourManagers.Remove(manager))
                throw new ArgumentException($"manager does not exist in the world");
            ++Version;
        }

        public T CreateManager<T>() where T : ScriptBehaviourManager, new()
        {
            return (T) CreateManagerInternal<T>();
        }

        public T GetOrCreateManager<T>() where T : ScriptBehaviourManager, new()
        {
            return (T) GetOrCreateManagerInternal<T>();
        }

        public T AddManager<T>(T manager) where T : ScriptBehaviourManager
        {
            m_BehaviourManagers.Add(manager);
            try
            {
                manager.CreateInstance(this);
            }
            catch
            {
                RemoveManagerInternal(manager);
                throw;
            }

            ++Version;
            return manager;
        }

        public T GetExistingManager<T>() where T : ScriptBehaviourManager
        {
            return (T) GetExistingManagerInternal(typeof(T));
        }

        public ScriptBehaviourManager GetExistingManager(Type type)
        {
            return GetExistingManagerInternal(type);
        }

        public void DestroyManager(ScriptBehaviourManager manager)
        {
            RemoveManagerInternal(manager);
            manager.DestroyInstance();
        }

        static int ms_SystemIDAllocator = 0;
        internal static int AllocateSystemID()
        {
            return ++ms_SystemIDAllocator;
        }

        public bool QuitUpdate { get; set; }

        public void Update()
        {
            InitializationSystemGroup initializationSystemGroup =
                GetExistingManager(typeof(InitializationSystemGroup)) as InitializationSystemGroup;
            SimulationSystemGroup simulationSystemGroup =
                GetExistingManager(typeof(SimulationSystemGroup)) as SimulationSystemGroup;
            PresentationSystemGroup presentationSystemGroup =
                GetExistingManager(typeof(PresentationSystemGroup)) as PresentationSystemGroup;

            initializationSystemGroup?.Update();
            simulationSystemGroup?.Update();
            presentationSystemGroup?.Update();
        }

    }
#endif
}
