using System;
using System.ComponentModel;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// Proxy object for an <see cref="Unity.Entities.Entity"/> so it can be selected and shown in the inspector.
    /// </summary>
    public class EntitySelectionProxy : ScriptableObject, ISerializationCallbackReceiver
    {
        /// <summary>
        /// Creates and configures an instance of EntitySelectionProxy wrapping the specified <see cref="Unity.Entities.Entity"/>.
        /// </summary>
        /// <param name="world">The <see cref="Unity.Entities.World"/> in which the Entity exists.</param>
        /// <param name="entity">The entity to be wrapped by this instance of EntitySelectionProxy.</param>
        /// <returns>A fully configured EntitySelectionProxy, wrapping the specified entity.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <see cref="world"/> is <code>null</code> or world.<see cref="Unity.Entities.World.IsCreated"/> is <code>false</code>.</exception>
        public static EntitySelectionProxy CreateInstance(World world, Entity entity)
        {
            if (world == null || !world.IsCreated)
                throw new ArgumentNullException(nameof(world));

            if (!EntityExistsAndIsValid(world, entity))
                entity = Entity.Null;

            var proxy = CreateInstance<EntitySelectionProxy>();
            proxy.hideFlags = HideFlags.HideAndDontSave;
            proxy.Initialize(world, entity);

            Undo.RegisterCreatedObjectUndo(proxy, "Create EntitySelectionProxy");

            return proxy;
        }

        /// <summary>
        /// Creates and selects an instance of EntitySelectionProxy wrapping the specified <see cref="Unity.Entities.Entity"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This operation does not return the created instance of EntitySelectionProxy. If you want to hold onto the created instance, please use <see cref="CreateInstance"/> instead.
        /// </para>
        /// <para>
        /// This operation may produce no results if <see cref="world"/> is <code>null</code>, if world.<see cref="Unity.Entities.World.IsCreated"/> is <code>false</code>, if the specified
        /// <see cref="entity"/> does not exist in the specified world, or if the current selection already points to the specified entity.
        /// </para>
        /// </remarks>
        /// <param name="world">The <see cref="Unity.Entities.World"/> in which the Entity exists.</param>
        /// <param name="entity">The entity to be wrapped by this instance of EntitySelectionProxy.</param>
        public static void SelectEntity(World world, Entity entity)
        {
            if (!EntityExistsAndIsValid(world, entity))
                return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Select Entity");
            CreateInstance(world, entity).Select();
        }

        // Workaround because EntityManager.Exists can potentially throw when used in the editor
        static bool EntityExistsAndIsValid(World world, Entity entity)
        {
            return world != null
                && world.IsCreated
                && entity.Index >= 0 && (uint)entity.Index < (uint)world.EntityManager.EntityCapacity
                && world.EntityManager.Exists(entity);
        }

        [SerializeField] int entityIndex;
        [SerializeField] int entityVersion;

        // Try to remember the world when performing Undo/Redo
        [SerializeField] string worldName;

        /// <summary>
        /// The <see cref="Unity.Entities.World"/> in which the wrapped <see cref="Unity.Entities.Entity"/> exists.
        /// </summary>
        public World World { get; private set; }

        /// <summary>
        /// The <see cref="Unity.Entities.Entity"/> wrapped by this instance of EntitySelectionProxy.
        /// </summary>
        public Entity Entity => new Entity { Index = entityIndex, Version = entityVersion };

        /// <summary>
        /// Whether the wrapped <see cref="Unity.Entities.Entity"/> currently exists and is valid.
        /// </summary>
        public bool Exists => World != null && World.IsCreated && World.EntityManager.Exists(Entity);

        /// <summary>
        /// <para>
        /// The <see cref="Unity.Entities.EntityContainer"/> allowing the <see cref="Unity.Entities.Entity"/> data to be visited.
        /// </para>
        /// <seealso cref="Unity.Properties.PropertyContainer.Visit"/>
        /// </summary>
        public EntityContainer Container { get; private set; }

        /// <summary>
        /// Sets this instance of EntitySelectionProxy as the active selection.
        /// </summary>
        /// <remarks>
        /// This operation produces no result if the current selection is the same instance or if the current selection is another instance
        /// of EntitySelectionProxy wrapping the same <see cref="Unity.Entities.Entity"/>.
        /// </remarks>
        public void Select()
        {
            // Don't reselect yourself
            if (Selection.activeObject == this)
                return;

            // Don't reselect the same entity
            if (Selection.activeObject is EntitySelectionProxy selectionProxy && selectionProxy.World == World && selectionProxy.Entity == Entity)
                return;

            Selection.activeObject = this;
        }

        void Initialize(World world, Entity entity)
        {
            entityIndex = entity.Index;
            entityVersion = entity.Version;
            CreateContext(world);
        }

        void CreateContext(World world)
        {
            if (world == null)
                return;

            World = world;
            Container = new EntityContainer(world.EntityManager, Entity);
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() => worldName = World?.Name;

        void ISerializationCallbackReceiver.OnAfterDeserialize() => CreateContext(FindWorldByName(worldName));

        static World FindWorldByName(string worldName)
        {
            if (string.IsNullOrEmpty(worldName))
                return null;

            foreach (var world in World.All)
            {
                if (world.Name == worldName)
                    return world;
            }

            return null;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("The delegate: EntityControlSelectButtonHandler is no longer used and can be safely removed from your code. (RemovedAfter 2021-01-13).", false)]
        public delegate void EntityControlSelectButtonHandler(World world, Entity entity);

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("The event: EntityControlSelectButton is no longer invoked. Please react to the global selection change instead. (RemovedAfter 2021-01-13).", false)]
#pragma warning disable 67, 618
        public event EntityControlSelectButtonHandler EntityControlSelectButton;
#pragma warning restore 67, 618

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("EntityManager is Obsolete. Please use World.EntityManager instead. (RemovedAfter 2021-01-13).", false)]
        public EntityManager EntityManager => World?.EntityManager ?? default;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("OnEntityControlSelectButton is no longer required. Simply create a new EntitySelectionProxy and select it instead. (RemovedAfter 2021-01-13).", false)]
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable UnusedParameter.Global
        // ReSharper disable once MemberCanBeMadeStatic.Global
        public void OnEntityControlSelectButton(World world, Entity entity) { /* NOOP */}
        // ReSharper restore UnusedParameter.Global

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("SetEntity has been deprecated with no replacement. If you need to select a different Entity, create a new EntitySelectionProxy. (RemovedAfter 2021-01-13).", false)]
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable UnusedParameter.Global
        // ReSharper disable once MemberCanBeMadeStatic.Global
        public void SetEntity(World world, Entity entity) { /* NOOP */}
        // ReSharper restore UnusedParameter.Global
    }
}
