using System;
using Unity.Core;

namespace Unity.Entities
{
    /// <summary>
    /// Giving quick and consistent access to buffers, components, time, enumeration, singletons and more.
    /// This includes any <see cref="IAspect"/>, <see cref="IJobEntity"/>, <see cref="SystemBase"/>, and <see cref="ISystem"/>.
    /// Suggested usage is:
    /// ```cs
    ///   using static Unity.Entities.SystemAPI;
    ///   [...]
    ///   partial struct SomeJob : IJobEntity { void Execute(ref EcsTestData e1) => e1.value += Time.deltaTime; }
    /// ```
    /// </summary>
    public static class SystemAPI
    {
        #region QueryBuilder
        /// <summary>
        /// Gives a fluent API for constructing EntityQueries similar to the EntityQueryBuilder. This API statically constructs the query as part of the system, and creates the query in the system's OnCreate.
        /// Calling this method should allow for construction of EntityQueries with almost no run-time performance overhead."
        /// </summary>
        /// <returns>An instance of `SystemAPIQueryBuilder`, which can be used to fluently construct queries.</returns>
        /// <exception cref="InternalCompilerInterface.ThrowCodeGenException">Exception indicating that this method has been called outside of a valid context.</exception>
        public static SystemAPIQueryBuilder QueryBuilder() => throw InternalCompilerInterface.ThrowCodeGenException();
        #endregion

        #region Query
        /////////////////////////////////// Query Caching ///////////////////////////////////

        /// <summary>
        /// Get Enumerable for iterating through Aspect, and Component types from inside a system
        /// </summary>
        /// <typeparam name="T1">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <returns>QueryEnumerable that allows enumerating over all Aspects, RefRO, and RefRW of a given type.</returns>
        /// <exception cref="InternalCompilerInterface.ThrowCodeGenException">Exception indicating that this method has been called outside of a valid context.</exception>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static QueryEnumerable<T1> Query<T1>()
            where T1 : IQueryTypeParameter
            => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Get Enumerable for iterating through Aspect, and Component types from inside a system
        /// </summary>
        /// <typeparam name="T1">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T2">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <returns>QueryEnumerable that allows enumerating over all Aspects, RefRO, and RefRW of a given type.</returns>
        /// <exception cref="InternalCompilerInterface.ThrowCodeGenException">Exception indicating that this method has been called outside of a valid context.</exception>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static QueryEnumerable<T1, T2> Query<T1, T2>()
            where T1 : IQueryTypeParameter
            where T2 : IQueryTypeParameter
            => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Get Enumerable for iterating through Aspect, and Component types from inside a system
        /// </summary>
        /// <typeparam name="T1">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T2">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T3">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <returns>QueryEnumerable that allows enumerating over all Aspects of a given type.</returns>
        /// <exception cref="InternalCompilerInterface.ThrowCodeGenException">Exception indicating that this method has been called outside of a valid context.</exception>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static QueryEnumerable<T1, T2, T3> Query<T1, T2, T3>()
            where T1 : IQueryTypeParameter
            where T2 : IQueryTypeParameter
            where T3 : IQueryTypeParameter
            => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Get Enumerable for iterating through Aspect, and Component types from inside a system
        /// </summary>
        /// <typeparam name="T1">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T2">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T3">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T4">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <returns>QueryEnumerable that allows enumerating over all Aspects, RefRO, and RefRW of a given type.</returns>
        /// <exception cref="InternalCompilerInterface.ThrowCodeGenException">Exception indicating that this method has been called outside of a valid context.</exception>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static QueryEnumerable<T1, T2, T3, T4> Query<T1, T2, T3, T4>()
            where T1 : IQueryTypeParameter
            where T2 : IQueryTypeParameter
            where T3 : IQueryTypeParameter
            where T4 : IQueryTypeParameter
            => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Get Enumerable for iterating through Aspect, and Component types from inside a system
        /// </summary>
        /// <typeparam name="T1">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T2">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T3">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T4">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T5">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <returns>QueryEnumerable that allows enumerating over all Aspects, RefRO, and RefRW of a given type.</returns>
        /// <exception cref="InternalCompilerInterface.ThrowCodeGenException">Exception indicating that this method has been called outside of a valid context.</exception>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static QueryEnumerable<T1, T2, T3, T4, T5> Query<T1, T2, T3, T4, T5>()
            where T1 : IQueryTypeParameter
            where T2 : IQueryTypeParameter
            where T3 : IQueryTypeParameter
            where T4 : IQueryTypeParameter
            where T5 : IQueryTypeParameter
            => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Get Enumerable for iterating through Aspect, and Component types from inside a system
        /// </summary>
        /// <typeparam name="T1">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T2">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T3">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T4">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T5">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T6">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <returns>QueryEnumerable that allows enumerating over all Aspects, RefRO, and RefRW of a given type.</returns>
        /// <exception cref="InternalCompilerInterface.ThrowCodeGenException">Exception indicating that this method has been called outside of a valid context.</exception>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static QueryEnumerable<T1, T2, T3, T4, T5, T6> Query<T1, T2, T3, T4, T5, T6>()
            where T1 : IQueryTypeParameter
            where T2 : IQueryTypeParameter
            where T3 : IQueryTypeParameter
            where T4 : IQueryTypeParameter
            where T5 : IQueryTypeParameter
            where T6 : IQueryTypeParameter
            => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Get Enumerable for iterating through Aspect, and Component types from inside a system
        /// </summary>
        /// <typeparam name="T1">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T2">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T3">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T4">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T5">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T6">Aspect, RefRO, or RefRW parameter type</typeparam>
        /// <typeparam name="T7">Aspect, RefRO, or RefRW type</typeparam>
        /// <returns>QueryEnumerable that allows enumerating over all Aspects, RefRO, and RefRW of a given type.</returns>
        /// <exception cref="InternalCompilerInterface.ThrowCodeGenException">Exception indicating that this method has been called outside of a valid context.</exception>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static QueryEnumerable<T1, T2, T3, T4, T5, T6, T7> Query<T1, T2, T3, T4, T5, T6, T7>()
            where T1 : IQueryTypeParameter
            where T2 : IQueryTypeParameter
            where T3 : IQueryTypeParameter
            where T4 : IQueryTypeParameter
            where T5 : IQueryTypeParameter
            where T6 : IQueryTypeParameter
            where T7 : IQueryTypeParameter
            => throw InternalCompilerInterface.ThrowCodeGenException();
        #endregion

        #region Time
        /////////////////////////////////// TimeData Caching ///////////////////////////////////

        /// <summary>
        /// The current Time data for calling system's world.
        /// </summary>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects</remarks>
        public static ref readonly TimeData Time => throw InternalCompilerInterface.ThrowCodeGenException();
        #endregion

        #region ComponentData
        /////////////////////////////////// GetComponentLookup Caching ///////////////////////////////////

        /// <summary>
        /// Gets a dictionary-like container containing all components of type T, keyed by Entity.
        /// </summary>
        /// <param name="isReadOnly">Whether the data is only read, not written.
        /// Access data as read-only whenever possible.</param>
        /// <typeparam name="T">A struct that implements <see cref="IComponentData"/>.</typeparam>
        /// <returns>All component data of type T.</returns>
        /// <remarks>
        /// When you call this method this method gets replaced direct access to a cached <see cref="ComponentLookup{T}"/>.
        /// </remarks>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects</remarks>
        public static ComponentLookup<T> GetComponentLookup<T>(bool isReadOnly = false)
            where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Look up the value of a component for an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of component to retrieve.</typeparam>
        /// <returns>A struct of type T containing the component value.</returns>
        /// <remarks>
        /// Use this method to look up data in another entity using its <see cref="Entity"/> object. For example, if you
        /// have a component that contains an Entity field, you can look up the component data for the referenced
        /// entity using this method.
        ///
        /// When iterating over a set of entities via [Entities.ForEach], do not use this method to access data of the
        /// current entity in the set. This function is much slower than accessing the data directly (by passing the
        /// component containing the data to your lambda iteration function as a parameter).
        ///
        /// When you call this method inside a job scheduled using [Entities.ForEach], this method gets replaced with component access methods
        /// through <see cref="ComponentLookup{T}"/>.
        ///
        /// This lookup method results in a slower, indirect memory access. When possible, organize your
        /// data to minimize the need for indirect lookups.
        ///
        /// [Entities.ForEach]: xref:Unity.Entities.SystemBase.Entities
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown if the component type has no fields.</exception>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects </remarks>
        public static T GetComponent<T>(Entity entity) where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Sets the value of a component of an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="component">The data to set.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <remarks>
        /// Use this method to look up and set data in another entity using its <see cref="Entity"/> object. For example, if you
        /// have a component that contains an Entity field, you can update the component data for the referenced
        /// entity using this method.
        ///
        /// When iterating over a set of entities via [Entities.ForEach], do not use this method to update data of the
        /// current entity in the set. This function is much slower than accessing the data directly (by passing the
        /// component containing the data to your lambda iteration function as a parameter).
        ///
        /// When you call this method on the main thread, it invokes <see cref="EntityManager.SetComponentData{T}"/>.
        /// (An [Entities.ForEach] function invoked with `Run()` executes on the main thread.) When you call this method
        /// inside a job scheduled using [Entities.ForEach], this method gets replaced with component access methods
        /// through <see cref="ComponentLookup{T}"/>.
        ///
        /// In both cases, this lookup method results in a slower, indirect memory access. When possible, organize your
        /// data to minimize the need for indirect lookups.
        ///
        /// [Entities.ForEach]: xref:Unity.Entities.SystemBase.Entities
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown if the component type has no fields.</exception>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects </remarks>
        public static void SetComponent<T>(Entity entity, T component) where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Checks whether an entity has a specific type of component.
        /// </summary>
        /// <param name="entity">The Entity object.</param>
        /// <typeparam name="T">The data type of the component.</typeparam>
        /// <remarks>
        /// Always returns false for an entity that has been destroyed.
        ///
        /// Use this method to check if another entity has a given type of component using its <see cref="Entity"/>
        /// object. For example, if you have a component that contains an Entity field, you can check whether the
        /// referenced entity has a specific type of component using this method. (Entities in the set always have
        /// required components, so you don’t need to check for them.)
        ///
        /// When iterating over a set of entities via `SystemState.Entities`, or <see cref="SystemAPI.Query{T}"/>, avoid using this method with the
        /// current entity in the set. It is generally faster to change your entity query methods to avoid
        /// optional components; this may require a different construction to handle each combination of optional and non-optional components.
        ///
        /// When you call this method this method gets replaced with component access methods through a cached <see cref="ComponentLookup{T}"/>.
        ///
        /// This lookup method results in a slower, indirect memory access. When possible, organize your data to minimize the need for indirect lookups.
        /// </remarks>
        /// <returns>True, if the specified entity has the component.</returns>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects </remarks>
        public static bool HasComponent<T>(Entity entity) where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Gets the value of a component for an entity associated with a system.
        /// </summary>
        /// <param name="systemHandle">The system handle.</param>
        /// <typeparam name="T">The type of component to retrieve.</typeparam>
        /// <returns>A struct of type T containing the component value.</returns>
        /// <remarks>
        /// Use this method to look up data in another system owned entity using its <see cref="SystemHandle"/> object.
        ///
        /// When you call this method inside a job scheduled using [Entities.ForEach], this method gets replaced with component access methods
        /// through <see cref="ComponentDataFromEntity{T}"/>.
        ///
        /// [Entities.ForEach]: xref:Unity.Entities.SystemBase.Entities
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown if the component type has no fields.</exception>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects </remarks>
        public static T GetComponent<T>(SystemHandle systemHandle) where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Gets a reference to a component for an entity associated with a system, for read/write access.
        /// </summary>
        /// <param name="systemHandle">The system handle.</param>
        /// <typeparam name="T">The type of component to retrieve.</typeparam>
        /// <returns>A struct of type T containing the component value.</returns>
        /// <remarks>
        /// Use this method to look up data in another system owned entity using its <see cref="SystemHandle"/> object.
        ///
        /// When you call this method inside a job scheduled using [Entities.ForEach], this method gets replaced with component access methods
        /// through <see cref="ComponentDataFromEntity{T}"/>.
        ///
        /// [Entities.ForEach]: xref:Unity.Entities.SystemBase.Entities
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown if the component type has no fields.</exception>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects </remarks>
        public static RefRW<T> GetComponentRW<T>(SystemHandle systemHandle) where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Sets the value of a component of an entity associated with a system.
        /// </summary>
        /// <param name="systemHandle">The system handle.</param>
        /// <param name="component">The data to set.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <remarks>
        /// Use this method to look up and set data in another system owned entity using its <see cref="SystemHandle"/> object.
        ///
        /// When you call this method on the main thread, it invokes <see cref="EntityManager.SetComponentData{T}"/>.
        /// (An [Entities.ForEach] function invoked with `Run()` executes on the main thread.) When you call this method
        /// inside a job scheduled using [Entities.ForEach], this method gets replaced with component access methods
        /// through <see cref="ComponentDataFromEntity{T}"/>.
        ///
        /// [Entities.ForEach]: xref:Unity.Entities.SystemBase.Entities
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown if the component type has no fields.</exception>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects </remarks>
        public static void SetComponent<T>(SystemHandle systemHandle, T component) where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Checks whether an entity associated with a system has a specific type of component.
        /// </summary>
        /// <param name="systemHandle">The system handle.</param>
        /// <typeparam name="T">The data type of the component.</typeparam>
        /// <remarks>
        /// Always returns false for an system that has been destroyed.
        ///
        /// Use this method to check if another system owned entity has a given type of component using its <see cref="SystemHandle"/>
        /// object.
        ///
        /// When you call this method this method gets replaced with component access methods through a cached <see cref="ComponentDataFromEntity{T}"/>.
        /// </remarks>
        /// <returns>True, if the specified system owned entity has the component.</returns>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects </remarks>
        public static bool HasComponent<T>(SystemHandle systemHandle) where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        #endregion

        #region Buffer
        /////////////////////////////////// GetBufferLookup Caching ///////////////////////////////////

        /// <summary>
        /// Gets a BufferLookup&lt;T&gt; object that can access a <see cref="DynamicBuffer{T}"/>.
        /// </summary>
        /// <remarks>
        /// This method gets replaced direct with access to a cached <see cref="BufferLookup{T}"/>.
        /// </remarks>
        /// <param name="isReadOnly">Whether the buffer data is only read or is also written. Access data in
        /// a read-only fashion whenever possible.</param>
        /// <typeparam name="T">The type of <see cref="IBufferElementData"/> stored in the buffer.</typeparam>
        /// <returns>An array-like object that provides access to buffers, indexed by <see cref="Entity"/>.</returns>
        /// <seealso cref="ComponentLookup{T}"/>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects</remarks>
        public static BufferLookup<T> GetBufferLookup<T>(bool isReadOnly = false)
            where T : unmanaged, IBufferElementData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Gets the dynamic buffer of an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <remarks>
        /// This method gets replaced with component access methods through a cached <see cref="BufferLookup{T}"/>.
        /// </remarks>
        /// <typeparam name="T">The type of the buffer's elements.</typeparam>
        /// <returns>The DynamicBuffer object for accessing the buffer contents.</returns>
        /// <exception cref="ArgumentException">Thrown if T is an unsupported type.</exception>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects</remarks>
        public static DynamicBuffer<T> GetBuffer<T>(Entity entity) where T : unmanaged, IBufferElementData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Checks whether an entity has a dynamic buffer of a specific IBufferElementData type.
        /// </summary>
        /// <param name="entity">The Entity object.</param>
        /// <typeparam name="T">The IBufferElementData type.</typeparam>
        /// <remarks>
        /// Always returns false for an entity that has been destroyed.
        ///
        /// Use this method to check if another entity has a dynamic buffer of a given IBufferElementData type using its <see cref="Entity"/>
        /// object.
        ///
        /// When iterating over a set of entities via [Entities.ForEach], avoid using this method with the
        /// current entity in the set. It is generally faster to change your entity query methods to avoid
        /// optional components; this may require a different [Entities.ForEach] construction to handle
        /// each combination of optional and non-optional components.
        ///
        /// When you call this method on the main thread, it invokes <see cref="EntityManager.HasBuffer{T}"/>.
        /// (An [Entities.ForEach] function invoked with `Run()` executes on the main thread.) When you call this method
        /// inside a job scheduled using [Entities.ForEach], this method gets replaced with component access methods
        /// through <see cref="BufferLookup{T}"/>.
        ///
        /// In both cases, this lookup method results in a slower, indirect memory access. When possible, organize your
        /// data to minimize the need for indirect lookups.
        ///
        /// [Entities.ForEach]: xref:Unity.Entities.SystemBase.Entities
        /// </remarks>
        /// <returns>True, if the specified entity has the component.</returns>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects</remarks>
        public static bool HasBuffer<T>(Entity entity) where T : unmanaged, IBufferElementData => throw InternalCompilerInterface.ThrowCodeGenException();
        #endregion

        #region StorageInfo
        /////////////////////////////////// GetEntityStorageInfoLookup Caching ///////////////////////////////////

        /// <summary>
        /// Gets a EntityStorageInfoLookup object that can access a <seealso cref="EntityStorageInfo"/>.
        /// </summary>
        /// <returns>A dictionary-like object that provides access to information about how Entities are stored,
        /// indexed by <see cref="Entity"/>.</returns>
        /// <seealso cref="EntityStorageInfoLookup"/>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects</remarks>
        public static EntityStorageInfoLookup GetEntityStorageInfoLookup() => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Checks if the entity exists inside this system's EntityManager.
        /// </summary>
        /// <remarks>
        /// This returns true for an entity that was destroyed with DestroyEntity, but still has a cleanup component.
        /// Prefer <see cref="ComponentLookup{T}.TryGetComponent"/> where applicable.
        /// Can be used inside of Entities.ForEach.
        /// </remarks>
        /// <param name="entity">The entity to check</param>
        /// <returns>True if the given entity exists or the entity has a Cleanup Component that is yet to be destroyed</returns>
        /// <seealso cref="EntityManager.Exists"/>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects</remarks>
        public static bool Exists(Entity entity) => throw InternalCompilerInterface.ThrowCodeGenException();
        #endregion

        #region Singleton
        /////////////////////////////////// Singleton Caching ///////////////////////////////////

        /// <summary>
        /// Gets the value of a singleton component.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.
        /// This component type must not implement <see cref="IEnableableComponent"/></typeparam>
        /// <returns>The component.</returns>
        /// <seealso cref="ComponentSystemBase.GetSingletonRW{T}"/>
        /// <seealso cref="EntityQuery.GetSingleton{T}"/>
        /// <seealso cref="EntityQuery.GetSingletonRW{T}"/>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static T GetSingleton<T>()
            where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Gets the value of a singleton component, and returns whether or not a singleton component of the specified type exists in the <see cref="World"/>.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.
        /// This component type must not implement <see cref="IEnableableComponent"/></typeparam>
        /// <param name="value">The component. if an <see cref="Entity"/> with the specified type does not exist in the <see cref="World"/>, this is assigned a default value</param>
        /// <returns>True, if exactly one <see cref="Entity"/> exists in the <see cref="World"/> with the provided component type.</returns>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static bool TryGetSingleton<T>(out T value)
            where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Gets a reference to the singleton component, for read/write access.
        /// </summary>
        /// <remarks>
        /// NOTE: this reference refers directly to the singleton's component memory.
        /// Structural changes to the chunk where the singleton resides can invalidate this reference
        /// and result in crashes or undefined behaviour if the reference is used after structural changes.
        /// </remarks>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.
        /// This component type must not implement <see cref="IEnableableComponent"/></typeparam>
        /// <returns>The component.</returns>
        /// <seealso cref="ComponentSystemBase.GetSingleton{T}"/>
        /// <seealso cref="EntityQuery.GetSingleton{T}"/>
        /// <seealso cref="EntityQuery.GetSingletonRW{T}"/>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static RefRW<T> GetSingletonRW<T>() where T : unmanaged, IComponentData =>  throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Sets the value of a singleton component.
        /// </summary>
        /// <param name="value">A component containing the value to assign to the singleton.</param>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.
        /// This component type must not implement <see cref="IEnableableComponent"/></typeparam>
        /// <seealso cref="EntityQuery.SetSingleton{T}"/>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static void SetSingleton<T>(T value)
            where T : unmanaged, IComponentData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Gets the Entity instance for a singleton.
        /// </summary>
        /// <typeparam name="T">The Type of the singleton component.
        /// This component type must not implement <see cref="IEnableableComponent"/></typeparam>
        /// <returns>The entity associated with the specified singleton component.</returns>
        /// <seealso cref="EntityQuery.GetSingletonEntity"/>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static Entity GetSingletonEntity<T>() => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Gets the singleton Entity, and returns whether or not a singleton <see cref="Entity"/> of the specified type exists in the <see cref="World"/>.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.
        /// This component type must not implement <see cref="IEnableableComponent"/></typeparam>
        /// <param name="value">The <see cref="Entity"/> associated with the specified singleton component.
        ///  If a singleton of the specified types does not exist in the current <see cref="World"/>, this is set to Entity.Null</param>
        /// <returns>True, if exactly one <see cref="Entity"/> exists in the <see cref="World"/> with the provided component type.</returns>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static bool TryGetSingletonEntity<T>(out Entity value) => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Gets the value of a singleton buffer component.
        /// </summary>
        /// <typeparam name="T">The <see cref="IBufferElementData"/> subtype of the singleton component buffer element.
        /// This component type must not implement <see cref="IEnableableComponent"/></typeparam>
        /// <param name="isReadOnly">Whether the buffer data is only read or is also written. Access data in
        /// a read-only fashion whenever possible.</param>
        /// <returns>The buffer.</returns>
        /// <seealso cref="EntityQuery.GetSingleton{T}"/>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static DynamicBuffer<T> GetSingletonBuffer<T>(bool isReadOnly = false)
            where T : unmanaged, IBufferElementData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Gets the value of a singleton buffer component, and returns whether or not a singleton buffer component of the specified type exists in the <see cref="World"/>.
        /// </summary>
        /// <typeparam name="T">The <see cref="IBufferElementData"/> subtype of the singleton buffer component.
        /// This component type must not implement <see cref="IEnableableComponent"/></typeparam>
        /// <param name="value">The buffer. if an <see cref="Entity"/> with the specified type does not exist in the <see cref="World"/>, this is assigned a default value</param>
        /// <returns>True, if exactly one <see cref="Entity"/> exists in the <see cref="World"/> with the provided component type.</returns>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static bool TryGetSingletonBuffer<T>(out DynamicBuffer<T> value)
            where T : unmanaged, IBufferElementData => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Checks whether a singleton component of the specified type exists.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.
        /// This component type must not implement <see cref="IEnableableComponent"/></typeparam>
        /// <returns>True, if a singleton of the specified type exists in the current <see cref="World"/>.</returns>
        /// <remarks> Not working in Entities.ForEach, IJobEntity, Utility methods, and Aspects</remarks>
        public static bool HasSingleton<T>() => throw InternalCompilerInterface.ThrowCodeGenException();
        #endregion

        #region Aspect
        /////////////////////////////////// Aspect Lookup Caching ///////////////////////////////////

        /// <summary>
        /// Look up an aspect for an entity with readwrite access.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of aspect to retrieve.</typeparam>
        /// <returns>An aspect struct of type T representing the aspect on the entity.</returns>
        /// <remarks>
        /// T must implement the <see cref="IAspect"/> interface.
        /// The given entity is assumed to have all the components required by the aspect type.
        /// </remarks>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects</remarks>
        public static T GetAspectRW<T>(Entity entity) where T : struct, IAspect => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Look up an aspect for an entity with readonly access.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of aspect to retrieve.</typeparam>
        /// <returns>An aspect struct of type T representing the aspect on the entity.</returns>
        /// <remarks>
        /// T must implement the <see cref="IAspect"/> interface.
        /// The given entity is assumed to have all the components required by the aspect type.
        /// </remarks>
        /// <remarks> Not working in IJobEntity, Utility methods, and Aspects</remarks>
        public static T GetAspectRO<T>(Entity entity) where T : struct, IAspect => throw InternalCompilerInterface.ThrowCodeGenException();
        #endregion
    }
}
