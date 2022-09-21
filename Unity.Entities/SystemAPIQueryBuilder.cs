using System;

namespace Unity.Entities
{
    /// <summary>
    /// Supports construction of queries matching one or multiple archetypes inside `ISystem` and `SystemBase` types.
    /// All queried components must be known at compile-time.
    /// </summary>
    public struct SystemAPIQueryBuilder
    {
        /// <summary>
        /// Specify all read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Mandatory component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAll<T1>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify all read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Mandatory component</typeparam>
        /// <typeparam name="T2">Mandatory component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAll<T1, T2>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify all read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Mandatory component</typeparam>
        /// <typeparam name="T2">Mandatory component</typeparam>
        /// <typeparam name="T3">Mandatory component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAll<T1, T2, T3>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify all read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Mandatory component</typeparam>
        /// <typeparam name="T2">Mandatory component</typeparam>
        /// <typeparam name="T3">Mandatory component</typeparam>
        /// <typeparam name="T4">Mandatory component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAll<T1, T2, T3, T4>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify all read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Mandatory component</typeparam>
        /// <typeparam name="T2">Mandatory component</typeparam>
        /// <typeparam name="T3">Mandatory component</typeparam>
        /// <typeparam name="T4">Mandatory component</typeparam>
        /// <typeparam name="T5">Mandatory component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAll<T1, T2, T3, T4, T5>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify all read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Mandatory component</typeparam>
        /// <typeparam name="T2">Mandatory component</typeparam>
        /// <typeparam name="T3">Mandatory component</typeparam>
        /// <typeparam name="T4">Mandatory component</typeparam>
        /// <typeparam name="T5">Mandatory component</typeparam>
        /// <typeparam name="T6">Mandatory component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAll<T1, T2, T3, T4, T5, T6>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify all read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Mandatory component</typeparam>
        /// <typeparam name="T2">Mandatory component</typeparam>
        /// <typeparam name="T3">Mandatory component</typeparam>
        /// <typeparam name="T4">Mandatory component</typeparam>
        /// <typeparam name="T5">Mandatory component</typeparam>
        /// <typeparam name="T6">Mandatory component</typeparam>
        /// <typeparam name="T7">Mandatory component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAll<T1, T2, T3, T4, T5, T6, T7>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify all read-write component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Mandatory component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAllRW<T1>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify all read-write component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Mandatory component</typeparam>
        /// <typeparam name="T2">Mandatory component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAllRW<T1, T2>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify optional read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Optional component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAny<T1>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify optional read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Optional component</typeparam>
        /// <typeparam name="T2">Optional component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAny<T1, T2>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify optional read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Optional component</typeparam>
        /// <typeparam name="T2">Optional component</typeparam>
        /// <typeparam name="T3">Optional component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAny<T1, T2, T3>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify optional read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Optional component</typeparam>
        /// <typeparam name="T2">Optional component</typeparam>
        /// <typeparam name="T3">Optional component</typeparam>
        /// <typeparam name="T4">Optional component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAny<T1, T2, T3, T4>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify optional read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Optional component</typeparam>
        /// <typeparam name="T2">Optional component</typeparam>
        /// <typeparam name="T3">Optional component</typeparam>
        /// <typeparam name="T4">Optional component</typeparam>
        /// <typeparam name="T5">Optional component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAny<T1, T2, T3, T4, T5>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify optional read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Optional component</typeparam>
        /// <typeparam name="T2">Optional component</typeparam>
        /// <typeparam name="T3">Optional component</typeparam>
        /// <typeparam name="T4">Optional component</typeparam>
        /// <typeparam name="T5">Optional component</typeparam>
        /// <typeparam name="T6">Optional component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAny<T1, T2, T3, T4, T5, T6>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify optional read-only component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Optional component</typeparam>
        /// <typeparam name="T2">Optional component</typeparam>
        /// <typeparam name="T3">Optional component</typeparam>
        /// <typeparam name="T4">Optional component</typeparam>
        /// <typeparam name="T5">Optional component</typeparam>
        /// <typeparam name="T6">Optional component</typeparam>
        /// <typeparam name="T7">Optional component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAny<T1, T2, T3, T4, T5, T6, T7>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify optional read-write component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Optional component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAnyRW<T1>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify optional read-write component types that must be present.
        /// </summary>
        /// <typeparam name="T1">Optional component</typeparam>
        /// <typeparam name="T2">Optional component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithAnyRW<T1, T2>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify component types that must NOT be present.
        /// </summary>
        /// <typeparam name="T1">Absent component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithNone<T1>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify component types that must NOT be present.
        /// </summary>
        /// <typeparam name="T1">Absent component</typeparam>
        /// <typeparam name="T2">Absent component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithNone<T1, T2>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify component types that must NOT be present.
        /// </summary>
        /// <typeparam name="T1">Absent component</typeparam>
        /// <typeparam name="T2">Absent component</typeparam>
        /// <typeparam name="T3">Absent component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithNone<T1, T2, T3>() => throw ThrowNotBuildException();


        /// <summary>
        /// Specify component types that must NOT be present.
        /// </summary>
        /// <typeparam name="T1">Absent component</typeparam>
        /// <typeparam name="T2">Absent component</typeparam>
        /// <typeparam name="T3">Absent component</typeparam>
        /// <typeparam name="T4">Absent component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithNone<T1, T2, T3, T4>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify component types that must NOT be present.
        /// </summary>
        /// <typeparam name="T1">Absent component</typeparam>
        /// <typeparam name="T2">Absent component</typeparam>
        /// <typeparam name="T3">Absent component</typeparam>
        /// <typeparam name="T4">Absent component</typeparam>
        /// <typeparam name="T5">Absent component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithNone<T1, T2, T3, T4, T5>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify component types that must NOT be present.
        /// </summary>
        /// <typeparam name="T1">Absent component</typeparam>
        /// <typeparam name="T2">Absent component</typeparam>
        /// <typeparam name="T3">Absent component</typeparam>
        /// <typeparam name="T4">Absent component</typeparam>
        /// <typeparam name="T5">Absent component</typeparam>
        /// <typeparam name="T6">Absent component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithNone<T1, T2, T3, T4, T5, T6>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify component types that must NOT be present.
        /// </summary>
        /// <typeparam name="T1">Absent component</typeparam>
        /// <typeparam name="T2">Absent component</typeparam>
        /// <typeparam name="T3">Absent component</typeparam>
        /// <typeparam name="T4">Absent component</typeparam>
        /// <typeparam name="T5">Absent component</typeparam>
        /// <typeparam name="T6">Absent component</typeparam>
        /// <typeparam name="T7">Absent component</typeparam>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithNone<T1, T2, T3, T4, T5, T6, T7>() => throw ThrowNotBuildException();

        /// <summary>
        /// Specify your own `EntityQueryOptions`.
        /// </summary>
        /// <remarks>
        /// This method may not be invoked more than once for each query description.
        /// Subsequent calls will override previous options, rather than adding to them. Use the bitwise OR operator '|'
        /// to combine multiple options.
        /// </remarks>
        /// <param name="options">The query options</param>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder WithOptions(EntityQueryOptions options) => throw ThrowNotBuildException();

        /// <summary>
        /// Finalize the existing query description. All `.WithXXX()` invocations chained after this method will create a new query description.
        /// </summary>
        /// <returns>This query builder object, to allow chaining multiple method calls.</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public SystemAPIQueryBuilder AddAdditionalQuery() => throw ThrowNotBuildException();

        /// <summary>
        /// Get or create an `EntityQuery` matching the query description(s).
        /// </summary>
        /// <remarks>If an `EntityQuery` in the containing system's existing cache
        /// matches the defined query, it gets retrieved. Otherwise, a new `EntityQuery` is created and then added to the containing system's cache.
        /// </remarks>
        /// <returns>The query</returns>
        /// <exception cref="ThrowNotBuildException"></exception>
        public EntityQuery Build() => throw InternalCompilerInterface.ThrowCodeGenException();

        static InvalidOperationException ThrowNotBuildException() => throw new InvalidOperationException("Source-generation will not run unless `.Build()` is invoked.");
    }
}
